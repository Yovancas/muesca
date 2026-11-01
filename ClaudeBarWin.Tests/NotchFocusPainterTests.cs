using System.Drawing.Imaging;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Pintado del pomodoro: la página Foco en cada estado (corriendo, en pausa, con la oferta, sin el Pro, con el ratón encima, en
/// pleno bote, con el aviso integrado), el mini anillo del notch cerrado (reposo, vista previa, Trabajando, Mínimo) y el aviso
/// del fin de fase, a cada escala y dentro de la pastilla. El arco es lo que QUEDA; el chip «Pro» solo sin el Pro; la raya de
/// Mínimo se llena con lo que queda salvo con la cuota en aviso.
/// </summary>
public class NotchFocusPainterTests
{
    private static readonly Strings Es = Localization.Get("es");
    private static readonly FocusTaskInfo Task = new("Escribir el informe", 0, 0);

    private static Bitmap Paint(NotchFrame f, out Size surface, Strings? s = null)
    {
        surface = NotchPlacement.Surface(f.Scale);
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var painter = new NotchContentPainter(s ?? Es);
        painter.Paint(g, surface, f);
        return bmp;
    }

    public static IEnumerable<object[]> Frames()
    {
        foreach (var s in new[] { 1f, 1.5f, 2f })
        {
            yield return new object[] { "pagina-corriendo", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Running(), Es, task: Task)) };
            yield return new object[] { "pagina-pausa", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Paused(), Es)) };
            yield return new object[] { "pagina-oferta", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Finished(), Es, pop: 0.85f)) };
            yield return new object[] { "pagina-sin-pro", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Running(), Es, locked: true)) };
            yield return new object[] { "pagina-hover", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Running(), Es,
                hover: new FocusHit(FocusControl.Skip), hoverIntensity: 1f, pressed: new FocusHit(FocusControl.Primary))) };
            yield return new object[] { "pagina-aviso", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Running(), Es), alert: true) };
            yield return new object[] { "pagina-cargando", FocusFixtures.Page(s, null) };
            var face = FocusFixtures.View(FocusFixtures.Running(), Es, face: true);
            yield return new object[] { "reposo", FocusFixtures.Closed(s, NotchMode.Idle, face) };
            yield return new object[] { "reposo-pausa", FocusFixtures.Closed(s, NotchMode.Idle, FocusFixtures.View(FocusFixtures.Paused(), Es, face: true)) };
            yield return new object[] { "reposo-ultimo-minuto", FocusFixtures.Closed(s, NotchMode.Idle,
                FocusFixtures.View(FocusFixtures.LastMinute(), Es, face: true)) };
            yield return new object[] { "vista-previa", FocusFixtures.Closed(s, NotchMode.Peek, face) };
            yield return new object[] { "trabajando", FocusFixtures.Closed(s, NotchMode.Working, face, new[] { FocusFixtures.Working() }) };
            yield return new object[] { "aviso-armandose", FocusFixtures.Alert(s, FocusFixtures.View(FocusFixtures.Finished(), Es), arm: 0.4f) };
            yield return new object[] { "aviso-armado", FocusFixtures.Alert(s, FocusFixtures.View(FocusFixtures.Finished(), Es), arm: 1f,
                held: ClaudeBarWin.Services.State.PermissionVerdict.Allow) };
            yield return new object[] { "aviso-solo-lectura", FocusFixtures.Alert(s, null, readOnly: true) };
        }
    }

    [Theory]
    [MemberData(nameof(Frames))]
    public void Every_state_paints_inside_the_pill(string name, NotchFrame f)
    {
        using var bmp = Paint(f, out var surface);
        var pill = NotchGeometry.PillRect(f.Width, f.Height, surface);
        float ears = NotchMotion.EarSize(f.Width, f.Scale) + 2f;
        int painted = 0;
        for (int y = 0; y < bmp.Height; y += 2)
            for (int x = 0; x < bmp.Width; x += 2)
            {
                if (bmp.GetPixel(x, y).A == 0) continue;
                painted++;
                Assert.True(x >= pill.Left - ears && x <= pill.Right + ears && y <= pill.Bottom + 1, $"{name}: ({x},{y}) fuera de {pill}");
            }
        Assert.True(painted > 100, name);
    }

    [Fact]
    public void The_page_fits_the_expanded_cap_with_the_alert_and_the_dots()
    {
        var size = NotchLayout.PageSize(NotchPage.Focus, new PageRows(1, 0, 0), withAlert: true, withDots: true, 1f);
        Assert.Equal(NotchLayout.ExpandedWidth, size.Width);
        Assert.True(size.Height <= NotchLayout.ExpandedMaxHeight + 0.01f);
        Assert.Equal(NotchLayout.ExpandedPadTop + FocusLayout.ContentHeight + NotchLayout.ExpandedPadBottom + NotchLayout.PageDotsRow,
            NotchLayout.PageSize(NotchPage.Focus, new PageRows(1, 0, 0), withAlert: false, withDots: true, 1f).Height, 3);
        // Cabe en una pantalla de 1366 × 768 a escala 1 (y la superficie del notch no crece por ella).
        Assert.True(NotchPlacement.Surface(1f).Width <= 1366 && NotchPlacement.Surface(1f).Height <= 768);
    }

    private static bool Accentish(Color c) => BrandPixels.IsAccent(c);

    /// <summary>El arco es lo que QUEDA: cubre las 12 y la derecha, y lo ya pasado (justo antes de las 12) queda en la pista.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void The_big_ring_shows_what_is_left_in_the_phase_color(float s)
    {
        var f = FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Running(), Es));   // 30 % del foco pasado
        using var bmp = Paint(f, out var surface);
        var pill = NotchGeometry.PillRect(f.Width, f.Height, surface);
        var ring = FocusLayout.Compute(NotchContentPainter.PageInner(pill, s, false, true), s).Ring;
        float rad = ring.Width / 2f - FocusLayout.RingStroke * s / 2f, cx = ring.Left + ring.Width / 2f, cy = ring.Top + ring.Height / 2f;
        Color At(double deg) => bmp.GetPixel((int)Math.Round(cx + rad * Math.Cos(deg * Math.PI / 180)),
            (int)Math.Round(cy + rad * Math.Sin(deg * Math.PI / 180)));
        Assert.True(Accentish(At(0)), $"a las 3: {At(0)}");      // queda
        Assert.True(Accentish(At(90)), $"a las 6: {At(90)}");    // queda
        Assert.False(Accentish(At(230)), $"a las 10: {At(230)}"); // ya pasó: pista
    }

    [Fact]
    public void The_pro_chip_shows_only_without_pro()
    {
        float s = 2f;
        int AccentInChips(bool locked)
        {
            var f = FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Running(), Es, locked: locked, task: Task));
            using var bmp = Paint(f, out var surface);
            var pill = NotchGeometry.PillRect(f.Width, f.Height, surface);
            var r = FocusLayout.Compute(NotchContentPainter.PageInner(pill, s, false, true), s);
            int n = 0;
            foreach (var chip in new[] { r.Chip(r.Task), r.Chip(r.Stats) })
                for (int y = (int)chip.Top; y < (int)chip.Bottom; y++)
                    for (int x = (int)chip.Left; x < (int)chip.Right; x++)
                        if (Accentish(bmp.GetPixel(x, y))) n++;
            return n;
        }
        Assert.True(AccentInChips(locked: true) > 20);
        Assert.Equal(0, AccentInChips(locked: false));
    }

    [Fact]
    public void The_minimal_line_fills_with_what_is_left_unless_the_quota_warns()
    {
        float s = 2f;
        var face = FocusFixtures.View(FocusFixtures.Running(), Es, face: true);
        var f = FocusFixtures.Closed(s, NotchMode.Minimal, face);
        using (var bmp = Paint(f, out var surface))
        {
            var pill = NotchGeometry.PillRect(f.Width, f.Height, surface);
            int y = (int)(pill.Top + pill.Height / 2f);
            Assert.True(Accentish(bmp.GetPixel((int)(pill.Left + pill.Width * 0.2f), y)), "lo que queda, en el color de la fase");
            Assert.False(Accentish(bmp.GetPixel((int)(pill.Left + pill.Width * 0.92f), y)), "lo ya pasado, como siempre");
        }
        var warned = f with { Quota = new RealUsage { FiveHour = new UsageWindow(97, null) } };
        using (var bmp = Paint(warned, out var surface))
        {
            var pill = NotchGeometry.PillRect(warned.Width, warned.Height, surface);
            Assert.False(Accentish(bmp.GetPixel((int)(pill.Left + pill.Width * 0.2f), (int)(pill.Top + pill.Height / 2f))),
                "con la cuota en aviso, manda su color");
        }
    }

    [Fact]
    public void The_alert_paints_its_wide_button_where_the_click_lands()
    {
        float s = 1.5f;
        var f = FocusFixtures.Alert(s, FocusFixtures.View(FocusFixtures.Finished(), Es), arm: 1f);
        using var bmp = Paint(f, out var surface);
        var pill = NotchGeometry.PillRect(f.Width, f.Height, surface);
        var button = NotchGeometry.FocusAlertButton(pill, s);
        Assert.False(button.IsEmpty);
        var (deny, allow) = NotchGeometry.PermissionButtons(pill, s);
        Assert.Equal(Rectangle.FromLTRB(deny.Left, deny.Top, allow.Right, allow.Bottom), button);
        // El centro del botón (entre los dos de siempre) también es botón: relleno, no el negro de la pastilla.
        var mid = bmp.GetPixel(button.Left + button.Width / 2, button.Top + 2);
        Assert.True(mid.R > 20 || mid.G > 20, $"centro del botón sin pintar: {mid}");
    }

    [Theory]
    [InlineData("es")]
    [InlineData("de")]
    [InlineData("ja")]
    [InlineData("zh-Hant")]
    public void Every_language_paints_the_page_and_the_alert(string lang)
    {
        var s = Localization.Get(lang);
        using var page = Paint(FocusFixtures.Page(1.25f, FocusFixtures.View(FocusFixtures.Finished(), s, task: Task)), out _, s);
        using var alert = Paint(FocusFixtures.Alert(1.25f, FocusFixtures.View(FocusFixtures.Finished(), s)), out _, s);
        using var idle = Paint(FocusFixtures.Closed(1.25f, NotchMode.Idle, FocusFixtures.View(FocusFixtures.Running(), s, face: true)), out _, s);
    }

    private static int Reddish(Bitmap bmp, RectangleF area)
    {
        int n = 0;
        for (int y = (int)area.Top; y < (int)area.Bottom; y++)
            for (int x = (int)area.Left; x < (int)area.Right; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.A > 0 && c.R > 150 && c.R - c.G > 100 && c.R - c.B > 100) n++;
            }
        return n;
    }

    /// <summary>Reposo con el pomodoro y la cuota de 5 h en aviso (desde el primer umbral configurado): el anillo de 5 h sale
    /// junto al mini anillo (la cuota alta no se esconde). Por debajo del umbral, solo el mini anillo.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(2f)]
    public void The_idle_mini_ring_keeps_the_5h_ring_when_the_quota_warns(float s)
    {
        var face = FocusFixtures.View(FocusFixtures.Paused(), Es, face: true);
        var calm = FocusFixtures.Closed(s, NotchMode.Idle, face);
        var size = FocusFace.IdleSize(s, quota: true);
        var hot = calm with { Width = size.Width, Height = size.Height, Quota = new RealUsage { FiveHour = new UsageWindow(97, null) } };
        Assert.True(size.Width > FocusFace.IdleSize(s).Width);
        using (var bmp = Paint(hot, out var surface))
        {
            var pill = NotchGeometry.PillRect(hot.Width, hot.Height, surface);
            Assert.True(Reddish(bmp, pill) > 10 * s, "el anillo de 5 h, en su color crítico");
        }
        // Umbral configurado más alto que el %: no hay aviso, solo el mini anillo.
        using (var bmp = Paint(hot with { QuotaAlertPct = 99 }, out var surface))
            Assert.Equal(0, Reddish(bmp, NotchGeometry.PillRect(hot.Width, hot.Height, surface)));
    }

    [Fact]
    public void The_minimal_line_yields_to_the_quota_from_the_configured_threshold()
    {
        float s = 2f;
        var face = FocusFixtures.View(FocusFixtures.Running(), Es, face: true);
        var f = FocusFixtures.Closed(s, NotchMode.Minimal, face) with
        {
            Quota = new RealUsage { FiveHour = new UsageWindow(70, null) }, QuotaAlertPct = 60,
        };
        using var bmp = Paint(f, out var surface);
        var pill = NotchGeometry.PillRect(f.Width, f.Height, surface);
        Assert.False(Accentish(bmp.GetPixel((int)(pill.Left + pill.Width * 0.2f), (int)(pill.Top + pill.Height / 2f))),
            "con la cuota sobre el umbral configurado (60 %), manda la cuota");
        using var calm = Paint(f with { QuotaAlertPct = 80 }, out _);
        Assert.True(Accentish(calm.GetPixel((int)(pill.Left + pill.Width * 0.2f), (int)(pill.Top + pill.Height / 2f))),
            "por debajo del umbral, la raya del pomodoro");
    }
}