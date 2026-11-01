using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.UI.Notch;
using ClaudeBarWin.UI.Onboarding;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Geometría del asistente (<see cref="OnboardingGeometry"/>): todo dentro de la forma que se pinta, nada se pisa, el clic
/// acierta el control que se pinta y Tab recorre lo que se puede usar; y su colocación (<see cref="OnboardingPlacement"/>):
/// cabe en 1366 × 768 a 100 % y escala con el DPI.
/// </summary>
public class OnboardingGeometryTests
{
    private static readonly Size Surface = new(1000, 600);

    private static RectangleF Pill(float s) =>
        OnboardingGeometry.PillRect(OnboardingGeometry.Width * s, OnboardingGeometry.Height * s, Surface.Width * s / 2f);

    public static IEnumerable<object[]> Cases()
    {
        foreach (var page in Enum.GetValues<OnboardingPage>())
            foreach (var s in new[] { 1f, 1.5f })
                foreach (var monitor in new[] { false, true })
                    yield return new object[] { page, s, monitor };
    }

    /// <summary>¿Cae el punto dentro de la forma (rectángulo con las esquinas de abajo redondeadas, como el notch)?</summary>
    private static bool InsideShape(RectangleF pill, float s, float x, float y)
    {
        if (x < pill.Left - 0.01f || x > pill.Right + 0.01f || y < pill.Top - 0.01f || y > pill.Bottom + 0.01f) return false;
        float rad = NotchGeometry.ClampRadius(pill, NotchLayout.CornerRadius(pill.Width, s));
        if (y <= pill.Bottom - rad) return true;
        float cx = x < pill.Left + rad ? pill.Left + rad : x > pill.Right - rad ? pill.Right - rad : x;
        float dx = x - cx, dy = y - (pill.Bottom - rad);
        return dx * dx + dy * dy <= rad * rad + 0.5f;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Every_control_is_inside_the_painted_shape(OnboardingPage page, float s, bool askMonitor)
    {
        var pill = Pill(s);
        var shape = new OnboardingShape(6, askMonitor, OnboardingOverlay.None);
        foreach (var (hit, r) in OnboardingGeometry.Controls(page, pill, s, shape))
        {
            Assert.False(r.IsEmpty, hit.ToString());
            foreach (var (x, y) in new[] { (r.Left, r.Top), (r.Right, r.Top), (r.Left, r.Bottom), (r.Right, r.Bottom) })
                Assert.True(InsideShape(pill, s, x, y), $"{page} {hit} {r} se sale de la forma");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Controls_do_not_overlap_and_the_click_hits_what_is_painted(OnboardingPage page, float s, bool askMonitor)
    {
        var pill = Pill(s);
        var shape = new OnboardingShape(5, askMonitor, OnboardingOverlay.None);
        var controls = OnboardingGeometry.Controls(page, pill, s, shape);
        for (int i = 0; i < controls.Count; i++)
        {
            for (int j = i + 1; j < controls.Count; j++)
            {
                var (a, ra) = controls[i];
                var (b, rb) = controls[j];
                // La flecha ‹ va dentro de su selector a propósito (gana en el clic).
                bool nested = b.Control is OnboardingControl.LanguagePrev or OnboardingControl.MonitorPrev
                    && a.Control is OnboardingControl.Language or OnboardingControl.Monitor;
                if (nested) continue;
                var inter = RectangleF.Intersect(ra, rb);
                Assert.True(inter.Width <= 0.01f || inter.Height <= 0.01f, $"{page}: {a} pisa {b}");
            }
            var (hit, r) = controls[i];
            var center = new Point((int)(r.Left + r.Width / 2f), (int)(r.Top + r.Height / 2f));
            Assert.Equal(hit, OnboardingGeometry.HitTest(center, page, pill, s, shape));
        }
    }

    [Fact]
    public void Outside_the_pill_and_in_empty_space_nothing_is_hit()
    {
        var pill = Pill(1f);
        var shape = new OnboardingShape(6, false, OnboardingOverlay.None);
        Assert.Equal(OnboardingHit.None, OnboardingGeometry.HitTest(new Point(2, 2), OnboardingPage.Profile, pill, 1f, shape));
        Assert.Equal(OnboardingHit.None, OnboardingGeometry.HitTest(
            new Point((int)pill.Left + 10, (int)pill.Top + 75), OnboardingPage.Profile, pill, 1f, shape));
    }

    [Fact]
    public void With_an_overlay_only_its_two_buttons_take_clicks()
    {
        var pill = Pill(1f);
        var shape = new OnboardingShape(6, true, OnboardingOverlay.ConfirmSkip);
        var controls = OnboardingGeometry.Controls(OnboardingPage.Settings, pill, 1f, shape);
        Assert.Equal(new[] { OnboardingControl.OverlaySecondary, OnboardingControl.OverlayPrimary }, controls.Select(c => c.Hit.Control));
        var next = OnboardingGeometry.Next(pill, 1f);
        Assert.Equal(OnboardingHit.None, OnboardingGeometry.HitTest(
            new Point((int)(next.Left + next.Width / 2), (int)(next.Top + next.Height / 2)), OnboardingPage.Settings, pill, 1f, shape));
        foreach (var (_, r) in controls) Assert.True(OnboardingGeometry.OverlayCard(pill, 1f).Contains(r));
    }

    [Fact]
    public void The_cycler_back_arrow_wins_inside_the_selector()
    {
        var pill = Pill(1f);
        var shape = new OnboardingShape(6, true, OnboardingOverlay.None);
        var control = OnboardingGeometry.SettingParts(pill, 1f, OnboardingControl.Monitor, true).Control;
        var (prev, nextArrow) = OnboardingGeometry.CyclerArrows(control);
        Assert.Equal(new OnboardingHit(OnboardingControl.MonitorPrev),
            OnboardingGeometry.HitTest(new Point((int)prev.Left + 4, (int)prev.Top + 4), OnboardingPage.Settings, pill, 1f, shape));
        Assert.Equal(new OnboardingHit(OnboardingControl.Monitor),
            OnboardingGeometry.HitTest(new Point((int)nextArrow.Left + 4, (int)nextArrow.Top + 4), OnboardingPage.Settings, pill, 1f, shape));
    }

    [Fact]
    public void Without_several_monitors_there_is_no_monitor_row()
    {
        var pill = Pill(1f);
        Assert.True(OnboardingGeometry.SettingParts(pill, 1f, OnboardingControl.Monitor, askMonitor: false).Control.IsEmpty);
        Assert.Equal(4, OnboardingGeometry.SettingRows(false).Count);
        var size = OnboardingGeometry.SettingParts(pill, 1f, OnboardingControl.Size, askMonitor: false).Control;
        var language = OnboardingGeometry.SettingParts(pill, 1f, OnboardingControl.Language, askMonitor: false).Control;
        Assert.Equal(OnboardingGeometry.SettingPitch, size.Top - language.Top, 3);
    }

    [Fact]
    public void Tab_goes_through_what_can_be_used_with_one_stop_per_segmented_control()
    {
        var shape = new OnboardingShape(6, true, OnboardingOverlay.None);
        var order = OnboardingGeometry.FocusOrder(OnboardingPage.Settings, shape, _ => true,
            c => c == OnboardingControl.Size ? 2 : 1);
        Assert.Equal(new[]
        {
            new OnboardingHit(OnboardingControl.Language), new OnboardingHit(OnboardingControl.Monitor),
            new OnboardingHit(OnboardingControl.Size, 2), new OnboardingHit(OnboardingControl.Open, 1),
            new OnboardingHit(OnboardingControl.Startup), new OnboardingHit(OnboardingControl.ResetSettings),
            new OnboardingHit(OnboardingControl.Back), new OnboardingHit(OnboardingControl.Next), new OnboardingHit(OnboardingControl.Skip),
        }, order);

        var features = OnboardingGeometry.FocusOrder(OnboardingPage.Features, shape,
            h => h.Control != OnboardingControl.Feature || h.Index != (int)SetupFeature.Lyrics, _ => 0);
        Assert.DoesNotContain(new OnboardingHit(OnboardingControl.Feature, (int)SetupFeature.Lyrics), features);
        Assert.Equal(new OnboardingHit(OnboardingControl.Feature, (int)SetupFeature.Notch), features[0]);

        var welcome = OnboardingGeometry.FocusOrder(OnboardingPage.Welcome, shape, _ => true, _ => 0);
        Assert.Equal(new[] { new OnboardingHit(OnboardingControl.UsageStats), new OnboardingHit(OnboardingControl.Next),
            new OnboardingHit(OnboardingControl.Skip) }, welcome);
    }

    [Fact]
    public void Focus_moves_both_ways_and_wraps_around()
    {
        var order = new[] { new OnboardingHit(OnboardingControl.Card, 0), new OnboardingHit(OnboardingControl.Card, 1),
            new OnboardingHit(OnboardingControl.Next) };
        Assert.Equal(order[0], OnboardingGeometry.MoveFocus(order, OnboardingHit.None, +1));
        Assert.Equal(order[2], OnboardingGeometry.MoveFocus(order, OnboardingHit.None, -1));
        Assert.Equal(order[1], OnboardingGeometry.MoveFocus(order, order[0], +1));
        Assert.Equal(order[0], OnboardingGeometry.MoveFocus(order, order[2], +1));
        Assert.Equal(order[2], OnboardingGeometry.MoveFocus(order, order[0], -1));
        Assert.True(OnboardingGeometry.SameControl(new(OnboardingControl.Size, 1), new(OnboardingControl.Size, 3)));
        Assert.False(OnboardingGeometry.SameControl(new(OnboardingControl.Card, 1), new(OnboardingControl.Card, 3)));
        Assert.Equal(OnboardingHit.None, OnboardingGeometry.MoveFocus(Array.Empty<OnboardingHit>(), order[0], 1));
    }

    [Fact]
    public void Dots_are_centered_on_the_footer_one_pitch_apart()
    {
        var pill = Pill(1f);
        var a = OnboardingGeometry.DotCenter(pill, 1f, 5, 0);
        var c = OnboardingGeometry.DotCenter(pill, 1f, 5, 2);
        Assert.Equal(pill.Left + pill.Width / 2f, c.X, 3);
        Assert.Equal(2 * NotchGeometry.PageDotPitch, c.X - a.X, 3);
        var back = OnboardingGeometry.Back(pill, 1f);
        Assert.Equal(back.Top + back.Height / 2f, a.Y, 3);
    }

    // ---- colocación

    [Fact]
    public void It_fits_a_1366x768_screen_at_100_percent_with_the_taskbar()
    {
        var monitor = new Rectangle(0, 0, 1366, 768);
        var work = new Rectangle(0, 0, 1366, 728);
        float s = OnboardingPlacement.Scale(96, monitor, work);
        Assert.Equal(1f, s);
        var surface = OnboardingPlacement.Surface(s);
        Assert.True(surface.Width <= monitor.Width);
        Assert.True(surface.Height <= work.Height, $"{surface.Height} > {work.Height}");
        var topLeft = OnboardingPlacement.TopLeft(monitor, surface);
        Assert.Equal(0, topLeft.Y);
        Assert.Equal((1366 - surface.Width) / 2, topLeft.X);
    }

    [Theory]
    [InlineData(96, 1920, 1080, 1040, 1f)]
    [InlineData(120, 1366, 768, 728, 1.25f)]
    [InlineData(144, 2560, 1440, 1392, 1.5f)]
    [InlineData(192, 3840, 2160, 2064, 2f)]
    [InlineData(144, 1280, 720, 680, 1.46f)]   // 150 % en una pantalla de 720: lo justo para caber
    public void It_scales_with_dpi_unless_it_would_not_fit(int dpi, int w, int h, int workBottom, float expected)
    {
        float s = OnboardingPlacement.Scale(dpi, new Rectangle(0, 0, w, h), new Rectangle(0, 0, w, workBottom));
        Assert.Equal(expected, s, 3);
        Assert.True(OnboardingPlacement.Surface(s).Height <= workBottom);
        Assert.True(OnboardingPlacement.Surface(s).Width <= w);
    }

    [Fact]
    public void A_monitor_below_another_keeps_its_own_origin()
    {
        var monitor = new Rectangle(1920, -1080, 1920, 1080);
        float s = OnboardingPlacement.Scale(96, monitor, new Rectangle(1920, -1080, 1920, 1040));
        Assert.Equal(1f, s);
        var tl = OnboardingPlacement.TopLeft(monitor, OnboardingPlacement.Surface(s));
        Assert.Equal(-1080, tl.Y);
        Assert.True(tl.X > 1920);
    }
}
