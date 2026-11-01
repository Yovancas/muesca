using System.Drawing;
using System.Drawing.Imaging;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Pintado de la página Tareas y del aviso de recordatorio (F5 · B5): todo dentro de la pastilla, en cada estado y escala,
/// y en el sitio que dice <see cref="TasksLayout"/> (la misma cuenta que usa la ventana para el clic).
/// </summary>
public class NotchTasksPainterTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 10, 29, 10, 0, 0);
    private static readonly NotchPage[] Four = { NotchPage.Claude, NotchPage.Music, NotchPage.Tasks, NotchPage.Shelf };
    private static readonly RealUsage Quota = new() { FiveHour = new UsageWindow(42, null), SevenDay = new UsageWindow(10, null) };

    private static NotchTask Task(string title, DateTime? at = null, bool done = false, int color = 0) =>
        new(Guid.NewGuid(), title, at, null, done, done ? Now : null, color);

    private static readonly NotchTask[] Items =
    {
        Task("Llamar a Fran por la reserva del sábado", Now.AddMinutes(-5), color: 4),   // vencida
        Task("Comprar pan", Now.AddHours(3), color: 0),
        Task("Mañana temprano", Now.AddDays(1).Date.AddHours(9), color: 1),
        Task("Sin hora", color: 2),
        Task("Hecha ya", Now.AddHours(-1), done: true, color: 3),
        Task("Otra", color: 5),
        Task("Y otra más", color: 0),
    };

    private static (Bitmap Bmp, RectangleF Pill) PaintTasks(TasksView? tasks, float scale = 1f, bool alert = false)
    {
        var view = new NotchPagesView(Four, NotchPage.Tasks, null, 1f, 0f, 0f, 2);
        int rows = (tasks?.Items.Count ?? 0) + (tasks?.Draft is not null ? 1 : 0);
        var size = NotchLayout.PageSize(NotchPage.Tasks, new PageRows(0, rows, 0), alert, true, scale);
        var surface = new Size((int)Math.Ceiling(700 * scale), (int)Math.Ceiling(200 * scale));
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("es"));
        NotchAlert? a = alert ? new(NotchAlertKind.Reminder, TaskAlerts.SessionId(Guid.NewGuid()), "Pan", null, T0, null) : null;
        painter.Paint(g, surface, new NotchFrame(size.Width, size.Height, scale, NotchMode.Expanded, a,
            Array.Empty<SessionState>(), Quota, 0f, 1f, Pages: view, Tasks: tasks));
        return (bmp, NotchGeometry.PillRect(size.Width, size.Height, surface));
    }

    private static TasksView View(int n, TaskDraftView? draft = null, int offset = 0, TaskHit hover = default,
        float intensity = 0f, TaskHit pressed = default) =>
        new(Items.Take(n).ToArray(), Now, true, draft, offset, hover, intensity, pressed);

    private static TaskDraftView Draft(string title, TaskQuick quick = TaskQuick.None, string digits = "",
        TaskField field = TaskField.Title)
    {
        var d = new TaskDraft(1);
        foreach (var c in title) d.OnChar(c);
        if (quick != TaskQuick.None) d.Choose(quick);
        if (field == TaskField.Time && d.Field != TaskField.Time) d.OnChar('\t');
        foreach (var c in digits) d.OnChar(c);
        return d.View();
    }

    public static IEnumerable<object[]> TaskViews()
    {
        foreach (var s in new[] { 1f, 1.5f })
        {
            yield return new object[] { null!, s, false };
            yield return new object[] { View(0), s, false };
            yield return new object[] { View(3, hover: new TaskHit(TaskControl.Add), intensity: 1f), s, true };
            yield return new object[] { View(7, offset: 2, hover: new TaskHit(TaskControl.Delete, 3), intensity: 0.6f,
                pressed: new TaskHit(TaskControl.Check, 4)), s, false };
            yield return new object[] { View(0, Draft("")), s, false };
            yield return new object[] { View(2, Draft(new string('m', 80), TaskQuick.Tomorrow9),
                hover: new TaskHit(TaskControl.Quick, (int)TaskQuick.In1Hour), intensity: 1f,
                pressed: new TaskHit(TaskControl.Save)), s, true };
            yield return new object[] { View(5, Draft("Cita", TaskQuick.Custom, "2599")), s, false };
            yield return new object[] { View(1, Draft("Cita", TaskQuick.Custom, "", TaskField.Time)), s, false };
        }
    }

    [Theory]
    [MemberData(nameof(TaskViews))]
    public void Tasks_page_paints_every_state_inside_the_pill(TasksView? tasks, float scale, bool alert)
    {
        var (bmp, pill) = PaintTasks(tasks, scale, alert);
        using (bmp)
        {
            Assert.Equal(255, bmp.GetPixel(bmp.Width / 2, 0).A);
            Assert.Equal(0, bmp.GetPixel(2, bmp.Height - 2).A);
            Assert.Equal(0, bmp.GetPixel(bmp.Width / 2, Math.Min(bmp.Height - 1, (int)pill.Bottom + 6)).A);
            Assert.Equal(0, bmp.GetPixel((int)pill.Right + 14, (int)pill.Top + 20).A);
        }
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void Each_row_shows_its_colour_where_the_layout_puts_the_check(float scale)
    {
        var (bmp, pill) = PaintTasks(View(2), scale);
        using (bmp)
        {
            var inner = NotchContentPainter.PageInner(pill, scale, withAlert: false, withDots: true);
            var r = TasksLayout.Compute(inner, scale, editing: false);
            // El aro izquierdo del círculo de la fila 0 (rojo) y de la fila 1 (azul).
            var c0 = r.Check(0);
            var c1 = r.Check(1);
            var red = bmp.GetPixel((int)(c0.Left + 0.75f * scale), (int)(c0.Top + c0.Height / 2));
            var blue = bmp.GetPixel((int)(c1.Left + 0.75f * scale), (int)(c1.Top + c1.Height / 2));
            Assert.True(red.R > red.B + 40, $"rojo {red}");
            Assert.True(blue.B > blue.R + 40, $"azul {blue}");
        }
    }

    [Fact]
    public void The_editor_lights_its_field_and_shows_the_quick_times_row()
    {
        var (edit, pill) = PaintTasks(View(0, Draft("Hola")));
        var (plain, _) = PaintTasks(View(0));
        using (edit)
        using (plain)
        {
            var inner = NotchContentPainter.PageInner(pill, 1f, withAlert: false, withDots: true);
            var e = TasksLayout.Compute(inner, 1f, editing: true);
            // Esquina derecha del campo (sin texto encima): relleno translúcido sobre negro.
            int field = edit.GetPixel((int)(e.Field.Right - 8), (int)(e.Field.Top + e.Field.Height / 2)).R;
            Assert.True(field > 12, $"campo {field}");
            // Primer chip («+15 min»): su cápsula se ve editando; sin editar ahí no hay nada (lista vacía).
            var chip = e.Chip(0);
            int x = (int)(chip.Left + 3), y = (int)(chip.Top + chip.Height / 2);
            Assert.True(edit.GetPixel(x, y).R > plain.GetPixel(x, y).R + 8, $"chip {edit.GetPixel(x, y)} vs {plain.GetPixel(x, y)}");
        }
    }

    private static int Bright(Bitmap b, RectangleF r)
    {
        int max = 0;
        for (int x = (int)r.Left; x < (int)Math.Min(r.Left + 50, r.Right); x++)
            for (int y = (int)r.Top; y < (int)r.Bottom; y++)
                max = Math.Max(max, b.GetPixel(x, y).G);
        return max;
    }

    // ---------------------------------------------------------------- aviso de recordatorio

    private static Bitmap PaintReminder(float scale, float arm, PermissionVerdict held, out RectangleF pill)
    {
        var size = NotchLayout.TargetSize(NotchMode.Reminder, scale, 0f);
        var surface = new Size((int)Math.Ceiling(700 * scale), (int)Math.Ceiling(200 * scale));
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(Localization.Get("es"));
        var a = new NotchAlert(NotchAlertKind.Reminder, TaskAlerts.SessionId(Guid.NewGuid()), "Comprar pan", null, T0, null);
        painter.Paint(g, surface, new NotchFrame(size.Width, size.Height, scale, NotchMode.Reminder, a,
            Array.Empty<SessionState>(), Quota, 0f, 1f, PermissionArm: arm, PermissionHeld: held));
        pill = NotchGeometry.PillRect(size.Width, size.Height, surface);
        return bmp;
    }

    [Theory]
    [InlineData(1f, 0f, PermissionVerdict.None)]
    [InlineData(1f, 1f, PermissionVerdict.Allow)]
    [InlineData(1.5f, 0.5f, PermissionVerdict.None)]
    [InlineData(1.5f, 1f, PermissionVerdict.Deny)]
    public void Reminder_paints_inside_the_pill_with_its_buttons(float scale, float arm, PermissionVerdict held)
    {
        using var bmp = PaintReminder(scale, arm, held, out var pill);
        Assert.Equal(0, bmp.GetPixel(2, bmp.Height - 2).A);
        Assert.Equal(0, bmp.GetPixel(bmp.Width / 2, (int)pill.Bottom + 6).A);
        var (snooze, done) = NotchGeometry.PermissionButtons(pill, scale);
        Assert.False(done.IsEmpty);
        // Esquina interior del botón «Hecha» (sin texto): el fondo del botón se ve sobre el negro.
        int px = bmp.GetPixel(done.Left + (int)(4 * scale), done.Top + done.Height / 2).R;
        Assert.True(px > 8, $"botón {px}");
        Assert.True(snooze.Right < done.Left);
    }

    [Fact]
    public void Armed_reminder_buttons_are_brighter_than_unarmed_ones()
    {
        using var off = PaintReminder(1f, 0f, PermissionVerdict.None, out var pill);
        using var on = PaintReminder(1f, 1f, PermissionVerdict.None, out _);
        var (_, done) = NotchGeometry.PermissionButtons(pill, 1f);
        int x = done.Left + 4, y = done.Top + done.Height / 2;
        Assert.True(on.GetPixel(x, y).R > off.GetPixel(x, y).R);
    }

    [Fact]
    public void Expanded_integrates_the_reminder_as_a_line()
    {
        var (bmp, pill) = PaintTasks(View(2), 1f, alert: true);
        using (bmp)
        {
            // Línea del aviso arriba (antes de la cabecera de la página): algo de texto en color de acento.
            var line = new RectangleF(pill.Left + 18, pill.Top + NotchLayout.ExpandedPadTop, 120, NotchLayout.ExpandedLine);
            Assert.True(Bright(bmp, line) > 60);
        }
    }
}
