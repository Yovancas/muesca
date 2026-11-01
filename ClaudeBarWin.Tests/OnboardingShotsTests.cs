using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Motion;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Onboarding;
using ClaudeBarWin.Services.Tutorial;
using ClaudeBarWin.UI;
using ClaudeBarWin.UI.Notch;
using ClaudeBarWin.UI.Onboarding;
using ClaudeBarWin.UI.Tutorial;
using static ClaudeBarWin.Tests.SetupProfilesTests;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Capturas para enseñar, SOLO si existe la variable de entorno <c>SHOTS_DIR</c> (sin ella no hacen nada): cada página del
/// asistente de bienvenida, el aviso de actualización, el candado y la tarjeta del Pro, la página Uso del notch (con datos,
/// con el hover, vacía, sin el Pro…) y el pomodoro (la página Foco en marcha, en pausa, en el fin de fase y sin el Pro; el
/// aviso del fin de fase con su bote; el mini anillo en el reposo, la vista previa, Trabajando y Mínimo), el tutorial
/// interactivo (un paso de cada capítulo, el primero en reposo y abierto, un paso hecho y lo del Pro sin el Pro), pintados con
/// los painters de verdad a 2x; los fotogramas de la entrada (el notch baja con el logo de Muesca y despliega la bienvenida)
/// para montar <c>installer/assets/splash.gif</c>; «Acerca de» en Ajustes; el logo de Muesca a varios tamaños (para compararlo
/// con branding/) y las capturas del README (<c>readme/&lt;idioma&gt;/</c>, que van a <c>assets/readme/</c>). Sobre un fondo de
/// escritorio, con la sombra y el alfa de la ventana como los compone Windows.
/// <code>
/// $env:SHOTS_DIR = "C:\shots"; dotnet test ClaudeBarWin.Tests --filter OnboardingShotsTests
/// $env:SHOTS_LANGS = "all"   # opcional: los 8 idiomas (por defecto, es y en)
/// </code>
/// </summary>
public sealed class OnboardingShotsTests
{
    private static readonly string? Dir = Environment.GetEnvironmentVariable("SHOTS_DIR");
    /// <summary>Idiomas de las capturas: <c>SHOTS_LANGS</c> separados por comas (p. ej. «es,en,de»; «all» = los 8 de la app),
    /// o español e inglés. Las de la Store se sacan en los 8 (store/screenshots.md).</summary>
    private static readonly string[] Langs = ShotLangs(Environment.GetEnvironmentVariable("SHOTS_LANGS"));

    private static string[] ShotLangs(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new[] { "es", "en" };
        if (value.Trim().Equals("all", StringComparison.OrdinalIgnoreCase)) return Localization.Languages.Select(l => l.Code).ToArray();
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(code => Localization.Languages.Any(l => l.Code == code)).ToArray();
    }
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(1000.0 / 60);
    private static readonly SetupMachine Machine2 = Machine(true,
        Mon(@"\\.\DISPLAY1", 0, 0, 2560, 1440, primary: true), Mon(@"\\.\DISPLAY2", 2560, 0, 1920, 1080));
    private static readonly RealUsage Quota = new()
    {
        FiveHour = new UsageWindow(42, DateTimeOffset.Now.AddHours(2)),
        SevenDay = new UsageWindow(18, DateTimeOffset.Now.AddDays(3)),
    };

    private static readonly Color WallTop = Color.FromArgb(46, 54, 88), WallBottom = Color.FromArgb(120, 78, 112);

    [Fact]
    public void Paints_every_wizard_page_and_the_update_offer_at_2x()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;
        Directory.CreateDirectory(Dir);
        foreach (var lang in Langs)
        {
            var shots = WizardShots(lang, 2f);
            Assert.All(shots, f => Assert.True(new FileInfo(f).Length > 0, f));
        }
        var update = UpdateShots(2f);
        Assert.All(update, f => Assert.True(new FileInfo(f).Length > 0, f));
    }

    [Fact]
    public void Paints_the_pro_lock_card_and_notice_at_2x()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;
        Directory.CreateDirectory(Dir);
        foreach (var lang in Langs)
            Assert.All(ProShots(lang, 2f), f => Assert.True(new FileInfo(f).Length > 0, f));
    }

    [Fact]
    public void Paints_the_usage_page_in_each_state_at_2x()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;
        Directory.CreateDirectory(Dir);
        foreach (var lang in Langs)
            Assert.All(UsageShots(lang, 2f), f => Assert.True(new FileInfo(f).Length > 0, f));
    }

    [Fact]
    public void Paints_the_page_arrows_visible_and_pressed_at_2x()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;
        Directory.CreateDirectory(Dir);
        Assert.All(ArrowShots(2f), f => Assert.True(new FileInfo(f).Length > 0, f));
    }

    [Fact]
    public void Paints_the_focus_page_and_the_pomodoro_in_each_state_at_2x()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;
        Directory.CreateDirectory(Dir);
        foreach (var lang in Langs)
            Assert.All(FocusShots(lang, 2f), f => Assert.True(new FileInfo(f).Length > 0, f));
    }

    [Fact]
    public void Writes_the_entrance_frames_for_the_splash()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;
        var dir = Path.Combine(Dir, "splash");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "frame-*.png")) File.Delete(old);

        // 30 fotogramas por segundo desde que el notch empieza a bajar hasta un rato con la bienvenida quieta.
        using var painter = new OnboardingPainter(Localization.Get("es"));
        var scene = NewScene(painter, "es", 1f, out var surface);
        var frame = TimeSpan.FromMilliseconds(1000.0 / 30);
        var now = TimeSpan.Zero;
        scene.Start(now);
        int n = 0;
        var openSince = TimeSpan.MaxValue;
        while (now < TimeSpan.FromSeconds(4))
        {
            Save(painter, scene, surface, Path.Combine(dir, $"frame-{n++:000}.png"));
            var next = now + frame;
            while (now < next) { now += TimeSpan.FromMilliseconds(4); scene.Step(now); }
            if (scene.Choreography.Stage == OnboardingStage.Open && openSince == TimeSpan.MaxValue) openSince = now;
            if (now - openSince > TimeSpan.FromMilliseconds(900)) break;
        }
        Save(painter, scene, surface, Path.Combine(dir, $"frame-{n++:000}.png"));
        Assert.True(n > 20, $"solo {n} fotogramas");
        Assert.Equal(OnboardingStage.Open, scene.Choreography.Stage);
    }

    [Fact]
    public void Paints_the_muesca_mark_at_the_sizes_of_the_branding_pngs()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;
        var dir = Path.Combine(Dir, "marca");
        Directory.CreateDirectory(dir);
        // Los mismos tamaños que branding/muesca-<n>.png, sobre transparente, para compararlos lado a lado.
        foreach (int n in new[] { 16, 24, 32, 48, 64, 128, 256, 512 })
        {
            using var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                BrandMark.Paint(g, new RectangleF(0, 0, n, n));
            }
            var path = Path.Combine(dir, $"muesca-{n}.png");
            bmp.Save(path, ImageFormat.Png);
            Assert.True(new FileInfo(path).Length > 0, path);
        }
    }

    [Fact]
    public void Paints_the_about_section_of_settings()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;
        Directory.CreateDirectory(Dir);
        foreach (var lang in new[] { "es", "en", "ja" })
        {
            var path = AboutShot(lang);
            Assert.True(new FileInfo(path).Length > 0, path);
        }
    }

    /// <summary>Ajustes › Acerca de: versión, el tutorial, importar tema y las dos notas (qué es y el aviso de marca).</summary>
    private static string AboutShot(string lang)
    {
        const int x = 16, w = 308;
        var cfg = new AppConfig { Language = lang, Theme = "dark" }.Normalize();
        var s = Localization.Get(lang);
        var rects = new Dictionary<string, Rectangle>();
        int h;
        using (var probe = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(probe))
            h = DashboardSettingsView.Draw(g, false, x, 0, w, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects, "0.5.0");
        using var full = new Bitmap(w + 2 * x, h + 16);
        using (var g = Graphics.FromImage(full))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Dark.Background);
            DashboardSettingsView.Draw(g, true, x, 0, w, cfg, s, Theme.Dark, Typography.Body, Typography.Caption,
                new Dictionary<string, Rectangle>(), "0.5.0");
        }
        int top = Math.Max(0, rects["special:importtheme"].Top - 110);
        using var cut = full.Clone(new Rectangle(0, top, full.Width, full.Height - top), full.PixelFormat);
        string path = Path.Combine(Dir!, $"acerca-de-{lang}.png");
        cut.Save(path, ImageFormat.Png);
        return path;
    }

    [Fact]
    public void Writes_the_readme_shots()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;
        foreach (var lang in new[] { "en", "es" })
            Assert.All(ReadmeShots(lang), f => Assert.True(new FileInfo(f).Length > 0, f));
    }

    /// <summary>
    /// Las capturas del README (README.md con las de <c>en</c>, README.es.md con las de <c>es</c>), con nombres fijos en
    /// <c>SHOTS_DIR/readme/&lt;idioma&gt;/</c> para copiarlas tal cual a <c>assets/readme/&lt;idioma&gt;/</c>: la bienvenida
    /// con el logo, el tutorial y el notch en cada función, sacado del notch de demostración del tutorial (datos de ejemplo,
    /// el painter de verdad), recortado a su sombra.
    /// </summary>
    private static IReadOnlyList<string> ReadmeShots(string lang)
    {
        var dir = Path.Combine(Dir!, "readme", lang);
        Directory.CreateDirectory(dir);
        var str = Localization.Get(lang);
        var files = new List<string>();
        const float s = 2f;

        using (var painter = new OnboardingPainter(str))
        {
            var welcome = NewScene(painter, lang, s, out var surface);
            var t = TimeSpan.Zero;
            welcome.Start(t);
            while (t < TimeSpan.FromMilliseconds(2500)) { t += Step; welcome.Step(t); }
            var path = Path.Combine(dir, "welcome.png");
            Save(painter, welcome, surface, path);
            files.Add(path);
        }

        using var tutPainter = new OnboardingPainter(str);
        using var notchPainter = new NotchContentPainter(str);
        var machine = new TutorialMachine();
        var scene = new TutorialScene(machine, str, tutPainter, ProState.Pro, reduceMotion: false,
            () => new DateTime(2026, 10, 30, 18, 55, 0), () => UsageFixtures.Now);
        var tutSurface = OnboardingPlacement.Surface(s, TutorialGeometry.Width, TutorialGeometry.Height);
        scene.Place(s, tutSurface);
        var now = TimeSpan.Zero;
        void Run(double ms)
        {
            var end = now + TimeSpan.FromMilliseconds(ms);
            while (now < end) { now += Step; scene.Step(now); }
        }
        void Go(TutorialChapter c, int step = 0)
        {
            scene.PointerLeave(now);
            scene.Activate(new TutorialHit(TutorialControl.Chapter, (int)c), now);
            if (step > 0) scene.Activate(new TutorialHit(TutorialControl.StepDot, step), now);
            Run(900);
        }
        void Notch(string name)
        {
            var f = scene.Frame().Notch!;
            var surface = NotchPlacement.Surface(f.Scale);
            using var layer = Desktop(surface);
            RectangleF drawn;
            using (var g = Graphics.FromImage(layer))
            {
                var shadow = notchPainter.PaintShadow(g, surface, f);
                var painted = notchPainter.Paint(g, surface, f);
                drawn = shadow.IsEmpty ? painted : RectangleF.Union(shadow, painted);
            }
            float pad = 28f * f.Scale;
            var crop = Rectangle.Round(RectangleF.Intersect(RectangleF.Inflate(drawn, pad, pad),
                new RectangleF(0, 0, surface.Width, surface.Height)));
            crop.Y = 0;   // pegado arriba, como el notch de verdad
            crop.Height = Math.Min(surface.Height, (int)Math.Ceiling(drawn.Bottom + pad));
            using var cut = layer.Clone(crop, layer.PixelFormat);
            var path = Path.Combine(dir, name + ".png");
            cut.Save(path, ImageFormat.Png);
            files.Add(path);
        }

        scene.Start(now);
        Run(2500);
        var tutorial = Path.Combine(dir, "tutorial.png");
        SaveTutorial(tutPainter, scene, tutSurface, tutorial);
        files.Add(tutorial);
        Notch("notch-idle");
        Go(TutorialChapter.Sessions, 1);
        Notch("notch-sessions");
        Go(TutorialChapter.Permissions);
        Run(1500);
        Notch("notch-permission");
        Go(TutorialChapter.Music, 2);
        scene.Key(Keys.Enter, false, now);
        Run(900);
        Notch("notch-music");
        Go(TutorialChapter.Tasks);
        Notch("notch-tasks");
        Go(TutorialChapter.Shelf, 1);
        Notch("notch-shelf");
        Go(TutorialChapter.Usage, 3);
        scene.Key(Keys.Enter, false, now);
        Run(700);
        Notch("notch-usage");
        Go(TutorialChapter.Focus, 1);
        Run(400);
        Notch("notch-focus");

        // La tarjeta del Pro, como la ve la edición de la Store.
        var cardSize = NotchProLayout.CardSize(s);
        var proSurface = NotchPlacement.Surface(s);
        var card = new NotchFrame(cardSize.Width, cardSize.Height, s, NotchMode.Expanded, null, Array.Empty<SessionState>(), Quota, 0f,
            1f, ShadowAlpha: 1f, ProCard: NotchProText.Card(new NotchProOffer(ProState.Free, null, true, lang == "es" ? "9,99 €" : "€9.99"),
                str, true));
        using (var bmp = Desktop(proSurface))
        {
            RectangleF drawn;
            using (var g = Graphics.FromImage(bmp))
            {
                notchPainter.PaintShadow(g, proSurface, card);
                drawn = notchPainter.Paint(g, proSurface, card);
            }
            float pad = 28f * s;
            var crop = Rectangle.Round(RectangleF.Intersect(new RectangleF(drawn.Left - pad, 0, drawn.Width + 2 * pad, drawn.Bottom + pad),
                new RectangleF(0, 0, proSurface.Width, proSurface.Height)));
            using var cut = bmp.Clone(crop, bmp.PixelFormat);
            var path = Path.Combine(dir, "pro.png");
            cut.Save(path, ImageFormat.Png);
            files.Add(path);
        }
        return files;
    }

    // ---------------------------------------------------------------- asistente

    private static OnboardingScene NewScene(OnboardingPainter painter, string lang, float s, out Size surface)
    {
        var cfg = new AppConfig { Language = lang }.Normalize();
        var services = new OnboardingServices(
            a => new SetupOutcome(SetupStatus.Applied, "ok", Array.Empty<string>(), Array.Empty<string>()),
            () => Quota, () => Array.Empty<SessionState>());
        var labels = new Dictionary<string, string>
        {
            [@"\\.\DISPLAY1"] = "LG ULTRAGEAR · 2560×1440 · " + (lang == "es" ? "principal" : "primary"),
            [@"\\.\DISPLAY2"] = "DELL U2419H · 1920×1080 · " + (lang == "es" ? "derecha" : "right"),
        };
        var wizard = new OnboardingWizard(new OnboardingContext(Machine2, cfg, false, false, Fresh: true, @"C:\Users\yo\.claude",
            new ProSummary(ProState.Trial, ProTrial.Days, Store: true)));
        var scene = new OnboardingScene(wizard, Localization.Get(lang), painter, services, reduceMotion: false, _ => labels,
            () => new DateTime(2026, 10, 29, 18, 55, 0));
        surface = OnboardingPlacement.Surface(s);
        scene.Place(s, surface);
        return scene;
    }

    private static IReadOnlyList<string> WizardShots(string lang, float s)
    {
        using var painter = new OnboardingPainter(Localization.Get(lang));
        var scene = NewScene(painter, lang, s, out var surface);
        var now = TimeSpan.Zero;
        void Run(double ms)
        {
            var end = now + TimeSpan.FromMilliseconds(ms);
            while (now < end) { now += Step; scene.Step(now); }
        }
        var files = new List<string>();
        void Shot(string name)
        {
            var path = Path.Combine(Dir!, $"onboarding-{lang}-{name}.png");
            Save(painter, scene, surface, path);
            files.Add(path);
        }
        Point Center(RectangleF r) => new((int)(r.Left + r.Width / 2f), (int)(r.Top + r.Height / 2f));

        scene.Start(now);
        Run(2500);
        Shot("1-bienvenida");
        scene.Key(Keys.Enter, false, now);
        Run(700);
        // El ratón sobre «Notch completo»: la tarjeta se ilumina con el acento; la vista previa, en su primera escena.
        scene.PointerMove(Center(OnboardingGeometry.Card(scene.Pill, s, (int)SetupProfile.FullNotch)));
        Run(400);
        Shot("2-perfiles");
        scene.Activate(new OnboardingHit(OnboardingControl.Card, (int)SetupProfile.FullNotch), now);
        Run(ProfilePreview.SceneFor.TotalMilliseconds * 1.3);   // la letra pequeña, en la vista previa
        Shot("2b-perfiles-notch-completo");
        scene.PointerLeave();
        scene.Activate(new OnboardingHit(OnboardingControl.Card, (int)SetupProfile.Coder), now);
        scene.Key(Keys.Enter, false, now);
        Run(400);
        Shot("3-que-se-activa");
        scene.Key(Keys.Enter, false, now);
        Run(400);
        scene.Key(Keys.Tab, false, now);   // con el foco del teclado a la vista
        Run(200);
        Shot("4-ajustes");
        scene.Key(Keys.Escape, false, now);
        Run(200);
        scene.Key(Keys.Escape, false, now);
        scene.Key(Keys.Enter, false, now);
        Run(400);
        Shot("5-claude-code");
        scene.Key(Keys.Enter, false, now);
        Run(400);
        Shot("6-resumen");
        scene.Key(Keys.Escape, false, now);
        Run(400);
        Shot("7-saltar");
        return files;
    }

    // ---------------------------------------------------------------- contador de uso

    [Fact]
    public void Paints_the_usage_counter_in_the_welcome_and_in_settings()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;
        Directory.CreateDirectory(Dir);
        foreach (var lang in Langs)
        {
            var files = new List<string>();
            using (var painter = new OnboardingPainter(Localization.Get(lang)))
            {
                var scene = NewScene(painter, lang, 2f, out var surface);
                var now = TimeSpan.Zero;
                void Run(double ms)
                {
                    var end = now + TimeSpan.FromMilliseconds(ms);
                    while (now < end) { now += Step; scene.Step(now); }
                }
                scene.Start(now);
                Run(2500);
                var row = OnboardingGeometry.UsageStatsRow(scene.Pill, 2f);
                scene.PointerMove(new Point((int)(row.Left + row.Width / 2f), (int)(row.Top + row.Height / 2f)));
                Run(400);
                string on = Path.Combine(Dir, $"contador-{lang}-1-bienvenida.png");
                Save(painter, scene, surface, on);
                scene.Activate(new OnboardingHit(OnboardingControl.UsageStats), now);
                Run(600);
                string off = Path.Combine(Dir, $"contador-{lang}-2-bienvenida-apagado.png");
                Save(painter, scene, surface, off);
                files.Add(on);
                files.Add(off);
            }
            files.Add(SettingsShot(lang));
            Assert.All(files, f => Assert.True(new FileInfo(f).Length > 0, f));
        }
    }

    /// <summary>Ajustes › Sistema del panel (donde vive el interruptor), recortado a la sección.</summary>
    private static string SettingsShot(string lang)
    {
        const int x = 16, w = 308;
        var cfg = new AppConfig { Language = lang, Theme = "dark" }.Normalize();
        var s = Localization.Get(lang);
        var rects = new Dictionary<string, Rectangle>();
        int h;
        using (var probe = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(probe))
            h = DashboardSettingsView.Draw(g, false, x, 0, w, cfg, s, Theme.Dark, Typography.Body, Typography.Caption, rects, "0.5.0");
        using var full = new Bitmap(w + 2 * x, h + 16);
        using (var g = Graphics.FromImage(full))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Theme.Dark.Background);
            DashboardSettingsView.Draw(g, true, x, 0, w, cfg, s, Theme.Dark, Typography.Body, Typography.Caption,
                new Dictionary<string, Rectangle>(), "0.5.0");
        }
        int top = Math.Max(0, rects["toggle:Startup"].Top - 40);
        int bottom = Math.Min(full.Height, rects["cycle:lang"].Bottom + 12);
        var crop = new Rectangle(0, top, full.Width, bottom - top);
        // A la escala del proceso (el panel usa el DPI del sistema), sin reescalar.
        using var cut = full.Clone(crop, full.PixelFormat);
        string path = Path.Combine(Dir!, $"contador-{lang}-3-ajustes.png");
        cut.Save(path, ImageFormat.Png);
        return path;
    }

    // ---------------------------------------------------------------- actualización

    private static IReadOnlyList<string> UpdateShots(float s)
    {
        var es = Localization.Get("es");
        var surface = NotchPlacement.Surface(s);
        var sessions = new[]
        {
            new SessionState
            {
                SessionId = "a", Cwd = @"C:\code\muesca", ProjectName = "muesca", Phase = SessionPhase.Processing,
                LastTool = "Edit", LastActivityUtc = DateTime.UtcNow,
            },
        };
        var files = new List<string>();
        using var painter = new NotchContentPainter(es);
        void Shot(string name, NotchFrame f)
        {
            var path = Path.Combine(Dir!, $"update-{name}.png");
            using var bmp = Desktop(surface);
            using (var g = Graphics.FromImage(bmp))
            {
                painter.PaintShadow(g, surface, f);
                painter.Paint(g, surface, f);
            }
            bmp.Save(path, ImageFormat.Png);
            files.Add(path);
        }
        var idle = NotchLayout.TargetSize(NotchMode.Idle, s, 0f);
        Shot("1-reposo", new NotchFrame(idle.Width, idle.Height, s, NotchMode.Idle, null, Array.Empty<SessionState>(), Quota, 0f, 1f,
            Update: NotchUpdateText.Build(new UpdateOfferView("0.6", 0.4, false), es, interactive: true)));
        var expanded = NotchLayout.PageSize(NotchPage.Claude, new PageRows(1, 0, 0), withAlert: true, withDots: false, s);
        NotchFrame Expanded(UpdateOfferView offer, bool interactive) => new(expanded.Width, expanded.Height, s, NotchMode.Expanded,
            null, sessions, Quota, 0.3f, 1f, ShadowAlpha: 1f, Update: NotchUpdateText.Build(offer, es, interactive));
        Shot("2-descargando", Expanded(new UpdateOfferView("0.6", 0.42, false), true));
        Shot("3-lista", Expanded(new UpdateOfferView("0.6", 1, true), true));
        Shot("4-lista-otro-monitor", Expanded(new UpdateOfferView("0.6", 1, true), false));
        return files;
    }

    // ---------------------------------------------------------------- Pro

    private static IReadOnlyList<string> ProShots(string lang, float s)
    {
        var str = Localization.Get(lang);
        var surface = NotchPlacement.Surface(s);
        var files = new List<string>();
        using var painter = new NotchContentPainter(str);
        void Shot(string name, NotchFrame f)
        {
            var path = Path.Combine(Dir!, $"pro-{lang}-{name}.png");
            using var bmp = Desktop(surface);
            using (var g = Graphics.FromImage(bmp))
            {
                painter.PaintShadow(g, surface, f);
                painter.Paint(g, surface, f);
            }
            bmp.Save(path, ImageFormat.Png);
            files.Add(path);
        }
        var pages = NotchPages.Default;
        NotchFrame Locked(NotchPage page)
        {
            var size = NotchProLayout.LockedPageSize(withAlert: false, withDots: true, s);
            return new NotchFrame(size.Width, size.Height, s, NotchMode.Expanded, null, Array.Empty<SessionState>(), Quota, 0f, 1f,
                ShadowAlpha: 1f, Pages: new NotchPagesView(pages, page, null, 1f, 0f, 0f, pages.ToList().IndexOf(page)),
                Locked: NotchProLocks.Music | NotchProLocks.Tasks | NotchProLocks.Shelf);
        }
        Shot("1-candado-musica", Locked(NotchPage.Music));
        Shot("2-candado-tareas", Locked(NotchPage.Tasks));
        Shot("3-candado-bandeja", Locked(NotchPage.Shelf));

        var card = NotchProLayout.CardSize(s);
        NotchFrame Card(NotchProOffer offer, bool interactive = true, ProCardButton hover = ProCardButton.None) =>
            new(card.Width, card.Height, s, NotchMode.Expanded, null, Array.Empty<SessionState>(), Quota, 0f, 1f, ShadowAlpha: 1f,
                ProCard: NotchProText.Card(offer, str, interactive) with { Hover = hover });
        var store = new NotchProOffer(ProState.Free, null, true, lang == "es" ? "9,99 €" : "€9.99");
        Shot("4-tarjeta-store", Card(store, hover: ProCardButton.Primary));
        Shot("5-tarjeta-github", Card(new NotchProOffer(ProState.Free, null, false, null)));
        Shot("6-tarjeta-prueba", Card(store with { State = ProState.Trial, TrialDaysLeft = 5 }));
        Shot("7-tarjeta-comprando", Card(store with { Message = ProMessage.Busy }));
        Shot("8-tarjeta-sin-conexion", Card(store with { Message = ProMessage.Offline }));
        Shot("9-tarjeta-otro-monitor", Card(store, interactive: false));
        Shot("10-tarjeta-pro", Card(store with { State = ProState.Pro }));

        var notice = NotchLayout.TargetSize(NotchMode.Completed, s, 0f);
        Shot("11-fin-de-la-prueba", new NotchFrame(notice.Width, notice.Height, s, NotchMode.Completed,
            new NotchAlert(NotchAlertKind.Notice, NotchForm.ProTrialNoticeId, NotchForm.ProTrialNoticeId, null, DateTime.UtcNow, null),
            Array.Empty<SessionState>(), Quota, 0f, 1f, ShadowAlpha: 1f));
        return files;
    }

    // ---------------------------------------------------------------- flechas de página

    private static IReadOnlyList<string> ArrowShots(float s)
    {
        var es = Localization.Get("es");
        var surface = NotchPlacement.Surface(s);
        var files = new List<string>();
        using var painter = new NotchContentPainter(es);
        void Shot(string name, NotchFrame f)
        {
            var path = Path.Combine(Dir!, $"flechas-{name}.png");
            using var bmp = Desktop(surface);
            using (var g = Graphics.FromImage(bmp))
            {
                painter.PaintShadow(g, surface, f);
                painter.Paint(g, surface, f);
            }
            bmp.Save(path, ImageFormat.Png);
            files.Add(path);
        }
        var pages = NotchPages.Default;
        var sessions = new[]
        {
            new SessionState
            {
                SessionId = "a", Cwd = @"C:\code\muesca", ProjectName = "muesca", Phase = SessionPhase.Processing,
                LastTool = "Edit", LastActivityUtc = DateTime.UtcNow,
            },
        };
        // Visibles en la primera página: la anterior no lleva a ningún sitio y no se pinta; la siguiente, tenue.
        var rest = new PageArrowLook(1f, 0f, 1f, 0f);
        var claude = NotchLayout.PageSize(NotchPage.Claude, new PageRows(1, 0, 0), withAlert: false, withDots: true, s);
        Shot("1-visibles", new NotchFrame(claude.Width, claude.Height, s, NotchMode.Expanded, null, sessions, Quota, 0.3f, 1f,
            ShadowAlpha: 1f, Pages: new NotchPagesView(pages, NotchPage.Claude, null, 1f, 0f, 0f, 0,
                new PageArrowsView(rest with { Alpha = 0f }, rest))));
        // Pulsada la siguiente en una página del medio (con candado: se navega igual): acento, crecida y en pleno empujón.
        var locked = NotchProLayout.LockedPageSize(withAlert: false, withDots: true, s);
        int music = pages.ToList().IndexOf(NotchPage.Music);
        var pressed = new PageArrowLook(1f, 1f, 1f + PageArrows.HoverGrow, PageArrows.HoverNudge + 3f);
        Shot("2-pulsada", new NotchFrame(locked.Width, locked.Height, s, NotchMode.Expanded, null, Array.Empty<SessionState>(), Quota,
            0f, 1f, ShadowAlpha: 1f, Pages: new NotchPagesView(pages, NotchPage.Music, null, 1f, 0f, 0f, music,
                new PageArrowsView(rest, pressed)),
            Locked: NotchProLocks.Music | NotchProLocks.Tasks | NotchProLocks.Shelf));
        return files;
    }

    // ---------------------------------------------------------------- página Uso

    private static IReadOnlyList<string> UsageShots(string lang, float s)
    {
        var str = Localization.Get(lang);
        var surface = NotchPlacement.Surface(s);
        var files = new List<string>();
        using var painter = new NotchContentPainter(str);
        void Shot(string name, UsageView? view, bool alert = false)
        {
            var path = Path.Combine(Dir!, $"uso-{lang}-{name}.png");
            var f = UsageFixtures.Frame(s, view, alert);
            using var bmp = Desktop(surface);
            using (var g = Graphics.FromImage(bmp))
            {
                painter.PaintShadow(g, surface, f);
                painter.Paint(g, surface, f);
            }
            bmp.Save(path, ImageFormat.Png);
            files.Add(path);
        }
        var numbers = UsageFixtures.Numbers(str);
        var spend = UsageFixtures.SpendChart(str);
        var quota = UsageFixtures.QuotaChart(str);
        int mid = UsageFixtures.MidBucket(spend);
        Shot("1-gasto", new UsageView(numbers, spend, UsageChartMode.Spend, ChartRange.Day1));
        Shot("2-gasto-hover", new UsageView(numbers, spend, UsageChartMode.Spend, ChartRange.Day1, Bucket: mid, TipIndex: mid,
            TipAlpha: 1f));
        Shot("3-gasto-sin-sonnet", new UsageView(numbers, UsageFixtures.SpendChart(str, new HashSet<string> { "Sonnet" }),
            UsageChartMode.Spend, ChartRange.Day1, Hover: new UsageHit(UsageControl.Legend, 1), HoverIntensity: 1f));
        Shot("4-cuota-5h", new UsageView(UsageFixtures.Numbers(str, QuotaFocus.Five), quota, UsageChartMode.Quota, ChartRange.Hours5));
        int q = quota.Count * 3 / 4;
        Shot("5-cuota-hover", new UsageView(UsageFixtures.Numbers(str, QuotaFocus.Five), quota, UsageChartMode.Quota, ChartRange.Hours5,
            Bucket: q, TipIndex: q, TipAlpha: 1f));
        var seven = UsageFixtures.QuotaChart(str, focus: QuotaFocus.Seven);
        Shot("6-cuota-7d-aviso", new UsageView(UsageFixtures.Numbers(str, QuotaFocus.Seven), seven, UsageChartMode.Quota,
            ChartRange.Hours5), alert: true);
        Shot("7-vacia", new UsageView(numbers, UsageChartBuilder.Quota(new List<PctPoint>(), ChartRange.Hours5, UsageFixtures.Now,
            QuotaFocus.None, new UsageQuotaInputs(null, null, null, null, new[] { 80, 95 }), new HashSet<string>(), true, str),
            UsageChartMode.Quota, ChartRange.Hours5));
        Shot("8-sin-pro", new UsageView(numbers, UsageFixtures.QuotaChart(str, projection: false), UsageChartMode.Quota,
            ChartRange.Hours5, Interactive: false, Locked: true));
        // «Mostrar gasto estimado» apagado: solo Cuota, sin el selector Gasto | Cuota.
        Shot("10-cuota-sin-gasto", new UsageView(UsageFixtures.Numbers(str, QuotaFocus.Five), quota, UsageChartMode.Quota,
            ChartRange.Hours5, Modes: false));
        Shot("11-cuota-sin-gasto-hover", new UsageView(UsageFixtures.Numbers(str, QuotaFocus.Five), quota, UsageChartMode.Quota,
            ChartRange.Hours5, Bucket: q, TipIndex: q, TipAlpha: 1f, Modes: false));
        Shot("9-cargando", null);
        return files;
    }

    // ---------------------------------------------------------------- página Foco (pomodoro)

    private static IReadOnlyList<string> FocusShots(string lang, float s)
    {
        var str = Localization.Get(lang);
        var surface = NotchPlacement.Surface(s);
        var files = new List<string>();
        using var painter = new NotchContentPainter(str);
        void Shot(string name, NotchFrame f)
        {
            var path = Path.Combine(Dir!, $"foco-{lang}-{name}.png");
            using var bmp = Desktop(surface);
            using (var g = Graphics.FromImage(bmp))
            {
                painter.PaintShadow(g, surface, f);
                painter.Paint(g, surface, f);
            }
            bmp.Save(path, ImageFormat.Png);
            files.Add(path);
        }
        var task = new FocusTaskInfo(lang == "es" ? "Escribir el informe trimestral" : "Write the quarterly report", 1, 3);
        Shot("1-en-marcha", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Running(), str, task: task)));
        Shot("2-en-pausa", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Paused(), str, task: task)));
        Shot("3-fin-de-fase-pagina", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Finished(), str, task: task)));
        // El aviso del fin de fase: el anillo entra con el muelle de atención (fotogramas del bote) y el botón se arma.
        var pop = new SpringValue(NotchMotion.FocusPopFrom, Motion.SpringAttention with { EpsX = 0.002, EpsV = 0.02 });
        pop.SetTarget(1);
        for (int i = 0; i < 3; i++)
        {
            Shot($"4{(char)('a' + i)}-fin-de-fase-aviso-bote", FocusFixtures.Alert(s,
                FocusFixtures.View(FocusFixtures.Finished(), str, pop: (float)pop.Value), arm: Math.Min(1f, 0.3f + 0.35f * i)));
            for (int k = 0; k < 5; k++) pop.Advance(1.0 / 60);
        }
        Shot("5-fin-de-fase-aviso", FocusFixtures.Alert(s, FocusFixtures.View(FocusFixtures.Finished(), str), arm: 1f));
        Shot("6-fin-de-descanso-aviso", FocusFixtures.Alert(s, FocusFixtures.View(FocusFixtures.Finished(), str), arm: 1f,
            ended: FocusPhase.ShortBreak, next: FocusPhase.Focus));
        Shot("7-aviso-otro-monitor", FocusFixtures.Alert(s, null, readOnly: true));
        var face = FocusFixtures.View(FocusFixtures.Running(), str, face: true);
        Shot("8-mini-anillo-reposo", FocusFixtures.Closed(s, NotchMode.Idle, face));
        Shot("9-mini-anillo-pausa", FocusFixtures.Closed(s, NotchMode.Idle, FocusFixtures.View(FocusFixtures.Paused(), str, face: true)));
        Shot("10-mini-anillo-ultimo-minuto", FocusFixtures.Closed(s, NotchMode.Idle,
            FocusFixtures.View(FocusFixtures.LastMinute(), str, face: true)));
        Shot("11-mini-anillo-vista-previa", FocusFixtures.Closed(s, NotchMode.Peek, face));
        Shot("12-mini-anillo-trabajando", FocusFixtures.Closed(s, NotchMode.Working, face, new[] { FocusFixtures.Working() }));
        Shot("13-minimo", FocusFixtures.Closed(s, NotchMode.Minimal, face));
        Shot("14-sin-pro", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Running(), str, locked: true)));
        Shot("15-hover-saltar", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Running(), str, task: task,
            hover: new FocusHit(FocusControl.Skip), hoverIntensity: 1f)));
        Shot("15b-hover-reiniciar-fundido", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Paused(), str, task: task,
            hover: new FocusHit(FocusControl.Reset), hoverIntensity: 0.5f)));
        Shot("16-con-aviso-integrado", FocusFixtures.Page(s, FocusFixtures.View(FocusFixtures.Running(), str, task: task), alert: true));
        return files;
    }

    // ---------------------------------------------------------------- tutorial

    [Fact]
    public void Paints_every_tutorial_chapter_a_done_step_and_pro_without_pro_at_2x()
    {
        if (string.IsNullOrWhiteSpace(Dir)) return;
        Directory.CreateDirectory(Dir);
        foreach (var lang in Langs)
            Assert.All(TutorialShots(lang, 2f), f => Assert.True(new FileInfo(f).Length > 0, f));
    }

    /// <summary>
    /// Un paso de cada capítulo con su gesto a medias o hecho (el notch abierto, la lista, el permiso armado, las flechas, la
    /// letra, el recordatorio, el archivo arrastrándose, el hover de la gráfica, el pomodoro y el menú de la bandeja), un paso
    /// recién hecho (con el visto ya asentado), un paso Pro con su chip y el cierre de un capítulo sin el Pro (la línea con el
    /// enlace). Sobre el escritorio de mentira, con la sombra.
    /// </summary>
    private static IReadOnlyList<string> TutorialShots(string lang, float s)
    {
        var str = Localization.Get(lang);
        using var painter = new OnboardingPainter(str);
        var machine = new TutorialMachine();
        var scene = new TutorialScene(machine, str, painter, ProState.Free, reduceMotion: false,
            () => new DateTime(2026, 10, 30, 18, 55, 0), () => UsageFixtures.Now);
        var surface = OnboardingPlacement.Surface(s, TutorialGeometry.Width, TutorialGeometry.Height);
        scene.Place(s, surface);
        var now = TimeSpan.Zero;
        void Run(double ms)
        {
            var end = now + TimeSpan.FromMilliseconds(ms);
            while (now < end) { now += Step; scene.Step(now); }
        }
        var files = new List<string>();
        void Shot(string name)
        {
            var path = Path.Combine(Dir!, $"tutorial-{lang}-{name}.png");
            SaveTutorial(painter, scene, surface, path);
            files.Add(path);
        }
        RectangleF Stage() => TutorialGeometry.Stage(OnboardingGeometry.PillRect(TutorialGeometry.Width * s, TutorialGeometry.Height * s,
            surface.Width / 2f), s);
        RectangleF Demo()
        {
            var f = scene.Frame().Notch!;
            var stage = Stage();
            var pill = NotchGeometry.PillRect(f.Width, f.Height, TutorialGeometry.StageSurface(stage));
            pill.Offset(MathF.Round(stage.Left), MathF.Round(stage.Top));
            return pill;
        }
        Point At(RectangleF r, float fx = 0.5f, float fy = 0.5f) => new((int)(r.Left + r.Width * fx), (int)(r.Top + r.Height * fy));
        void Go(TutorialChapter c, int step = 0)
        {
            scene.PointerLeave(now);
            scene.Activate(new TutorialHit(TutorialControl.Chapter, (int)c), now);
            if (step > 0) scene.Activate(new TutorialHit(TutorialControl.StepDot, step), now);
            Run(900);
        }

        scene.Start(now);
        Run(2500);
        // 1. El notch: empieza en reposo (los dos anillos) y el ratón encima lo abre (paso hecho, con el visto).
        Shot("01a-notch-en-reposo");
        scene.PointerMove(At(Demo()), now);
        Run(1200);
        Shot("01b-notch-abierto");
        // 2. Sesiones: la lista con «trabajando», «tu turno» y «en reposo», desplazada con la rueda.
        Go(TutorialChapter.Sessions, 1);
        scene.Key(Keys.Down, false, now);
        Run(600);
        Shot("02-sesiones");
        // 3. Permisos: el aviso con los botones a medio armar (paso Pro: lleva el chip).
        Go(TutorialChapter.Permissions);
        scene.Activate(new TutorialHit(TutorialControl.StepDot, 1), now);
        scene.Activate(new TutorialHit(TutorialControl.StepDot, 0), now);
        Run(180);
        Shot("03-permisos-armandose-pro");
        // 4. Páginas: las flechas con el ratón encima.
        Go(TutorialChapter.Pages, 2);
        var pages = Demo();
        scene.PointerMove(At(pages, 0.2f, 0.3f), now);
        Run(600);
        Shot("04-paginas-flechas");
        // 5. Música: la letra abierta (hecho).
        Go(TutorialChapter.Music, 2);
        scene.Key(Keys.Enter, false, now);
        Run(900);
        Shot("05-musica-letra-hecho");
        // 6. Tareas: el recordatorio con «Hecha» y «+10 min».
        Go(TutorialChapter.Tasks, 2);
        Shot("06-tareas-recordatorio");
        // 7. Bandeja: el archivo de ejemplo arrastrándose encima del notch (abierto ya en la Bandeja).
        Go(TutorialChapter.Shelf);
        scene.PointerDown(At(TutorialGeometry.SampleFile(Stage(), s)), now);
        scene.PointerMove(At(Demo()), now);
        Run(700);
        scene.PointerMove(At(Demo(), 0.5f, 0.55f), now);
        Shot("07-bandeja-arrastrando");
        scene.PointerUp(At(Demo(), 0.5f, 0.55f), now);
        // 8. Uso: el hover de la gráfica de la cuota (Pro).
        Go(TutorialChapter.Usage, 3);
        scene.Key(Keys.Enter, false, now);
        Run(700);
        Shot("08-uso-grafica-hover");
        // 9. Foco: el pomodoro en marcha.
        Go(TutorialChapter.Focus, 1);
        Run(400);
        Shot("09-foco");
        // 10. Bandeja de Windows: el menú con «Monitor del notch» abierto.
        Go(TutorialChapter.Tray, 2);
        scene.Key(Keys.Enter, false, now);
        Run(500);
        Shot("10-bandeja-menu-monitores");
        // «Saltar» hasta salir de los pasos, con un frame entre pulsaciones (la vista que decide si se puede saltar se
        // calcula en Step) y un tope para que un cambio en el tutorial falle el test en vez de colgarlo.
        void SkipWhile(Func<bool> cond)
        {
            for (int i = 0; cond(); i++)
            {
                Assert.True(i < 200, $"el tutorial no avanza con «Saltar» (fase {machine.Phase})");
                // Un paso ya hecho no se salta: se pasa con «Siguiente» (y en el cierre de capítulo, también).
                bool next = machine.Phase != TutorialPhase.Step || machine.StepDone;
                scene.Activate(new TutorialHit(next ? TutorialControl.Next : TutorialControl.Skip), now);
                Run(50);
            }
        }
        // Cierre de un capítulo con pasos Pro, sin el Pro: la línea discreta con «Ver Pro ›».
        Go(TutorialChapter.Music);
        SkipWhile(() => machine.Phase == TutorialPhase.Step);
        Run(600);
        Shot("11-fin-de-capitulo-sin-pro");
        // El final.
        Go(TutorialChapter.Tray);
        SkipWhile(() => machine.Phase != TutorialPhase.Finished);
        Run(600);
        Shot("12-final");
        return files;
    }

    /// <summary>El tutorial como lo compone Windows: sombra y pastilla en su capa, con el alfa de la ventana.</summary>
    private static void SaveTutorial(OnboardingPainter painter, TutorialScene scene, Size surface, string path)
    {
        var frame = scene.Frame();
        using var layer = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(layer))
        {
            painter.PaintTutorialShadow(g, surface, frame);
            painter.PaintTutorial(g, surface, frame);
        }
        using var bmp = Desktop(surface);
        using (var g = Graphics.FromImage(bmp))
        using (var attrs = new ImageAttributes())
        {
            attrs.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Clamp(scene.WindowAlpha, 0f, 1f) });
            g.DrawImage(layer, new Rectangle(Point.Empty, surface), 0, 0, surface.Width, surface.Height, GraphicsUnit.Pixel, attrs);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    // ---------------------------------------------------------------- composición

    /// <summary>Fondo de escritorio (degradado) del tamaño de la superficie.</summary>
    private static Bitmap Desktop(Size surface)
    {
        var bmp = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var wall = new LinearGradientBrush(new Rectangle(Point.Empty, surface), WallTop, WallBottom, LinearGradientMode.Vertical);
        g.FillRectangle(wall, 0, 0, surface.Width, surface.Height);
        return bmp;
    }

    /// <summary>El frame del asistente como lo compone Windows: sombra y pastilla en su capa, con el alfa de la ventana.</summary>
    private static void Save(OnboardingPainter painter, OnboardingScene scene, Size surface, string path)
    {
        var frame = scene.Frame();
        using var layer = new Bitmap(surface.Width, surface.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(layer))
        {
            painter.PaintShadow(g, surface, frame);
            painter.Paint(g, surface, frame);
        }
        using var bmp = Desktop(surface);
        using (var g = Graphics.FromImage(bmp))
        using (var attrs = new ImageAttributes())
        {
            attrs.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Clamp(scene.WindowAlpha, 0f, 1f) });
            g.DrawImage(layer, new Rectangle(Point.Empty, surface), 0, 0, surface.Width, surface.Height, GraphicsUnit.Pixel, attrs);
        }
        bmp.Save(path, ImageFormat.Png);
    }
}
