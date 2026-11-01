using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Tareas del notch: <see cref="TaskStore"/> (JSON atómico en un directorio temporal) y
/// <see cref="TaskReminders"/> (PURO: cuándo vence un recordatorio y el siguiente plazo).
/// </summary>
public sealed class NotchTasksTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 28, 10, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbw-tasks-" + Guid.NewGuid().ToString("N"));
    private string PathJson => Path.Combine(_dir, "tasks.json");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private static NotchTask Item(DateTime? remind, DateTime? snoozed = null, bool done = false) =>
        new(Guid.NewGuid(), "t", remind, snoozed, done, done ? remind : null, 0);

    // ---------------- TaskStore ----------------

    [Fact]
    public void Add_persists_and_reloads()
    {
        var s = new TaskStore(PathJson);
        int changed = 0;
        s.Changed += () => changed++;
        var a = s.Add("  Llamar a Ana  ", T0.AddHours(1), color: 3);
        var b = s.Add("Comprar pan", null, color: 0);

        Assert.Equal(2, changed);
        Assert.Equal("Llamar a Ana", a.Title);
        Assert.True(File.Exists(PathJson));

        var again = new TaskStore(PathJson);
        Assert.Equal(new[] { a, b }, again.Items);
    }

    [Fact]
    public void Focuses_from_the_pomodoro_are_counted_and_old_files_read_as_zero()
    {
        var s = new TaskStore(PathJson);
        var a = s.Add("Informe", null, color: 0);
        Assert.Equal(0, a.Focuses);
        int changed = 0;
        s.Changed += () => changed++;
        s.AddFocus(a.Id);
        s.AddFocus(a.Id);
        s.AddFocus(Guid.NewGuid());   // ya no existe: nada (ni escribe)
        Assert.Equal(2, changed);
        Assert.Equal(2, new TaskStore(PathJson).Items.Single().Focuses);

        // Un tasks.json de antes del pomodoro no trae «Focuses» (0); uno negativo a mano, también 0.
        File.WriteAllText(PathJson, """{"Version":1,"Tasks":[{"Id":"8d6c3c55-6a36-4b1e-9d0b-7c1d8f7f2a10","Title":"Viejo","Done":false,"Color":1}]}""");
        Assert.Equal(0, new TaskStore(PathJson).Items.Single().Focuses);
        File.WriteAllText(PathJson, """{"Version":1,"Tasks":[{"Id":"8d6c3c55-6a36-4b1e-9d0b-7c1d8f7f2a10","Title":"Raro","Done":false,"Color":1,"Focuses":-4}]}""");
        Assert.Equal(0, new TaskStore(PathJson).Items.Single().Focuses);
    }

    [Fact]
    public void Missing_or_corrupt_file_starts_empty()
    {
        Assert.Empty(new TaskStore(PathJson).Items);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathJson, "{ roto");
        var s = new TaskStore(PathJson);
        Assert.Empty(s.Items);
        s.Add("nueva", null, 0);
        Assert.Single(new TaskStore(PathJson).Items);
    }

    [Fact]
    public void Title_is_trimmed_to_80()
    {
        var s = new TaskStore(PathJson);
        var t = s.Add(new string('x', 79) + "😀" + "resto", null, 0);
        // No parte el emoji por la mitad: se queda en 79 (el par sustituto no cabe entero).
        Assert.Equal(new string('x', 79), t.Title);
        Assert.Equal(80, s.Add(new string('y', 200), null, 0).Title.Length);
        Assert.Equal("dos líneas", s.Add("dos\r\nlíneas", null, 0).Title);
        Assert.Throws<ArgumentException>(() => s.Add("   ", null, 0));
    }

    [Fact]
    public void Color_is_clamped()
    {
        var s = new TaskStore(PathJson);
        Assert.Equal(5, s.Add("a", null, 9).Color);
        Assert.Equal(0, s.Add("b", null, -1).Color);
    }

    [Fact]
    public void Update_Remove_Complete()
    {
        var s = new TaskStore(PathJson);
        var a = s.Add("a", T0, 0);
        s.Update(a with { Title = "a2", Color = 2 });
        Assert.Equal("a2", s.Items[0].Title);

        s.Complete(a.Id, T0.AddMinutes(5));
        Assert.True(s.Items[0].Done);
        Assert.Equal(T0.AddMinutes(5), s.Items[0].DoneAt);

        s.Remove(a.Id);
        Assert.Empty(s.Items);
        Assert.Empty(new TaskStore(PathJson).Items);
    }

    [Fact]
    public void Done_tasks_are_purged_the_next_day()
    {
        var s = new TaskStore(PathJson);
        var a = s.Add("hecha", null, 0);
        var b = s.Add("pendiente", null, 0);
        s.Complete(a.Id, T0);
        s.Purge(T0.Date.AddHours(23).AddMinutes(59));
        Assert.Equal(2, s.Items.Count);
        s.Purge(T0.Date.AddDays(1));
        Assert.Equal(new[] { b.Id }, s.Items.Select(i => i.Id));
    }

    [Fact]
    public void Snooze_moves_reminder()
    {
        var s = new TaskStore(PathJson);
        var a = s.Add("a", T0, 0);
        var fired = new HashSet<Guid>();

        Assert.Equal(a.Id, TaskReminders.Due(s.Items, T0, fired)?.Id);
        s.Snooze(a.Id, TimeSpan.FromMinutes(10), T0.AddMinutes(1));
        fired.Remove(a.Id); // el llamador rearma el aviso al posponer

        Assert.Equal(T0.AddMinutes(11), s.Items[0].SnoozedUntilLocal);
        Assert.Null(TaskReminders.Due(s.Items, T0.AddMinutes(5), fired));
        Assert.Equal(T0.AddMinutes(11), TaskReminders.NextDue(s.Items, T0.AddMinutes(5)));
        Assert.Equal(a.Id, TaskReminders.Due(s.Items, T0.AddMinutes(11), fired)?.Id);
    }

    [Fact]
    public void Changing_RemindAt_clears_snooze()
    {
        var s = new TaskStore(PathJson);
        var a = s.Add("a", T0, 0);
        s.Snooze(a.Id, TimeSpan.FromMinutes(10), T0);
        s.Update(s.Items[0] with { RemindAtLocal = T0.AddHours(3) });
        Assert.Null(s.Items[0].SnoozedUntilLocal);
    }

    // ---------------- TaskReminders ----------------

    [Fact]
    public void Due_fires_once()
    {
        var t = Item(T0);
        var items = new[] { t };
        var fired = new HashSet<Guid>();
        Assert.Null(TaskReminders.Due(items, T0.AddSeconds(-1), fired));
        Assert.Same(t, TaskReminders.Due(items, T0, fired));
        Assert.Contains(t.Id, fired);
        Assert.Null(TaskReminders.Due(items, T0.AddSeconds(1), fired));
    }

    [Fact]
    public void Due_after_suspend_fires_once_on_return()
    {
        // El PC se suspende a las 9:59 con un recordatorio a las 10:00 y vuelve a las 10:45.
        var t = Item(T0);
        var items = new[] { t };
        var fired = new HashSet<Guid>();
        Assert.Null(TaskReminders.Due(items, T0.AddMinutes(-1), fired));
        Assert.Same(t, TaskReminders.Due(items, T0.AddMinutes(45), fired));
        Assert.Null(TaskReminders.Due(items, T0.AddMinutes(46), fired));
    }

    [Fact]
    public void Stale_over_12h_does_not_fire()
    {
        var items = new[] { Item(T0) };
        var fired = new HashSet<Guid>();
        Assert.Null(TaskReminders.Due(items, T0.AddHours(12).AddSeconds(1), fired));
        Assert.NotNull(TaskReminders.Due(items, T0.AddHours(12), new HashSet<Guid>()));
    }

    [Fact]
    public void Done_or_without_reminder_never_fires()
    {
        var items = new[] { Item(T0, done: true), Item(null) };
        Assert.Null(TaskReminders.Due(items, T0.AddMinutes(1), new HashSet<Guid>()));
        Assert.Null(TaskReminders.NextDue(items, T0.AddMinutes(-10)));
    }

    [Fact]
    public void Due_returns_the_oldest_first()
    {
        var late = Item(T0.AddMinutes(5));
        var early = Item(T0);
        var items = new[] { late, early };
        var fired = new HashSet<Guid>();
        Assert.Same(early, TaskReminders.Due(items, T0.AddMinutes(10), fired));
        Assert.Same(late, TaskReminders.Due(items, T0.AddMinutes(10), fired));
        Assert.Null(TaskReminders.Due(items, T0.AddMinutes(10), fired));
    }

    [Fact]
    public void NextDue_is_earliest_pending()
    {
        var items = new[]
        {
            Item(T0.AddHours(2)),
            Item(T0.AddHours(1), snoozed: T0.AddHours(3)),   // pospuesta: cuenta la nueva hora
            Item(T0.AddMinutes(30)),
            Item(T0.AddMinutes(10), done: true),
            Item(T0.AddMinutes(-5)),                          // ya vencida: no es «siguiente»
        };
        Assert.Equal(T0.AddMinutes(30), TaskReminders.NextDue(items, T0));
        Assert.Equal(T0.AddHours(2), TaskReminders.NextDue(items, T0.AddMinutes(30)));
        Assert.Null(TaskReminders.NextDue(items, T0.AddHours(3)));
    }
}
