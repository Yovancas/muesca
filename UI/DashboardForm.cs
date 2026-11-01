using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.Services.Motion;

namespace ClaudeBarWin.UI;

/// <summary>
/// Borderless popup near the tray. Shows real 5h/7d quota, per-model weekly limits,
/// local spend estimate, service health, and an integrated usage chart. The window
/// height auto-fits whichever sections are enabled. Theme/position/sticky/on-top configurable.
/// </summary>
public sealed class DashboardForm : Form
{
    // Datos del gráfico (Tabs y, desde T13a, las series dinámicas por familia) en DashboardDataView.

    private AppSnapshot? _snap;
    private AppConfig _cfg = new();
    // Estado del Pro para Ajustes (candado en las filas Pro del notch); lo pone la bandeja. Sin decir nada, sin candados.
    private Services.Licensing.ProState _pro = Services.Licensing.ProState.Pro;
    private PlanInfo _plan = new("", "");
    private Strings _s = new();
    private Theme _theme = Theme.Dark;

    // Caché GDI+ del panel (F3 Task 3): pinceles/plumas/fuentes reutilizados entre frames. Se publica
    // como caché ambiente mientras se mide/pinta y se invalida al cambiar de tema o de DPI.
    private readonly GdiCache _gdi = new();
    private readonly System.Windows.Forms.Timer _tick;

    // ---- Motion (F3): reloj bajo demanda + fade de apertura ----
    // Stopwatch monótono (inmune a cambios de hora) propiedad del form: la única fuente de
    // tiempo del motor. Cada tick pasa el delta a los AnimatedValue. El MotionScheduler decide
    // la cadencia del _tick (33 ms si algo anima + panel visible; 1000 ms countdown; parar si oculto).
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly MotionScheduler _scheduler = new();
    private double _lastTickMs;                 // marca del último Advance, para el delta
    private double _openedAtMs;                 // elapsed del clock al abrir el panel (stagger, Tarea 4)
    private double _targetOpacity = 1.0;        // opacidad de destino (config); la animada va por _fade
    // Opacidad 0..1 con Rápido (lenguaje de movimiento §4 #1–#2; el umbral fino 0,002 hace invisible el
    // encaje final). Se aplica como alfa de la ventana layered (SetLayeredWindowAttributes), nunca con
    // Form.Opacity: WinForms quita WS_EX_LAYERED al llegar a 1,0 y vuelve el destello.
    private readonly SpringValue _fade = new(1.0, Motion.Rapido);
    private bool _closing;                       // fade de cierre en vuelo: Hide() cuando el alfa < 0,03
    private bool _closeShift;                    // el contenido se desplaza hacia la bandeja (cierre o su vuelta)
    private int _trayDir = 1;                    // +1 bandeja abajo, −1 arriba (entrada escalonada y cierre)

    // Tween de números/barras (Tarea 2): AnimatedValue por clave ("bar:5h"/"bar:7d"/"num:crit"/"pace").
    // UpdateData hace SetTarget(target); el paint muestrea Display(); el tick hace Advance(delta). El color
    // de las barras va por el objetivo (no parpadea); solo el ancho/número deslizan. La cabecera y las
    // secciones lo reciben como sampler opcional. Con reduce-motion (Tarea 7) el Display devuelve el target.
    private readonly MotionState _motion = new();
    private bool _reduceMotion;                  // gate único de reduce-motion (Tarea 7 lo enchufa a config; default OFF)

    // Vida de la mascota (Tarea 5): el MascotAnimator (puro) elige frame/blink/spinner/verbo a partir
    // del tiempo en la fase actual + una semilla estable. La identidad del gato (fase y su reloj, humor
    // con histéresis+decay, semilla y último Salto) vive en el MascotDirector (lenguaje de movimiento §5),
    // fuera del form. Con reduce-motion el animador devuelve el frame base y no hay saltos.
    //
    // Salto de atención + celebración de reset (§4 #7): al entrar en una fase que pide atención
    // (WaitingFor*) o al celebrar una renovación de cuota, el gato salta con gravedad (Hop) según el
    // calendario de atención (entrada, 3,2 / 6,4 / 9,6 / 12,8 / 22,8 s; después, quieto). La celebración
    // (ResetDetector en UpdateData) enciende además el humor Happy y el chip "✓ cuota renovada".
    private readonly MascotDirector _mascot = new();
    // Ojos que miran el cursor (§5): solo con WM_MOUSEMOVE de la propia ventana; vuelven al centro 1,5 s
    // después de salir. _faceCenter lo reporta la cabecera al pintar (coordenadas de layout).
    private readonly EyeTracker _eyes = new();
    private Point _faceCenter;
    private readonly ResetDetector _resetDetector = new();
    private double _celebrationUntilMs = double.NegativeInfinity; // elapsed hasta el que dura el destello
    private bool _resetPending;                  // hay un reset detectado sin "consumir" en el humor todavía

    /// <summary>Tiempo (ms) en la fase actual de la mascota (elapsed crudo del reloj de fase).</summary>
    private double MascotElapsedMs() =>
        _renderOverride is { } o ? o.MascotElapsedMs
        : _mascot.PhaseElapsedMs(_clock.Elapsed.TotalMilliseconds);

    /// <summary>Humor vigente de la mascota: el override del render-test manda si está presente.</summary>
    private Mood MascotMoodCurrent() =>
        _renderOverride is { MascotMood: { } m } ? m : _mascot.Mood;

    /// <summary>
    /// Muestrea el estado del animador para la fase global vigente. La puerta única de reduce-motion
    /// se PROPAGA al animador (<c>reduceMotion: _reduceMotion</c>), que colapsa al frame base estático
    /// (sin spinner ni jitter). No se confía en elapsed=0 para suprimir el spinner: <c>Sample(Processing,
    /// 0)</c> aún devolvería el glifo <c>SpinnerSequence[0]</c>, así que el gate va por el flag.
    /// En cada toque de suelo del Salto la cara alternativa dura 90 ms (micro-interacción 1).
    /// </summary>
    private MascotState SampleMascot()
    {
        var phase = _liveView.GlobalPhase;
        var st = MascotAnimator.Sample(phase, MascotElapsedMs(), _mascot.Seed, _reduceMotion);
        if (!_reduceMotion && _renderOverride is null && _mascot.Landing(_clock.Elapsed.TotalMilliseconds))
            st = st with { Face = MascotDirector.LandingFace(MascotSprite.BaseFace(phase), _mascot.Mood) };
        if (!_reduceMotion && _renderOverride is null)
        {
            var (ex, ey) = _eyes.Offset;
            st = st with { EyeDx = ex, EyeDy = ey };
        }
        return st;
    }

    /// <summary>
    /// Sincroniza el <see cref="MascotDirector"/> con la fase global: reinicia el reloj de fase si
    /// cambió, empuja el humor con el evento derivado y (re)programa los saltos de atención mientras la
    /// fase pida atención O haya una celebración de reset en curso (T-v039 F3c). Los saltos exigen
    /// live + mascota visibles; con reduce-motion se cancelan. Idempotente por tick.
    /// </summary>
    private void SyncMascotPhase()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        var phase = _liveView.GlobalPhase;

        // Una celebración de reset recién detectada se "consume" como evento de humor (Happy) una vez;
        // tiene prioridad sobre la fase salvo que esta pida atención (Alert manda).
        MoodEvent ev = _resetPending && !phase.NeedsAttention()
            ? MoodEvent.ResetCelebrated
            : MoodEventFor(phase);
        _resetPending = false;
        bool wantsHops = (phase.NeedsAttention() || CelebrationActive())
                         && _cfg.LiveSessionsEnabled && _cfg.ShowMascot;
        _mascot.Sync(phase, ev, now, wantsHops, _reduceMotion);
    }

    private static MoodEvent MoodEventFor(SessionPhase phase) => phase switch
    {
        SessionPhase.WaitingForApproval or SessionPhase.WaitingForInput => MoodEvent.AttentionRequired,
        SessionPhase.Processing or SessionPhase.Compacting => MoodEvent.LongProcessing,
        _ => MoodEvent.None,
    };

    /// <summary>
    /// Offset (px enteros ≥ 0) del Salto de la mascota en el instante actual (0 si reduce-motion). La
    /// altura se proyecta al DPI aquí, en el punto de uso; los tiempos no escalan.
    /// </summary>
    private double MascotBounceOffsetY()
    {
        if (_renderOverride is { } o) return _reduceMotion ? 0 : o.MascotBounceOffsetY;
        if (_reduceMotion) return 0;
        return _mascot.HopOffsetPx(_clock.Elapsed.TotalMilliseconds, Dpi.Factor);
    }

    /// <summary>Margen (px) alrededor de la banda de la mascota para su repintado parcial: pico del
    /// Salto (sin pasada: la gravedad no rebasa el pico) + 2 px de antialias.</summary>
    private static int MascotDirtyPad => (int)Math.Ceiling(Dpi.ScaleF(Motion.BounceAmplitudePx)) + 2;

    /// <summary>
    /// ¿Lo único que cambió en la mascota desde el tick anterior es el ángulo del spinner? (sin Salto en
    /// vuelo ni mezcla de color, y con el mismo frame, cara, verbo y cruce del verbo).
    /// </summary>
    private bool OnlySpinnerMoved(MascotState now)
        => _lastTickMascot is { } prev && !BounceActive() && !_mascotColor.IsAnimating
           && now.SpinnerGlyph != '\0' && VerbBlendSettled(prev) && VerbBlendSettled(now)
           && prev with { SpinnerAngleDeg = now.SpinnerAngleDeg, SpinnerGlyph = now.SpinnerGlyph } == now;

    private static bool VerbBlendSettled(MascotState st) => st.VerbBlend >= 1.0;

    /// <summary>¿Sigue activo el Salto (botes + última cara de aterrizaje), para alimentar al scheduler?</summary>
    private bool BounceActive() => !_reduceMotion && _mascot.HopActive(_clock.Elapsed.TotalMilliseconds);

    /// <summary>¿Está visible el destello de celebración de reset ahora mismo?</summary>
    private bool CelebrationActive() =>
        _renderOverride is { } o ? (!_reduceMotion && o.Celebrating)
        : (!_reduceMotion && _clock.Elapsed.TotalMilliseconds < _celebrationUntilMs);

    /// <summary>Texto del destello de celebración ("✓ cuota renovada" lo monta la cabecera) o null.</summary>
    private string? CelebrationText() => CelebrationActive() ? _s.QuotaRenewed : null;

    /// <summary>ms desde que se disparó la celebración en curso.</summary>
    private double CelebrationElapsedMs() =>
        _clock.Elapsed.TotalMilliseconds - (_celebrationUntilMs - ChipMotion.TotalMs);

    /// <summary>Fotograma del chip de celebración (§4 #6); pleno en el render-test.</summary>
    private ChipMotion.Frame CelebrationChipFrame() =>
        _renderOverride is not null ? ChipMotion.Frame.Full : ChipMotion.Sample(CelebrationElapsedMs(), Dpi.Factor);

    /// <summary>¿El chip está entrando o saliendo (repintado completo)?</summary>
    private bool CelebrationChipAnimating() =>
        CelebrationActive() && _renderOverride is null && ChipMotion.IsAnimating(CelebrationElapsedMs());

    // Crossfade del color de fase/humor de la mascota (F3 Task 6): antes cambiaba de golpe.
    private readonly ColorCrossfade _mascotColor = new(Color.Empty);
    private bool _mascotColorPrimed;              // false ⇒ el próximo objetivo se fija sin animar

    /// <summary>Apunta el crossfade al color de la fase/humor vigentes (salta si no hay color previo o con reduce-motion).</summary>
    private void SyncMascotColor()
    {
        var target = MascotRenderer.MoodColor(_theme, _liveView.GlobalPhase, _mascot.Mood);
        _mascotColor.SetTarget(target, snap: !_mascotColorPrimed || _reduceMotion);
        _mascotColorPrimed = true;
    }

    // Hover (Tarea 3): clave del rect interactivo bajo el cursor (o null). OnMouseMove la recalcula
    // sobre los diccionarios de rects ya existentes vía HoverHitTest; si cambia, repinta. El realce es
    // un fondo redondeado HoverBg DETRÁS del rect, dibujado solo en la pasada de pintado: nunca toca el
    // layout. F3 Task 2: HoverHighlight (muelles) conserva el último rect durante el fade-out y se
    // desliza de un elemento a otro en vez de saltar.
    private readonly HoverHighlight _hover = new();
    // Destello al pulsar una fila de sesión (micro-interacción 3): +14 % y vuelta con Rápido.
    private readonly PressFlash _press = new();

    // Repintado parcial (F3 Task 1): con Claude trabajando solo se mueve la mascota, así que el tick
    // invalida su celda (rect de la banda que reporta la cabecera, inflado por el bote) en vez del
    // panel entero. En reposo el tick lento solo repinta si cambia la firma (minuto, texto del pie).
    private Rectangle _mascotRect;
    private Rectangle _chipRect;                 // rect del chip de celebración (repintado parcial, §4 #6)
    private Rectangle _spinnerRect;              // rect del arco del spinner en el último pintado (§4 #8)
    private MascotState? _lastTickMascot;        // estado de la mascota en el tick anterior
    private Rectangle? _paintClip;               // zona sucia del OnPaint en curso (null fuera de él)
    private InvalidationKind _lastPlanned = InvalidationKind.Full;
    private bool _celebrationPainted;            // estado del chip en el último plan (su borde fuerza Full)
    private bool _chipWasMoving;                 // el chip se movía en el tick anterior (pinta su último fotograma)
    private SlowTickSignature? _lastSlowSig;
    private double _lastSigCheckMs = double.NegativeInfinity;

    /// <summary>Repintados completos pedidos por el tick (diagnóstico de CPU: ≈0 con solo la mascota viva).</summary>
    internal int FullTickRepaints { get; private set; }

    // ---- Override de tiempo de motion para el render-test (Tarea 8) ----
    // El render es offline (sin reloj de UI ni ticks): para capturar las microinteracciones en un
    // FOTOGRAMA FIJO, PrepareForRender puede inyectar un override determinista (tSinceOpen, hover,
    // tiempo/humor de la mascota, destello de celebración). Cuando está presente, los muestreadores
    // de motion leen de aquí en vez del Stopwatch vivo. En la app real es siempre null (cuelga del reloj).
    private RenderMotionOverride? _renderOverride;

    /// <summary>
    /// Estado de motion congelado que el render-test inyecta para capturar las microinteracciones a
    /// medio camino sin reloj real. Todo es puro/determinista: no toca el <see cref="Stopwatch"/>.
    /// </summary>
    public readonly record struct RenderMotionOverride(
        double TSinceOpenMs,
        string? HoveredKey = null,
        double MascotElapsedMs = 0,
        Mood? MascotMood = null,
        bool Celebrating = false,
        int MascotBounceOffsetY = 0);

    /// <summary>
    /// Resuelve el gate ÚNICO de reduce-motion para un <paramref name="cfg"/>: lee
    /// <c>cfg.ReduceMotion</c> (default <c>false</c> = animaciones ON, decisión de Yovan). Este es el
    /// único punto del que cuelga todo el colapso a estado final: <see cref="MotionState.SetTarget"/>
    /// hace snap, <see cref="MotionScheduler"/> no pide fast-tick, <see cref="MascotAnimator"/> queda
    /// en frame base y stagger/bounce/fade van a su estado final. El default NO depende del SO; existe
    /// <see cref="MotionPrefs.OsReducedMotion"/> para una futura opción "seguir Windows".
    /// </summary>
    private static bool ResolveReduceMotion(AppConfig cfg) => cfg.ReduceMotion;

    /// <summary>
    /// Tiempo (ms) transcurrido desde la apertura del panel, para la entrada escalonada (Tarea 4).
    /// En vivo = reloj − <c>_openedAtMs</c>. Con reduce-motion devuelve +∞ ⇒ todas las secciones
    /// asentadas (offset 0) por el gate de <see cref="Stagger"/>. (Tarea 8 añadirá un override de
    /// tiempo para el render-test; aquí basta con el reloj.)
    /// </summary>
    private double TSinceOpenMs() =>
        _reduceMotion ? double.PositiveInfinity
        : _renderOverride is { } o ? o.TSinceOpenMs
        : _clock.Elapsed.TotalMilliseconds - _openedAtMs;

    /// <summary>¿Sigue en vuelo la entrada escalonada (cabecera, secciones o footer)?</summary>
    private bool StaggerActive()
    {
        if (_reduceMotion) return false;
        int last = DashboardDataView.VisibleSectionCount(_cfg, _snap) + 1; // índice del footer
        return Stagger.IsActive(TSinceOpenMs(), last, Dpi.Factor);
    }

    private DateTime _shownAtUtc = DateTime.MinValue;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow; // para no marcar stale durante la 1ª fetch
    // Footer (fix F3 Tarea 8): "ahora" CONGELADO por repaint para que medir(draw=false) y pintar(draw=true)
    // del footer produzcan las mismas líneas, y firma del último footer pintado para detectar (en el tick)
    // cuándo el nº de líneas cambió (fresh→stale / cruce de wrap del relativo) y hace falta Relayout().
    private DateTime _footerNowUtc = DateTime.MinValue;
    private (int Stale, int Footer, int Seal) _lastFooterSig;
    private bool _sticky;
    private bool _menuOpen;
    private string _appliedPlacement = "";

    private bool _dragging;
    private Point _dragOffset;
    private Rectangle _closeRect;

    // View mode (v0.3): "data" muestra cabecera + secciones; "settings" muestra el panel de ajustes.
    private string _viewMode = "data"; // "data" | "settings"
    private Rectangle _gearRect, _backRect;
    private readonly Dictionary<string, Rectangle> _sectionRects = new();   // "quota"/"sessions"/"spend"/"chart"
    private readonly Dictionary<string, Rectangle> _settingsRects = new();  // clave de acción ("toggle:X"/"theme:dark"/…)

    // Scroll del panel de ajustes (v0.3.7): el panel se LIMITA en alto (MaxPanelHeightPct del área
    // útil) y el contenido rueda. El offset/altos viven aquí; la matemática pura en DashboardSettingsView.
    private int _settingsScroll;                 // desplazamiento actual del contenido (px, 0 = arriba)
    // Acumulador de la rueda (v0.3.7+): los trackpads de precisión mandan muchos eventos pequeños
    // (±1…±40, smooth-scrolling) en vez del diente de ±120 del ratón clásico. WheelToPixels acumula aquí
    // el delta (en su dominio escalado, opaco) y solo convierte a píxeles la parte entera, conservando el
    // resto para el siguiente evento ⇒ scroll suave con trackpad sin cambiar la sensación del ratón.
    // Se resetea junto con _settingsScroll (en ShowSettings) para no arrastrar resto entre aperturas.
    private int _settingsWheelAccum;
    private int _settingsContentH;               // alto real del contenido de ajustes (sin tope)
    private int _settingsViewportTop;            // y donde arranca la zona scrollable (bajo "‹ Ajustes")
    private static int SettingsViewportBottomPad => Dpi.Scale(12); // aire entre el final del viewport y el borde (T11)
    private readonly Dictionary<string, Rectangle> _scratchRects = new();   // dict desechable para MEDIR

    // Chart
    private Func<ChartRange, Task<List<HistoryBucket>>>? _historyProvider;
    private Func<ChartRange, Task<List<PctPoint>>>? _pctProvider;

    // Live sessions (mascot + instance list)
    private Func<LiveSessionsView>? _liveProvider;
    private LiveSessionsView _liveView = new();
    private ChartRange _chartRange = ChartRange.Hours5;
    private string _chartMode = "spend";       // "spend" | "percent"
    private string _chartPctWindow = "7d";     // "5h" | "7d"
    private List<HistoryBucket> _chartData = new();
    private List<PctPoint> _pctData = new();
    private bool _chartLoading;
    private readonly Dictionary<ChartRange, Rectangle> _tabRects = new();
    private readonly Dictionary<string, Rectangle> _modeRects = new();   // "spend"/"percent"
    private readonly Dictionary<string, Rectangle> _pctWinRects = new(); // "5h"/"7d"
    private readonly Dictionary<string, Rectangle> _liveRowRects = new(); // sessionId → row

    public event Action<Point>? Moved;
    public event Action<string>? ChartModeChanged;
    public event Action<string>? ChartWindowChanged;
    public event Action<string>? SessionClicked;

    /// <summary>Emitido cuando el panel de ajustes cambia un valor: el host lo aplica vía MutateConfig.</summary>
    public event Action<Action<AppConfig>>? SettingsChanged;

    /// <summary>Emitido cuando se clica una fila del panel cuya clave NO es mutación simple de config
    /// (claves "special:*", p.ej. "special:importtheme"/"special:hooktoggle"). El host las maneja.</summary>
    public event Action<string>? SpecialActionRequested;

    /// <summary>
    /// Cambia a la vista de ajustes (⚙). F10 (v0.3.9, state-preservation): NO resetea
    /// <see cref="_settingsScroll"/> — reabrir el panel conserva la posición de scroll de la sesión
    /// (antes saltaba siempre arriba). El acumulador sub-píxel de la rueda sí se limpia (residuo de
    /// trackpad, irrelevante para la posición). <see cref="LayoutContent"/> ya ACOTA el scroll guardado
    /// con <see cref="DashboardSettingsView.ClampScroll"/> en cada pasada, así que si el contenido
    /// encogió (p.ej. menos filas) el pulgar NUNCA queda fuera de rango. Reajusta el alto y repinta.
    /// </summary>
    public void ShowSettings() { _viewMode = "settings"; _settingsWheelAccum = 0; Relayout(); Invalidate(); }

    // ---- Ganchos SOLO para el render de GIFs (--render-gif): conducir el scroll de ajustes a mano ----
    // En vivo el scroll lo gobierna la rueda (OnMouseWheel); para el GIF de "ajustes" necesitamos barrer
    // el desplazamiento fotograma a fotograma de forma determinista. Estos dos miembros internal exponen
    // SOLO lo justo para eso, sin tocar el comportamiento en runtime: nada en la app llama a esto.

    /// <summary>
    /// Fija el scroll del panel de ajustes para el render (px desde arriba). NO resetea el acumulador de
    /// rueda ni relayoutea: a diferencia de <see cref="ShowSettings"/> (que pone el scroll a 0), aquí solo
    /// se siembra el valor y se repinta; el siguiente <c>DrawToBitmap</c> lo acota a [0, overflow] en
    /// <see cref="LayoutContent"/> y dibuja el contenido desplazado. Llamar <see cref="ShowSettings"/> UNA
    /// vez antes del barrido (resetea a 0) y luego este por cada fotograma.
    /// </summary>
    internal void SetSettingsScrollForRender(int px) { _settingsScroll = Math.Max(0, px); Invalidate(); }

    /// <summary>
    /// Overflow máximo de scroll del panel de ajustes = <c>max(0, contentH − viewportH)</c>, calculado con
    /// los campos que ya puebla <see cref="LayoutContent"/> (alto real del contenido y top del viewport) y
    /// el <see cref="Control.Height"/> ya acotado al tope. Solo es válido tras un primer
    /// <c>DrawToBitmap</c> en modo ajustes (esos campos quedan poblados). 0 si el contenido cabe entero.
    /// </summary>
    internal int SettingsMaxScrollForRender
    {
        get
        {
            // Mismo viewportH que usan OnMouseWheel/LayoutContent: alto útil bajo el chrome + "‹ Ajustes".
            int viewportH = Height - _settingsViewportTop - SettingsViewportBottomPad;
            return Math.Max(0, _settingsContentH - viewportH);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---- DPI (T11, §2 P0 #1): geometría base del panel en px de diseño (96 DPI) ----
    // Las fuentes (en puntos) escalaban solas con el DPI del Graphics; la geometría era px fijos →
    // al 125/150% el texto crecía dentro de filas que no, con solapes y un panel de 340px enano.
    // ApplyDpiScale proyecta ancho/padding con Dpi.Scale; el alto lo recalcula Relayout (auto-fit).
    private const int BaseWidth = 340, BaseHeight = 380, BasePanelPad = 18;

    public DashboardForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(Dpi.Scale(BaseWidth), Dpi.Scale(BaseHeight));
        BackColor = _theme.Background;
        DoubleBuffered = true;
        TopMost = true;
        Padding = new Padding(Dpi.Scale(BasePanelPad));

        _tick = new System.Windows.Forms.Timer { Interval = Motion.SlowTickMs };
        _tick.Tick += (_, _) => OnMotionTick();
    }

    /// <summary>
    /// Sincroniza el factor de escala ambiente (<see cref="Dpi.Apply"/>) con el DPI real de la ventana
    /// y reaplica la geometría base (ancho fijo + padding); el alto lo recalcula <see cref="Relayout"/>.
    /// Se llama al abrir (<see cref="ShowConfigured"/>) y al cambiar de monitor (<see cref="OnDpiChanged"/>).
    /// A propósito NO se llama desde <see cref="PrepareForRender"/>: el harness de render queda a
    /// factor 1.0 determinista (PNG idénticos a 96 DPI sea cual sea el monitor de la máquina).
    /// </summary>
    private void ApplyDpiScale()
    {
        Dpi.Apply(DeviceDpi);
        _gdi.Invalidate();
        Width = Dpi.Scale(BaseWidth);
        Padding = new Padding(Dpi.Scale(BasePanelPad));
    }

    /// <summary>
    /// WM_DPICHANGED del form top-level (colocación/arrastre a un monitor con otro DPI bajo
    /// PerMonitorV2): re-escala la geometría y re-layouta. Nota: el <c>OnDpiChangedAfterParent</c> que
    /// cita el spec aplica a CONTROLES hijos; en un Form top-level el aviso llega por
    /// <see cref="Form.OnDpiChanged"/> — se overridean ambos para cubrir también un reparenting futuro.
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpiScale();
        Relayout();
        Invalidate();
    }

    /// <inheritdoc cref="OnDpiChanged"/>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDpiScale();
        Relayout();
        Invalidate();
    }

    /// <summary>
    /// Latido del motor. Bajo demanda: integra el tiempo transcurrido en los AnimatedValue
    /// activos, ajusta el intervalo del timer vía <see cref="MotionScheduler"/> (33 ms si algo
    /// anima, 1000 ms si solo countdown, parar si oculto) y solo repinta si algo cambió. Con el
    /// panel oculto no hace trabajo (invariante CPU 24/7).
    /// </summary>
    private void OnMotionTick()
    {
        if (!Visible) { _tick.Stop(); return; }

        double now = _clock.Elapsed.TotalMilliseconds;
        double delta = now - _lastTickMs;
        _lastTickMs = now;
        if (delta < 0) delta = 0;

        bool fading = !_fade.IsSettled;
        if (fading)
        {
            MotionState.AdvanceSpring(_fade, delta);
            ApplyFadeOpacity();
        }
        if (_closing && PanelChoreography.ShouldHide(_fade.Value))
        {
            // Fade de cierre casi en 0 (~180 ms): ahora sí se oculta (OnVisibleChanged para el reloj).
            _closing = false;
            Hide();
            return;
        }
        if (!_closing && _fade.IsSettled) _closeShift = false; // reabierto del todo: sin desplazamiento

        // Alto anclado al borde con muelle (Task 6): posición+tamaño en una llamada por fotograma.
        bool resizing = !_heightSpring.IsSettled;
        if (resizing)
        {
            MotionState.AdvanceSpring(_heightSpring, delta);
            ApplyBounds((int)Math.Round(_heightSpring.Value));
        }

        // Tween de números/barras: integra el delta en todos los AnimatedValue por clave.
        _motion.Advance(delta);

        // Hover (Tarea 3): fade y deslizamiento del realce con el mismo reloj (muelles en segundos).
        if (_hover.IsAnimating) _hover.Advance(delta / 1000.0);
        if (_press.IsAnimating) _press.Advance(delta / 1000.0);
        _eyes.Advance(delta / 1000.0, now);

        // Vida de la mascota (Tarea 5): reloj de fase + humor (histéresis/decay). El animador es
        // puro y se muestrea en el paint; aquí solo sincronizamos fase/humor con el reloj.
        SyncMascotPhase();
        SyncMascotColor();
        if (_mascotColor.IsAnimating) _mascotColor.Advance(delta / 1000.0);
        bool mascotAlive = !_reduceMotion && _cfg.LiveSessionsEnabled && _cfg.ShowMascot
                           && MascotAnimator.IsAnimatedPhase(_liveView.GlobalPhase);

        bool mascotAnimating = mascotAlive || BounceActive()
                               || (_cfg.ShowMascot && (_mascotColor.IsAnimating || _eyes.IsAnimating));
        var mascotNow = SampleMascot();
        bool celebration = CelebrationActive();
        bool staggering = StaggerActive();
        bool animating = fading || resizing || _motion.IsAnimating
                         || _hover.IsAnimating || _press.IsAnimating || mascotAnimating
                         || celebration || staggering;

        // Footer: congela SIEMPRE el "ahora" común de este repaint (medir/pintar leen el mismo instante)
        // y deja que el relativo ("hace N min") avance aunque haya una animación larga en curso. Es solo
        // una asignación, coste cero.
        _footerNowUtc = DateTime.UtcNow;

        // Red de seguridad del footer (fix F3 Tarea 8): si el nº de líneas del footer cambió desde el
        // último pintado (fresh→stale / cruce de wrap del relativo), re-mide el alto ANTES de invalidar
        // para no recortar el sello. La MEDICIÓN (CreateGraphics()+MeasureString del footer) corre como
        // mucho a 1 Hz junto con la firma del tick lento (abajo), nunca en cada fast-tick: el nº de
        // líneas solo cambia por el reloj de pared, que muta ~1/min, nunca por las animaciones.

        // Repintado parcial (Task 1): el chip de celebración no se anima, pero su aparición/desaparición
        // cuenta como cambio de todo el panel. El último fotograma de una animación (ya asentada tras el
        // Advance de arriba) se pinta con el mismo alcance que el anterior para no dejar un frame viejo.
        bool celebrationEdge = celebration != _celebrationPainted;
        _celebrationPainted = celebration;
        // El fundido de opacidad es alfa de ventana (no repinta); solo el cierre desplaza el contenido.
        var planned = InvalidationPlanner.Plan(!_fade.IsSettled && _closeShift, staggering,
            _motion.IsAnimating || celebrationEdge || resizing,
            _hover.IsAnimating || _press.IsAnimating, mascotAnimating);
        var paint = planned;
        if (_lastPlanned == InvalidationKind.Full) paint = InvalidationKind.Full;
        else if (_lastPlanned == InvalidationKind.MascotOnly && paint == InvalidationKind.None) paint = InvalidationKind.MascotOnly;
        _lastPlanned = planned;

        // Firma del tick lento (≤1 Hz animando; en cada despertar quieto): minuto de pared + texto del pie
        // + cuentas atrás. Las cuentas atrás van por minutos y el relativo del pie avanza a destiempo; si
        // ninguno cambió, no hay nada nuevo que pintar fuera de la mascota. De paso re-mide el pie.
        if (paint != InvalidationKind.Full
            && (!animating || now - _lastSigCheckMs >= Motion.SlowTickMs - Motion.FastTickMs))
        {
            _lastSigCheckMs = now;
            var (relayouted, sig) = ReconcileFooterHeight();
            if (relayouted || InvalidationPlanner.SlowTickNeedsRepaint(_lastSlowSig, sig))
                paint = InvalidationKind.Full;
            _lastSlowSig = sig;
        }

        if (paint == InvalidationKind.Full)
        {
            FullTickRepaints++;
            Invalidate();
        }
        else if (paint == InvalidationKind.MascotOnly && !_mascotRect.IsEmpty)
        {
            // §4 #8: si desde el tick anterior solo ha girado el spinner (mismo frame, cara, verbo y
            // cruce; sin Salto ni mezcla de color), basta con su arco. Si no, la celda del gato.
            if (OnlySpinnerMoved(mascotNow) && !_spinnerRect.IsEmpty)
                Invalidate(_spinnerRect);
            else
                Invalidate(Rectangle.Inflate(_mascotRect, MascotDirtyPad, MascotDirtyPad));
        }
        // Chip de celebración en vuelo (§4 #6): solo su rect, ampliado 6 px hacia arriba (la subida de
        // salida) y un poco a los lados (el florecer pasa del 100 %).
        bool chipMoving = CelebrationChipAnimating() || _chipWasMoving;
        _chipWasMoving = CelebrationChipAnimating();
        if (paint != InvalidationKind.Full && chipMoving && !_chipRect.IsEmpty)
        {
            int up = Dpi.Scale(Motion.ChipDirtyUpPx), side = Spacing.Scaled.Xs;
            Invalidate(new Rectangle(_chipRect.X - side, _chipRect.Y - up, _chipRect.Width + 2 * side, _chipRect.Height + up + 1));
        }

        _lastTickMascot = mascotNow;

        // Reajusta la cadencia para el siguiente latido (bajo demanda).
        // Quieto (§4, «Cadencia del panel»): sin tick de 1 Hz; se duerme hasta el próximo cambio de un
        // texto del reloj (≈ 1 despertar por minuto). Nada con el panel oculto.
        int interval = _scheduler.DesiredIntervalMs(visible: true, animating: animating,
            idleWakeMs: animating ? Motion.FastTickMs : IdleWakeMs());
        if (interval <= 0) { _tick.Stop(); return; }
        if (_tick.Interval != interval) _tick.Interval = interval;
    }

    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x2;
    private byte _appliedAlpha;
    private bool _alphaApplied;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    /// <summary>
    /// <c>WS_EX_LAYERED</c> fijo desde la creación (§4 #1): la opacidad del panel es el alfa de la ventana
    /// layered y nunca se quita el estilo (quitarlo al llegar a 255 provoca el destello de WinForms).
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED;
            return cp;
        }
    }

    /// <summary>Traslada la opacidad animada al alfa de la ventana layered (solo si cambia el byte).</summary>
    private void ApplyFadeOpacity()
    {
        if (!IsHandleCreated) return;
        byte a = PanelChoreography.AlphaByte(_fade.Value);
        if (_alphaApplied && a == _appliedAlpha) return;
        try
        {
            if (SetLayeredWindowAttributes(Handle, 0, a, LWA_ALPHA)) { _appliedAlpha = a; _alphaApplied = true; }
        }
        catch (EntryPointNotFoundException) { } // fuera de Windows (tests): sin alfa de ventana
        catch (DllNotFoundException) { }
    }

    /// <summary>Fija el tema; si cambia, los recursos cacheados del anterior se liberan (Task 3).</summary>
    private void SetTheme(Theme theme)
    {
        if (!ReferenceEquals(theme, _theme))
        {
            _gdi.Invalidate();
            _mascotColorPrimed = false; // otro tema: el color de la mascota salta, no se mezcla
            _barColorsPrimed = false;   // ni el de las barras
        }
        _theme = theme;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _gdi.Dispose();
        base.Dispose(disposing);
    }

    public void SetHistoryProvider(Func<ChartRange, Task<List<HistoryBucket>>> provider) => _historyProvider = provider;
    public void SetPercentProvider(Func<ChartRange, Task<List<PctPoint>>> provider) => _pctProvider = provider;
    public void SetLiveSessionsProvider(Func<LiveSessionsView> provider) => _liveProvider = provider;

    /// <summary>El estado del Pro (al arrancar y en cada cambio): Ajustes marca con candado lo que no se puede usar.</summary>
    public void SetProState(Services.Licensing.ProState pro)
    {
        if (_pro == pro) return;
        _pro = pro;
        if (IsHandleCreated && !IsDisposed) Invalidate();
    }

    /// <summary>«Iniciar con Windows» se ha leído o ha cambiado en segundo plano (edición de la Store; desde el hilo de UI): la
    /// fila de Ajustes deja «Comprobando…» / «Cambiando…» y enseña el estado nuevo.</summary>
    public void OnStartupChanged()
    {
        if (!IsHandleCreated || IsDisposed || _viewMode != "settings") return;
        Relayout();
        Invalidate();
    }

    /// <summary>Llamar cuando cambien las sesiones en vivo (desde el hilo de UI vía BeginInvoke).</summary>
    public void OnLiveSessionsChanged()
    {
        if (_liveProvider is not null) _liveView = _liveProvider();
        // Vida de la mascota (Tarea 5): resincroniza fase, humor y saltos con la nueva fase global.
        SyncMascotPhase();
        SyncMascotColor();
        Relayout();
        Invalidate();
        // Si la mascota ha cobrado vida (fase animada o bote activo) y el panel está visible, arranca el fast-tick.
        if (Visible && !_reduceMotion && _cfg.LiveSessionsEnabled && _cfg.ShowMascot
            && (MascotAnimator.IsAnimatedPhase(_liveView.GlobalPhase) || BounceActive() || _mascotColor.IsAnimating))
            EnsureFastTick();
    }

    /// <summary>Show the same config menu on right-click, without the auto-hide closing it.</summary>
    public void AttachContextMenu(ContextMenuStrip menu)
    {
        ContextMenuStrip = menu;
        menu.Opening += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => { _menuOpen = false; _shownAtUtc = DateTime.UtcNow; };
    }

    public void UpdateData(AppSnapshot snap, AppConfig cfg, PlanInfo plan)
    {
        _snap = snap;
        _cfg = cfg;
        _plan = plan;
        if (_cfg.LiveSessionsEnabled && _liveProvider is not null) _liveView = _liveProvider();
        _s = Localization.ForConfig(cfg);
        SetTheme(ThemeResolver.Resolve(cfg));
        _sticky = cfg.DashboardSticky;
        TopMost = cfg.DashboardAlwaysOnTop;
        // No pisar el fade de apertura: si hay un fade en vuelo, solo guardamos el objetivo;
        // la opacidad se fija directa únicamente cuando no hay animación de fade.
        _targetOpacity = Math.Clamp(cfg.DashboardOpacity, 0.3, 1.0);
        // Un fade de apertura en vuelo se re-apunta (el muelle conserva su velocidad); cerrándose no se
        // toca (el Hide llega al asentarse en 0); si no, opacidad directa.
        if (_closing) { }
        else if (!_fade.IsSettled && !_reduceMotion) _fade.SetTarget(_targetOpacity);
        else { _fade.Snap(_targetOpacity); ApplyFadeOpacity(); }
        _chartMode = cfg.ChartMode;
        _chartPctWindow = cfg.ChartPctWindow;
        _reduceMotion = ResolveReduceMotion(cfg);
        BackColor = _theme.Background;

        // Celebración de reset (Tarea 6): compara el ResetsAt/utilización previo y nuevo de 5h/7d. Al
        // detectar un reset enciende el destello in-panel + marca el humor Happy pendiente. NO toca el
        // sistema de notificaciones (eso es F4). Con reduce-motion se omite el destello (estado final).
        var (reset5, reset7) = DetectQuotaReset();
        // Barras y cifras (§4 #5): reapunta cada muelle al nuevo objetivo (desde el valor actual, sin
        // salto); una renovación vacía con Blando y deja rastro. El render muestrea; el tick los Advance.
        // Con reduce-motion colapsan.
        RetargetMotion(reset5, reset7);
        // T-v039 F3c: si la celebración acaba de encenderse, arranca el Salto de la mascota YA (no
        // espera al siguiente fast-tick) para que el gato Happy también salte al renovarse la cuota.
        SyncMascotPhase();
        // Si algo arrancó a animar, asegúrate de que el reloj rápido esté latiendo (panel visible).
        if (Visible && (_motion.IsAnimating || CelebrationActive() || BounceActive()) && !_reduceMotion) EnsureFastTick();

        if (IsHandleCreated)
            BeginInvoke(() =>
            {
                Relayout();
                Invalidate();
                if (Visible && cfg.ShowChart && !cfg.CollapsedChart) _ = ReloadChart();
                // Datos nuevos ⇒ nuevos plazos del reloj (pie, cuentas atrás): reprograma el despertar quieto.
                if (Visible && _tick.Enabled && _tick.Interval != Motion.FastTickMs) _tick.Interval = IdleWakeMs();
            });
    }

    /// <summary>
    /// Reapunta los <see cref="AnimatedValue"/> de las barras/números al objetivo del snapshot actual.
    /// Las claves replican lo que pinta el render: <c>bar:5h</c>/<c>bar:7d</c> (cuerpo) y <c>num:crit</c>
    /// (la barra crítica de la cabecera = la ventana de mayor utilización). Si no hay dato, no toca nada.
    /// </summary>
    private void RetargetMotion(bool reset5, bool reset7)
    {
        var usage = _snap?.Usage;
        if (usage is null) return;
        void Aim(string key, UsageWindow w, bool reset)
        {
            if (reset) _motion.Reset(key, w.UtilizationPct, _reduceMotion);
            else _motion.SetTarget(key, w.UtilizationPct, _reduceMotion);
        }
        if (usage.FiveHour is { } w5) Aim("bar:5h", w5, reset5);
        if (usage.SevenDay is { } w7) Aim("bar:7d", w7, reset7);
        var crit = CriticalWindow(usage);
        if (crit is not null) Aim("num:crit", crit, ReferenceEquals(crit, usage.FiveHour) ? reset5 : reset7);

        // Color del relleno mezclado con Rápido (§4 #5); salta la primera vez, con otro tema o reduce-motion.
        bool snapColor = _reduceMotion || !_barColorsPrimed;
        if (usage.FiveHour is { } c5) _motion.SetColorTarget("bar:5h", BarColor(c5), snapColor);
        if (usage.SevenDay is { } c7) _motion.SetColorTarget("bar:7d", BarColor(c7), snapColor);
        _barColorsPrimed = true;
    }

    private bool _barColorsPrimed;               // false ⇒ el próximo color de barra se fija sin mezclar

    /// <summary>Color del relleno de una barra para su utilización (el mismo criterio que <see cref="QuotaBar"/>).</summary>
    private Color BarColor(UsageWindow w)
        => ColorMath.RiskColor(w.UtilizationPct, _theme, _cfg.WarnThresholdPct, _cfg.CriticalThresholdPct);

    /// <summary>Barra crítica de la cabecera: la de mayor utilización entre 5h/7d (mismo criterio que DashboardHeader).</summary>
    private static UsageWindow? CriticalWindow(RealUsage usage)
        => usage.FiveHour is null ? usage.SevenDay
            : usage.SevenDay is null ? usage.FiveHour
            : usage.FiveHour.UtilizationPct >= usage.SevenDay.UtilizationPct ? usage.FiveHour : usage.SevenDay;

    /// <summary>
    /// Llenado de apertura (§4 #5): cada barra parte de 0 y se llena con Blando, 25 ms después de la
    /// anterior. Solo en una apertura nueva (no al reabrir durante el cierre) y nunca con reduce-motion.
    /// </summary>
    private void FillBarsForOpen()
    {
        var usage = _snap?.Usage;
        if (usage is null || _reduceMotion) return;
        if (usage.FiveHour is { } w5) _motion.Fill("bar:5h", w5.UtilizationPct, 0, false);
        if (usage.SevenDay is { } w7) _motion.Fill("bar:7d", w7.UtilizationPct, Motion.BarFillStaggerMs, false);
        if (CriticalWindow(usage) is { } crit) _motion.Fill("num:crit", crit.UtilizationPct, 0, false);
    }

    /// <summary>
    /// Alimenta el <see cref="ResetDetector"/> con las ventanas 5h/7d del snapshot actual. Si alguna
    /// se ha reseteado (el <c>ResetsAt</c> saltó hacia adelante o la utilización cayó en picado),
    /// enciende el destello de celebración in-panel durante <see cref="Motion.CelebrationMs"/> y marca
    /// el humor Happy pendiente. Con reduce-motion no hay destello (estado final), pero el detector se
    /// alimenta igual para no disparar una celebración tardía al desactivarse reduce-motion.
    /// </summary>
    private (bool Five, bool Seven) DetectQuotaReset()
    {
        var usage = _snap?.Usage;
        bool r5 = false, r7 = false;
        if (usage is not null)
        {
            r5 = _resetDetector.Observe("5h", usage.FiveHour);
            r7 = _resetDetector.Observe("7d", usage.SevenDay);
        }
        if ((r5 || r7) && !_reduceMotion)
        {
            _celebrationUntilMs = _clock.Elapsed.TotalMilliseconds + ChipMotion.TotalMs;
            _resetPending = true;
        }
        return (r5, r7);
    }

    /// <summary>Arranca/acelera el tick a la cadencia rápida para que el tween sea fluido.</summary>
    private void EnsureFastTick()
    {
        if (_tick.Interval != Motion.FastTickMs) _tick.Interval = Motion.FastTickMs;
        if (!_tick.Enabled)
        {
            // Reanuda el reloj sin contar como delta el tiempo en que estuvo parado.
            _lastTickMs = _clock.Elapsed.TotalMilliseconds;
            _tick.Start();
        }
    }

    public void ShowConfigured(AppConfig cfg)
    {
        _cfg = cfg;
        _s = Localization.ForConfig(cfg);
        SetTheme(ThemeResolver.Resolve(cfg));
        _sticky = cfg.DashboardSticky;
        TopMost = cfg.DashboardAlwaysOnTop;
        _targetOpacity = Math.Clamp(cfg.DashboardOpacity, 0.3, 1.0);
        _chartMode = cfg.ChartMode;
        _chartPctWindow = cfg.ChartPctWindow;
        _reduceMotion = ResolveReduceMotion(cfg);
        BackColor = _theme.Background;
        // Fix F3 (minor): siembra el "ahora" CONGELADO del footer ANTES del primer Relayout()/Show().
        // Sin esto, en el arranque (antes del primer OnMotionTick) _footerNowUtc seguía en MinValue y
        // BuildFooterLines caía a DateTime.UtcNow fresco en CADA llamada: medir (Relayout, T_a) y pintar
        // (OnPaint, T_b) leían dos relojes distintos, y un cruce de wrap del relativo o del flag stale
        // entre T_a y T_b dejaba la ventana 1 línea corta y recortaba el sello en el primer frame.
        // Con la semilla, el primer ciclo medir+pintar comparte instante; el primer OnMotionTick lo
        // refresca como hasta ahora. El render-test usa _renderOverride y no pasa por aquí.
        _footerNowUtc = DateTime.UtcNow;
        // T11b (§3 #18): la pantalla de referencia se captura AL ABRIR con el cursor — el usuario
        // acaba de clicar el tray en ESA pantalla. PlaceWindow y el tope de alto de ajustes la
        // comparten vía TargetScreen() (antes: PrimaryScreen vs FromControl vs FromPoint, cada uno
        // la suya → panel en la pantalla equivocada en multi-monitor).
        _openScreen = Screen.FromPoint(Cursor.Position);
        // T11a: proyecta la geometría base al DPI real de la ventana antes de medir/colocar. Si al
        // colocarse cambia de monitor (otro DPI), OnDpiChanged re-escala y re-layouta (baile PMv2).
        ApplyDpiScale();
        Relayout();
        _appliedPlacement = PlacementKey(cfg);
        _shownAtUtc = DateTime.UtcNow;

        // Fade de apertura: arranca en 0 y sube al objetivo con un muelle crítico (F3 Task 4).
        // _openedAtMs marca el instante de apertura (lo usa el stagger de la Tarea 4). Con reduce-motion
        // (gate único) salta ⇒ Show() directo a opacidad objetivo, sin fade ni slide.
        // Reabrir durante el fade de cierre (Task 6) lo invierte desde la opacidad actual, sin volver a
        // la entrada escalonada (el contenido ya está asentado en pantalla).
        bool reopening = _closing && Visible;
        _closing = false;
        _lastTickMs = _clock.Elapsed.TotalMilliseconds;
        if (!reopening) { _openedAtMs = _lastTickMs; _closeShift = false; FillBarsForOpen(); }
        // Bandeja arriba o abajo: decide el sentido de la entrada escalonada y del cierre.
        var scr = TargetScreen();
        _trayDir = PanelChoreography.TrayDirection(scr.Bounds, scr.WorkingArea);
        _lastPlanned = InvalidationKind.Full;
        _lastSlowSig = null;
        _lastSigCheckMs = double.NegativeInfinity;
        if (reopening && !_reduceMotion)
        {
            // El muelle conserva su velocidad: el cierre se invierte sin saltos.
            _fade.SetTarget(_targetOpacity);
        }
        else
        {
            _closeShift = false;
            _fade.Snap(_reduceMotion ? _targetOpacity : 0.0);
            _fade.SetTarget(_targetOpacity);
            ApplyFadeOpacity();
        }

        Show();
        BringToFront();
        Activate();
        SetForegroundWindow(Handle);
        _tick.Interval = _reduceMotion ? Motion.SlowTickMs : Motion.FastTickMs; // reduce-motion: solo countdown
        _tick.Start();
        if (cfg.ShowChart && !cfg.CollapsedChart) _ = ReloadChart();
    }

    /// <summary>
    /// For offline rendering (render-test): set everything synchronously and size to fit.
    /// <para>
    /// El parámetro opcional <paramref name="motion"/> congela las microinteracciones en un FOTOGRAMA
    /// FIJO (tSinceOpen, hover, tiempo/humor de la mascota, destello de celebración) sin reloj de UI:
    /// el render-test las captura a medio camino. <paramref name="live"/> fija la fase global (mascota).
    /// Con ambos null el comportamiento es el de hoy (estado final, sin motion).
    /// </para>
    /// </summary>
    public void PrepareForRender(AppSnapshot snap, AppConfig cfg, PlanInfo plan,
        List<HistoryBucket> buckets, List<PctPoint> pct, ChartRange range,
        RenderMotionOverride? motion = null, LiveSessionsView? live = null)
    {
        _snap = snap;
        _cfg = cfg;
        _plan = plan;
        _s = Localization.ForConfig(cfg);
        SetTheme(ThemeResolver.Resolve(cfg));
        BackColor = _theme.Background;
        _chartMode = cfg.ChartMode;
        _chartPctWindow = cfg.ChartPctWindow;
        _reduceMotion = ResolveReduceMotion(cfg);
        _chartData = buckets;
        _pctData = pct;
        _chartRange = range;
        _chartLoading = false;
        if (live is not null) _liveView = live;
        _renderOverride = motion;

        // Tween a medio camino para el render-test: siembra cada barra/número en su VALOR DE ARRANQUE
        // (0%) y reapunta al objetivo, luego avanza el reloj de motion tSinceOpen ms. Así Display()
        // devuelve un valor intermedio (no el target sembrado de primera aparición). Con reduce-motion
        // o sin override, queda en el target final (estado de hoy).
        if (motion is { } mo && !_reduceMotion)
        {
            SeedMotionForRender();
            _motion.Advance(mo.TSinceOpenMs);
            // Fade de apertura: la misma curva de muelle que en vivo, muestreada en tSinceOpen.
            double target = Math.Clamp(cfg.DashboardOpacity, 0.3, 1.0);
            Opacity = MotionState.SpringProgressAt(mo.TSinceOpenMs) * target;
        }

        _ = Handle;
        using var scope = GdiCache.Use(_gdi);
        using var g = CreateGraphics();
        Height = LayoutContent(g, draw: false);
    }

    /// <summary>
    /// Siembra los <see cref="AnimatedValue"/> de barras/números en su valor de arranque (0%) y los
    /// reapunta al objetivo del snapshot, para que el render-test capture el tween a medio vuelo. Solo
    /// lo usa <see cref="PrepareForRender"/> con override; la app real arranca el tween en vivo.
    /// </summary>
    private void SeedMotionForRender()
    {
        var usage = _snap?.Usage;
        if (usage is null) return;
        void Seed(string key, double target)
        {
            _motion.Display(key, 0.0, false); // siembra la clave en 0 (valor de arranque)
            _motion.SetTarget(key, target, false); // reapunta al objetivo (tween desde 0)
        }
        if (usage.FiveHour is { } w5) Seed("bar:5h", w5.UtilizationPct);
        if (usage.SevenDay is { } w7) Seed("bar:7d", w7.UtilizationPct);
        UsageWindow? crit = usage.FiveHour is null ? usage.SevenDay
            : usage.SevenDay is null ? usage.FiveHour
            : usage.FiveHour.UtilizationPct >= usage.SevenDay.UtilizationPct ? usage.FiveHour : usage.SevenDay;
        if (crit is not null) Seed("num:crit", crit.UtilizationPct);
    }

    // v0.3.10 (auditoría cruzada): cada carga lleva una generación. Solo la ÚLTIMA pedida aplica su
    // resultado: antes, pulsar 30D y enseguida 1H hacía que el escaneo lento de 30D terminase después y
    // pintase 30 barras diarias bajo la pestaña "1H". Además no se lanza una carga idéntica (mismo
    // modo+rango) si ya hay una en vuelo: abrir el panel disparaba dos escaneos iguales a la vez.
    private int _chartGen;
    private string? _chartInFlight;

    private async Task ReloadChart()
    {
        var mode = _chartMode;
        var range = _chartRange;
        string key = mode + "|" + range;
        if (_chartInFlight == key) return;
        int gen = ++_chartGen;
        _chartInFlight = key;
        try
        {
            if (mode == "percent")
            {
                if (_pctProvider is null) return;
                _chartLoading = _pctData.Count == 0;
                if (_chartLoading && IsHandleCreated) BeginInvoke(Invalidate);
                List<PctPoint> data;
                try { data = await _pctProvider(range); }
                catch { data = new(); }
                if (gen != _chartGen || mode != _chartMode || range != _chartRange) return;
                _pctData = data;
            }
            else
            {
                if (_historyProvider is null) return;
                _chartLoading = _chartData.Count == 0;
                if (_chartLoading && IsHandleCreated) BeginInvoke(Invalidate);
                List<HistoryBucket> data;
                try { data = await _historyProvider(range); }
                catch { data = new(); }
                if (gen != _chartGen || mode != _chartMode || range != _chartRange) return;
                _chartData = data;
            }
            _chartLoading = false;
            if (IsHandleCreated) BeginInvoke(() => { Relayout(); Invalidate(); });
        }
        finally
        {
            if (_chartInFlight == key && gen == _chartGen) _chartInFlight = null;
        }
    }

    // ---------- placement & auto-size ----------

    // Alto del panel con muelle (F3 Task 6): con el panel abierto, un cambio de alto (sección plegada,
    // datos nuevos) se anima anclado a su borde en vez de saltar. Posición y tamaño se aplican en UNA
    // llamada (SetBounds): antes Height y luego Location dejaban ver un frame intermedio.
    // Lenguaje de movimiento §4 #9: el alto usa Ajuste (cambio dentro del mismo modo, sin pasada).
    private readonly SpringValue _heightSpring = new(0, Motion.Ajuste);

    private void Relayout()
    {
        if (!IsHandleCreated) return;
        int needed;
        using (GdiCache.Use(_gdi))
        using (var g = CreateGraphics())
            needed = LayoutContent(g, draw: false);
        bool animate = Visible && !_reduceMotion && !_closing && _fade.IsSettled && needed != Height;
        if (animate)
        {
            _heightSpring.SetTarget(needed);
            EnsureFastTick();
        }
        else
        {
            _heightSpring.Snap(needed);
            ApplyBounds(needed);
        }
        _appliedPlacement = PlacementKey(_cfg);
    }

    /// <summary>Coloca el panel con el alto <paramref name="height"/> en una sola llamada (anclado a su borde).</summary>
    private void ApplyBounds(int height)
    {
        // Arrastrando: la posición la manda el ratón; solo cambia el alto.
        if (_dragging) { if (Height != height) Height = height; return; }
        var b = PanelBounds(_cfg, height);
        if (b != Bounds) SetBounds(b.X, b.Y, b.Width, b.Height);
    }

    private static string PlacementKey(AppConfig cfg) => $"{cfg.DashboardPosition}|{cfg.DashboardX}|{cfg.DashboardY}";

    private Screen? _openScreen; // pantalla donde se abrió por última vez (cursor en ShowConfigured)

    /// <summary>
    /// Pantalla de referencia ÚNICA del panel (T11b, §3 #18). Antes cada consumidor resolvía la suya:
    /// <c>PlaceWindow</c> SIEMPRE PrimaryScreen (panel en la pantalla equivocada en multi-monitor),
    /// el tope de alto de ajustes <c>FromControl</c> y el clamp <c>FromPoint</c>. Ahora: visible ⇒ la
    /// pantalla donde el panel ESTÁ (estable: un refresh de datos con el cursor en otro monitor no lo
    /// teletransporta); oculto ⇒ la pantalla del cursor capturada al abrir (<see cref="ShowConfigured"/>),
    /// con PrimaryScreen de fallback (p.ej. el render-test, que nunca se muestra → determinista).
    /// (El clamp de posiciones "Custom" sigue siendo por-punto en <see cref="ClampToScreen"/>: un punto
    /// guardado en el monitor 2 se acota DENTRO del monitor 2, no se arrastra a la pantalla activa.)
    /// </summary>
    private Screen TargetScreen() =>
        Visible ? Screen.FromRectangle(Bounds) : _openScreen ?? Screen.PrimaryScreen!;

    /// <summary>Bounds del panel para un alto dado según la colocación de la config (geometría pura en <see cref="PanelPlacement"/>).</summary>
    private Rectangle PanelBounds(AppConfig cfg, int height)
    {
        var wa = TargetScreen().WorkingArea;
        int m = Dpi.Scale(8); // margen al borde de pantalla (T11: escala con el DPI)
        Point? custom = cfg.DashboardPosition == "Custom" && cfg.DashboardX >= 0 && cfg.DashboardY >= 0
            ? ClampToScreen(new Point(cfg.DashboardX, cfg.DashboardY), height)
            : null;
        string position = cfg.DashboardPosition == "Custom" && custom is null ? "" : cfg.DashboardPosition;
        return PanelPlacement.Bounds(position, wa, new Size(Width, height), m, custom);
    }

    private Point ClampToScreen(Point p, int height)
    {
        var screen = Screen.FromPoint(p) ?? Screen.PrimaryScreen!;
        var wa = screen.WorkingArea;
        int x = Math.Clamp(p.X, wa.Left, Math.Max(wa.Left, wa.Right - Width));
        int y = Math.Clamp(p.Y, wa.Top, Math.Max(wa.Top, wa.Bottom - height));
        return new Point(x, y);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        // Invariante CPU 24/7: con el panel oculto, parar el reloj de animación (0 trabajo extra).
        // Al mostrarse, ShowConfigured ya arranca el tick a la cadencia rápida para el fade.
        if (!Visible)
        {
            _tick.Stop();
            _closing = false;
            _heightSpring.Snap(_heightSpring.Target);
            // Descarta el hover para que un re-open no muestre un realce obsoleto (sin tick que lo limpie).
            _hover.Reset();
            _press.Reset();
            _eyes.Reset();
        }
    }

    /// <summary>
    /// Esc cierra el panel (F3): el dismiss por foco ya existía (OnDeactivate→Hide); Esc lo añade
    /// como atajo. Click fuera / ✕ siguen funcionando.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            HideAnimated();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private const int WM_CLOSE = 0x0010;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;

    /// <summary>
    /// El panel vive toda la sesión: se oculta, nunca se cierra. Un cierre lo DESTRUÍA y el siguiente clic en el icono
    /// reventaba con ObjectDisposedException en cada intento, sin forma de volver a abrirlo hasta reiniciar ClaudeBar
    /// (log del 3 y del 5-oct-2026). Alt+F4 (SC_CLOSE) y cualquier WM_CLOSE que le llegue se quedan en ocultarlo.
    /// Se intercepta el MENSAJE y no <see cref="Form.OnFormClosing"/>: WinForms nunca reinicia CloseReason tras un
    /// cierre cancelado en un form de nivel superior, así que clasificar por motivo fallaba desde el segundo cierre.
    /// Apagado de Windows (WM_QUERYENDSESSION/WM_ENDSESSION) y salida de la app (Dispose) no pasan por aquí.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLOSE || (m.Msg == WM_SYSCOMMAND && ((int)(long)m.WParam & 0xFFF0) == SC_CLOSE))
        {
            HideAnimated();
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_menuOpen) return; // config menu is open over the panel
        if (_viewMode == "settings") return; // no autocerrar mientras se ajusta
        if (_sticky) return;
        if ((DateTime.UtcNow - _shownAtUtc).TotalMilliseconds < 600) return;
        HideAnimated();
    }

    // F3 Task 6: sin CS_DROPSHADOW (su sombra cuadrada no se desvanecía con el contenido). En Win11 se
    // piden esquinas redondeadas a DWM; en Win10 el atributo no existe y el fallo se ignora.
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int pref = DWMWCP_ROUND;
            _ = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch
        {
            // Win10 / sin DWM: esquinas cuadradas, como antes.
        }
        // Ventana layered nueva: sin alfa asignado sería invisible; se fija el de la opacidad vigente.
        _alphaApplied = false;
        ApplyFadeOpacity();
    }

    /// <summary>¿Está abierto y no cerrándose? (el fade de cierre aún lo deja visible).</summary>
    public bool IsOpen => Visible && !_closing;

    /// <summary>
    /// Cierre (§4 #2): la opacidad baja con Rápido, el contenido se desplaza hasta 4 px hacia la bandeja
    /// y el <see cref="Control.Hide"/> real llega cuando el alfa baja de 0,03 (~180 ms, en
    /// <see cref="OnMotionTick"/>). Oculto o con reduce-motion se oculta al instante. Reabrir durante el
    /// cierre lo invierte conservando la velocidad del muelle (ver ShowConfigured).
    /// </summary>
    public void HideAnimated()
    {
        if (!Visible || _reduceMotion || !IsHandleCreated) { _closing = false; Hide(); return; }
        if (_closing) return;
        _closing = true;
        _closeShift = true;
        _fade.SetTarget(0.0);
        EnsureFastTick();
    }

    // ---------- input ----------

    /// <summary>
    /// Rueda del ratón en la vista de ajustes (v0.3.7): desplaza el contenido WheelStepPx por diente,
    /// acotado a [0, overflow]. En la vista de datos no hace nada (esa vista auto-dimensiona).
    /// <para>
    /// Compatibilidad con TRACKPADS de precisión: en vez de <c>e.Delta / 120</c> (que truncaba a 0 con
    /// los deltas pequeños del smooth-scrolling y dejaba el panel sin rodar), acumulamos el delta crudo
    /// en <see cref="_settingsWheelAccum"/> y convertimos a píxeles solo la parte entera vía
    /// <see cref="DashboardSettingsView.WheelToPixels"/>; el resto se conserva para el siguiente evento.
    /// Un diente de ratón clásico (±120) sigue desplazando exactamente <c>WheelStepPx</c> (48px).
    /// </para>
    /// </summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_viewMode != "settings") return;
        int viewportH = Height - _settingsViewportTop - SettingsViewportBottomPad;
        if (_settingsContentH <= viewportH) return;
        // Acumula el delta crudo y extrae los píxeles enteros (el resto queda para el próximo evento).
        var (px, rest) = DashboardSettingsView.WheelToPixels(_settingsWheelAccum, e.Delta);
        _settingsWheelAccum = rest;
        if (px == 0) return; // delta de trackpad aún insuficiente para 1px: sigue sumando, no repintes
        // Signo: delta positivo (rueda arriba) REDUCE el scroll (sube el contenido).
        int ns = DashboardSettingsView.ClampScroll(_settingsScroll - px, _settingsContentH, viewportH);
        if (ns != _settingsScroll) { _settingsScroll = ns; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (_closeRect.Contains(e.Location)) { HideAnimated(); return; }

        // ⚙ → abrir ajustes (solo en la vista de datos). ShowSettings resetea además el scroll.
        if (_viewMode == "data" && _gearRect.Contains(e.Location))
        {
            ShowSettings(); return;
        }

        // Vista de ajustes: ‹ vuelve a datos; cada fila clicada emite su mutación. Sin drag aquí.
        if (_viewMode == "settings")
        {
            if (_backRect.Contains(e.Location)) { _viewMode = "data"; Relayout(); Invalidate(); return; }
            foreach (var (key, r) in _settingsRects)
            {
                if (r.Contains(e.Location))
                {
                    if (DashboardSettingsView.ActionFor(key) is { } a) SettingsChanged?.Invoke(a);
                    else SpecialActionRequested?.Invoke(key); // claves "special:*": diálogo/instalador en el host
                    Invalidate();
                    return;
                }
            }
            return; // dentro de ajustes, fuera de rects: no arrastrar
        }

        // Cabeceras de sección plegables (vista de datos): alternar Collapsed* vía SettingsChanged.
        foreach (var (key, r) in _sectionRects)
        {
            if (r.Contains(e.Location))
            {
                SettingsChanged?.Invoke(ToggleSection(key));
                Relayout(); Invalidate();
                return;
            }
        }

        // Mode toggle ($ / %)
        foreach (var (mode, rect) in _modeRects)
        {
            if (rect.Contains(e.Location))
            {
                if (_chartMode != mode)
                {
                    _chartMode = mode;
                    _chartData = new(); _pctData = new();
                    ChartModeChanged?.Invoke(mode);
                    _ = ReloadChart();
                }
                return;
            }
        }

        // Percent window selector (5h / 7d)
        foreach (var (win, rect) in _pctWinRects)
        {
            if (rect.Contains(e.Location))
            {
                if (_chartPctWindow != win)
                {
                    _chartPctWindow = win;
                    ChartWindowChanged?.Invoke(win);
                    if (IsHandleCreated) BeginInvoke(Invalidate); // same data, different series
                }
                return;
            }
        }

        foreach (var (range, rect) in _tabRects)
        {
            if (rect.Contains(e.Location))
            {
                if (_chartRange != range) { _chartRange = range; _chartData = new(); _pctData = new(); _ = ReloadChart(); }
                return;
            }
        }

        foreach (var (id, rect) in _liveRowRects)
        {
            if (rect.Contains(e.Location))
            {
                // Micro-interacción 3: el fondo de la fila destella y vuelve con Rápido (sin toast).
                _press.Trigger("live:" + id, rect, snap: _reduceMotion);
                if (!_reduceMotion) { EnsureFastTick(); Invalidate(); }
                SessionClicked?.Invoke(id);
                return;
            }
        }

        _dragging = true;
        _dragOffset = new Point(Cursor.Position.X - Location.X, Cursor.Position.Y - Location.Y);
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
            return;
        }
        // Ojos que miran el cursor (§5): solo con el gato visible en la vista de datos; con el cursor
        // quieto no hay fotogramas (el muelle se asienta) y con reduce-motion no se siguen.
        if (_cfg.ShowMascot && !_reduceMotion && Visible && _viewMode == "data" && _faceCenter != Point.Empty)
        {
            _eyes.Track(e.X - _faceCenter.X, e.Y - _faceCenter.Y, Dpi.Factor, _clock.Elapsed.TotalMilliseconds);
            if (_eyes.IsAnimating) EnsureFastTick();
        }
        // Hover (Tarea 3): clave del rect interactivo bajo el cursor (precedencia estable, ver
        // HoveredRects). Cursor Hand cuando hay clave. Si la clave cambia, arranca/rearma el fade-in
        // del realce y repinta (bajo demanda, panel visible).
        string? key = HoverHitTest.Resolve(e.Location, HoveredRects());
        Cursor = key is not null ? Cursors.Hand : Cursors.Default;
        if (key != _hover.Key)
        {
            // Realce: entra (o se desliza desde el anterior) al llegar a un rect; se desvanece en su
            // sitio al salir a un hueco. Con reduce-motion (Tarea 7) salta al estado final.
            if (key is not null && HoveredRect(key) is { } r) _hover.Enter(key, r, snap: _reduceMotion);
            else _hover.Leave(snap: _reduceMotion);
            if (Visible && !_reduceMotion) EnsureFastTick();
            Invalidate();
        }
    }

    /// <summary>
    /// Pares (clave → rect) candidatos al hover, en <b>orden de precedencia</b> (primero gana en
    /// solapes). En la vista de datos: chrome pequeño (✕/⚙) y controles finos (tabs, modos, ventanas,
    /// filas vivas) antes que las cabeceras de sección, que son los contenedores grandes. En ajustes:
    /// ✕/‹ y las filas. Se consume desde <see cref="OnMouseMove"/> y el realce del paint.
    /// </summary>
    private IEnumerable<KeyValuePair<string, Rectangle>> HoveredRects()
    {
        yield return new("chrome:close", _closeRect);
        if (_viewMode == "settings")
        {
            yield return new("chrome:back", _backRect);
            foreach (var (k, r) in _settingsRects) yield return new("set:" + k, r);
            yield break;
        }
        yield return new("chrome:gear", _gearRect);
        foreach (var (k, r) in _tabRects) yield return new("tab:" + (int)k, r);
        foreach (var (k, r) in _modeRects) yield return new("mode:" + k, r);
        foreach (var (k, r) in _pctWinRects) yield return new("pctwin:" + k, r);
        foreach (var (k, r) in _liveRowRects) yield return new("live:" + k, r);
        foreach (var (k, r) in _sectionRects) yield return new("sec:" + k, r);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Cursor = Cursors.Default;
        if (!_dragging) return;
        _dragging = false;
        // Remember the dragged spot locally so a later chart change (Relayout/PlaceWindow)
        // doesn't snap the panel back to the old configured position.
        _cfg.DashboardPosition = "Custom";
        _cfg.DashboardX = Location.X;
        _cfg.DashboardY = Location.Y;
        _appliedPlacement = $"Custom|{Location.X}|{Location.Y}";
        Moved?.Invoke(Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        // Los ojos vuelven al centro 1,5 s después (un plazo del despertar quieto, sin tick periódico).
        if (!_reduceMotion && Visible)
        {
            _eyes.Leave(_clock.Elapsed.TotalMilliseconds);
            if (_tick.Enabled && _tick.Interval != Motion.FastTickMs) _tick.Interval = IdleWakeMs();
        }
        // El cursor abandonó el panel: descarta el hover (fade-out del realce a 0, en su último rect).
        if (_hover.Key is null) return;
        _hover.Leave(snap: _reduceMotion);
        if (Visible && !_reduceMotion) EnsureFastTick();
        Invalidate();
    }

    // ---------- paint / layout ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (GdiCache.Use(_gdi))
        {
            // Cierre (§4 #2): todo el contenido se desplaza en px enteros hacia la bandeja.
            int shift = _closeShift && !_reduceMotion
                ? PanelChoreography.CloseShiftPx(_fade.Value, _targetOpacity, Dpi.Factor, _trayDir)
                : 0;
            if (shift != 0) e.Graphics.TranslateTransform(0, shift);
            // Realce de hover DETRÁS del contenido: usa los rects de la pasada anterior (los mismos sobre
            // los que OnMouseMove resolvió la clave). Nunca toca el layout — es decoración bajo el texto.
            DrawHoverHighlight(e.Graphics);
            DrawPressFlash(e.Graphics);
            _paintClip = e.ClipRectangle;
            try { LayoutContent(e.Graphics, draw: true); }
            finally
            {
                _paintClip = null;
                if (shift != 0) e.Graphics.TranslateTransform(0, -shift);
            }
        }
        _gdi.EndFrame();
        // Un cruce de texto que nace al pintar (cuenta atrás que cambia de minuto) necesita fotogramas
        // aunque el tick que lo provocó estuviera quieto.
        if (_renderOverride is null && Visible && !_reduceMotion && _motion.IsAnimating) EnsureFastTick();
    }

    /// <summary>
    /// ¿Alcanza la zona sucia del pintado en curso la banda que empieza en <paramref name="top"/>? Con
    /// el repintado parcial de la mascota (Task 1) las secciones de debajo se recorren solo midiendo
    /// (mismo avance de y y mismos rects que pintando: medir==pintar) y no gastan en dibujar texto/gráfica
    /// que el clip descartaría. El desplazamiento de la entrada escalonada solo baja el dibujo.
    /// </summary>
    private bool PaintReaches(int top) => _paintClip is not { } c || c.Bottom > top;

    /// <summary>
    /// Pinta un fondo redondeado <c>theme.HoverBg</c> detrás del rect bajo el cursor, con la
    /// intensidad del realce (alfa) y en su rect animado (F3 Task 2: se desvanece en el último rect y se
    /// desliza entre elementos). Intensidad ≈0 ⇒ no pinta. No altera el layout.
    /// T7b (§3 #6): el token sustituye a BgElevated puro (invisible en claro y CLI) y, en la vista de
    /// ajustes, el realce de las filas se recorta al viewport del scroll (la fila cortada arriba
    /// sangraba ~2px sobre el chrome "‹ Ajustes" por el Inflate).
    /// </summary>
    private void DrawHoverHighlight(Graphics g)
    {
        RectangleF rect;
        double intensity;
        string? key;
        if (_renderOverride is not null)
        {
            // En el render-test el realce se fuerza desde el override (intensidad plena, sin tick que lo anime).
            key = _renderOverride.Value.HoveredKey;
            if (key is null || HoveredRect(key) is not { } r) return;
            rect = r;
            intensity = 1.0;
        }
        else
        {
            if (!_hover.Visible) return;
            key = _hover.LastKey;
            // El elemento se movió con el contenido (scroll/relayout): el realce lo acompaña. Si ya no
            // existe, termina de desvanecerse donde estaba.
            if (key is not null && HoveredRect(key) is { } live) _hover.Follow(live);
            rect = _hover.Rect;
            intensity = _hover.Intensity;
        }
        if (rect.Width <= 0 || rect.Height <= 0) return;

        // Alfa proporcional a la intensidad sobre HoverBg (+8 %); un poco de aire alrededor del rect y
        // radio de 6 px (§4 #4).
        int alpha = (int)Math.Round(Math.Clamp(intensity, 0.0, 1.0) * _theme.HoverBg.A);
        if (alpha <= 0) return;
        var bg = Color.FromArgb(alpha, _theme.HoverBg);
        var sp = Spacing.Scaled;
        var padded = RectangleF.Inflate(rect, sp.Xs, sp.Xs / 2f);
        int radius = Dpi.Scale(Motion.HoverRadiusPx);
        using var b = GdiCache.BrushFor(bg);

        // Filas de ajustes: el rect ya viene intersecado con el viewport, pero el Inflate vuelve a
        // sacarlo (±2px) → clip al MISMO viewport que usa LayoutContent para pintar el contenido.
        if (_viewMode == "settings" && key?.StartsWith("set:", StringComparison.Ordinal) == true)
        {
            int viewportH = Math.Max(0, Height - _settingsViewportTop - SettingsViewportBottomPad);
            var prevClip = g.Clip;
            g.SetClip(new Rectangle(0, _settingsViewportTop, Width, viewportH));
            try { Shapes.FillRounded(g, b, padded, radius); }
            finally { g.Clip = prevClip; }
            return;
        }
        Shapes.FillRounded(g, b, padded, radius);
    }

    /// <summary>
    /// Destello de pulsación de una fila de sesión (micro-interacción 3): <c>theme.PressBg</c> (+14 %)
    /// sobre el realce, con alfa según su intensidad (vuelve con Rápido). Solo decoración bajo el texto.
    /// </summary>
    private void DrawPressFlash(Graphics g)
    {
        if (_renderOverride is not null || !_press.Visible) return;
        if (_press.Key is { } key && HoveredRect(key) is { } live) _press.Follow(live);
        int alpha = (int)Math.Round(_press.Intensity * _theme.PressBg.A);
        if (alpha <= 0) return;
        var sp = Spacing.Scaled;
        var padded = RectangleF.Inflate(_press.Rect, sp.Xs, sp.Xs / 2f);
        using var b = GdiCache.BrushFor(Color.FromArgb(alpha, _theme.PressBg));
        Shapes.FillRounded(g, b, padded, Dpi.Scale(Motion.HoverRadiusPx));
    }

    /// <summary>Rect vigente de la clave de hover <paramref name="key"/> (null si ya no existe o es vacío).</summary>
    private Rectangle? HoveredRect(string key)
    {
        foreach (var kv in HoveredRects())
            if (kv.Key == key) return kv.Value.Width > 0 && kv.Value.Height > 0 ? kv.Value : null;
        return null;
    }

    /// <summary>Walks the sections top-to-bottom. Returns the required window height.</summary>
    private int LayoutContent(Graphics g, bool draw)
    {
        // Fuentes del sistema de diseño (estáticas y compartidas: NO se hace Dispose de ellas).
        var titleFont = Typography.Title;
        var planFont = Typography.Caption;
        var labelFont = Typography.Body;
        var smallFont = Typography.Caption;
        var mono = Typography.Mono;
        // Los tabs de la gráfica quieren peso bold para legibilidad de las píldoras (de la caché GDI+).
        using var tabFont = GdiCache.FontFor("Segoe UI", 8f, FontStyle.Bold);
        using var fg = GdiCache.BrushFor(_theme.TextPrimary);
        using var dim = GdiCache.BrushFor(_theme.TextSecondary);

        int x = Padding.Left;
        int y = Padding.Top;
        int w = Width - Padding.Horizontal;

        // Chrome común a ambas vistas: título + plan + botón cerrar.
        if (draw)
        {
            g.DrawString(Brand.Name, titleFont, fg, x, y);
            // T11: el chrome (botón ✕ y avance de la banda de título) escala con el DPI.
            _closeRect = new Rectangle(Width - Dpi.Scale(26), Dpi.Scale(10), Dpi.Scale(18), Dpi.Scale(18));
            // T8c: el plan ("Max 20x · resets…") se elide antes de la columna del ✕ — un display largo
            // se pintaba de largo bajo el botón y rebasaba el borde del panel. Solo pintado (el avance
            // y += 50 no cambia) → medir==pintar.
            string planShown = TextWrap.FitLine(_plan.Display, x, _closeRect.X, Spacing.Scaled.Sm,
                t => g.MeasureString(t, planFont).Width);
            g.DrawString(planShown, planFont, dim, x, y + Dpi.Scale(24));
            using var closeFont = GdiCache.FontFor("Segoe UI", 11f, FontStyle.Bold);
            g.DrawString("✕", closeFont, dim, _closeRect.X, _closeRect.Y - 2);
        }
        y += Dpi.Scale(50);

        // ----- Vista de ajustes: alto LIMITADO + scroll (v0.3.7) -----
        // El panel medía más que la pantalla ("de arriba a abajo"). Ahora: chrome + "‹ Ajustes" fijos
        // arriba; el contenido se mide ENTERO (pasada de medida con dict desechable), el alto de la
        // ventana se acota a MaxPanelHeightPct del área útil, y el contenido se pinta desplazado
        // -_settingsScroll con CLIP al viewport. Los rects clicables salen de la pasada de pintado (ya
        // desplazados) y se INTERSECAN con el viewport para que una fila medio fuera no responda bajo
        // el chrome. Con todo visible no hay barra ni scroll (comportamiento de siempre).
        if (_viewMode == "settings")
        {
            // F11 (v0.3.9): la etiqueta de volver usa _s.Back ("‹ Volver"/"‹ Back"/"‹ Zurück"…), NO
            // _s.Settings ("‹ Ajustes"): el chevron + el nombre de la sección se leía como TÍTULO, no
            // como afordancia de volver. _s.Back existe en los 9 idiomas (revive el string "muerto").
            string backLabel = "‹ " + _s.Back;
            if (draw)
            {
                using var bb = GdiCache.BrushFor(_theme.TextSecondary);
                g.DrawString(backLabel, labelFont, bb, x, y);
            }
            // T8d: la zona de clic se mide con el texto localizado (el 80×20 fijo dejaba media
            // etiqueta DE/FR/NL sin responder al clic).
            _backRect = DashboardSettingsView.BackHitRect(g, backLabel, labelFont, x, y, w);
            y += Dpi.Scale(24); // T11: fila "‹ Ajustes" escalada
            int contentTop = y;
            _settingsViewportTop = contentTop;

            // 1) Medir el contenido completo (sin tope) — el dict es desechable: los rects buenos
            //    (desplazados) los registra la pasada de pintado de abajo.
            _scratchRects.Clear();
            _settingsContentH = DashboardSettingsView.Draw(g, draw: false, x, contentTop, w,
                _cfg, _s, _theme, labelFont, smallFont, _scratchRects, pro: _pro) - contentTop;

            // 2) Tope de alto: % del área útil de la pantalla de referencia (T11b: la MISMA que usa
            //    PlaceWindow vía TargetScreen, antes FromControl podía resolver otra en multi-monitor).
            var wa = TargetScreen().WorkingArea;
            int maxH = wa.Height * DashboardSettingsView.MaxPanelHeightPct / 100;
            int fullH = contentTop + _settingsContentH + Dpi.Scale(18);
            int h = Math.Min(fullH, maxH);
            int viewportH = h - contentTop - SettingsViewportBottomPad;
            _settingsScroll = DashboardSettingsView.ClampScroll(_settingsScroll, _settingsContentH, viewportH);

            if (draw)
            {
                var viewport = new Rectangle(0, contentTop, Width, viewportH);
                var prevClip = g.Clip;
                g.SetClip(viewport);
                try
                {
                    DashboardSettingsView.Draw(g, draw: true, x, contentTop - _settingsScroll, w,
                        _cfg, _s, _theme, labelFont, smallFont, _settingsRects, pro: _pro);
                }
                finally { g.Clip = prevClip; }

                // Filas medio fuera del viewport: recortar su zona clicable (y de hover) al viewport.
                foreach (var k in _settingsRects.Keys.ToList())
                {
                    var r = Rectangle.Intersect(_settingsRects[k], viewport);
                    if (r.Height <= 0) _settingsRects.Remove(k); else _settingsRects[k] = r;
                }

                // Barrita de scroll (solo con overflow): pista sutil + pulgar proporcional.
                if (_settingsContentH > viewportH)
                {
                    int trackX = Width - DashboardSettingsView.ScrollBarW - DashboardSettingsView.ScrollBarMargin;
                    using (var tb = GdiCache.BrushFor(Color.FromArgb(50, _theme.TextMuted)))
                        Shapes.FillRounded(g, tb, new Rectangle(trackX, contentTop, DashboardSettingsView.ScrollBarW, viewportH), 2);
                    var thumb = DashboardSettingsView.ThumbRect(trackX, contentTop, viewportH, _settingsContentH, _settingsScroll);
                    using var thb = GdiCache.BrushFor(Color.FromArgb(160, _theme.TextMuted));
                    Shapes.FillRounded(g, thb, thumb, 2);
                }
            }
            return h;
        }

        // ----- Vista de datos: cabecera de un vistazo + secciones plegables -----
        _settingsRects.Clear();
        _backRect = Rectangle.Empty;

        // Entrada escalonada (Tarea 4): cabecera=0, secciones de datos=1..n, footer=n+1. Cada bloque se
        // dibuja envuelto en una traslación vertical (OffsetY px → 0) desfasada por su índice; el y de
        // layout NO cambia. El transform solo se aplica en la pasada de pintado (medir devuelve el mismo y).
        double tSinceOpen = TSinceOpenMs();

        // Cabecera (índice 0): desplazamiento en px enteros hacia la bandeja + fundido (§4 #3).
        int hOff = DashboardDataView.EntranceOffset(draw, _reduceMotion, tSinceOpen, 0, _trayDir);
        int headerTop = y;
        // try/finally (fix F3 minor, mismo patrón que DashboardDataView.Section): garantiza que el pop
        // del transform ocurra aunque DashboardHeader.Draw lance, para no contaminar frames siguientes.
        if (hOff != 0) g.TranslateTransform(0, hOff);
        try
        {
            y = DashboardHeader.Draw(g, draw, x, y, w,
                _snap, _liveView, _cfg, _s, _theme, SampleMascot(), MascotMoodCurrent(),
                labelFont, smallFont, mono, ref _gearRect,
                _motion, _reduceMotion, MascotBounceOffsetY(), CelebrationText(),
                r => _mascotRect = r, CelebrationChipFrame(),
                _renderOverride is null && _mascotColorPrimed ? _mascotColor.Current : null,
                r => _chipRect = r, r => { if (draw) _spinnerRect = r; }, p => _faceCenter = p);
            DashboardDataView.CoverEntrance(g, draw, _reduceMotion, tSinceOpen, 0, _theme.Background,
                x, headerTop, w, y - headerTop);
        }
        finally
        {
            if (hOff != 0) g.TranslateTransform(0, -hOff);
        }

        // Secciones de datos (índices 1..n)
        y = DashboardDataView.Draw(g, draw && PaintReaches(y), x, y, w,
            _snap, _liveView, _cfg, _s, _theme,
            labelFont, smallFont, tabFont,
            _chartMode, _chartRange, _chartPctWindow,
            _chartData, _pctData, _chartLoading,
            _sectionRects, _tabRects, _modeRects, _pctWinRects, _liveRowRects,
            _motion, _reduceMotion, tSinceOpen, firstSectionIndex: 1, trayDirection: _trayDir);

        // Footer (índice n+1): se desplaza después de la última sección de datos.
        int footerIndex = DashboardDataView.VisibleSectionCount(_cfg, _snap) + 1;
        int fOff = DashboardDataView.EntranceOffset(draw, _reduceMotion, tSinceOpen, footerIndex, _trayDir);
        if (fOff != 0) g.TranslateTransform(0, fOff);
        try
        {

        // footer: "Actualizado · hace N min · pista", con marcador stale si el dato envejece, más el
        // sello de privacidad. F2 truncaba ambos al ancho fijo del panel (340 px); ahora se ENVUELVEN a
        // varias líneas. PERO el nº de líneas depende del reloj de pared (el flag stale + el texto del
        // relativo), y el alto de la ventana solo se recalcula en Relayout(). Para no recortar el sello
        // entre snapshots, las líneas las construye FooterLayout (PURO, único origen de verdad) con un
        // "ahora" CONGELADO por pasada (_footerNowUtc) — así medir(draw=false) y pintar(draw=true) del
        // mismo repaint coinciden — y OnMotionTick re-layouta si la firma del footer cambió (la red de
        // seguridad que faltaba). El marcador stale conserva su color Warn en su línea corta.
        y += 4;
        int footerTop = y;
        bool drawFooter = draw && PaintReaches(y);
        int lineH = (int)Math.Ceiling(smallFont.GetHeight(g));
        double Mw(string str) => g.MeasureString(str, smallFont).Width;

        var footerLines = BuildFooterLines(w, Mw);
        // Snapshot de la firma SOLO en la pasada de pintado: es la que el tick compara para decidir si
        // el alto reservado quedó obsoleto. (Medir no debe pisar la firma del último pintado.)
        if (drawFooter) _lastFooterSig = FooterLayout.Signature(footerLines);

        bool sealStarted = false;
        foreach (var fl in footerLines)
        {
            // Aire entre el bloque footer y el sello: una sola vez, antes de la primera línea del sello.
            if (fl.Kind == FooterLayout.LineKind.Seal && !sealStarted)
            {
                y += Spacing.Scaled.Xs;
                sealStarted = true;
            }
            if (drawFooter)
            {
                Color c = fl.Kind switch
                {
                    // Marcador stale = texto pequeño → variante AA del ámbar (T6b; en claro Warn caía a 2.8:1).
                    FooterLayout.LineKind.Stale => _theme.WarnText,
                    FooterLayout.LineKind.Seal => _theme.TextMuted,
                    _ => _theme.TextSecondary,
                };
                using var brush = GdiCache.BrushFor(c);
                g.DrawString(fl.Text, smallFont, brush, x, y);
            }
            y += lineH;
        }
        DashboardDataView.CoverEntrance(g, drawFooter, _reduceMotion, tSinceOpen, footerIndex, _theme.Background,
            x, footerTop, w, y - footerTop);
        return y + Spacing.Scaled.Sm;
        }
        finally
        {
            if (fOff != 0) g.TranslateTransform(0, -fOff);  // deshace el desplazamiento del footer
        }
    }

    /// <summary>
    /// Construye las líneas del footer (marcador stale + estado/actualización + sello) vía
    /// <see cref="FooterLayout"/>, el ÚNICO origen de verdad compartido por el pintado y por la guardia
    /// de re-layout del tick lento. El "ahora" está congelado en <see cref="_footerNowUtc"/> (lo fija el
    /// tick antes de invalidar; en arranque/Relayout = el reloj de pared) para que medir(draw=false) y
    /// pintar(draw=true) del mismo repaint produzcan EXACTAMENTE las mismas líneas (invariante de altura).
    /// </summary>
    private List<FooterLayout.Line> BuildFooterLines(int width, Func<string, double> measure)
    {
        DateTime now = _footerNowUtc == DateTime.MinValue ? DateTime.UtcNow : _footerNowUtc;
        // ¿el dato está desfasado? No marcar durante los primeros RefreshSeconds tras arrancar (1ª fetch).
        bool grace = (now - _startedAtUtc) < TimeSpan.FromSeconds(Math.Max(15, _cfg.RefreshSeconds));
        bool stale = _snap is not null
            && _snap.LatestState == UsageFetchState.Ok
            && !grace
            && UsageFormat.IsSnapshotStaleAt(_snap, now, _cfg.RefreshSeconds);
        return FooterLayout.Build(_snap, _s, stale, _sticky, now, width, measure);
    }

    /// <summary>
    /// Red de seguridad del invariante medir/pintar para el footer (fix F3 Tarea 8): el alto del bloque
    /// footer depende del reloj (flag stale + texto del relativo), pero la ventana solo se re-mide en
    /// <see cref="Relayout"/>. Sin esto, una transición fresh→stale o un cruce de wrap del relativo entre
    /// dos repaints a 1 Hz dejaba la ventana 1 línea corta y RECORTABA el sello. Aquí, en cada tick,
    /// congelamos un "ahora" común, medimos la firma del footer con él y, si cambió respecto a la última
    /// pintada, hacemos <see cref="Relayout"/> (re-mide el alto) ANTES de invalidar. Devuelve true si
    /// re-layoutó. Barato: solo mide texto a ≤1 Hz. Devuelve además la firma del tick lento (minuto de
    /// pared + texto del pie) con la que el tick decide si hay algo nuevo que pintar (Task 1).
    /// </summary>
    private (bool Relayouted, SlowTickSignature Signature) ReconcileFooterHeight()
    {
        _footerNowUtc = DateTime.UtcNow;
        if (!IsHandleCreated || !Visible || _renderOverride is not null)
            return (false, SlowTickSignature.From(_footerNowUtc, ""));
        var prev = _lastFooterSig;
        List<FooterLayout.Line> lines;
        using (var g = CreateGraphics())
        {
            int w = Width - Padding.Horizontal;
            lines = BuildFooterLines(w, str => g.MeasureString(str, Typography.Caption).Width);
        }
        // Las cuentas atrás (resolución de minutos) forman parte de la firma: cambian a destiempo del pie.
        var u = _snap?.Usage;
        string countdowns = UsageFormat.CountdownMinutesAt(u?.FiveHour?.ResetsAt, _s.Resetting, DateTimeOffset.Now)
            + "|" + UsageFormat.CountdownMinutesAt(u?.SevenDay?.ResetsAt, _s.Resetting, DateTimeOffset.Now);
        var sig = SlowTickSignature.From(_footerNowUtc, string.Join("\n", lines.Select(l => l.Text)) + "\n" + countdowns);
        if (FooterLayout.Signature(lines) == prev) return (false, sig);
        Relayout();   // re-mide el alto con el footer actual; LayoutContent(draw:true) ya leerá _footerNowUtc
        return (true, sig);
    }

    /// <summary>
    /// ms hasta el próximo cambio de un texto que depende del reloj (cuentas atrás 5h/7d, «hace N min»
    /// del pie, umbral de dato envejecido y fin de la gracia de arranque), acotado a [50 ms, 60 s]:
    /// el panel quieto se despierta solo entonces (lenguaje de movimiento §4 y micro-interacción 4).
    /// </summary>
    private int IdleWakeMs()
    {
        var nowUtc = DateTime.UtcNow;
        var nowOff = DateTimeOffset.UtcNow;
        var u = _snap?.Usage;
        double? relative = null, stale = null;
        if (_snap is { } sn)
        {
            var at = sn.UsageAtUtc.Kind == DateTimeKind.Local ? sn.UsageAtUtc.ToUniversalTime() : sn.UsageAtUtc;
            relative = IdleCadence.RelativeChangeMs(at, nowUtc);
            if (!sn.LiveFromStatusline)
                stale = IdleCadence.InstantChangeMs(at.AddSeconds(3 * _cfg.RefreshSeconds), nowUtc);
        }
        double? grace = IdleCadence.InstantChangeMs(_startedAtUtc.AddSeconds(Math.Max(15, _cfg.RefreshSeconds)), nowUtc);
        // Vuelta de los ojos al centro (§5): un plazo más, no un tick.
        double? eyes = _eyes.NextDeadlineMs is { } at2 ? Math.Max(0, at2 - _clock.Elapsed.TotalMilliseconds) : null;
        return IdleCadence.Earliest(
            IdleCadence.CountdownChangeMs(u?.FiveHour?.ResetsAt, nowOff),
            IdleCadence.CountdownChangeMs(u?.SevenDay?.ResetsAt, nowOff),
            relative, stale, grace, eyes);
    }

    /// <summary>Traduce la clave de una sección plegable clicada a la mutación de config (Collapsed*).</summary>
    private static Action<AppConfig> ToggleSection(string sectionKey) => sectionKey switch
    {
        "quota" => c => c.CollapsedQuota = !c.CollapsedQuota,
        "sessions" => c => c.CollapsedSessions = !c.CollapsedSessions,
        "spend" => c => c.CollapsedSpend = !c.CollapsedSpend,
        "chart" => c => c.CollapsedChart = !c.CollapsedChart,
        _ => _ => { },
    };
}
