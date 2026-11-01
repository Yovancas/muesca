using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.UI;

namespace ClaudeBarWin.Tests;

/// <summary>
/// T0: la mascota Idle debe verse con <c>ShowMascot=on</c> AUNQUE <c>LiveSessionsEnabled=off</c>
/// (desacople de la puerta de visibilidad en <see cref="DashboardHeader"/>). Las puertas de
/// ANIMACIÓN (bote/fast-tick) siguen exigiendo live on, pero el bloque Idle estático ya reserva
/// alto y desplaza la columna de texto. Verificado además el invariante de 2 pasadas (medir==pintar).
/// </summary>
public class DashboardHeaderTests
{
    private const int X = 16, Y = 0, W = 308;

    // Bitmap/Graphics reales para que MeasureString/GetHeight tengan un contexto válido.
    private static Bitmap NewBmp() => new(W + X * 2, 400);

    private static AppConfig Cfg(bool showMascot, bool liveEnabled, bool collapsedQuota = false) => new()
    {
        ShowMascot = showMascot,
        LiveSessionsEnabled = liveEnabled,
        ShowHealth = true,
        // T5b: el glance de cuota del header solo existe con la sección "Cuota" PLEGADA (expandida
        // duplicaba su primera fila). Los tests que ejercitan el glance pasan collapsedQuota: true.
        CollapsedQuota = collapsedQuota,
    };

    // Header con snapshot vacío: la columna derecha apenas aporta alto, así que el bloque de la
    // mascota (Idle) domina el bottom cuando está visible → cambio observable en el y devuelto.
    private static int RunHeader(Graphics g, bool draw, AppConfig cfg, int bounce = 0)
    {
        Rectangle gear = Rectangle.Empty;
        var live = new LiveSessionsView(); // GlobalPhase = Idle por defecto
        var s = Localization.Get("en");
        return DashboardHeader.Draw(
            g, draw, X, Y, W,
            snap: null, live, cfg, s, Theme.Dark,
            MascotAnimator.StaticState, Mood.Neutral,
            Typography.Body, Typography.Caption, Typography.Mono,
            ref gear,
            motion: null, reduceMotion: false,
            mascotBounceOffsetY: bounce, celebration: null);
    }

    [Fact]
    public void Mascot_idle_reserves_height_when_ShowMascot_on_and_live_off()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        int withMascot = RunHeader(g, draw: false, Cfg(showMascot: true, liveEnabled: false));
        int withoutMascot = RunHeader(g, draw: false, Cfg(showMascot: false, liveEnabled: false));

        // Con la puerta vieja (live && mascota) ambos eran iguales (mascota nunca se pintaba con
        // live off). Tras T0, el bloque Idle reserva alto > 0 y el header crece. Esta es la regresión
        // que reproduce el bug "activo Mostrar mascota y no pasa nada".
        Assert.True(withMascot > withoutMascot,
            $"con ShowMascot on (live off) el header debe reservar alto para la mascota Idle: " +
            $"mascota={withMascot} sin={withoutMascot}");
    }

    [Fact]
    public void Header_measure_equals_paint_with_mascot_on_and_live_off()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: true, liveEnabled: false);

        int measured = RunHeader(g, draw: false, cfg);
        int painted = RunHeader(g, draw: true, cfg);

        // Invariante de 2 pasadas: medir y pintar devuelven el MISMO y aunque la mascota esté visible.
        Assert.Equal(measured, painted);
    }

    [Fact]
    public void Header_measure_equals_paint_with_mascot_off()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: false, liveEnabled: false);

        int measured = RunHeader(g, draw: false, cfg);
        int painted = RunHeader(g, draw: true, cfg);

        Assert.Equal(measured, painted);
    }

    [Fact]
    public void Bounce_offset_does_not_change_layout_height()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: true, liveEnabled: false);

        // El bote es puramente visual (transform en la pasada de pintado): NO debe alterar el y de
        // layout. Distintos offsets de bote -> mismo alto devuelto (puerta de animación intacta).
        int noBounce = RunHeader(g, draw: true, cfg, bounce: 0);
        int bounced = RunHeader(g, draw: true, cfg, bounce: 6);

        Assert.Equal(noBounce, bounced);
    }

    // --- T9c (§3 #12): engranaje ⚙ como path GDI+ (el glifo MDL2 salía borroso con franjas ClearType) ---

    [Fact]
    public void GearOutline_has_four_points_per_tooth_between_both_radii()
    {
        const float cx = 12f, cy = 12f, rOuter = 7.5f, rRoot = 5.6f;
        const int teeth = 8;
        var pts = DashboardHeader.GearOutline(cx, cy, rOuter, rRoot, teeth);

        Assert.Equal(teeth * 4, pts.Length);
        int atOuter = 0, atRoot = 0;
        foreach (var p in pts)
        {
            double d = Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
            Assert.InRange(d, rRoot - 0.01, rOuter + 0.01);
            if (Math.Abs(d - rOuter) < 0.01) atOuter++;
            if (Math.Abs(d - rRoot) < 0.01) atRoot++;
        }
        // Cada diente aporta 2 puntos en la cresta (radio exterior) y 2 en el valle (radio raíz).
        Assert.Equal(teeth * 2, atOuter);
        Assert.Equal(teeth * 2, atRoot);
    }

    [Fact]
    public void GearOutline_is_deterministic()
    {
        var a = DashboardHeader.GearOutline(10f, 10f, 7f, 5f, 8);
        var b = DashboardHeader.GearOutline(10f, 10f, 7f, 5f, 8);
        Assert.Equal(a, b); // misma entrada → mismos puntos (medir==pintar)
    }

    [Fact]
    public void DrawGear_paints_a_ring_with_a_punched_hole()
    {
        // Path GDI+ con AA: anillo sólido del color del glifo, agujero central PERFORADO (se ve el
        // fondo del botón) y nada fuera del radio exterior. Sustituye al glifo ClearType borroso.
        using var bmp = new Bitmap(24, 24);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        DashboardHeader.DrawGear(g, new Rectangle(0, 0, 24, 24), Color.White);

        var hole = bmp.GetPixel(12, 12);     // centro = agujero → fondo
        Assert.True(hole.R < 40, $"el centro debe quedar perforado (fondo), fue R={hole.R}");
        var ring = bmp.GetPixel(12 + 4, 12); // anillo sólido entre agujero y raíz
        Assert.True(ring.R > 200, $"el anillo debe pintarse del color del glifo, fue R={ring.R}");
        var outside = bmp.GetPixel(23, 12);  // fuera del radio exterior → fondo
        Assert.True(outside.R < 40, $"fuera del engranaje debe verse el fondo, fue R={outside.R}");
    }

    // --- T9: anti-corte de las líneas de la cabecera (salud / pace) con la mascota robando ancho ---

    // Medidor sintético: 10 px por carácter (la elipsis "…" mide 10). Determinista, sin GDI+/fuentes.
    private static double M(string s) => s.Length * 10.0;

    [Fact]
    public void FitHeaderLine_leaves_right_margin_and_elides_when_too_long()
    {
        // drawX=100, rightEdge=300, margin=Spacing.Md(12) → ancho útil = 300-12-100 = 188 px (18 chars).
        // Texto de 30 chars (300 px) no cabe → se elide y cabe en el ancho útil reservando el margen.
        var shown = DashboardHeader.FitHeaderLine(new string('a', 30), drawX: 100, rightEdge: 300,
            margin: Spacing.Md, measure: M);
        Assert.EndsWith("…", shown);
        Assert.True(M(shown) <= 300 - Spacing.Md - 100,
            $"la línea elidida ({M(shown)}px) rebasa el ancho útil con margen derecho {Spacing.Md}px");
    }

    [Fact]
    public void FitHeaderLine_keeps_text_unchanged_when_it_fits()
    {
        var shown = DashboardHeader.FitHeaderLine("ok", drawX: 100, rightEdge: 300, margin: Spacing.Md, measure: M);
        Assert.Equal("ok", shown);
    }

    [Fact]
    public void FitHeaderLine_never_reaches_the_right_edge()
    {
        // Para cualquier longitud, el texto dibujado termina al menos `margin` px antes del borde.
        const int drawX = 80, rightEdge = 280, margin = Spacing.Md;
        for (int len = 1; len <= 40; len++)
        {
            var shown = DashboardHeader.FitHeaderLine(new string('x', len), drawX, rightEdge, margin, M);
            Assert.True(drawX + M(shown) <= rightEdge - margin + 1e-6,
                $"len {len}: la línea llega a {drawX + M(shown)} (borde {rightEdge}, margen {margin})");
        }
    }

    // --- T8e: el chip de celebración se ELIDE en vez de desaparecer ---

    [Fact]
    public void CelebrationChip_keeps_short_text_intact_and_anchors_left_of_gear()
    {
        var (shown, chip) = DashboardHeader.CelebrationChip("✓ ok", x: 16, gearLeft: 300, y: 0,
            gearSize: 24, textH: 15, M);

        Assert.Equal("✓ ok", shown);
        Assert.True(chip.X >= 16, $"chip.X={chip.X} se sale por la izquierda de x=16");
        Assert.Equal(300 - Spacing.Sm, chip.Right);
    }

    [Fact]
    public void CelebrationChip_elides_long_text_instead_of_disappearing()
    {
        // Antes: si el chip no cabía entre x y el ⚙ (chip.X < x), el destello NO se pintaba. Ahora el
        // texto se elide con elipsis medida y el chip permanece dentro de [x, gearLeft - Sm].
        var (shown, chip) = DashboardHeader.CelebrationChip(new string('w', 40), x: 16, gearLeft: 300, y: 0,
            gearSize: 24, textH: 15, M);

        Assert.NotEqual(string.Empty, shown);
        Assert.EndsWith(TextWrap.Ellipsis, shown);
        Assert.True(chip.X >= 16, $"chip.X={chip.X} se sale por la izquierda de x=16");
        Assert.True(chip.Right <= 300 - Spacing.Sm, $"chip.Right={chip.Right} invade el gutter del ⚙");
    }

    [Fact]
    public void Header_draws_an_elided_celebration_chip_when_the_text_is_too_long()
    {
        // Pixel-check del defecto: con un texto de celebración más ancho que la fila superior, antes no
        // se pintaba NADA a la izquierda del ⚙; ahora debe haber chip (elidido).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        g.Clear(Theme.Dark.Background);
        Rectangle gear = Rectangle.Empty;
        var cfg = Cfg(showMascot: false, liveEnabled: false);
        const string longCeleb = "cuota semanal renovada por completo — a por otra semana de contexto";

        DashboardHeader.Draw(g, draw: true, X, Y, W, snap: null, new LiveSessionsView(), cfg,
            Localization.Get("es"), Theme.Dark, MascotAnimator.StaticState, Mood.Neutral,
            Typography.Body, Typography.Caption, Typography.Mono, ref gear,
            motion: null, reduceMotion: false, mascotBounceOffsetY: 0, celebration: longCeleb);

        var bg = Theme.Dark.Background;
        bool painted = false;
        for (int px = X; px < gear.X - 2 && !painted; px++)
            for (int py = Y; py < Y + 20 && !painted; py++)
            {
                var p = bmp.GetPixel(px, py);
                if (p.R != bg.R || p.G != bg.G || p.B != bg.B) painted = true;
            }
        Assert.True(painted, "el chip de celebración desapareció con texto largo: debe elidirse y pintarse");
    }

    // --- v0.3.5 P0 #4: el header NO duplica la "Sesión (5h)" (barra+reset+pace) de la sección "Cuota" ---

    // Snapshot completo (usage 5h/7d + pace con ETA que agota antes del reset) para ejercitar el glance.
    private static AppSnapshot FullSnap(DateTime now)
    {
        var usage = new RealUsage
        {
            FiveHour = new UsageWindow(62, new DateTimeOffset(now.AddHours(1).AddMinutes(40), TimeSpan.Zero)),
            SevenDay = new UsageWindow(84, new DateTimeOffset(now.AddDays(2).AddHours(6), TimeSpan.Zero)),
        };
        var paceFive = new PaceResult("5h", 62, 1.30, 47.7,
            new DateTimeOffset(now.AddHours(1).AddMinutes(10), TimeSpan.Zero),
            new DateTimeOffset(now.AddHours(1).AddMinutes(40), TimeSpan.Zero), true, PaceStatus.Critical);
        var paceSeven = new PaceResult("7d", 84, 0.95, 88.4, null,
            new DateTimeOffset(now.AddDays(2).AddHours(6), TimeSpan.Zero), false, PaceStatus.Ok);
        return new AppSnapshot
        {
            Usage = usage, LatestState = UsageFetchState.Ok, UsageAtUtc = now,
            Health = new HealthStatus(HealthLevel.Operational, "All Systems Operational"),
            PaceFive = paceFive, PaceSeven = paceSeven
        };
    }

    private static int RunHeaderSnap(Graphics g, bool draw, AppConfig cfg, AppSnapshot? snap)
    {
        Rectangle gear = Rectangle.Empty;
        var live = new LiveSessionsView();
        var s = Localization.Get("es");
        return DashboardHeader.Draw(g, draw, X, Y, W, snap, live, cfg, s, Theme.Dark,
            MascotAnimator.StaticState, Mood.Neutral, Typography.Body, Typography.Caption, Typography.Mono,
            ref gear, motion: null, reduceMotion: false, mascotBounceOffsetY: 0, celebration: null);
    }

    [Fact]
    public void Header_measure_equals_paint_with_full_usage_and_pace()
    {
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: false, liveEnabled: false);
        var snap = FullSnap(DateTime.UtcNow);

        Assert.Equal(RunHeaderSnap(g, draw: false, cfg, snap), RunHeaderSnap(g, draw: true, cfg, snap));
    }

    [Fact]
    public void Header_height_does_not_reserve_quota_bar_reset_and_pace()
    {
        // P0 #4: el header ya NO pinta la barra de cuota (etiqueta+%+barra+reset) ni la línea de pace,
        // que duplicaban la primera fila de "Cuota". Con usage presente el header solo añade UN glance de
        // una línea (~16px) respecto a sin usage: jamás el bloque de barra+reset+pace (~60px). Si se hubiera
        // dejado la barra duplicada, la diferencia sería mucho mayor.
        // T5b: el glance solo vive con la sección Cuota PLEGADA → collapsedQuota: true.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: false, liveEnabled: false, collapsedQuota: true);

        int withUsage = RunHeaderSnap(g, draw: false, cfg, FullSnap(DateTime.UtcNow));
        int noUsage = RunHeaderSnap(g, draw: false, cfg, new AppSnapshot
        {
            Health = new HealthStatus(HealthLevel.Operational, "All Systems Operational"),
            LatestState = UsageFetchState.Ok, UsageAtUtc = DateTime.UtcNow
        });

        int delta = withUsage - noUsage;
        Assert.True(delta > 0, "con usage (y Cuota plegada) debe aparecer el glance de una línea");
        Assert.True(delta <= 20, $"el header solo añade UN glance (~16px), no la barra+reset+pace duplicada (delta={delta})");
    }

    [Fact]
    public void Header_height_independent_of_pace_eta()
    {
        // Como el header ya NO pinta el pace, su alto es idéntico tenga o no el pace una ETA de
        // "agotamiento antes del reset" (lo que antes añadía/movía la línea de pace recortada "⚠ vie 1…").
        // collapsedQuota: true para que el glance exista y el test siga siendo significativo (T5b).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: false, liveEnabled: false, collapsedQuota: true);
        var now = DateTime.UtcNow;

        var withEta = FullSnap(now); // PaceFive.ExhaustsBeforeReset = true (ETA presente)
        var noEta = new AppSnapshot
        {
            Usage = withEta.Usage,
            Health = withEta.Health,
            LatestState = UsageFetchState.Ok, UsageAtUtc = now,
            PaceFive = new PaceResult("5h", 62, 0.80, 47.7, null,
                new DateTimeOffset(now.AddHours(1).AddMinutes(40), TimeSpan.Zero), false, PaceStatus.Ok),
            PaceSeven = withEta.PaceSeven
        };

        Assert.Equal(RunHeaderSnap(g, draw: false, cfg, withEta), RunHeaderSnap(g, draw: false, cfg, noEta));
    }

    // --- v0.3.5 P1 #2: la mascota tiene su PROPIA banda a ancho completo (sin colisión con el estado) ---

    [Fact]
    public void Mascot_band_stacks_above_status_block_not_in_parallel()
    {
        // La mascota ya NO comparte fila con el bloque de estado: ocupa una banda propia ARRIBA y el estado
        // va DEBAJO (apilados, no en paralelo). Por tanto, con mascota + salud el header es más alto que
        // con SOLO salud, en al menos el alto de la banda de la mascota (que ya conocemos de un header con
        // mascota y sin estado). Si fueran en paralelo (diseño viejo) el alto sería el MÁXIMO, no la suma.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);

        var snapHealth = new AppSnapshot
        {
            Health = new HealthStatus(HealthLevel.Operational, "All Systems Operational"),
            LatestState = UsageFetchState.Ok, UsageAtUtc = DateTime.UtcNow
        };

        int mascotOnlyBand = RunHeader(g, draw: false, Cfg(showMascot: true, liveEnabled: false))
                           - RunHeader(g, draw: false, Cfg(showMascot: false, liveEnabled: false));
        Assert.True(mascotOnlyBand > 0, "la banda de la mascota debe reservar alto");

        int statusOnly = RunHeaderSnap(g, draw: false, Cfg(showMascot: false, liveEnabled: false), snapHealth);
        int mascotPlusStatus = RunHeaderSnap(g, draw: false, Cfg(showMascot: true, liveEnabled: false), snapHealth);

        // Apilados: el alto con mascota+estado supera al de solo estado en (aprox.) la banda de la mascota.
        Assert.True(mascotPlusStatus >= statusOnly + mascotOnlyBand - 1,
            $"la mascota debe APILARSE sobre el estado (su banda), no ir en paralelo: " +
            $"mascota+estado={mascotPlusStatus}, soloEstado={statusOnly}, banda={mascotOnlyBand}");
    }

    [Fact]
    public void Mascot_band_measure_equals_paint_with_full_usage_and_health()
    {
        // medir==pintar con la mascota en su banda + salud + glance de cuota (el caso real del dashboard).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: true, liveEnabled: false);
        var snap = FullSnap(DateTime.UtcNow);

        Assert.Equal(RunHeaderSnap(g, draw: false, cfg, snap), RunHeaderSnap(g, draw: true, cfg, snap));
    }

    [Fact]
    public void Mascot_band_measure_equals_paint_with_bounce()
    {
        // El bote (transform de pintado) no rompe el invariante con la banda apilada: medir(0)==pintar(bounce).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: true, liveEnabled: false);

        int measured = RunHeader(g, draw: false, cfg, bounce: 0);
        int painted = RunHeader(g, draw: true, cfg, bounce: 7);
        Assert.Equal(measured, painted);
    }

    [Fact]
    public void Header_measure_equals_paint_with_mascot_and_health_on_narrow_panel()
    {
        // Cabecera estrecha con mascota + salud: el elidido debe ser idéntico en medir y pintar (mismo y).
        using var bmp = new Bitmap(160, 400);
        using var g = Graphics.FromImage(bmp);
        Rectangle gear = Rectangle.Empty;
        var live = new LiveSessionsView();
        var s = Localization.Get("en");
        var cfg = Cfg(showMascot: true, liveEnabled: false);
        var snap = new AppSnapshot { Health = new HealthStatus(HealthLevel.Operational, "Operational") };

        int Run(bool draw) => DashboardHeader.Draw(g, draw, 8, 0, 120, snap, live, cfg, s, Theme.Dark,
            MascotAnimator.StaticState, Mood.Neutral, Typography.Body, Typography.Caption, Typography.Mono,
            ref gear, motion: null, reduceMotion: false, mascotBounceOffsetY: 0, celebration: null);

        Assert.Equal(Run(false), Run(true));
    }

    // --- T5a: el glance elige la ventana de PEOR estado (Critical > Warn > Ok; a igualdad, mayor %) ---

    private static UsageWindow Win(double pct) => new(pct, DateTimeOffset.UtcNow.AddHours(2));

    private static PaceResult Pace(PaceStatus st, double util) =>
        new("x", util, 1.0, util, null, DateTimeOffset.UtcNow.AddHours(2), st == PaceStatus.Critical, st);

    [Fact]
    public void PickWorst_prefers_critical_pace_over_higher_green_percent()
    {
        // El caso de la auditoría (§3 jerarquía): sesión 62% con pace CRÍTICO vs semana 84% en verde.
        // Por mayor % ganaba la semana (84%, Ok) y el header enseñaba verde mientras la sesión ardía.
        var cfg = new AppConfig();
        var a = Win(62); var b = Win(84);

        var worst = DashboardHeader.PickWorst(a, Pace(PaceStatus.Critical, 62), b, Pace(PaceStatus.Ok, 84), cfg);

        Assert.Same(a, worst);
    }

    [Fact]
    public void PickWorst_prefers_warn_pace_over_ok_with_higher_percent()
    {
        // Over (Warn) en la ventana baja gana a Ok en la alta: el estado manda sobre el %.
        var cfg = new AppConfig();
        var a = Win(30); var b = Win(60);

        var worst = DashboardHeader.PickWorst(a, Pace(PaceStatus.Over, 30), b, null, cfg);

        Assert.Same(a, worst);
    }

    [Fact]
    public void PickWorst_falls_back_to_higher_percent_on_equal_status()
    {
        var cfg = new AppConfig();
        // Ambas Ok (sin pace, bajo el umbral warn 70) → mayor % (comportamiento previo conservado).
        var a = Win(40); var b = Win(60);
        Assert.Same(b, DashboardHeader.PickWorst(a, null, b, null, cfg));

        // Ambas Warn por pace → de nuevo mayor %.
        var c = Win(50); var d = Win(72);
        Assert.Same(d, DashboardHeader.PickWorst(c, Pace(PaceStatus.Over, 50), d, Pace(PaceStatus.Over, 72), cfg));
    }

    [Fact]
    public void PickWorst_uses_config_thresholds_when_no_pace()
    {
        // Sin pace cae a los umbrales de config (mismo criterio que QuotaBar): 91% ≥ crit(90) → Critical.
        var cfg = new AppConfig(); // warn 70 / crit 90 por defecto
        var a = Win(91); var b = Win(84);

        Assert.Equal(UsageStatus.Critical, DashboardHeader.WindowStatus(a, null, cfg));
        Assert.Equal(UsageStatus.Warn, DashboardHeader.WindowStatus(b, null, cfg));
        Assert.Same(a, DashboardHeader.PickWorst(a, null, b, null, cfg));
    }

    [Fact]
    public void PickWorst_handles_missing_windows()
    {
        var cfg = new AppConfig();
        var a = Win(10);

        Assert.Same(a, DashboardHeader.PickWorst(a, null, null, null, cfg));
        Assert.Same(a, DashboardHeader.PickWorst(null, null, a, null, cfg));
        Assert.Null(DashboardHeader.PickWorst(null, null, null, null, cfg));
    }

    // --- T5b: el glance desaparece cuando la sección "Cuota" está EXPANDIDA (duplicaba su 1ª fila) ---

    [Fact]
    public void Glance_hidden_when_quota_section_expanded()
    {
        // Con Cuota expandida las barras completas van justo debajo del header: el glance duplicaba
        // la fila "Week (7d) · 84%" (§3 jerarquía). Expandida (CollapsedQuota=false) el header debe
        // medir lo MISMO con y sin usage (cero filas de glance).
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: false, liveEnabled: false, collapsedQuota: false);

        int withUsage = RunHeaderSnap(g, draw: false, cfg, FullSnap(DateTime.UtcNow));
        int noUsage = RunHeaderSnap(g, draw: false, cfg, new AppSnapshot
        {
            Health = new HealthStatus(HealthLevel.Operational, "All Systems Operational"),
            LatestState = UsageFetchState.Ok, UsageAtUtc = DateTime.UtcNow
        });

        Assert.Equal(noUsage, withUsage);
    }

    [Fact]
    public void Glance_measure_equals_paint_when_quota_collapsed()
    {
        // medir==pintar con el glance VISIBLE (Cuota plegada): la condición de visibilidad no depende
        // de la pasada (draw), así que ambas reservan la misma fila.
        using var bmp = NewBmp();
        using var g = Graphics.FromImage(bmp);
        var cfg = Cfg(showMascot: false, liveEnabled: false, collapsedQuota: true);
        var snap = FullSnap(DateTime.UtcNow);

        Assert.Equal(RunHeaderSnap(g, draw: false, cfg, snap), RunHeaderSnap(g, draw: true, cfg, snap));
    }

    // --- F12 (v0.3.9): el espaciado del header se TOKENIZA sin cambiar el aspecto (mismos valores) ---
    // Antes eran literales mágicos sueltos (los históricos 50/22/14/6 a 96 DPI → 24/16/8/4/12/10 dentro
    // de Dpi.Scale). Tokenizados (patrón de DashboardSettingsView), a factor 1.0 deben resolver EXACTO a
    // los valores previos (render-test pixel-perfect) y seguir escalando con el DPI.

    [Fact]
    public void Header_spacing_tokens_resolve_to_previous_literals_at_96dpi()
    {
        // A factor ambiente 1.0 (default de los tests) cada token == su literal histórico.
        Assert.Equal(24, DashboardHeader.GearSize);
        Assert.Equal(16, DashboardHeader.StatusRowHeight);
        Assert.Equal(8, DashboardHeader.HealthDotSize);
        Assert.Equal(4, DashboardHeader.HealthDotInsetY);
        Assert.Equal(12, DashboardHeader.HealthDotGutter);
        Assert.Equal(4, DashboardHeader.DividerAir);
        Assert.Equal(10, DashboardHeader.DividerBottomGap);
    }

    [Fact]
    public void Header_spacing_tokens_scale_with_dpi()
    {
        // Siguen pasando por Dpi.Scale: a 150% crecen como el resto del layout (no quedan fijos a 96).
        try
        {
            Dpi.Apply(144);
            Assert.Equal(Dpi.Scale(24, 1.5f), DashboardHeader.GearSize);
            Assert.Equal(Dpi.Scale(16, 1.5f), DashboardHeader.StatusRowHeight);
            Assert.Equal(Dpi.Scale(8, 1.5f), DashboardHeader.HealthDotSize);
            Assert.Equal(Dpi.Scale(10, 1.5f), DashboardHeader.DividerBottomGap);
        }
        finally { Dpi.Apply(96); }
    }
}
