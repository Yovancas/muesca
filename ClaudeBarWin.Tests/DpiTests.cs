using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T11 (auditoría §2 P0 #1): escalado DPI centralizado. Las fuentes (en puntos) ya escalaban con el
/// DPI del Graphics, pero la geometría del panel era px fijos de 96 DPI → solapes al 125/150%.
/// <see cref="Dpi"/> proyecta los px de diseño al factor vigente; a factor 1.0 es IDENTIDAD exacta
/// (el render-test a 96 DPI queda pixel-perfect). El factor ambiente es [ThreadStatic]: estos tests
/// lo mutan solo en SU hilo (try/finally a 96) sin contaminar a los tests de layout en paralelo.
/// </summary>
public class DpiTests
{
    // ---------------- helper puro (sin ambiente) ----------------

    [Fact]
    public void FactorFor_96_dpi_is_identity()
    {
        Assert.Equal(1f, Dpi.FactorFor(96));
    }

    [Fact]
    public void FactorFor_scales_125_and_150_percent()
    {
        Assert.Equal(1.25f, Dpi.FactorFor(120));
        Assert.Equal(1.5f, Dpi.FactorFor(144));
        Assert.Equal(2f, Dpi.FactorFor(192));
    }

    [Fact]
    public void FactorFor_invalid_dpi_falls_back_to_identity()
    {
        Assert.Equal(1f, Dpi.FactorFor(0));
        Assert.Equal(1f, Dpi.FactorFor(-10));
    }

    [Fact]
    public void Scale_at_factor_1_is_exact_identity_for_the_layout_range()
    {
        // Garantía pixel-perfect del render-test a 96 DPI: factor 1.0 no mueve NI un px.
        for (int px = 0; px <= 400; px++)
            Assert.Equal(px, Dpi.Scale(px, 1f));
    }

    [Fact]
    public void Scale_rounds_half_away_from_zero()
    {
        // 11·1.5 = 16.5 → 17 (como el escalado de Windows; Math.Round a secas haría banker's → 16).
        Assert.Equal(17, Dpi.Scale(11, 1.5f));
        Assert.Equal(3, Dpi.Scale(2, 1.25f));   // 2.5 → 3
        Assert.Equal(33, Dpi.Scale(22, 1.5f));
        Assert.Equal(425, Dpi.Scale(340, 1.25f));
    }

    // ---------------- factor ambiente (ThreadStatic, default 1.0) ----------------

    [Fact]
    public void Apply_sets_the_ambient_factor_for_this_thread()
    {
        try
        {
            Dpi.Apply(144);
            Assert.Equal(1.5f, Dpi.Factor);
            Assert.Equal(36, Dpi.Scale(24)); // la sobrecarga ambiente usa el factor aplicado
        }
        finally { Dpi.Apply(96); }
        Assert.Equal(1f, Dpi.Factor);
        Assert.Equal(24, Dpi.Scale(24));
    }

    // ---------------- los rects/avances de layout escalan con el factor ----------------

    private static int RunQuotaBar(Graphics g)
    {
        using var fg = new SolidBrush(Theme.Dark.TextPrimary);
        using var dim = new SolidBrush(Theme.Dark.TextSecondary);
        var win = new UsageWindow(50, DateTimeOffset.UtcNow.AddHours(2));
        return QuotaBar.Draw(g, draw: false, "Session (5h)", win, pace: null, 16, 0, 300,
            new AppConfig(), Localization.Get("en"), Theme.Dark, Typography.Body, Typography.Caption, fg, dim);
    }

    [Fact]
    public void QuotaBar_row_advance_scales_with_dpi()
    {
        using var bmp = new Bitmap(400, 200);
        using var g = Graphics.FromImage(bmp);

        int at100 = RunQuotaBar(g);
        int at150;
        try { Dpi.Apply(144); at150 = RunQuotaBar(g); }
        finally { Dpi.Apply(96); }

        // Composición exacta de la barra: fila label/% (22) + barra (BarH 11 + 3 de aire) + reset (14).
        Assert.Equal(22 + 11 + 3 + 14, at100);
        Assert.Equal(Dpi.Scale(22, 1.5f) + Dpi.Scale(11, 1.5f) + 3 + Dpi.Scale(14, 1.5f), at150);
        Assert.True(at150 > at100, "a 150% la barra debe reservar más alto que a 96 DPI");
    }

    private static Rectangle RunHeaderGear(Graphics g)
    {
        Rectangle gear = Rectangle.Empty;
        var cfg = new AppConfig { ShowMascot = false, LiveSessionsEnabled = false, ShowHealth = true };
        DashboardHeader.Draw(g, draw: false, 16, 0, 308,
            snap: null, new LiveSessionsView(), cfg, Localization.Get("en"), Theme.Dark,
            MascotAnimator.StaticState, Mood.Neutral,
            Typography.Body, Typography.Caption, Typography.Mono, ref gear);
        return gear;
    }

    [Fact]
    public void Header_gear_button_scales_with_dpi()
    {
        using var bmp = new Bitmap(400, 400);
        using var g = Graphics.FromImage(bmp);

        var at100 = RunHeaderGear(g);
        Rectangle at150;
        try { Dpi.Apply(144); at150 = RunHeaderGear(g); }
        finally { Dpi.Apply(96); }

        Assert.Equal(24, at100.Width);
        Assert.Equal(Dpi.Scale(24, 1.5f), at150.Width);
        Assert.Equal(at150.Width, at150.Height); // sigue siendo cuadrado
    }

    [Fact]
    public void Toggle_pill_track_scales_with_dpi()
    {
        using var bmp = new Bitmap(400, 100);
        using var g = Graphics.FromImage(bmp);

        var at100 = DashboardSettingsView.TogglePill(g, draw: false, on: true, rightX: 300, y: 0, rowH: 40, Theme.Dark);
        Rectangle at150;
        try
        {
            Dpi.Apply(144);
            at150 = DashboardSettingsView.TogglePill(g, draw: false, on: true, rightX: 300, y: 0, rowH: 40, Theme.Dark);
        }
        finally { Dpi.Apply(96); }

        Assert.Equal(new Size(36, 20), at100.Size);
        Assert.Equal(new Size(Dpi.Scale(36, 1.5f), Dpi.Scale(20, 1.5f)), at150.Size);
    }

    [Fact]
    public void Settings_measure_equals_paint_at_150_percent()
    {
        // El invariante medir==pintar debe sobrevivir al escalado: ambas pasadas leen el MISMO factor
        // ambiente, así que el y devuelto coincide también a 150%.
        using var bmp = new Bitmap(400, 2000);
        using var g = Graphics.FromImage(bmp);
        var cfg = new AppConfig();
        var s = Localization.Get("en");
        var rects = new Dictionary<string, Rectangle>();

        try
        {
            Dpi.Apply(144);
            int measured = DashboardSettingsView.Draw(g, draw: false, 16, 0, 308, cfg, s, Theme.Dark,
                Typography.Body, Typography.Caption, rects, version: "0.0.0");
            int painted = DashboardSettingsView.Draw(g, draw: true, 16, 0, 308, cfg, s, Theme.Dark,
                Typography.Body, Typography.Caption, rects, version: "0.0.0");
            Assert.Equal(measured, painted);
        }
        finally { Dpi.Apply(96); }
    }
}
