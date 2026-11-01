using System.Drawing.Imaging;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests.Licensing;

/// <summary>
/// Las funciones Pro enganchadas a <see cref="ProGate"/>: lo que recibe el notch con cada estado (<see cref="NotchProGating"/>),
/// las páginas con candado (siguen en el carrusel, sin contenido ni servicios), el pipe de permisos y la página con candado
/// pintada con el chip «Pro».
/// </summary>
public class NotchProGatingTests
{
    private static readonly NotchOptions All = new(false, false, true, true, 0, @"\\.\DISPLAY2", false,
        Music: true, Lyrics: true, ShelfClearOnStart: true, Tasks: true, CompactLyrics: true);

    [Fact]
    public void Free_turns_off_every_paid_service_and_locks_its_pages()
    {
        var (o, mode) = NotchProGating.Apply(All, NotchMonitorMode.AllMonitors, ProState.Free);
        Assert.False(o.Music);
        Assert.False(o.Lyrics);           // ni lrclib.net
        Assert.False(o.CompactLyrics);
        Assert.False(o.Tasks);            // ni avisos ni temporizadores
        Assert.False(o.ShelfClearOnStart);   // los datos nunca se borran por no tener el Pro
        // La página Uso sigue (es gratis), con sus gráficas quietas; la página Foco también, con lo avanzado del pomodoro con el chip.
        Assert.Equal(NotchProLocks.Music | NotchProLocks.Tasks | NotchProLocks.Shelf | NotchProLocks.UsageCharts
            | NotchProLocks.FocusPro, o.Locked);
        Assert.Equal(NotchMonitorMode.Fixed, mode);   // un solo notch…
        Assert.Equal("", o.MonitorDeviceName);       // …en el principal
        // Lo gratis sigue igual.
        Assert.Equal(All.HideInFullscreen, o.HideInFullscreen);
        Assert.Equal(All.Sound, o.Sound);
        Assert.Equal(All.Clock, o.Clock);
        Assert.Equal(All.Mascot, o.Mascot);
    }

    [Theory]
    [InlineData(ProState.Trial)]
    [InlineData(ProState.Pro)]
    public void Trial_and_pro_get_exactly_what_the_config_asks(ProState state)
    {
        foreach (var requested in Enum.GetValues<NotchMonitorMode>())
        {
            var (o, mode) = NotchProGating.Apply(All, requested, state);
            Assert.Equal(All, o);
            Assert.Equal(requested, mode);
        }
    }

    [Theory]
    [InlineData(NotchMonitorMode.Fixed)]
    [InlineData(NotchMonitorMode.FollowActive)]
    [InlineData(NotchMonitorMode.AllMonitors)]
    public void Free_is_always_one_notch_on_the_primary_monitor(NotchMonitorMode requested)
    {
        var (o, mode) = NotchProGating.Apply(All, requested, ProState.Free);
        Assert.Equal(NotchMonitorMode.Fixed, mode);
        Assert.Equal("", o.MonitorDeviceName);
    }

    /// <summary>Dónde estará el notch (lo usa también el asistente para aterrizar): sin el Pro, el principal aunque la
    /// prueba dejara guardado otro monitor u otro modo; con él, lo guardado.</summary>
    [Theory]
    [InlineData(NotchMonitorMode.Fixed)]
    [InlineData(NotchMonitorMode.FollowActive)]
    [InlineData(NotchMonitorMode.AllMonitors)]
    public void The_placement_is_the_primary_without_pro_and_the_saved_one_with_it(NotchMonitorMode saved)
    {
        Assert.Equal((NotchMonitorMode.Fixed, ""), NotchProGating.Placement(saved, @"\\.\DISPLAY2", ProState.Free));
        Assert.Equal((saved, @"\\.\DISPLAY2"), NotchProGating.Placement(saved, @"\\.\DISPLAY2", ProState.Trial));
        Assert.Equal((saved, @"\\.\DISPLAY2"), NotchProGating.Placement(saved, @"\\.\DISPLAY2", ProState.Pro));
        Assert.Equal((saved, ""), NotchProGating.Placement(saved, null, ProState.Pro));
    }

    [Fact]
    public void Only_what_is_turned_on_gets_a_lock()
    {
        var off = All with { Music = false, Tasks = false, Pages = new[] { NotchPage.Claude, NotchPage.Music } };
        var (o, _) = NotchProGating.Apply(off, NotchMonitorMode.Fixed, ProState.Free);
        // Ninguna página: solo las gráficas de Uso y lo avanzado del pomodoro, siempre en sus páginas.
        Assert.Equal(NotchProLocks.UsageCharts | NotchProLocks.FocusPro, o.Locked);
        // Sin Música ni Tareas en Ajustes y sin la Bandeja en la lista: solo Claude, como siempre.
        Assert.Equal(new[] { NotchPage.Claude }, NotchPages.Effective(o.Pages, o.Music, o.Tasks, o.Locked));
    }

    [Fact]
    public void Locked_pages_stay_in_the_carousel_in_their_order()
    {
        var (o, _) = NotchProGating.Apply(All, NotchMonitorMode.Fixed, ProState.Free);
        Assert.Equal(NotchPages.Default, NotchPages.Effective(o.Pages, o.Music, o.Tasks, o.Locked));
        Assert.Equal(new[] { NotchPage.Claude, NotchPage.Usage, NotchPage.Focus, NotchPage.Shelf },
            NotchPages.Effective(null, music: false, tasks: false, NotchProLocks.None));
        Assert.Equal(new[] { NotchPage.Claude, NotchPage.Usage, NotchPage.Music, NotchPage.Focus, NotchPage.Shelf },
            NotchPages.Effective(null, music: false, tasks: false, NotchProLocks.Music));
        Assert.Equal(NotchPages.Effective(null, music: false, tasks: false, NotchProLocks.None),
            NotchPages.Effective(null, music: false, tasks: false, NotchProLocks.UsageCharts));   // no es una página
        Assert.Equal(NotchPages.Effective(null, music: false, tasks: false, NotchProLocks.None),
            NotchPages.Effective(null, music: false, tasks: false, NotchProLocks.FocusPro));      // tampoco
        Assert.False(NotchProLock.Has(NotchProLocks.FocusPro, NotchPage.Focus));   // la página Foco es gratis
        Assert.True(NotchProLock.Has(o.Locked, NotchPage.Music));
        Assert.True(NotchProLock.Has(o.Locked, NotchPage.Tasks));
        Assert.True(NotchProLock.Has(o.Locked, NotchPage.Shelf));
        Assert.False(NotchProLock.Has(o.Locked, NotchPage.Claude));   // la página de Claude es gratis siempre
        Assert.False(NotchProLock.Has(NotchProLocks.None, NotchPage.Music));
    }

    [Fact]
    public void Free_keeps_the_basic_pomodoro_with_the_default_durations_without_touching_the_config()
    {
        var custom = All with { Focus = new FocusSettings(new FocusDurations(50, 10, 30, 3), Sound: false) };
        var (free, _) = NotchProGating.Apply(custom, NotchMonitorMode.Fixed, ProState.Free);
        Assert.Equal(FocusDurations.Default, free.FocusOrDefault.Durations);
        Assert.False(free.FocusOrDefault.Sound);   // el sonido no es Pro
        Assert.True((free.Locked & NotchProLocks.FocusPro) != 0);
        var (pro, _) = NotchProGating.Apply(custom, NotchMonitorMode.Fixed, ProState.Pro);
        Assert.Equal(custom.Focus, pro.Focus);
        Assert.True((pro.Locked & NotchProLocks.FocusPro) == 0);
        var (trial, _) = NotchProGating.Apply(custom, NotchMonitorMode.Fixed, ProState.Trial);
        Assert.Equal(new FocusDurations(50, 10, 30, 3), trial.FocusOrDefault.Durations);
    }

    [Fact]
    public void The_plan_carries_the_pomodoro_settings_from_the_config()
    {
        var cfg = new AppConfig
        {
            NotchEnabled = true, NotchFocusMinutes = 45, NotchShortBreakMinutes = 10, NotchLongBreakMinutes = 30,
            NotchLongBreakEvery = 3, NotchFocusSound = false,
        };
        var pro = TrayAppContext.PlanNotch(cfg, false, false, ProState.Pro).Options.FocusOrDefault;
        Assert.Equal(new FocusSettings(new FocusDurations(45, 10, 30, 3), Sound: false), pro);
        var free = TrayAppContext.PlanNotch(cfg, false, false, ProState.Free).Options.FocusOrDefault;
        Assert.Equal(new FocusSettings(FocusDurations.Default, Sound: false), free);
    }

    [Fact]
    public void The_permission_pipe_needs_pro_besides_the_usual_conditions()
    {
        var cfg = new AppConfig { NotchEnabled = true, NotchPermissions = true, LiveSessionsEnabled = true };
        Assert.False(TrayAppContext.PermissionPipeWanted(cfg, false, false, ProState.Free));
        Assert.True(TrayAppContext.PermissionPipeWanted(cfg, false, false, ProState.Trial));
        Assert.True(TrayAppContext.PermissionPipeWanted(cfg, false, false, ProState.Pro));
        Assert.False(TrayAppContext.PermissionPipeWanted(cfg, false, true, ProState.Pro));    // asistente a la vista
        Assert.False(TrayAppContext.PermissionPipeWanted(cfg, true, false, ProState.Pro));    // cerrando
        Assert.False(TrayAppContext.PermissionPipeWanted(new AppConfig { NotchEnabled = true, LiveSessionsEnabled = true },
            false, false, ProState.Pro));
        Assert.False(TrayAppContext.PermissionPipeWanted(cfg, false, false, (ProState)9));
    }

    // ---- página con candado

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void The_locked_page_fits_and_its_parts_are_where_they_are_painted(float s)
    {
        var surface = NotchPlacement.Surface(s);
        foreach (var (alert, dots) in new[] { (false, false), (true, true), (false, true) })
        {
            var size = NotchProLayout.LockedPageSize(alert, dots, s);
            Assert.True(size.Height <= NotchLayout.ExpandedMaxHeight * s + 0.01f);
            var pill = NotchGeometry.PillRect(size.Width, size.Height, surface);
            var inner = NotchContentPainter.PageInner(pill, s, alert, dots);
            var head = NotchProLayout.LockedHeader(inner, s);
            var chip = NotchProLayout.Chip(inner, s);
            var (body, hint) = NotchProLayout.LockedBody(inner, s);
            Assert.True(head.Contains(chip), $"chip {chip} fuera de {head}");
            Assert.True(body.Top >= head.Bottom - 0.01f);
            Assert.True(hint.Bottom <= inner.Bottom + 0.5f, $"pista {hint} fuera de {inner}");
            Assert.True(NotchProLayout.HitLockedPage(new Point((int)(chip.Left + 2), (int)(chip.Top + 2)), inner));
            Assert.False(NotchProLayout.HitLockedPage(new Point((int)inner.Left - 4, (int)inner.Top + 4), inner));
        }
    }

    [Fact]
    public void A_locked_page_paints_the_pro_chip_with_the_accent_and_an_unlocked_one_does_not()
    {
        var surface = NotchPlacement.Surface(1f);
        var pages = new[] { NotchPage.Claude, NotchPage.Music, NotchPage.Tasks, NotchPage.Shelf };
        var size = NotchProLayout.LockedPageSize(false, true, 1f);
        foreach (var page in new[] { NotchPage.Music, NotchPage.Tasks, NotchPage.Shelf })
        {
            var frame = new NotchFrame(size.Width, size.Height, 1f, NotchMode.Expanded, null, Array.Empty<SessionState>(), null, 0f, 1f,
                Pages: new NotchPagesView(pages, page, null, 1f, 0f, 0f, Array.IndexOf(pages, page)), Locked: NotchProLock.For(page));
            var pill = NotchGeometry.PillRect(size.Width, size.Height, surface);
            var chip = Rectangle.Round(NotchProLayout.Chip(NotchContentPainter.PageInner(pill, 1f, false, true), 1f));
            Assert.True(AccentPixels(frame, surface, chip) > 6, $"sin chip en {page}");
            Assert.Equal(0, AccentPixels(frame with { Locked = NotchProLocks.None }, surface, chip));
        }
    }

    private static int AccentPixels(NotchFrame f, Size surface, Rectangle area)
    {
        using var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var painter = new NotchContentPainter(Localization.Get("es")))
            painter.Paint(g, surface, f);
        float accentHue = Theme.Dark.Accent.GetHue();
        int n = 0;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                var c = bmp.GetPixel(x, y);
                // El tono del acento (±12°), aunque sea tenue sobre el negro: ni grises ni el texto del chip.
                if (c.A < 40 || c.GetSaturation() < 0.35f || Math.Max(c.R, Math.Max(c.G, c.B)) < 50) continue;
                float d = Math.Abs(c.GetHue() - accentHue);
                if (Math.Min(d, 360f - d) <= 12f) n++;
            }
        return n;
    }
}
