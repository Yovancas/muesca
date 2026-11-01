using System.Drawing;
using System.Globalization;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Página Tareas (F5 · B5), todo PURO: horas rápidas (<see cref="TaskTimes"/>), borrador de la edición en línea
/// (<see cref="TaskDraft"/>, alimentado con <c>WM_CHAR</c>), orden de la lista (<see cref="TaskList"/>), reloj de
/// recordatorios (<see cref="TaskReminderClock"/>), id de los avisos (<see cref="TaskAlerts"/>), foco de la edición
/// (<see cref="TaskEditFocus"/> con un doble de Win32) y maquetación compartida pintar/clic (<see cref="TasksLayout"/>).
/// </summary>
public class TasksPageTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 10, 20, 30);
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private static NotchTask Task(string title, DateTime? remind = null, DateTime? snoozed = null, bool done = false,
        DateTime? doneAt = null) => new(Guid.NewGuid(), title, remind, snoozed, done, done ? doneAt ?? Now : null, 0);

    // ---------------------------------------------------------------- TaskTimes

    [Theory]
    [InlineData("9", 9, 0)]
    [InlineData("18", 18, 0)]
    [InlineData("930", 9, 30)]
    [InlineData("0930", 9, 30)]
    [InlineData("1830", 18, 30)]
    [InlineData("0000", 0, 0)]
    [InlineData("2359", 23, 59)]
    public void Custom_digits_parse_as_HHMM(string digits, int h, int m) =>
        Assert.Equal(new TimeSpan(h, m, 0), TaskTimes.ParseCustom(digits));

    [Theory]
    [InlineData("")]
    [InlineData("24")]
    [InlineData("2400")]
    [InlineData("1860")]
    [InlineData("975")]
    [InlineData("12345")]
    [InlineData("1a30")]
    public void Invalid_custom_digits_are_null(string digits) => Assert.Null(TaskTimes.ParseCustom(digits));

    [Fact]
    public void Quick_times_resolve_from_now()
    {
        Assert.Null(TaskTimes.Resolve(TaskQuick.None, "", Now));
        Assert.Equal(Now.AddMinutes(15), TaskTimes.Resolve(TaskQuick.In15Min, "", Now));
        Assert.Equal(Now.AddHours(1), TaskTimes.Resolve(TaskQuick.In1Hour, "", Now));
        Assert.Equal(new DateTime(2026, 10, 30, 9, 0, 0), TaskTimes.Resolve(TaskQuick.Tomorrow9, "", Now));
    }

    [Fact]
    public void Custom_time_is_today_if_still_ahead_else_tomorrow()
    {
        Assert.Equal(new DateTime(2026, 10, 29, 18, 30, 0), TaskTimes.Resolve(TaskQuick.Custom, "1830", Now));
        Assert.Equal(new DateTime(2026, 10, 30, 9, 0, 0), TaskTimes.Resolve(TaskQuick.Custom, "900", Now));
        // La misma hora y minuto de ahora ya pasó (hay segundos): mañana.
        Assert.Equal(new DateTime(2026, 10, 30, 10, 20, 0), TaskTimes.Resolve(TaskQuick.Custom, "1020", Now));
        Assert.Null(TaskTimes.Resolve(TaskQuick.Custom, "", Now));
        Assert.Null(TaskTimes.Resolve(TaskQuick.Custom, "2599", Now));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("1", "1")]
    [InlineData("18", "18")]
    [InlineData("930", "9:30")]
    [InlineData("1830", "18:30")]
    public void Digits_are_shown_with_a_colon(string digits, string shown) =>
        Assert.Equal(shown, TaskTimes.FormatDigits(digits));

    [Fact]
    public void Labels_say_today_tomorrow_or_the_date()
    {
        Assert.Equal("18:30", TaskTimes.Label(new DateTime(2026, 10, 29, 18, 30, 0), Now, Es, true, "mañana"));
        Assert.Equal("mañana 09:00", TaskTimes.Label(new DateTime(2026, 10, 30, 9, 0, 0), Now, Es, true, "mañana"));
        Assert.Equal("lun 2 nov 08:05", TaskTimes.Label(new DateTime(2026, 11, 2, 8, 5, 0), Now, Es, true, "mañana"));
        Assert.Equal("6:30 PM", TaskTimes.Label(new DateTime(2026, 10, 29, 18, 30, 0), Now,
            CultureInfo.GetCultureInfo("en-US"), false, "tomorrow"));
    }

    // ---------------------------------------------------------------- TaskDraft

    private static TaskDraft Typed(string text)
    {
        var d = new TaskDraft();
        foreach (var c in text) d.OnChar(c);
        return d;
    }

    [Fact]
    public void Typing_builds_the_title_and_Enter_commits()
    {
        var d = Typed("Comprar pan");
        Assert.Equal("Comprar pan", d.Title);
        Assert.Equal(TaskKey.Commit, d.OnChar('\r'));
        Assert.True(d.TryBuild(Now, out var title, out var at));
        Assert.Equal("Comprar pan", title);
        Assert.Null(at);
    }

    [Fact]
    public void Enter_on_a_blank_title_does_not_commit_and_Esc_cancels()
    {
        var d = Typed("   ");
        Assert.False(d.CanSave);
        Assert.Equal(TaskKey.None, d.OnChar('\r'));
        Assert.False(d.TryBuild(Now, out _, out _));
        Assert.Equal(TaskKey.Cancel, d.OnChar('\x1b'));
    }

    [Fact]
    public void Title_is_capped_at_80_without_splitting_surrogate_pairs()
    {
        var d = Typed(new string('a', 79));
        Assert.Equal(TaskKey.None, d.OnChar('\uD83D'));   // par sustituto: no cabe entero (79 + 2 > 80)
        Assert.Equal(TaskKey.None, d.OnChar('\uDE00'));
        Assert.Equal(79, d.Title.Length);
        Assert.Equal(TaskKey.Changed, d.OnChar('b'));
        Assert.Equal(TaskKey.None, d.OnChar('c'));
        Assert.Equal(TaskStore.MaxTitle, d.Title.Length);
    }

    [Fact]
    public void Surrogate_pairs_arrive_in_two_WM_CHAR_and_backspace_removes_the_whole_pair()
    {
        var d = Typed("ok ");
        d.OnChar('\uD83D');
        Assert.Equal("ok ", d.Title);                       // la mitad alta espera a su pareja
        Assert.Equal(TaskKey.Changed, d.OnChar('\uDE00'));
        Assert.Equal("ok 😀", d.Title);
        d.OnChar('\b');
        Assert.Equal("ok ", d.Title);
        Assert.Equal(TaskKey.None, d.OnChar('\uDE00'));     // mitad baja suelta: se ignora
        Assert.Equal("ok ", d.Title);
    }

    [Fact]
    public void Control_characters_are_ignored_and_ctrl_backspace_deletes_a_word()
    {
        var d = Typed("llamar a Fran");
        Assert.Equal(TaskKey.None, d.OnChar('\x01'));
        Assert.Equal(TaskKey.None, d.OnChar('\n'));
        Assert.Equal(TaskKey.Changed, d.OnChar('\x7f'));
        Assert.Equal("llamar a ", d.Title);
        Assert.Equal(TaskKey.Paste, d.OnChar('\x16'));      // Ctrl+V: el llamador lee el portapapeles
    }

    [Fact]
    public void Paste_flattens_lines_drops_control_chars_and_fits()
    {
        var d = Typed("x");
        Assert.True(d.Paste("una\r\nlista\tcon\u0007cosas"));
        Assert.Equal("xuna lista concosas", d.Title);   // el timbre (0x07) se quita sin dejar hueco
        d.Paste(new string('z', 200));
        Assert.Equal(TaskStore.MaxTitle, d.Title.Length);
        Assert.False(d.Paste("más"));
    }

    [Fact]
    public void Quick_chips_toggle_and_custom_moves_typing_to_the_time()
    {
        var d = Typed("t");
        d.Choose(TaskQuick.In1Hour);
        Assert.Equal(TaskQuick.In1Hour, d.Quick);
        d.Choose(TaskQuick.In1Hour);
        Assert.Equal(TaskQuick.None, d.Quick);

        d.Choose(TaskQuick.Custom);
        Assert.Equal(TaskField.Time, d.Field);
        foreach (var c in "18:305") d.OnChar(c);             // solo dígitos y como mucho 4
        Assert.Equal("1830", d.TimeDigits);
        Assert.Equal("t", d.Title);
        Assert.True(d.TryBuild(Now, out _, out var at));
        Assert.Equal(new DateTime(2026, 10, 29, 18, 30, 0), at);
    }

    [Fact]
    public void Typing_digits_in_the_time_field_selects_custom_and_Tab_switches_field()
    {
        var d = Typed("t");
        Assert.Equal(TaskKey.Changed, d.OnChar('\t'));
        Assert.Equal(TaskField.Time, d.Field);
        d.OnChar('9');
        Assert.Equal(TaskQuick.Custom, d.Quick);
        d.OnChar('\t');
        d.OnChar('x');
        Assert.Equal("tx", d.Title);
        Assert.Equal("9", d.TimeDigits);
    }

    [Fact]
    public void An_impossible_custom_time_blocks_saving_but_an_empty_one_means_no_reminder()
    {
        var d = Typed("t");
        d.Choose(TaskQuick.Custom);
        Assert.True(d.CanSave);                              // vacía = sin recordatorio
        Assert.True(d.TryBuild(Now, out _, out var none));
        Assert.Null(none);
        foreach (var c in "2599") d.OnChar(c);
        Assert.False(d.TimeValid);
        Assert.Equal(TaskKey.None, d.OnChar('\r'));
        d.OnChar('\x7f');                                     // Ctrl+Retroceso en la hora: la vacía
        Assert.Equal("", d.TimeDigits);
    }

    [Fact]
    public void Color_cycles_through_the_palette()
    {
        var d = new TaskDraft(TaskStore.MaxColor);
        d.CycleColor();
        Assert.Equal(0, d.Color);
        d.CycleColor();
        Assert.Equal(1, d.Color);
        Assert.Equal(TaskStore.MaxColor, new TaskDraft(99).Color);
    }

    // ---------------------------------------------------------------- TaskList

    [Fact]
    public void Order_puts_timed_pending_first_then_untimed_then_done()
    {
        var a = Task("sin hora 1");
        var b = Task("tarde", Now.AddHours(5));
        var c = Task("hecha", Now.AddHours(1), done: true);
        var d = Task("pronto (aplazada)", Now.AddHours(9), snoozed: Now.AddMinutes(10));
        var e = Task("sin hora 2");
        Assert.Equal(new[] { d, b, a, e, c }, TaskList.Order(new[] { a, b, c, d, e }));
    }

    // ---------------------------------------------------------------- TaskReminderClock

    [Fact]
    public void Poll_fires_each_due_task_once_and_NextWakeIn_points_to_the_next()
    {
        var clock = new TaskReminderClock();
        var due1 = Task("1", Now.AddMinutes(-2));
        var due2 = Task("2", Now.AddMinutes(-1));
        var later = Task("3", Now.AddMinutes(30));
        var items = new[] { later, due2, due1 };

        Assert.Equal(new[] { due1, due2 }, clock.Poll(items, Now));
        Assert.Empty(clock.Poll(items, Now.AddSeconds(5)));
        Assert.Equal(TimeSpan.FromMinutes(30), clock.NextWakeIn(items, Now));
        Assert.Null(clock.NextWakeIn(new[] { due1 }, Now));
    }

    [Fact]
    public void After_a_long_suspend_the_due_reminder_fires_once_on_return()
    {
        var clock = new TaskReminderClock();
        var t = Task("reunión", Now.AddMinutes(5));
        Assert.Empty(clock.Poll(new[] { t }, Now));
        // El PC se suspende 3 h: el temporizador no llegó a disparar; al volver se sondea.
        var back = Now.AddHours(3);
        Assert.Equal(new[] { t }, clock.Poll(new[] { t }, back));
        Assert.Empty(clock.Poll(new[] { t }, back.AddMinutes(1)));
    }

    [Fact]
    public void Rearm_lets_a_snoozed_task_ring_again()
    {
        var clock = new TaskReminderClock();
        var t = Task("x", Now.AddMinutes(-1));
        clock.Poll(new[] { t }, Now);
        var snoozed = t with { SnoozedUntilLocal = Now + TaskTimes.Snooze };
        clock.Rearm(t.Id);
        Assert.Empty(clock.Poll(new[] { snoozed }, Now));
        Assert.Equal(TaskTimes.Snooze, clock.NextWakeIn(new[] { snoozed }, Now));
        Assert.Equal(new[] { snoozed }, clock.Poll(new[] { snoozed }, Now + TaskTimes.Snooze));
    }

    [Fact]
    public void Removed_tasks_are_forgotten()
    {
        var clock = new TaskReminderClock();
        var t = Task("x", Now.AddMinutes(-1));
        clock.Poll(new[] { t }, Now);
        Assert.True(clock.HasFired(t.Id));
        clock.Poll(Array.Empty<NotchTask>(), Now);
        Assert.False(clock.HasFired(t.Id));
    }

    // ---------------------------------------------------------------- TaskAlerts

    [Fact]
    public void Alert_ids_round_trip_and_reject_other_sessions()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, TaskAlerts.TaskId(TaskAlerts.SessionId(id)));
        Assert.Null(TaskAlerts.TaskId("quota:5h"));
        Assert.Null(TaskAlerts.TaskId("task:no-es-un-guid"));
        Assert.Null(TaskAlerts.TaskId(""));
    }

    // ---------------------------------------------------------------- TaskEditFocus

    private sealed class FakeHost : IEditFocusHost
    {
        public IntPtr Self { get; } = new(100);
        public IntPtr Fg = new(7);
        public bool NoActivate = true, ActivateWorks = true;
        public readonly HashSet<IntPtr> Alive = new() { new IntPtr(7), new IntPtr(100) };
        public readonly List<string> Log = new();

        public IntPtr Foreground() => Fg;
        public void SetNoActivate(bool on) { NoActivate = on; Log.Add(on ? "noact+" : "noact-"); }
        public bool Activate()
        {
            Log.Add("activate");
            if (ActivateWorks) Fg = Self;
            return ActivateWorks;
        }
        public bool IsWindow(IntPtr h) => Alive.Contains(h);
        public void Restore(IntPtr h) { Log.Add("restore:" + h); Fg = h; }
    }

    [Fact]
    public void Edit_focus_drops_noactivate_takes_focus_and_gives_it_back()
    {
        var host = new FakeHost();
        var f = new TaskEditFocus(host);
        Assert.True(f.Begin());
        Assert.True(f.Active);
        Assert.False(host.NoActivate);
        Assert.Equal(host.Self, host.Fg);

        f.End(restoreFocus: true);
        Assert.False(f.Active);
        Assert.True(host.NoActivate);
        Assert.Equal(new IntPtr(7), host.Fg);
        Assert.Equal(new[] { "noact-", "activate", "noact+", "restore:7" }, host.Log);
    }

    [Fact]
    public void Edit_focus_restores_noactivate_if_activation_fails()
    {
        var host = new FakeHost { ActivateWorks = false };
        var f = new TaskEditFocus(host);
        Assert.False(f.Begin());
        Assert.False(f.Active);
        Assert.True(host.NoActivate);
    }

    [Fact]
    public void Edit_focus_does_not_steal_back_when_the_user_went_elsewhere()
    {
        var host = new FakeHost();
        var f = new TaskEditFocus(host);
        f.Begin();
        host.Fg = new IntPtr(55);                 // el usuario hizo clic en otra ventana
        f.End(restoreFocus: false);
        Assert.Equal(new IntPtr(55), host.Fg);
        Assert.True(host.NoActivate);

        f.Begin();
        host.Alive.Remove(new IntPtr(55));        // la ventana anterior se cerró mientras tanto
        f.End(restoreFocus: true);
        Assert.DoesNotContain(host.Log, l => l == "restore:55");
        f.End(restoreFocus: true);                // dos veces: no hace nada
        Assert.True(host.NoActivate);
    }

    [Fact]
    public void Edit_focus_never_restores_to_itself()
    {
        var host = new FakeHost();
        host.Fg = host.Self;
        var f = new TaskEditFocus(host);
        f.Begin();
        f.End(restoreFocus: true);
        Assert.DoesNotContain(host.Log, l => l.StartsWith("restore", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- TasksLayout

    private static readonly RectangleF Inner = new(18, 12, 444, 120);   // 1 cabecera + 4 filas de 22 + holgura

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Layout_header_and_rows_are_inside_the_page(float s)
    {
        var inner = new RectangleF(Inner.X * s, Inner.Y * s, Inner.Width * s, Inner.Height * s);
        var r = TasksLayout.Compute(inner, s, editing: false);
        Assert.True(inner.Contains(r.AddButton));
        Assert.True(r.Title.Right <= r.AddButton.Left);
        Assert.Equal(4, r.Rows);
        Assert.True(r.Row(3).Bottom <= inner.Bottom + 0.5f);
        Assert.Equal(RectangleF.Empty, r.Row(4));

        var e = TasksLayout.Compute(inner, s, editing: true);
        Assert.Equal(3, e.Rows);                                   // la fila de las horas rápidas ocupa una
        Assert.True(e.Field.Left >= e.ColorDot.Right && e.Field.Right <= e.SaveButton.Left);
        for (int i = 0; i < TasksLayout.Chips; i++)
        {
            Assert.True(inner.Contains(e.Chip(i)), $"chip {i}");
            if (i > 0) Assert.True(e.Chip(i).Left > e.Chip(i - 1).Right);
        }
        Assert.True(e.List.Top >= e.ChipsRow.Bottom);
    }

    [Fact]
    public void HitTest_finds_add_check_row_and_delete()
    {
        var r = TasksLayout.Compute(Inner, 1f, editing: false);
        PointF Center(RectangleF x) => new(x.Left + x.Width / 2f, x.Top + x.Height / 2f);

        Assert.Equal(new TaskHit(TaskControl.Add), TasksLayout.HitTest(Center(r.AddButton), Inner, 1f, 2, 0, false));
        Assert.Equal(new TaskHit(TaskControl.Check, 1), TasksLayout.HitTest(Center(r.Check(1)), Inner, 1f, 2, 0, false));
        Assert.Equal(new TaskHit(TaskControl.Delete, 0), TasksLayout.HitTest(Center(r.Delete(0)), Inner, 1f, 2, 0, false));
        Assert.Equal(new TaskHit(TaskControl.Row, 0), TasksLayout.HitTest(Center(r.Row(0)), Inner, 1f, 2, 0, false));
        // Sin fila: nada. Con desplazamiento, el índice ya lo suma.
        Assert.Equal(default, TasksLayout.HitTest(Center(r.Row(3)), Inner, 1f, 2, 0, false));
        Assert.Equal(new TaskHit(TaskControl.Row, 3), TasksLayout.HitTest(Center(r.Row(0)), Inner, 1f, 9, 3, false));
        // Fuera de la página: nada.
        Assert.Equal(default, TasksLayout.HitTest(new PointF(2, 2), Inner, 1f, 2, 0, false));
    }

    [Fact]
    public void HitTest_while_editing_finds_field_colour_chips_and_save()
    {
        var r = TasksLayout.Compute(Inner, 1f, editing: true);
        PointF Center(RectangleF x) => new(x.Left + x.Width / 2f, x.Top + x.Height / 2f);

        Assert.Equal(new TaskHit(TaskControl.Field), TasksLayout.HitTest(Center(r.Field), Inner, 1f, 0, 0, true));
        Assert.Equal(new TaskHit(TaskControl.Color), TasksLayout.HitTest(Center(r.ColorDot), Inner, 1f, 0, 0, true));
        Assert.Equal(new TaskHit(TaskControl.Save), TasksLayout.HitTest(Center(r.SaveButton), Inner, 1f, 0, 0, true));
        Assert.Equal(new TaskHit(TaskControl.Quick, (int)TaskQuick.In15Min), TasksLayout.HitTest(Center(r.Chip(0)), Inner, 1f, 0, 0, true));
        Assert.Equal(new TaskHit(TaskControl.Quick, (int)TaskQuick.Custom), TasksLayout.HitTest(Center(r.Chip(3)), Inner, 1f, 0, 0, true));
        // Editando no hay «+ Añadir» y las filas siguen debajo de las horas.
        Assert.Equal(new TaskHit(TaskControl.Row, 0), TasksLayout.HitTest(Center(r.Row(0)), Inner, 1f, 1, 0, true));
    }

    [Fact]
    public void Chips_map_to_quick_times_in_order()
    {
        Assert.Equal(new[] { TaskQuick.In15Min, TaskQuick.In1Hour, TaskQuick.Tomorrow9, TaskQuick.Custom },
            Enumerable.Range(0, TasksLayout.Chips).Select(TasksLayout.ChipQuick));
    }
}
