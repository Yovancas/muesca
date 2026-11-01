using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Notch.Platform;
using ClaudeBarWin.Services.State;
using static ClaudeBarWin.UI.Notch.NotchNative;

namespace ClaudeBarWin.UI.Notch;

/// <summary>Ajustes del notch que toca la ventana (los de la máquina de modos salen de aquí).
/// <paramref name="SizePercent"/>: 0 = Auto, 100/115/130/150. <paramref name="MonitorDeviceName"/>: "" = principal.
/// <paramref name="Pages"/>: páginas del expandido (F5); <c>null</c> = <see cref="NotchPages.Default"/>, Claude siempre la primera.
/// <paramref name="Music"/>: servicio de música (página Música y reposo con la canción); <paramref name="Lyrics"/>: letras
/// (LRCLIB: el único destino de red del notch). <paramref name="ShelfClearOnStart"/>: vaciar la bandeja al arrancar.
/// <paramref name="Clock"/>: reloj intercalado con los anillos en reposo; <paramref name="Mascot"/>: el gato.
/// <paramref name="Tasks"/>: tareas y recordatorios (los avisos suenan aunque la página no esté en la lista ni a la vista).
/// <paramref name="Contrast"/>: contraste adaptativo (F5 · B6). <paramref name="CompactLyrics"/>: letra en el notch pequeño
/// (con <paramref name="Lyrics"/>); <paramref name="CompactLyricsArt"/> / <paramref name="CompactLyricsQuota"/>: sus ranuras
/// (carátula o «♪» a la izquierda; anillo de 5 h a la derecha). Todos salen de Ajustes (F5 · B8) y se aplican en caliente
/// con <see cref="NotchForm.ApplyOptions"/>; sin Música ni Tareas, sus páginas no se ofrecen. <paramref name="Locked"/>:
/// páginas con el candado del Pro (<see cref="NotchProGating"/>): se ofrecen, pero sin su contenido ni sus servicios.
/// <paramref name="Focus"/>: el pomodoro de la página Foco (duraciones y sonido); null = <see cref="FocusSettings.Default"/>.</summary>
public sealed record NotchOptions(bool OpenOnClick, bool Minimal, bool HideInFullscreen, bool Sound,
    int SizePercent, string MonitorDeviceName, bool ReduceMotion, int AutoMinimalSeconds = 0,
    IReadOnlyList<NotchPage>? Pages = null, bool Music = true, bool Lyrics = true, bool ShelfClearOnStart = false,
    bool Clock = true, bool Mascot = true, bool Tasks = true, bool Contrast = true,
    bool CompactLyrics = true, bool CompactLyricsArt = true, bool CompactLyricsQuota = true,
    NotchProLocks Locked = NotchProLocks.None, FocusSettings? Focus = null, bool ShowSpend = true)
{
    /// <summary>El pomodoro con lo de serie si no se dijo nada.</summary>
    public FocusSettings FocusOrDefault => Focus ?? FocusSettings.Default;
}

/// <summary>
/// Ventana del notch: una superficie layered fija (<see cref="NotchPlacement.Surface"/>) centrada arriba del
/// monitor elegido, donde <see cref="LayeredGdiRenderer"/> pinta la pastilla. Capa Win32 fina: decide
/// <see cref="NotchStateMachine"/>, mueve <see cref="NotchAnimator"/> y pinta <see cref="NotchContentPainter"/>.
///
/// <list type="bullet">
/// <item><b>Sin foco nunca:</b> <c>WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW</c>, <c>WM_MOUSEACTIVATE</c> → no activar.</item>
/// <item><b>Ratón:</b> solo la zona de <see cref="NotchGeometry.HitTest"/> (pintada con alfa 1/255) recibe
/// clics; el resto de la superficie es alfa 0 y deja pasar el ratón a lo que haya debajo. La sombra y el halo
/// van en <see cref="NotchShadowWindow"/> (<c>WS_EX_TRANSPARENT</c>), justo debajo: si fueran en esta ventana,
/// sus píxeles con alfa &gt; 0 se comerían los clics de otras apps (<c>HTTRANSPARENT</c> no cruza procesos).</item>
/// <item><b>0 % CPU en reposo:</b> el bucle (<c>Application.Idle</c> + <c>DwmFlush</c>) solo vive mientras el
/// animador se mueve; el spinner va a <see cref="NotchMotion.SpinnerFps"/> con un timer; los plazos de la
/// máquina (hover, gracia, caducidad de avisos) con un timer de un solo disparo. Si DWM no compone
/// (DwmFlush falla o vuelve al instante) el bucle pasa a un timer de 16 ms en vez de girar al 100 %.</item>
/// <item><b>DPI/monitor:</b> WM_DPICHANGED (sin aplicar el rect sugerido), WM_DISPLAYCHANGE y cambios del área
/// de trabajo ⇒ 400 ms después se recalculan escala y superficie y el notch reaparece recentrado. Nunca reinicia.</item>
/// <item><b>Topmost</b> solo por eventos: cambio de primer plano y <c>TaskbarCreated</c> (Explorer reiniciado);
/// primero la sombra y luego la pastilla, para que esta quede siempre encima de su sombra.</item>
/// <item><b>Pantalla completa</b> (si se oculta en ella): <c>Hide</c> y todo parado; al salir, reaparece sin foco.</item>
/// <item><b>Snap Layouts:</b> mientras se arrastra una ventana, la pastilla baja al 35 %.</item>
/// <item><b>Permisos (F2):</b> escucha <see cref="PermissionBroker.Changed"/> (llega desde el hilo del pipe) y lo pasa
/// al hilo de UI con <see cref="ClaudeStateHub.SyncPermissions"/>. Clic al SOLTAR en Permitir / Denegar
/// (<see cref="NotchGeometry.PermissionButtons"/>) ⇒ la máquina decide si vale (armado) y el broker contesta; clic en
/// el resto del Permiso ⇒ trae al frente la terminal de la sesión (<see cref="TerminalFocus"/>). Mientras se arma,
/// el relleno de los botones se repinta con el timer del spinner.</item>
/// <item><b>Páginas (F5):</b> en el expandido, <see cref="PageCarousel"/> decide la página (rueda: una por gesto; clic en
/// los puntos de <see cref="NotchGeometry.PageDotAt"/>) y <see cref="PageTransition"/> anima el cruce y la píldora; el
/// alto lo da <see cref="NotchLayout.PageSize"/> (mismo modo ⇒ el animador lo reajusta con Ajuste). Sobre la lista de
/// sesiones de Claude que no cabe entera, la rueda la desplaza (<see cref="NotchPages.RouteWheel"/>). Cada vez que se
/// abre el expandido empieza en Claude. El bucle solo vive mientras algo se mueve.</item>
/// <item><b>Música (F5 · B3):</b> <see cref="MediaService"/> (solo eventos) se crea al mostrarse si el ajuste está activo.
/// Página Música: botones con hover y pulsación (acción al SOLTAR sobre el mismo control), clic en la barra = saltar, botón
/// «Letra» que divide la vista y clic en una línea = saltar a ella; todo con <see cref="MusicLayout"/>, la MISMA cuenta que
/// se pinta. Repintado de la posición/letra con un timer de un solo disparo (<see cref="MusicClock"/>) SOLO con la página
/// visible y sonando. La letra se pide (<see cref="LyricsController"/> → LRCLIB, caché primero) solo con la página visible, la vista de
/// letra abierta y el ajuste activo, o (letra en el notch pequeño) mientras suena una pista. Reposo con música sonando y sin
/// Claude trabajando: carátula mini + título (<see cref="IdleMusic"/>, otra maquetación del mismo modo ⇒ cruce del
/// animador) o, con letra sincronizada, la letra compacta (<see cref="CompactLyrics"/>: la línea actual, con su propio
/// cruce al cambiar de línea y el ancho a saltos de 16 px). Su repintado es el plazo de un disparo de la máquina, en el
/// instante de la siguiente línea: nada a 60 fps salvo los 120 ms del cruce. Su anillo de la cuota va atenuado; con el
/// ratón encima (la Vista previa sobre el reposo, que aquí NO cambia de contenido: el animador sigue pintando el reposo)
/// o con el aviso de cuota, entero, y con el ratón encima además con el % (<see cref="CompactQuotaRing"/>).</item>
/// <item><b>Bandeja (F5 · B4):</b> la ventana acepta arrastres (<c>AllowDrop</c>, OLE): algo aceptable encima
/// (<see cref="ShelfDataTransfer.Read"/> + <see cref="ShelfDrop.Effect"/>: nunca «mover») abre el expandido YA en la
/// página Bandeja; soltar lo guarda en <see cref="ShelfStore"/> (rutas, enlaces http(s) o texto). En la página, una fila
/// de tarjetas (<see cref="ShelfLayout"/>): clic al SOLTAR en una la abre (<see cref="ShelfOpen"/>: el texto se copia,
/// los programas se enseñan en el Explorador; una entrada borrada o movida se marca y no hace nada), Ctrl/Mayús
/// seleccionan (modificadores del propio <c>WM_LBUTTONUP</c>, sin ganchos), «Vaciar» / «Quitar (N)», flechas ‹ › (una
/// página) y rueda (una tarjeta) cuando no caben todas, con el muelle del carrusel, y arrastrar una tarjeta (o la
/// selección) fuera con <c>DoDragDrop</c> (copiar o vincular). La disponibilidad de los archivos se mira al cambiar la
/// bandeja o al mostrarse la página (<see cref="ShelfSnapshot"/>), nunca por frame. Miniaturas del sistema y tamaños:
/// <see cref="ShelfThumbnails"/> en un hilo STA de fondo (<see cref="StaWorker"/>), de vuelta por <c>BeginInvoke</c>,
/// que repinta; al pintar solo se leen.</item>
/// <item><b>Tareas (F5 · B5):</b> página Tareas, edición en línea y recordatorios en <c>NotchForm.Tasks.cs</c>.</item>
/// <item><b>Foco:</b> el pomodoro (página, mini anillo del notch cerrado y aviso del fin de fase) en <c>NotchForm.Focus.cs</c>.</item>
/// <item><b>Contraste (F5 · B6):</b> <see cref="AdaptiveContrast"/> lee una franja de 1 px bajo la pastilla de reposo
/// (<see cref="GdiBackdropSampler"/>) SOLO al mostrarse, al cambiar el primer plano, al soltar un arrastre y al salir de
/// pantalla completa, con debounce de 300 ms (timer de un solo disparo); si cambia, se repinta una vez.</item>
/// <item><b>Reloj y gato (F5 · B7):</b> <see cref="IdleScene"/> decide la maquetación del reposo, la vista previa y la raya
/// (anillos 7 s ↔ reloj 5 s, el gato tras 15 s de calma) y sus cambios entran en el MISMO plazo de un disparo que la
/// máquina (<see cref="IdleScene.NextWakeIn"/>): nada de timers periódicos. Cada cambio de cara es otra maquetación del
/// mismo modo ⇒ el animador funde el contenido; el gato lleva su propio alfa para no parpadear en ese fundido. Sus ojos
/// siguen al cursor con <c>WM_MOUSEMOVE</c> de la propia ventana (solo con el ratón encima; repinta solo si cambia el píxel).</item>
/// <item><b>Modo de monitor (29-oct):</b> los servicios (música, letras, bandeja, tareas) viven en <see cref="NotchShared"/>,
/// uno para todas las ventanas; sin él, la ventana crea el suyo (fijo, como siempre). Las mueve y reparte <see cref="NotchHost"/>:
/// <see cref="MoveToMonitor"/> se muda encogiéndose, se mueve y vuelve a crecer (reduce-motion: salto) y, con un notch por
/// monitor, solo la ventana <see cref="Interactive"/> lleva los botones del Permiso y del Recordatorio y suena; las demás
/// los enseñan en solo lectura y un clic en ellas pide ser la interactiva (<see cref="PromoteRequested"/>).</item>
/// </list>
/// </summary>
public sealed partial class NotchForm : Form
{
    /// <summary>Espera tras un cambio de DPI/pantalla/área de trabajo antes de recolocar (llegan en ráfaga).</summary>
    private const int RelayoutDebounceMs = 400;

    private readonly ClaudeStateHub _hub;
    private readonly NotchShared _shared;
    private readonly bool _ownsShared;                  // sin host (tests, uso suelto): los servicios son suyos
    private readonly NotchContentPainter _painter;
    private readonly INotchRenderer _renderer;
    private readonly NotchAnimator _anim = new();
    private readonly FullscreenWatcher _fullscreen;
    private readonly NotchShadowWindow _shadow = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Forms.Timer _wake = new();       // plazos de la máquina de modos
    private readonly System.Windows.Forms.Timer _frameTimer = new(); // spinner solo / respaldo sin DWM
    private readonly System.Windows.Forms.Timer _relayout = new() { Interval = RelayoutDebounceMs };
    private readonly uint _taskbarCreated;

    private NotchOptions _options;
    private NotchStateMachine _sm;

    private float _scale = 1f;
    private Size _surface;
    private bool _surfaceTall;          // superficie alta (solo con la página Uso o mientras su forma no quepa en la baja)
    private Point _topLeft;
    private IntPtr _monitor;

    private bool _idleHooked;
    private bool _timerLoop;            // DwmFlush no sirve (DWM no compone): bucle por timer
    private int _fastFlushes;
    private bool _pointerInside;
    private bool _hiddenForFullscreen;
    private int _sessionOffset;         // rueda en el expandido: primera sesión listada
    private int _wheelAccum;
    private IReadOnlyList<NotchPage> _pages;
    private PageCarousel _carousel;
    private readonly PageTransition _pageAnim;
    private bool _wasExpanded;          // para empezar en Claude cada vez que se abre el expandido
    private readonly PageArrows _arrows = new();       // flechas ‹ › de página (solo en la interactiva)
    private PageArrow _arrowHover, _arrowHeld;         // flecha bajo el ratón y la pulsada (el clic va al soltar sobre ella)
    private HoverHold _hoverHold;       // cambio de página del usuario: zona de hover = forma vieja ∪ nueva hasta asentarse
    private bool _hoverSettlePosted;    // ya se pidió cerrar la retención (fuera del frame)
    private PermissionVerdict _held;    // botón del Permiso pulsado (DOWN) y aún sin soltar; None si ninguno
    private string? _heldRequestId;      // petición a la que pertenecía ese botón al pulsarlo
    private string? _paintedKey;         // petición + texto que el painter pintó ENTEROS en el último frame (null si no)
    private TimeSpan _paintedSince;      // desde cuándo se ven enteros (armado sobre lo que SE PINTA)
    private float _armShown = 1f;        // relleno de los botones pintado en el último frame
    // Avisos que ya sonaron (o se callaron): la campanita va con el aviso VISIBLE, una sola vez aunque otro lo
    // tape y luego vuelva. Tabla débil: se vacía sola cuando la cola suelta el aviso.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<NotchAlert, object> _chimed = new();

    // ---- modo de monitor (29-oct)
    /// <summary>Aviso con botones enseñado en solo lectura (otra ventana es la interactiva): otra maquetación del mismo modo.</summary>
    private const int LayoutReadOnly = 16;
    private bool _interactive = true;                   // botones del Permiso / Recordatorio y campanita
    private string? _migrateTo;                         // seguir a la ventana activa: monitor al que se muda (encogiéndose)
    private string _device = "";                        // nombre GDI del monitor donde está de hecho
    private bool _monitorMissing;                       // StrictMonitor y su monitor se desconectó: oculta hasta que la cierren
    private (bool InUse, bool Holds, bool Hidden) _place;   // lo último avisado con PlaceChanged

    // ---- música (F5 · B3)
    /// <summary>Maquetación del reposo con la canción (para <see cref="NotchAnimator.SetTarget"/>).</summary>
    private const int LayoutIdleMusic = 1;
    /// <summary>Reposo con la letra compacta: lleva el bit del reposo con música (sin reloj ni gato, como él).</summary>
    private const int LayoutCompactLyrics = LayoutIdleMusic | 8;
    private readonly CompactLyricsMotion _compactMotion = new();
    private readonly CompactQuotaRing _compactQuota = new();
    private IReadOnlyList<LyricLine> _compactLines = Array.Empty<LyricLine>();
    // Medida de la línea compacta (px lógicos), rehecha solo si cambia el texto o la escala.
    private string? _compactMeasured;
    private float _compactMeasuredScale = -1f, _compactMeasuredWidth;
    private readonly System.Windows.Forms.Timer _musicTick = new();   // posición/letra de la página Música (un disparo)
    private readonly IdleMusic _idleMusic = new();
    private readonly MusicMotion _musicMotion = new();
    private readonly MusicIconMotion _iconMotion = new(playing: true);   // morph pausa↔reproducir y deslizamiento de saltos
    private readonly ClaudeBarWin.Services.Motion.HoverHighlight _musicHover = new();
    private readonly LyricsController _lyrics;          // el de NotchShared (uno para todas las ventanas)
    private readonly AdaptiveContrast _contrast = new(new GdiBackdropSampler());
    private readonly System.Windows.Forms.Timer _contrastTimer = new();   // debounce del muestreo (un disparo)
    private NowPlaying? _np;
    private NowPlaying? _idleTrack;                       // la última del reposo con música (para el fundido de salida)
    private IReadOnlyList<LyricLine> _shownLines = Array.Empty<LyricLine>();
    // Letra plana con tiempos estimados (letras v2): se recalcula solo si cambia el texto o la duración, así la vista
    // recibe la MISMA lista mientras tanto (comparación por referencia en UpdateMusicLine).
    private string? _plainText;
    private TimeSpan _plainDuration;
    private IReadOnlyList<LyricLine> _plainLines = Array.Empty<LyricLine>();
    // Textos de la fila de la letra (fuente de «Otra letra» y aviso), rehechos solo cuando cambia lo que enseñan.
    private (string? Source, int Index, int Count, bool Loading) _sourceKey;
    private string? _sourceLabel;
    private (LyricsState State, double Offset) _noticeKey;
    private string? _notice;
    private bool _lyricsTextStale = true;                 // otro idioma: rehacer los dos textos
    private MusicHit _musicHeld;                          // control pulsado (DOWN) y aún sin soltar
    private MusicHit _musicHoverHit;                      // control del realce (se conserva mientras se desvanece)
    private TimeSpan _lastStep;
    private bool _inSync;

    // ---- bandeja (F5 · B4)
    /// <summary>Lo que dura «Copiado» en la fila tras copiar un texto.</summary>
    private const int ShelfCopiedMs = 1200;
    private const int MK_LBUTTON = 0x0001, MK_SHIFT = 0x0004, MK_CONTROL = 0x0008;
    private readonly System.Windows.Forms.Timer _shelfFlash = new() { Interval = ShelfCopiedMs };   // un disparo
    private readonly ShelfSelection _shelfSel = new();
    private readonly ClaudeBarWin.Services.Motion.HoverHighlight _shelfHover = new();
    private readonly IShellLauncher _shell = new ShellLauncher();
    private bool _shelfSeen;                              // ya tomó su foto de la bandeja compartida
    private ShelfSnapshot _shelfSnap = ShelfSnapshot.Empty;
    private ShelfHit _shelfHeld;                          // tarjeta / botón pulsado (DOWN) y aún sin soltar
    private bool _shelfHeldWithSelection;                 // ¿había selección al pulsar? («Quitar (N)» frente a «Vaciar»)
    private ShelfHit _shelfHoverHit;                      // control del realce (se conserva mientras se desvanece)
    private Point _shelfPressAt;                          // dónde se pulsó (umbral de arrastre)
    private int _shelfFirst;                              // flechas y rueda: primera tarjeta a la vista (objetivo)
    // Desplazamiento animado de la fila de tarjetas (px lógicos) con el muelle del carrusel; reduce-motion = salto.
    private readonly ClaudeBarWin.Services.Motion.SpringValue _shelfScroll = new(0, ClaudeBarWin.Services.Motion.Motion.Ajuste);
    private int _shelfWheelAccum;
    // Miniaturas del sistema en un hilo STA de fondo (las de NotchShared); las tarjetas guardan las de la foto actual y su línea gris.
    private readonly ShelfThumbnails _shelfThumbs;
    private IReadOnlyList<Bitmap?> _shelfCardThumbs = Array.Empty<Bitmap?>();
    private IReadOnlyList<string> _shelfDetails = Array.Empty<string>();
    private Guid? _shelfCopied;
    private bool _dragOver;                               // algo aceptable se arrastra encima
    private ShelfDrop _dragDrop = ShelfDrop.None;         // lo que traía al entrar (por si al soltar no se puede releer)
    private bool _selfDrag;                               // arrastre que sale de la propia bandeja: no se acepta encima
    // ---- aviso de actualización: la oferta (de NotchHost), su texto ya formateado y el botón bajo el ratón / pulsado
    private UpdateOfferView? _updateOffer;
    private NotchUpdateView? _updateView;
    private bool _updateHover, _updatePressed;
    // ---- reloj y gato del reposo (F5 · B7)
    private readonly IdleScene _idleScene = new();
    private NotchMode _targetMode = NotchMode.Hidden;   // lo último que se pidió al animador (alfa del gato)
    private int _targetLayout;
    private bool _catSteady;                            // el último cambio de maquetación tenía al gato a los dos lados

    /// <param name="shared">Servicios compartidos con las otras ventanas (<see cref="NotchHost"/>); null = los suyos propios.</param>
    public NotchForm(ClaudeStateHub hub, NotchOptions options, Strings? strings = null, NotchShared? shared = null)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        // Sin host: lo que vuelve del hilo de fondo entra por BeginInvoke (si la ventana se cierra, lanza y la imagen se suelta).
        _ownsShared = shared is null;
        _shared = shared ?? new NotchShared(hub, options, work => BeginInvoke(work));

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        Text = Brand.Name + " Notch";

        _painter = new NotchContentPainter(strings ?? Localization.Get("en"));
        _renderer = new LayeredGdiRenderer(_painter, ownsPainter: true);
        _anim.ReduceMotion = options.ReduceMotion;
        _sm = NewStateMachine(options);
        _pages = NotchPages.Effective(options.Pages, options.Music, options.Tasks, options.Locked);
        _carousel = new PageCarousel(_pages);
        _pageAnim = new PageTransition(_carousel.Current, _carousel.Index) { ReduceMotion = options.ReduceMotion };
        _arrows.ReduceMotion = options.ReduceMotion;
        _musicMotion.ReduceMotion = options.ReduceMotion;
        _iconMotion.ReduceMotion = options.ReduceMotion;
        _compactMotion.ReduceMotion = options.ReduceMotion;
        _compactQuota.ReduceMotion = options.ReduceMotion;
        ApplyIdleOptions(options);
        _lyrics = _shared.Lyrics;
        _shared.LyricsChanged += OnLyricsChanged;
        _shared.MusicChanged += OnMusic;
        _shared.ShelfChanged += OnShelfChanged;
        _shared.TasksChanged += OnTasksChanged;
        _shared.ReminderDue += OnReminderDue;
        _shared.ReminderCleared += OnReminderCleared;
        _shared.PermissionAnswered += OnPermissionAnswered;
        _shared.UsageChanged += OnUsageChanged;
        _shared.UsageSeriesChanged += OnUsageSeries;
        _shared.UsageSeriesError += OnUsageSeriesError;
        _shared.UsageViewRestored += OnUsageViewRestored;
        LoadUsageView(_shared.UsageView);   // lo de la última apertura (también en un monitor recién conectado)
        _shared.FocusChanged += OnFocusChanged;
        _shared.FocusEnded += OnFocusEnded;
        _shared.FocusAlertCleared += OnFocusAlertCleared;
        _shared.SetHidden(this, false);

        _fullscreen = new FullscreenWatcher(() => _monitor);
        _fullscreen.Changed += OnFullscreenChanged;
        _fullscreen.MoveSizeChanged += OnMoveSize;
        _fullscreen.ForegroundChanged += ReassertTopmost;
        _fullscreen.ForegroundChanged += RequestBackdrop;
        _fullscreen.MoveSizeChanged += moving => { if (!moving) RequestBackdrop(); };
        _fullscreen.Changed += on => { if (on) CancelBackdrop(); else RequestBackdrop(); };

        _wake.Tick += (_, _) => { _wake.Stop(); _sm.Tick(DateTime.UtcNow); Sync(); };
        _frameTimer.Tick += (_, _) => { Frame(); Pump(); };
        _relayout.Tick += (_, _) => { _relayout.Stop(); Relayout(); };
        _musicTick.Tick += (_, _) => { _musicTick.Stop(); Frame(); Pump(); ScheduleMusicTick(); };
        _shelfFlash.Tick += (_, _) => { _shelfFlash.Stop(); _shelfCopied = null; RenderFrame(); };
        _shelfThumbs = _shared.ShelfThumbs;
        _shelfThumbs.Changed += OnShelfThumbs;
        _contrastTimer.Tick += (_, _) => { _contrastTimer.Stop(); SampleBackdrop(); };
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

        _hub.SessionsChanged += OnSessions;
        _hub.Signal += OnSignal;
        _hub.QuotaThresholdCrossed += OnQuotaCrossing;
        _hub.QuotaChanged += OnQuota;
        _hub.Permissions.Changed += OnPermissionsChanged;
        // Recordatorios ya vencidos (otra ventana los encoló antes de crearse esta: monitor recién conectado).
        foreach (var (id, title) in _shared.ActiveReminders) _sm.OnTaskReminder(id, title, DateTime.UtcNow);
        ResetUsageView();   // con lo que ya tenga el contenedor compartido (monitor recién conectado)

        ComputePlacement();
        Bounds = new Rectangle(_topLeft, _surface);
    }

    /// <summary>Textos del idioma vigente (cambio de idioma en caliente). Sin diseñador: no se serializa.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Strings Strings
    {
        get => _painter.Strings;
        set
        {
            _painter.Strings = value ?? throw new ArgumentNullException(nameof(value));
            _lyricsTextStale = true;
            _sourceLabel = null;
            RebuildShelfCards();   // «Carpeta» / «2,4 MB» en el idioma nuevo
            RebuildUpdateView();
            RebuildProCard();
            ResetUsageView();   // números y gráfica en el idioma nuevo
            Sync();
        }
    }

    /// <summary>Aplica ajustes nuevos sin recrear la ventana.</summary>
    public void ApplyOptions(NotchOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var old = _options;
        if (_migrateTo is not null)
        {
            // Mudándose (seguir a la ventana activa): el monitor de destino sigue la mudanza en curso; otro la cancela y salta.
            if (SameDevice(o.MonitorDeviceName, _migrateTo)) o = o with { MonitorDeviceName = old.MonitorDeviceName };
            else _migrateTo = null;
        }
        _options = o;
        _anim.ReduceMotion = o.ReduceMotion;
        _pageAnim.ReduceMotion = o.ReduceMotion;
        _arrows.ReduceMotion = o.ReduceMotion;
        _musicMotion.ReduceMotion = o.ReduceMotion;
        _iconMotion.ReduceMotion = o.ReduceMotion;
        _compactMotion.ReduceMotion = o.ReduceMotion;
        _compactQuota.ReduceMotion = o.ReduceMotion;
        if (o.ReduceMotion) _shelfScroll.Snap(_shelfScroll.Target);
        ApplyIdleOptions(o);
        if (!o.Lyrics)
        {
            if (_musicMotion.ShowLyrics)
            {
                _musicMotion.SetLyrics(false, _clock.Elapsed);
                _musicMotion.Settle();
            }
            // Cancela lo que estuviera en vuelo hacia lrclib.net (si ninguna otra ventana la quiere).
            QuietLyrics(() => _shared.UpdateLyrics(this, wanted: false, quiet: true));
        }
        _shared.ApplyOptions(o);
        if (old.Contrast != o.Contrast)
        {
            if (o.Contrast)
            {
                _painter.Contrast = _contrast.Current;   // OnLight (Reset al apagar) hasta el primer muestreo
                RequestBackdrop();
            }
            else
            {
                CancelBackdrop();
                _contrast.Reset();                           // nada de contraste viejo al volver a encenderlo
                _painter.Contrast = NotchContrast.OnLight;   // el notch de siempre
            }
        }
        if (old.Music != o.Music)
        {
            if (o.Music) { if (IsHandleCreated && Visible) _ = StartMusicAsync(); }
            else _shared.StopMusic();
        }

        var pages = NotchPages.Effective(o.Pages, o.Music, o.Tasks, o.Locked);
        if (!pages.SequenceEqual(_pages))
        {
            // Otras páginas (Ajustes): de vuelta a Claude, sin animación.
            _pages = pages;
            _carousel = new PageCarousel(_pages);
            _pageAnim.Jump(_carousel.Current, _carousel.Index);
            _sessionOffset = 0;
            _wheelAccum = 0;
            ResetShelfPointer();
            if (ShelfEnabled) EnsureShelf();
            EndTaskEdit(save: true, restoreFocus: true);
            ResetTasksPointer();
            _tasksOffset = 0;
            ResetUsagePointer();
            ResetFocusPointer();
        }
        if (old.Locked != o.Locked)
        {
            // Candado del Pro puesto o quitado (compra, fin de la prueba): la misma lista de páginas con otro contenido. Lo que
            // se tuviera pulsado o a medias en ellas se suelta; la bandeja se carga si se acaba de desbloquear.
            ResetShelfPointer();
            ResetTasksPointer();
            _tasksOffset = 0;
            if (ShelfEnabled) EnsureShelf();
            // Las gráficas de Uso ganan o pierden la interacción: rango, series y proyección de nuevo, sin transición.
            ResetUsagePointer();
            ResetUsageView();
            ResetFocusPointer();   // y la tarea y la semana del pomodoro, el chip o su fila
        }
        else if (old.ShowSpend != o.ShowSpend)
        {
            // «Mostrar gasto estimado»: apagado, la página Uso pasa a Cuota sin selector; encendido, vuelve lo elegido.
            ResetUsagePointer();
            ResetUsageView();
        }
        if (!o.Tasks) UnloadTasks();
        else if (IsHandleCreated && Visible) EnsureTasks();

        if (old.OpenOnClick != o.OpenOnClick || old.Minimal != o.Minimal || old.HideInFullscreen != o.HideInFullscreen
            || old.AutoMinimalSeconds != o.AutoMinimalSeconds)
        {
            // La máquina toma los ajustes en el constructor: se rehace con el estado vivo (sesiones,
            // pantalla completa, puntero). Los avisos aún en cola se pierden: solo pasa al tocar Ajustes.
            var now = DateTime.UtcNow;
            _sm = NewStateMachine(o);
            _shared.ForgetActiveReminders();   // se perdieron en todas (mismos ajustes): una ventana nueva tampoco los enseña
            _sm.OnFullscreen(_fullscreen.IsFullscreen);
            if (_pointerInside) _sm.OnPointerEnter(now);
            _sm.Tick(now);
            if (_hiddenForFullscreen && !(o.HideInFullscreen && _fullscreen.IsFullscreen)) ShowAgain();
            else if (!_hiddenForFullscreen && o.HideInFullscreen && _fullscreen.IsFullscreen) HideForFullscreen();
        }

        if (old.SizePercent != o.SizePercent || old.MonitorDeviceName != o.MonitorDeviceName) Relayout();
        else Sync();
    }

    // ---------------------------------------------------------------- modo de monitor (29-oct)

    /// <summary>Monitor donde está de hecho (nombre GDI, de <c>Screen.DeviceName</c>): el elegido o, si no está, el principal.</summary>
    internal string MonitorDevice => _device;

    /// <summary>Los ajustes que tiene ahora (el host no los vuelve a aplicar si no cambian).</summary>
    internal NotchOptions Options => _options;

    /// <summary>Todos los monitores: si su monitor se desconecta, se oculta (no cae al principal, donde ya hay otra) hasta que el
    /// host la cierre. Fijo y siguiendo a la ventana activa, no: cae al principal como siempre.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool StrictMonitor { get; set; }

    /// <summary>Oculta por pantalla completa en su monitor.</summary>
    internal bool HiddenForFullscreen => _hiddenForFullscreen;

    /// <summary>Se está usando: puntero encima, vista previa o expandido (el principal efectivo no cambia mientras tanto).</summary>
    internal bool InUse => NotchMonitorPolicy.InUse(_sm.Mode, _pointerInside);

    /// <summary>Seguir a la ventana activa: no se muda ahora (expandido, puntero encima, Permiso o Recordatorio a la vista).</summary>
    internal bool HoldsPlace => _migrateTo is null && NotchMonitorPolicy.HoldsPlace(_sm.Mode, _pointerInside);

    /// <summary>Cambió <see cref="InUse"/>, <see cref="HoldsPlace"/> o <see cref="HiddenForFullscreen"/>.</summary>
    internal event Action<NotchForm>? PlaceChanged;

    /// <summary>Clic en un aviso de solo lectura: este notch quiere ser el interactivo (todos los monitores).</summary>
    internal event Action<NotchForm>? PromoteRequested;

    /// <summary>Llegó <c>WM_DISPLAYCHANGE</c> (monitores conectados, desconectados o reordenados).</summary>
    internal event Action? DisplayChanged;

    /// <summary>
    /// La primera vez que se vea, sin la animación de aparecer: ya con su tamaño y su contenido. La pone el host antes de
    /// enseñarla cuando el asistente de bienvenida acaba de encogerse en este monitor hasta el notch de reposo (el relevo:
    /// esta queda encima, idéntica, y el asistente se va). Se consume en <c>OnShown</c>.
    /// </summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool AppearSettled { get; set; }

    private bool _appearingSettled;   // dentro del primer Sync (OnShown) de una ventana que aparece ya asentada

    /// <summary>Clic en «Actualizar» (solo en la ventana interactiva, con la versión lista).</summary>
    internal event Action<NotchForm>? UpdateClicked;

    /// <summary>La oferta de actualización que enseña (la de <see cref="NotchHost.SetUpdateOffer"/>); null sin oferta.</summary>
    internal UpdateOfferView? UpdateOffer => _updateOffer;

    /// <summary>Lo que pinta de la oferta (texto ya formateado y si lleva botón).</summary>
    internal NotchUpdateView? UpdateView => _updateView;

    /// <summary>
    /// Versión nueva (de <c>IAppUpdates</c>, por el host): un punto de acento en el reposo y, en el expandido, «Nueva versión
    /// 0.6» con el anillo de la descarga o el botón «Actualizar». null la quita. Lo mismo otra vez no repinta.
    /// </summary>
    internal void SetUpdateOffer(UpdateOfferView? offer)
    {
        if (Equals(offer, _updateOffer)) return;
        _updateOffer = offer;
        if (offer is not { Ready: true }) _updatePressed = false;
        RebuildUpdateView();
        if (Alive) Sync();
    }

    /// <summary>El botón «Actualizar» (lo llama el clic al soltar sobre él). Solo la interactiva y con la versión lista.</summary>
    internal bool RequestUpdate()
    {
        if (!_interactive || _updateView is not { Button: true }) return false;
        UpdateClicked?.Invoke(this);
        return true;
    }

    private void RebuildUpdateView() =>
        _updateView = _updateOffer is { } o ? NotchUpdateText.Build(o, _painter.Strings, _interactive) : null;

    /// <summary>¿El expandido lleva la línea de arriba? (el aviso integrado o, si no hay, la versión nueva).</summary>
    private bool TopLineShown => _anim.ShownAlert is not null || _updateView is not null;

    /// <summary>
    /// Ventana interactiva: la que lleva los botones del Permiso y del Recordatorio y la que suena (todos los monitores: el
    /// principal efectivo; con una sola ventana, siempre). Al pasar a serlo, el aviso a la vista se da por sonado (ya sonó, o
    /// se calló, en la que lo era); al dejar de serlo se suelta la pulsación de un botón.
    /// </summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Interactive
    {
        get => _interactive;
        set
        {
            if (_interactive == value) return;
            _interactive = value;
            if (value && _sm.ActiveAlert is { } a && !_chimed.TryGetValue(a, out _)) _chimed.Add(a, a);
            _held = PermissionVerdict.None;
            _heldRequestId = null;
            _reminderHeld = PermissionVerdict.None;
            _reminderHeldId = null;
            _paintedKey = null;   // lo pintado (con o sin botones) ya no es lo que se va a ver
            _updatePressed = false;
            _arrowHover = _arrowHeld = PageArrow.None;   // las flechas, solo en la interactiva
            RebuildUpdateView();  // el botón «Actualizar», solo en la interactiva
            ResetProPointer();
            RebuildProCard();     // y los de la tarjeta del Pro
            ResetUsagePointer();  // y el hover de la gráfica de Uso
            ResetFocusPointer();  // y los botones del pomodoro y el de su aviso
            _focusAlertHeld = false;
            if (Alive) Sync();
        }
    }

    /// <summary>
    /// Seguir a la ventana activa: se muda a <paramref name="device"/> encogiéndose (el animador va a oculto), se mueve y vuelve a
    /// crecer allí con el DPI de ese monitor (<see cref="Relayout"/>, como cualquier cambio de monitor). Con reduce-motion, sin
    /// mostrarse u oculta por pantalla completa, salto directo (y sin <paramref name="animate"/>: su monitor ya no está y la
    /// ventana ya cayó al principal). Volver al monitor de partida a medias cancela la mudanza.
    /// </summary>
    internal void MoveToMonitor(string device, bool animate = true)
    {
        device ??= "";
        if (SameDevice(device, _options.MonitorDeviceName))
        {
            if (_migrateTo is null) return;
            _migrateTo = null;   // vuelve a crecer donde está
            Sync();
            return;
        }
        if (!animate || _options.ReduceMotion || !IsHandleCreated || !Visible || _hiddenForFullscreen)
        {
            _migrateTo = null;
            _options = _options with { MonitorDeviceName = device };
            Relayout();
            return;
        }
        _migrateTo = device;
        if (_pointerInside) PointerLeave();   // nada de hover en una pastilla que se va
        Sync();
    }

    /// <summary>Se está mudando de monitor (encogiéndose hacia <see cref="MoveToMonitor"/>).</summary>
    internal bool Moving => _migrateTo is not null;

    /// <summary>Deja la mudanza en curso y vuelve a crecer donde está (cambio de modo a mitad: el host decide de nuevo).</summary>
    internal void CancelMove()
    {
        if (_migrateTo is null) return;
        _migrateTo = null;
        Sync();
    }

    /// <summary>La pastilla ya no se ve: al monitor nuevo, y desde ahí vuelve a crecer (44 × 0, como al aparecer).</summary>
    private void FinishMove()
    {
        if (_migrateTo is not { } to) return;
        _migrateTo = null;
        _options = _options with { MonitorDeviceName = to };
        Relayout();
    }

    private static bool SameDevice(string? a, string? b) =>
        string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>Avisa al host si cambió lo que decide dónde va el notch y quién es el interactivo.</summary>
    private void NotifyPlace()
    {
        var place = (InUse, HoldsPlace, _hiddenForFullscreen);
        if (place == _place) return;
        _place = place;
        PlaceChanged?.Invoke(this);
    }

    // ---------------------------------------------------------------- ventana

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    // La ventana layered se pinta entera con UpdateLayeredWindow: nada de WM_PAINT.
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _shadow.Create(Bounds);   // oculta; se muestra con la pastilla (OnVisibleChanged)
        // Destino de arrastre OLE (bandeja). Solo recibe donde la pastilla tiene alfa > 0: el resto de la superficie
        // deja pasar también los arrastres a lo que haya debajo.
        try { AllowDrop = true; }
        catch (InvalidOperationException ex) { AppLog.Warn($"notch: sin arrastrar y soltar: {ex.Message}"); }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Hooks en el hilo de UI y primera evaluación AQUÍ, no en OnHandleCreated: allí caería en plena
        // llamada a Show() y, con un juego ya a pantalla completa, el Hide() quedaría pisado por el Show.
        _fullscreen.Start();
        ReassertTopmost();
        _anim.Reset();   // lo que pasó antes de mostrarse no se vio: aparece desde 44 × 0 (o ya asentada, en el relevo)
        _appearingSettled = AppearSettled;
        AppearSettled = false;
        try { Relayout(); }
        finally { _appearingSettled = false; }
        _ = StartMusicAsync();
        // Con otras ventanas ya en marcha (monitor recién conectado), lo que suena ya está en el contenedor compartido.
        if (_np is null && _shared.NowPlaying is { } playing) OnMusic(playing);
        if (ShelfEnabled) EnsureShelf();
        EnsureTasks();
        RequestBackdrop();
    }

    protected override void WndProc(ref Message m)
    {
        if (TasksWndProc(ref m)) return;
        switch (m.Msg)
        {
            case WM_MOUSEACTIVATE:
                m.Result = MA_NOACTIVATE;
                return;

            case WM_NCHITTEST:
            {
                var p = new Point(LoWordSigned(m.LParam) - _topLeft.X, HiWordSigned(m.LParam) - _topLeft.Y);
                m.Result = HitTest(p) ? HTCLIENT : HTTRANSPARENT;
                return;
            }

            case WM_MOUSEMOVE:
            {
                var mp = new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam));
                bool inside = HitTest(mp);
                if (inside && !_pointerInside) PointerEnter();
                else if (!inside && _pointerInside) PointerLeave();
                if (inside)
                {
                    UpdateHover(mp);
                    PageArrowHover(mp);
                    ProCardHover(mp);
                    MusicHover(mp);
                    MascotPointer(mp);
                    ShelfHover(mp);
                    TasksHover(mp);
                    UsageHover(mp);
                    FocusHover(mp);
                    if (((long)m.WParam & MK_LBUTTON) != 0) ShelfMaybeDragOut(mp);
                }
                return;
            }

            case WM_MOUSELEAVE:
                // Mientras algo se arrastra encima manda el arrastre (OnDragLeave), no el ratón.
                if (_pointerInside && !_dragOver) PointerLeave();
                return;

            case WM_LBUTTONDOWN:
                // Sin pasar por WinForms: su WM_LBUTTONDOWN captura el ratón para la ventana y, como el
                // WM_LBUTTONUP se atiende aquí, la captura no se soltaba nunca (el panel dejaba de recibir el ratón).
                // Solo se recuerda qué botón del Permiso se pulsó: la acción va al soltar sobre ESE mismo botón.
                PermissionPress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)));
                UpdatePress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)));
                PageArrowPress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)));
                ProCardPress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)));
                MusicPress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)));
                ShelfPress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)), doubleClick: false);
                TasksPress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)));
                UsagePress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)));
                FocusPress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)));
                ReminderPress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)));
                FocusAlertPress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)));
                return;

            case WM_LBUTTONDBLCLK:
                // La clase de ventana de WinForms lleva CS_DBLCLKS: el segundo de dos clics rápidos llega así. Solo cuenta
                // como otra pulsación en las flechas ‹ › de la bandeja (ShelfClickRules.Press) y en las de página; en lo demás
                // (tarjetas, «Quitar (N)», música, tareas, recordatorios, Permiso) haría la acción dos veces o la del botón ya cambiado.
                // Tampoco va a WinForms: capturaría el ratón.
                ShelfPress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)), doubleClick: true);
                // Igual las flechas de página: dos clics rápidos avanzan dos páginas.
                PageArrowPress(new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam)));
                return;

            case WM_LBUTTONUP:
                if (Capture) Capture = false;   // red de seguridad: jamás quedarse con el ratón
                {
                    var p = new Point(LoWordSigned(m.LParam), HiWordSigned(m.LParam));
                    if (HitTest(p))
                    {
                        if (!ReadOnlyAlertClick() && !ReminderClick(p) && !FocusAlertClick(p) && !PermissionClick(p) && !ProNoticeClick()
                            && !ProCardClick(p) && !UpdateClick(p) && !PageArrowClick(p) && !PageDotClick(p) && !ProLockClick(p)
                            && !MusicClick(p) && !ShelfClick(p, (long)m.WParam) && !TasksClick(p) && !UsageClick(p) && !FocusClick(p)
                            && !FocusRingClick(p) && !UsageRingClick(p))
                            _sm.OnClick(DateTime.UtcNow);
                        Sync();
                    }
                }
                return;

            case WM_MOUSEWHEEL:
                // El punto de WM_MOUSEWHEEL viene en coordenadas de pantalla.
                OnWheel(HiWordSigned(m.WParam),
                    new Point(LoWordSigned(m.LParam) - _topLeft.X, HiWordSigned(m.LParam) - _topLeft.Y));
                return;

            case WM_MOUSEHWHEEL:
                // Deslizamiento horizontal (trackpad con dos dedos o rueda inclinable): siempre cambia de página.
                OnHWheel(HiWordSigned(m.WParam));
                m.Result = (IntPtr)1;   // atendido: sin esto algunos drivers de trackpad lo reenvían como desplazamiento
                return;

            case WM_DPICHANGED:
                // Se ignora el rect sugerido: el notch se recoloca solo, centrado arriba y a su escala.
                m.Result = IntPtr.Zero;
                ScheduleRelayout();
                return;

            case WM_DISPLAYCHANGE:
                ScheduleRelayout();
                DisplayChanged?.Invoke();   // el host: altas y bajas de monitores (todos los monitores)
                break;

            case WM_SETTINGCHANGE:
                if ((long)m.WParam == SPI_SETWORKAREA) ScheduleRelayout();
                break;

            default:
                if (_taskbarCreated != 0 && m.Msg == (int)_taskbarCreated) ReassertTopmost();
                break;
        }
        base.WndProc(ref m);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        // La sombra sigue a la pastilla. Al mostrarla se reordenan: ShowWindow la deja arriba del todo.
        if (Visible) { _shadow.SetVisible(true); ReassertTopmost(); }
        else _shadow.SetVisible(false);
    }

    // ---------------------------------------------------------------- entrada

    private bool HitTest(Point p) =>
        Visible && !_hiddenForFullscreen && _migrateTo is null
        && NotchGeometry.HitTest(p, _anim.Width, _anim.Height, _scale, _surface, _hoverHold);

    /// <summary>
    /// Clic en un Permiso o Recordatorio que aquí sale en solo lectura (la interactiva es otra ventana): este notch pide ser
    /// el interactivo (sus botones aparecen aquí, armándose como siempre). <c>false</c> si no es el caso (clic normal).
    /// </summary>
    private bool ReadOnlyAlertClick()
    {
        if (!NotchAlertRouting.ReadOnlyHere(_sm.ActiveAlert, _interactive)
            || _sm.Mode is not (NotchMode.Permission or NotchMode.Reminder)) return false;
        PromoteRequested?.Invoke(this);
        return true;
    }

    private void PointerEnter()
    {
        var tme = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE, hwndTrack = Handle };
        TrackMouseEvent(ref tme);
        _pointerInside = true;
        _sm.OnPointerEnter(DateTime.UtcNow);
        Sync();
    }

    private void PointerLeave()
    {
        _pointerInside = false;
        _held = PermissionVerdict.None;   // sin captura no llegará el UP: salir cancela la pulsación
        _heldRequestId = null;
        _updateHover = _updatePressed = false;
        _arrowHover = _arrowHeld = PageArrow.None;   // las flechas se van (Sync ⇒ UpdateArrows)
        ResetProPointer();
        _wheelAccum = 0;
        _musicHeld = default;
        if (_musicHover.Key is not null) _musicHover.Leave(snap: _options.ReduceMotion);
        _shelfHeld = default;
        _shelfWheelAccum = 0;
        if (_shelfHover.Key is not null) _shelfHover.Leave(snap: _options.ReduceMotion);
        _tasksHeld = default;
        _tasksWheelAccum = 0;
        _reminderHeld = PermissionVerdict.None;
        _reminderHeldId = null;
        if (_tasksHover.Key is not null) _tasksHover.Leave(snap: _options.ReduceMotion);
        _usageHeld = default;
        _usageWheelAccum = 0;
        UsagePlotHover(Point.Empty, over: false);   // el tooltip se funde
        if (_usageHover.Key is not null) _usageHover.Leave(snap: _options.ReduceMotion);
        _focusHeld = default;
        _focusAlertHeld = false;
        if (_focusHover.Key is not null) _focusHover.Leave(snap: _options.ReduceMotion);
        _idleScene.OnPointer(null, DateTime.UtcNow);   // los ojos vuelven al centro (lo pinta el Sync de abajo)
        _sm.OnPointerLeave(DateTime.UtcNow);
        Sync();
    }

    /// <summary>
    /// Clic en un Permiso con botones: Permitir / Denegar (si la máquina lo acepta: armado y sin contestar) o, fuera de
    /// los botones, enfocar la terminal de la sesión. <c>false</c> si no hay Permiso con botones (clic normal).
    /// </summary>
    private bool PermissionClick(Point p)
    {
        var held = _held;
        string? heldId = _heldRequestId;
        _held = PermissionVerdict.None;
        _heldRequestId = null;
        if (!_interactive || _sm.Mode != NotchMode.Permission || _sm.ActiveAlert is not { RequestId: not null } alert) return false;
        var over = PermissionButtonAt(p);
        if (over == PermissionVerdict.None)
        {
            // Soltar fuera de los botones tras pulsar en uno cancela (no enfoca la terminal).
            if (held == PermissionVerdict.None
                && _hub.Sessions.FirstOrDefault(s => s.SessionId == alert.SessionId) is { } session) TerminalFocus.TryFocus(session);
            return true;
        }

        // Como un botón convencional: pulsar y soltar sobre el MISMO botón y de la MISMA petición. Pulsar en Denegar
        // (o en el texto) y soltar en Permitir no cuenta.
        if (over != held || heldId != alert.RequestId) return true;
        // Lo que se pinta tiene que ser lo que se aprueba (entero, y visto 400 ms), y quieto bajo el cursor (spec de
        // movimiento §3.4).
        if (!ShownMatches(alert) || !PaintedArmed(alert) || !_anim.ShapeSettled) return true;
        // Refuerzo contra el clic sintético: la posición REAL del cursor al generarse este UP también cae en el botón.
        if (!PermissionInputGuard.MessagePosInside(GetMessagePos(), _topLeft, PermissionButtonRect(over))) return true;

        bool onAllow = over == PermissionVerdict.Allow;
        var now = DateTime.UtcNow;
        if (_sm.OnPermissionButton(onAllow, now) is not { } requestId) return true;   // sin armar o ya contestado
        var verdict = onAllow ? PermissionVerdict.Allow : PermissionVerdict.Deny;
        if (!_hub.Permissions.Resolve(requestId, verdict, now)) _sm.OnPermissionRejected(requestId, now);
        else _shared.AnnouncePermissionAnswer(this, alert.SessionId, requestId, verdict);   // las de solo lectura confirman a la vez
        return true;
    }

    // ---- aviso de actualización

    /// <summary>¿Se ve la línea de la actualización en el expandido, quieta? (sin aviso integrado encima y del todo abierta).</summary>
    private bool UpdateRowShown => _updateView is not null && _anim.ShownAlert is null && _sm.Mode == NotchMode.Expanded
        && _anim.ShownMode == NotchMode.Expanded && _anim.ContentAlpha >= 1f && !_proCardOpen;

    private bool OverUpdateButton(Point p) => UpdateRowShown && _updateView is { Button: true }
        && NotchUpdateLayout.HitButton(p, NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface), _scale);

    private void UpdateHover(Point p)
    {
        bool over = OverUpdateButton(p);
        if (over == _updateHover) return;
        _updateHover = over;
        if (!over) _updatePressed = false;
        RenderFrame();
    }

    private void UpdatePress(Point p)
    {
        bool over = OverUpdateButton(p);
        if (over == _updatePressed) return;
        _updatePressed = over;
        RenderFrame();
    }

    /// <summary>
    /// Clic al SOLTAR en la línea de la actualización: sobre el botón pulsado, «Actualizar»; en una ventana que no es la
    /// interactiva (todos los monitores), pide serlo (el botón aparece aquí, como con los Permisos). <c>false</c> si no era suyo.
    /// </summary>
    private bool UpdateClick(Point p)
    {
        bool pressed = _updatePressed;
        _updatePressed = false;
        if (!UpdateRowShown) return false;
        var pill = NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface);
        if (!NotchUpdateLayout.HitRow(p, pill, _scale)) return false;
        if (!_interactive)
        {
            if (_updateView is { Ready: true }) PromoteRequested?.Invoke(this);
            return true;
        }
        if (pressed && NotchUpdateLayout.HitButton(p, pill, _scale)) RequestUpdate();
        return true;   // la línea no contrae el expandido
    }

    /// <summary>WM_LBUTTONDOWN: recuerda el botón del Permiso bajo el cursor (y pinta la pulsación).</summary>
    private void PermissionPress(Point p)
    {
        // Un botón sin armar ignora la pulsación: pulsar antes y soltar ya armado no cuenta.
        bool armed = _interactive && _sm.PermissionActionable && _sm.PermissionArmed(DateTime.UtcNow) && _anim.ShapeSettled
            && _sm.ActiveAlert is { } active && PaintedArmed(active);
        var over = HitTest(p) && armed ? PermissionButtonAt(p) : PermissionVerdict.None;
        // Un PostMessage sin ratón de verdad no cuenta como pulsación: el botón izquierdo pulsado (GetAsyncKeyState) y el
        // cursor real (GetMessagePos) sobre ESE botón.
        if (over != PermissionVerdict.None
            && !(PermissionInputGuard.RealPress(GetAsyncKeyState(VK_LBUTTON))
                && PermissionInputGuard.MessagePosInside(GetMessagePos(), _topLeft, PermissionButtonRect(over))))
            over = PermissionVerdict.None;
        string? id = over == PermissionVerdict.None ? null : _sm.ActiveAlert?.RequestId;
        if (over == _held && id == _heldRequestId) return;
        _held = over;
        _heldRequestId = id;
        RenderFrame();
    }

    /// <summary>Botón del Permiso (Permitir / Denegar) bajo <paramref name="p"/>; <c>None</c> si ninguno.</summary>
    private PermissionVerdict PermissionButtonAt(Point p)
    {
        var (deny, allow) = NotchGeometry.PermissionButtons(NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface), _scale);
        return allow.Contains(p) ? PermissionVerdict.Allow : deny.Contains(p) ? PermissionVerdict.Deny : PermissionVerdict.None;
    }

    /// <summary>Rectángulo (superficie del notch) del botón <paramref name="verdict"/>; vacío con <c>None</c>.</summary>
    private Rectangle PermissionButtonRect(PermissionVerdict verdict)
    {
        var (deny, allow) = NotchGeometry.PermissionButtons(NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface), _scale);
        return verdict switch
        {
            PermissionVerdict.Allow => allow,
            PermissionVerdict.Deny => deny,
            _ => Rectangle.Empty,
        };
    }

    /// <summary>¿El aviso que SE PINTA es el de la máquina (misma petición y mismo texto)? En un fundido puede no serlo.</summary>
    private bool ShownMatches(NotchAlert alert) =>
        _anim.ShownAlert is { } shown && shown.RequestId == alert.RequestId
        && shown.Title == alert.Title && shown.Detail == alert.Detail;

    private static string PermissionKey(string requestId, string title, string? detail) =>
        requestId + "\n" + title + "\n" + detail;

    /// <summary>Tras cada frame: ¿el painter pintó ENTERO un Permiso con botones? El armado de lo pintado cuenta desde que
    /// esa petición con ese texto se ve entera; si deja de verse entera o cambia, vuelve a empezar.</summary>
    private void TrackPaintedPermission()
    {
        string? key = _painter.LastPermissionPaint is { Complete: true } p ? PermissionKey(p.RequestId, p.Title, p.Detail) : null;
        if (key == _paintedKey) return;
        _paintedKey = key;
        _paintedSince = _clock.Elapsed;
    }

    /// <summary>¿Lo último que se pintó entero es ESTE aviso (misma petición, mismo título y mismo detalle)?</summary>
    private bool PaintedKeyMatches(NotchAlert alert) =>
        alert.RequestId is { } id && _paintedKey is not null && _paintedKey == PermissionKey(id, alert.Title, alert.Detail);

    /// <summary>Relleno del armado sobre lo que SE PINTA: 0 hasta que el aviso se ve entero y 1 a los 400 ms.</summary>
    private float PaintedArmProgress(NotchAlert alert) => !PaintedKeyMatches(alert) ? 0f
        : (float)Math.Clamp((_clock.Elapsed - _paintedSince).TotalMilliseconds / NotchTiming.PermissionArm.TotalMilliseconds, 0, 1);

    /// <summary>¿El aviso lleva <see cref="NotchTiming.PermissionArm"/> pintado ENTERO? (además del armado de la máquina).</summary>
    private bool PaintedArmed(NotchAlert alert) => PaintedArmProgress(alert) >= 1f;

    /// <summary>Rueda en el expandido: cambia de página (una por gesto, <see cref="PageCarousel"/>) o, sobre la lista de
    /// sesiones de Claude que no cabe entera (o sin más páginas), la desplaza (acumula el delta: trackpads de precisión).
    /// La ventana nunca tiene foco: la rueda llega gracias a «Desplazar ventanas inactivas al pasar el ratón»
    /// (activo por defecto en Windows 10/11); con esa opción apagada no hay rueda. Verificar en la Task 9.</summary>
    /// <summary>Deslizamiento horizontal: cambia de página (una por gesto, inercia del trackpad incluida).</summary>
    private void OnHWheel(int delta)
    {
        if (_sm.Mode != NotchMode.Expanded || _anim.ShownMode != NotchMode.Expanded || _pages.Count < 2 || _proCardOpen) return;
        int d = NotchPages.HorizontalToCarouselDelta(delta);
        _wheelAccum = 0;
        _shelfWheelAccum = 0;
        _tasksWheelAccum = 0;
        _usageWheelAccum = 0;
        if (_carousel.OnWheel(d, DateTime.UtcNow)) PageChanged(d < 0 ? 1 : -1);
    }

    private void OnWheel(int delta, Point p)
    {
        if (_sm.Mode != NotchMode.Expanded || _anim.ShownMode != NotchMode.Expanded || _proCardOpen) return;
        bool withAlert = TopLineShown, dots = _pages.Count > 1;
        var pill = NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface);
        bool overList, overflows;
        int rows;
        if (PageLocked(_carousel.Current))
        {
            // Página con el candado del Pro: no hay lista que desplazar, la rueda solo cambia de página.
            rows = 1;
            overList = overflows = false;
        }
        else if (_carousel.Current == NotchPage.Shelf)
        {
            // La fila de tarjetas de la Bandeja, con la MISMA geometría que se pinta.
            var r = ShelfLayout.Compute(NotchContentPainter.PageInner(pill, _scale, withAlert, dots), _scale, _shelfSnap.Items.Count);
            rows = r.Slots;
            overList = r.Strip.Contains(p);
            overflows = r.Overflows;
        }
        else if (_carousel.Current == NotchPage.Tasks)
        {
            (rows, overList) = TasksWheelInfo(p, withAlert, dots);
            overflows = _taskItems.Count > rows;
        }
        else if (_carousel.Current == NotchPage.Usage)
        {
            // Sobre la gráfica con varias series (Pro): las alterna; en el resto de la página, cambia de página.
            rows = 1;
            (overList, overflows) = UsageWheelInfo(p);
        }
        else
        {
            overList = NotchContentPainter.SessionColumn(pill, _scale, withAlert, dots).Contains(p);
            rows = NotchContentPainter.ExpandedRows(_anim.Height, _scale, withAlert, dots);
            overflows = ListedSessions().Count > rows;
        }
        switch (NotchPages.RouteWheel(_carousel.Current, _pages.Count, overList, overflows))
        {
            case WheelTarget.Pages:
                _wheelAccum = 0;
                _shelfWheelAccum = 0;
                _tasksWheelAccum = 0;
                _usageWheelAccum = 0;
                if (_carousel.OnWheel(delta, DateTime.UtcNow)) PageChanged(delta < 0 ? 1 : -1);
                return;
            case WheelTarget.UsageSeries:
                UsageWheel(delta);
                return;
            case WheelTarget.TaskList:
                ScrollTasks(delta, rows);
                return;
            case WheelTarget.ShelfList:
                ScrollShelf(delta);
                return;
            default:
                ScrollSessions(delta, rows);
                return;
        }
    }

    /// <summary>Desplaza la fila de tarjetas de la Bandeja una tarjeta por paso de rueda (hacia abajo = las siguientes).</summary>
    private void ScrollShelf(int delta)
    {
        _shelfWheelAccum += delta;
        int steps = _shelfWheelAccum / WHEEL_DELTA;
        if (steps == 0) return;
        _shelfWheelAccum -= steps * WHEEL_DELTA;
        if (!ScrollShelfTo(ShelfLayout.Step(_shelfFirst, _shelfSnap.Items.Count, -steps))) return;
        RehoverShelf();   // con reduce-motion ya saltó; si se desliza, cada frame lo sigue (Frame)
        Frame();
        Pump();
    }

    /// <summary>Desplaza la lista de sesiones de la página Claude (<paramref name="rows"/> = filas que se pintan).</summary>
    private void ScrollSessions(int delta, int rows)
    {
        _wheelAccum += delta;
        int steps = _wheelAccum / WHEEL_DELTA;
        if (steps == 0) return;
        _wheelAccum -= steps * WHEEL_DELTA;
        // Las mismas filas que pinta el expandido (pueden ser menos de 4): así la última sesión se alcanza.
        int max = Math.Max(0, ListedSessions().Count - rows);
        int next = Math.Clamp(_sessionOffset - steps, 0, max);   // rueda hacia abajo (delta < 0) avanza
        if (next == _sessionOffset) return;
        _sessionOffset = next;
        RenderFrame();
    }

    /// <summary>Clic en un punto de página del expandido: salta a esa página. <c>true</c> si el clic era de los puntos
    /// (también el de la página actual: no debe contraer el expandido).</summary>
    private bool PageDotClick(Point p)
    {
        if (_pages.Count < 2 || _sm.Mode != NotchMode.Expanded || _anim.ShownMode != NotchMode.Expanded || _proCardOpen) return false;
        int i = NotchGeometry.PageDotAt(p, NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface), _scale, _pages.Count);
        if (i < 0) return false;
        int from = _carousel.Index;
        if (i == from) return true;
        _carousel.GoTo(_pages[i]);
        PageChanged(i > from ? 1 : -1);
        return true;
    }

    /// <summary>Flechas visibles: expandido asentado con varias páginas, el ratón encima, sin la tarjeta del Pro y solo en la
    /// ventana interactiva (con un notch por monitor, la interacción va en una).</summary>
    private bool ArrowsShown => _interactive && _pages.Count > 1 && _pointerInside && _sm.Mode == NotchMode.Expanded
        && _anim.ShownMode == NotchMode.Expanded && !_proCardOpen && _migrateTo is null;

    /// <summary>Flecha bajo <paramref name="p"/> (aunque no lleve a ningún sitio); <see cref="PageArrow.None"/> sin flechas.</summary>
    private PageArrow PageArrowAt(Point p) => !ArrowsShown ? PageArrow.None
        : NotchGeometry.PageArrowAt(p, NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface), _scale, _pages.Count);

    /// <summary>Objetivos de las flechas (aparecer, disponibles, realce). Sin reservas: también en cada frame.</summary>
    private void UpdateArrows() => _arrows.Update(ArrowsShown, _carousel.Index, _pages.Count, _arrowHover);

    private void PageArrowHover(Point p)
    {
        var over = PageArrowAt(p);
        if (over == _arrowHover) return;
        _arrowHover = over;
        UpdateArrows();
        Frame();
        Pump();
    }

    private void PageArrowPress(Point p)
    {
        _arrowHeld = PageArrowAt(p);
        if (_arrowHeld == PageArrow.None || PageArrows.Target(_carousel.Index, _pages.Count, _arrowHeld) < 0) return;
        _arrows.Press(_arrowHeld);   // el empujón hacia su dirección
        Frame();
        Pump();
    }

    /// <summary>Clic al SOLTAR en una flecha: a la página anterior o siguiente (sin dar la vuelta), con el cruce de siempre.
    /// <c>true</c> si el clic era de una flecha, también de la que no lleva a ningún sitio (no debe contraer el expandido).</summary>
    private bool PageArrowClick(Point p)
    {
        var held = _arrowHeld;
        _arrowHeld = PageArrow.None;
        var side = PageArrowAt(p);
        if (side == PageArrow.None) return false;
        int to = PageArrows.Target(_carousel.Index, _pages.Count, side);
        if (to < 0 || held != side) return true;
        _carousel.GoTo(_pages[to]);
        PageChanged((int)side);
        return true;
    }

    /// <summary>La página del carrusel cambió: cruce + píldora, y el alto nuevo (Sync ⇒ Ajuste en el animador).</summary>
    private void PageChanged(int direction)
    {
        if (_pointerInside)
        {
            // Pedido con el ratón encima (punto, rueda, flecha, anillo…): si la página nueva es más pequeña y deja el cursor
            // fuera, eso no es «el ratón se ha ido» (NotchStateMachine.OnUserPageChange). Mientras la forma cambia de
            // tamaño, la zona de hover es la unión de la vieja y la nueva (la nueva la pone Sync).
            _sm.OnUserPageChange(DateTime.UtcNow);
            _hoverHold = HoverHold.Start(_hoverHold, new SizeF(_anim.Width, _anim.Height));
            _hoverSettlePosted = false;
        }
        _pageAnim.Go(_carousel.Current, _carousel.Index, direction, _clock.Elapsed);
        _sessionOffset = 0;
        _wheelAccum = 0;
        _musicHover.Reset();
        _musicHeld = default;
        _musicMotion.Settle();
        ResetShelfPointer();
        if (_carousel.Current == NotchPage.Shelf) RefreshShelf();   // al mostrarse: ¿siguen ahí los archivos?
        ResetTasksPointer();
        _tasksOffset = 0;
        if (_carousel.Current != NotchPage.Tasks) EndTaskEdit(save: true, restoreFocus: true);
        if (_carousel.Current == NotchPage.Usage) EnterUsagePage();   // al mostrarse: cuenta atrás y datos al día
        else ResetUsagePointer();
        if (_carousel.Current == NotchPage.Focus) EnterFocusPage();   // al mostrarse: una oferta muy vieja caduca
        else ResetFocusPointer();
        Sync();
    }

    // ---------------------------------------------------------------- estado

    private void OnSessions(IReadOnlyList<SessionState> s) { if (Alive) { _sm.OnSessions(s, DateTime.UtcNow); Sync(); } }
    private void OnSignal(ClaudeSignal sig)
    {
        if (!Alive) return;
        _sm.OnSignal(sig, DateTime.UtcNow);
        Sync();
    }

    private void OnQuotaCrossing(QuotaCrossing c)
    {
        if (!Alive) return;
        _sm.OnQuotaCrossing(c, DateTime.UtcNow);
        Sync();
    }

    /// <summary>Campanita con «Terminado», el aviso rojo de cuota y el Permiso con botones (sonido de atención, F2) cuando
    /// el aviso SE MUESTRA (no al entrar en la cola: varios «Terminado» seguidos suenan uno a uno, a la vez que salen);
    /// nunca en pantalla completa ni presentación (aunque el notch no se oculte en ellas) ni con el sonido apagado. Lo
    /// que se calla no suena después. Un Permiso suena una vez por petición aunque se vuelva a armar.</summary>
    private void ChimeIfNewAlert()
    {
        if (_sm.ActiveAlert is not { } a || _chimed.TryGetValue(a, out _)) return;
        _chimed.Add(a, a);
        // El fin de fase del pomodoro lleva su aviso discreto, con su ajuste (y solo en la interactiva: FocusChime lo mira).
        if (a.Kind == NotchAlertKind.Focus)
        {
            FocusChime();
            return;
        }
        // Todos los monitores: solo suena la interactiva (nunca dos campanitas); en las demás el aviso se da por sonado.
        if (!NotchAlertRouting.MayChime(_interactive)) return;
        if (NotchSound.ChimeKey(a) is { } key && !_shared.TryChimeRequest(key)) return;
        // Pantalla completa en su monitor o en el de otra ventana (todos los monitores: el juego del monitor 1 oculta ese
        // notch y la interactiva pasa al 2, pero la campanita seguiría sonando en plena partida).
        bool fullscreen = NotchAlertRouting.QuietForFullscreen(_fullscreen.IsFullscreen, _shared.AnyFullscreen);
        if (NotchSound.ShouldPlay(a, _options.Sound, fullscreen)) NotchSound.Play();
    }
    private void OnQuota(RealUsage _) { if (Alive) Sync(); }

    /// <summary>Otra ventana contestó un Permiso (todos los monitores): aquí también «Permitido» / «Denegado» y luego sale.</summary>
    private void OnPermissionAnswered(object origin, string sessionId, string requestId, PermissionVerdict verdict)
    {
        if (!Alive || ReferenceEquals(origin, this)) return;   // la que contestó ya enseña su confirmación
        _sm.OnPermissionAnsweredElsewhere(sessionId, requestId, verdict, DateTime.UtcNow);
        Sync();
    }

    /// <summary>El broker cambió (a menudo en el hilo del pipe): el hub lo traduce en señales en el hilo de UI.</summary>
    private void OnPermissionsChanged()
    {
        if (!Alive || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (Alive) _hub.SyncPermissions(DateTime.UtcNow); }); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { /* cerrándose */ }
    }

    private bool Alive => !IsDisposed && !Disposing;

    /// <summary>¿Va esa página con el candado del Pro? (<see cref="NotchOptions.Locked"/>, de <see cref="NotchProGating"/>).</summary>
    private bool PageLocked(NotchPage page) => NotchProLock.Has(_options.Locked, page);

    private NotchStateMachine NewStateMachine(NotchOptions o)
    {
        var sm = new NotchStateMachine(new NotchSettings(o.OpenOnClick, o.Minimal, o.HideInFullscreen,
            o.AutoMinimalSeconds > 0 ? TimeSpan.FromSeconds(o.AutoMinimalSeconds) : null));
        var now = DateTime.UtcNow;
        sm.OnSessions(_hub.Sessions, now);
        // Peticiones de permiso ya anunciadas por el hub (ventana o máquina rehechas): siguen contestables.
        foreach (var p in _hub.Permissions.Pending.Where(p => p.PreviewComplete))
            sm.OnSignal(new PermissionPending(p.SessionId, p.ProjectName, now, p.Tool, p.Preview, p.RequestId), now);
        return sm;
    }

    /// <summary>Lleva el modo de la máquina al animador, reprograma el próximo plazo y arranca el bucle si hace falta.</summary>
    private void Sync()
    {
        if (!Alive) return;
        // Editando una tarea y el expandido deja de verse (pantalla completa, un Permiso con botones encima…): se guarda lo
        // escrito si vale y se suelta el foco. EndTaskEdit vuelve a llamar a Sync sin borrador.
        if (_taskDraft is not null && (_sm.Mode != NotchMode.Expanded || _hiddenForFullscreen))
        {
            EndTaskEdit(save: true, restoreFocus: true);
            return;
        }
        if (_interactive && _sm.Mode == NotchMode.Permission && _sm.ActiveAlert is { RequestId: { } unfitId } unfit
            && _painter.PermissionDetailLines(unfit.Detail, _scale) == 0)
        {
            // El comando no cabe ENTERO en 4 líneas: sin botones (solo lectura, «Responde en la terminal») y el helper
            // suelta ya, así Claude Code enseña su diálogo sin esperar al plazo.
            _hub.Permissions.Cancel(unfitId);
            _sm.OnPermissionUnanswerable(unfitId, DateTime.UtcNow);
        }
        UpdateIdleScene(DateTime.UtcNow);
        RefreshUsageMinute();
        ChimeIfNewAlert();
        ReportProNoticeIfShown();
        if (_hiddenForFullscreen)
        {
            ScheduleWake();
            NotifyPlace();
            return;
        }

        // Canción con letra sonando: el reposo no se encoge solo (pedido del 29-oct). Antes de leer el modo: si ya estaba
        // encogido, esto lo despierta en esta misma pasada.
        // Pomodoro corriendo: tampoco (su mini anillo es lo que se mira). En pausa sí puede encogerse: la raya enseña lo que queda.
        _sm.SetKeepAwake(CompactLyrics.KeepAwake(_options.Music, _options.Lyrics, _options.CompactLyrics, _np,
            _lyrics.State, _lyrics.Lines.Count) || Pomodoro.Run == Services.Notch.Features.FocusRun.Running, DateTime.UtcNow);
        var mode = _sm.Mode;
        if (mode != NotchMode.Expanded)
        {
            _sessionOffset = 0;
            _wheelAccum = 0;
            if (_musicHover.Key is not null || _musicHover.Visible) _musicHover.Reset();
            _musicHeld = default;
            // La tarjeta del Pro vive dentro del expandido: al contraerse (o al taparlo un Permiso) se cierra.
            _proCardOpen = false;
            ResetProPointer();
            // La gráfica de Uso: sin transición ni hover a medias para la próxima vez.
            if (_usageMorph.IsAnimating) _usageMorph.Settle();
            if (_usagePlot || _usageHeld.Control != UsageControl.None || _usageHover.Key is not null) ResetUsagePointer();
            if (_focusHeld.Control != FocusControl.None || _focusHover.Key is not null) ResetFocusPointer();
            if (_wasExpanded)
            {
                // Al cerrar se olvidan la selección y el desplazamiento de la bandeja.
                ResetShelfPointer();
                _shelfSel.Clear();
                ResetShelfScroll();
                ResetTasksPointer();
                _tasksOffset = 0;
                // Las series ocultas de Uso no se recuerdan: la próxima apertura las enseña todas.
                _usageHiddenSpend.Clear();
                _usageHiddenQuota.Clear();
            }
        }
        if (mode != NotchMode.Expanded)
        {
            _hoverHold = default;
            _arrowHover = _arrowHeld = PageArrow.None;   // cerrado: las flechas, ocultas y quietas para la próxima vez
            UpdateArrows();
            _arrows.Settle();
        }
        if (mode == NotchMode.Expanded && !_wasExpanded && _carousel.Index != 0)
        {
            // Cada apertura empieza en Claude. Aquí el contenido del expandido aún no se ve (espera a la forma): sin salto.
            _carousel.GoTo(_pages[0]);
            _pageAnim.Jump(_carousel.Current, _carousel.Index);
        }
        _wasExpanded = mode == NotchMode.Expanded;
        // La letra ANTES de elegir la maquetación: un acierto de la caché (síncrono) ya cuenta para la letra compacta. Su
        // Changed dentro de Sync se ignora (_inSync): lo de abajo ya lo tiene en cuenta.
        _inSync = true;
        try { _shared.UpdateLyrics(this, LyricsWanted); }
        finally { _inSync = false; }
        // Desde qué % la cuota de 5 h está en aviso (el primer umbral configurado): aquí, no en cada frame.
        _quotaAlertPct = CompactLyrics.QuotaAlertPct(_hub.QuotaThresholds);
        // Pomodoro en marcha: su mini anillo en el reposo (manda sobre la canción y el reloj) y en la vista previa sobre el
        // reposo; en pausa, solo si no suena música (si suena, vuelve la canción). En marcha o en pausa, en la ranura de
        // Trabajando. Los avisos y el expandido lo tapan: nunca es un aviso. Con la cuota de 5 h en aviso, el reposo se
        // ensancha para llevar también su anillo.
        bool claudeActive = _hub.Sessions.Any(s => s.Phase.IsActive());
        bool focusFace = FocusIdle(DateTime.UtcNow) && (mode == NotchMode.Idle || (mode == NotchMode.Peek && !claudeActive));
        bool focusWorking = FocusMini && (mode == NotchMode.Working || (mode == NotchMode.Peek && claudeActive));
        float extra = mode == NotchMode.Working
            ? _painter.MeasureWorkingExtra(_hub.Sessions, _scale) + FocusFace.WorkingExtra(focusWorking) : 0f;
        // Relevo del asistente de bienvenida: la primera vez que se ve, ya asentada (sin crecer desde 44 × 0), justo encima
        // del notch de reposo en que se ha convertido el asistente.
        bool appearSettled = _appearingSettled;
        _anim.ReduceMotion = _options.ReduceMotion || appearSettled;
        // Reposo con música sonando (y Claude sin trabajar: si no, el modo sería Trabajando): carátula mini + título o,
        // con letra sincronizada y la pista sonando, la letra compacta. Con el ratón encima del reposo (Vista previa) la
        // letra compacta se queda: el animador sigue pintando el reposo (sin cruce, la línea no salta) y solo crece el
        // anillo de la cuota con su % (pedido del 29-oct). La carátula + título, no: ahí va la Vista previa de siempre.
        bool songShown = _options.Music && _idleMusic.Show(DateTime.UtcNow) && !focusFace;
        bool idleMusic = mode == NotchMode.Idle && songShown;
        bool compact = CompactFace(idleMusic || (_sm.PeekOverIdle && songShown)) == IdleMusicFace.Lyrics;
        bool compactPeek = compact && mode == NotchMode.Peek;
        // Anillo de la letra compacta: atenuado en reposo; entero con el ratón encima o con el aviso de cuota (y el % solo
        // con el ratón). Mientras la letra compacta no se pinta, sin animación: aparece ya como toca.
        _compactQuota.Set(compactPeek, CompactLyrics.QuotaAlert(_hub.Quota?.FiveHour?.UtilizationPct, _hub.QuotaThresholds),
            snap: _anim.ShownLayout != LayoutCompactLyrics);
        // Reloj y gato (B7): 0 = la maquetación de siempre (también con música: entonces no hay calma).
        int idleLayout = idleMusic || focusFace ? 0 : _idleScene.Layout;
        // Todos los monitores: el Permiso / Recordatorio de la interactiva sale aquí en solo lectura, sin botones.
        bool readOnly = mode is NotchMode.Permission or NotchMode.Reminder
            && NotchAlertRouting.ReadOnlyHere(_sm.ActiveAlert, _interactive);
        var size = readOnly ? NotchLayout.ReadOnlyAlertSize(_scale)
            : mode == NotchMode.Expanded && _proCardOpen ? NotchProLayout.CardSize(_scale)
            : mode == NotchMode.Expanded && PageLocked(_carousel.Current)
            ? NotchProLayout.LockedPageSize(_sm.ActiveAlert is not null || _updateView is not null, _pages.Count > 1, _scale)
            : mode == NotchMode.Expanded
            ? NotchLayout.PageSize(_carousel.Current,
                new PageRows(_hub.Sessions.Count(x => x.Phase != SessionPhase.Ended), TaskPageRows, _shelfSnap.Items.Count),
                _sm.ActiveAlert is not null || _updateView is not null, _pages.Count > 1, _scale)
            : mode == NotchMode.Permission && _sm.ActiveAlert is { RequestId: not null } withButtons
                ? NotchLayout.PermissionButtonsSize(_scale, _painter.PermissionDetailLines(withButtons.Detail, _scale))
                : compact ? CompactLyrics.Size(CompactExtra(DateTime.UtcNow), _scale)
                : focusFace && mode == NotchMode.Idle
                    ? FocusFace.IdleSize(_scale, _hub.Quota?.FiveHour?.UtilizationPct >= _quotaAlertPct)
                : idleMusic ? NotchLayout.IdleMusicSize(_scale)
                : idleLayout != 0 ? IdleScene.SizeFor(mode, idleLayout, ClockTextWidth(idleLayout), _scale)
                : NotchLayout.TargetSize(mode, _scale, extra);
        int layout = readOnly ? LayoutReadOnly : mode == NotchMode.Expanded && _proCardOpen ? LayoutProCard
            : compact ? LayoutCompactLyrics : idleMusic ? LayoutIdleMusic : focusFace || focusWorking ? LayoutFocus : idleLayout;
        if (mode != _targetMode || layout != _targetLayout)
            _catSteady = mode == _targetMode && (layout & _targetLayout & IdleScene.LayoutMascot) != 0;
        _targetMode = mode;
        _targetLayout = layout;
        if (_hoverHold.Active) _hoverHold = _hoverHold with { To = size };
        // Mudándose de monitor: se encoge hasta no verse (Frame termina la mudanza) y vuelve a crecer en el nuevo.
        if (_migrateTo is not null) _anim.SetTarget(NotchMode.Hidden, null, SizeF.Empty, _scale, _clock.Elapsed);
        else _anim.SetTarget(mode, _sm.ActiveAlert, size, _scale, _clock.Elapsed, _targetLayout,
            compactPeek ? NotchMode.Idle : null);
        if (appearSettled) _anim.ReduceMotion = _options.ReduceMotion;

        ScheduleWake();
        Frame();
        Pump();
        ScheduleMusicTick();
        NotifyPlace();
    }

    private void ScheduleWake()
    {
        _wake.Stop();
        var now = DateTime.UtcNow;
        // Oculto por pantalla completa no se consultan los plazos visuales: nadie los vería (ShowAgain → Sync reprograma).
        bool hidden = _hiddenForFullscreen;
        var next = NextWake(_sm.NextWakeIn(now),
            !hidden && _options.Music ? _idleMusic.NextChangeIn(now) : null,
            hidden ? null : _idleScene.NextWakeIn(now, DateTime.Now),
            hidden,
            hidden ? null : CompactNextLineIn(now),
            hidden ? null : UsageNextMinuteIn(),
            hidden ? null : FocusNextTickIn());
        if (next is not { } d) return;
        _wake.Interval = (int)Math.Clamp(Math.Ceiling(d.TotalMilliseconds), 1, int.MaxValue);
        _wake.Start();
    }

    /// <summary>
    /// Próximo despertar del reloj de plazos: el más cercano de la máquina de modos (<paramref name="machine"/>: avisos,
    /// minimizado automático…), el fin de la gracia del reposo con música (<paramref name="idleMusic"/>) y la cara del
    /// carrusel, el minuto del reloj o el gato (<paramref name="face"/>) y el cambio de línea de la letra compacta
    /// (<paramref name="lyricLine"/>), el minuto de la página Uso (<paramref name="usageMinute"/>) y lo que enseña el pomodoro
    /// (<paramref name="focus"/>). Con el notch oculto por pantalla completa solo cuenta la máquina: los plazos visuales
    /// despertarían el hilo durante todo el juego para no pintar nada (revisión API, punto 7). PURO.
    /// </summary>
    internal static TimeSpan? NextWake(TimeSpan? machine, TimeSpan? idleMusic, TimeSpan? face, bool hiddenForFullscreen,
        TimeSpan? lyricLine = null, TimeSpan? usageMinute = null, TimeSpan? focus = null)
    {
        var next = machine;
        if (hiddenForFullscreen) return next;
        // Fin de la gracia del reposo con música pausada: vuelve a los anillos (Sync, en el tick).
        if (idleMusic is { } idle && (next is null || idle < next)) next = idle;
        // Cara del carrusel, minuto del reloj a la vista y el gato (aparecer, parpadeo, dormirse); dormido, nada.
        if (face is { } f && (next is null || f < next)) next = f;
        // Letra compacta: la siguiente línea (sin música sonando o tras la última, null).
        if (lyricLine is { } l && (next is null || l < next)) next = l;
        // Página Uso a la vista: el minuto siguiente (la cuenta atrás del reinicio va por minutos).
        if (usageMinute is { } u && (next is null || u < next)) next = u;
        // Pomodoro corriendo: el segundo de la página o el minuto del mini anillo (parado o en pausa, null: nada que contar).
        if (focus is { } fo && (next is null || fo < next)) next = fo;
        return next;
    }

    // ---------------------------------------------------------------- pantalla completa, Snap, topmost

    private void OnFullscreenChanged(bool on)
    {
        if (!Alive) return;
        _shared.SetFullscreen(this, on);   // para que tampoco suene la interactiva de otro monitor
        _sm.OnFullscreen(on);
        if (on && _options.HideInFullscreen) HideForFullscreen();
        else if (!on && _hiddenForFullscreen) ShowAgain();
        Sync();
    }

    private void HideForFullscreen()
    {
        _hiddenForFullscreen = true;
        // Nadie ve el notch: lo pendiente se cierra sin decisión y lo nuevo se rechaza al momento (Claude Code enseña su
        // diálogo sin esperar el plazo). El cambio llega a la máquina por Changed → SyncPermissions (solo lectura). Con un
        // notch por monitor, solo si no se ve ninguno.
        _shared.SetHidden(this, true);
        if (_migrateTo is not null)
        {
            // A media mudanza: ya no hay nada que encoger. Se queda con el monitor nuevo y lo mira en cuanto pase la ráfaga.
            _options = _options with { MonitorDeviceName = _migrateTo };
            _migrateTo = null;
            ScheduleRelayout();
        }
        StopLoop();
        _wake.Stop();
        _musicTick.Stop();
        _pointerInside = false;
        _paintedKey = null;   // lo pintado antes de ocultarse ya no se ve
        _anim.Reset();   // al volver, aparece desde 44 × 0
        Hide();
    }

    private void ShowAgain()
    {
        _hiddenForFullscreen = false;
        if (_monitorMissing) return;       // su monitor se fue: sigue oculta hasta que el host la cierre
        _shared.SetHidden(this, false);   // vuelve a verse: se aceptan peticiones otra vez
        Show();          // ShowWithoutActivation: SW_SHOWNOACTIVATE, no roba el foco
        ReassertTopmost();
        _shared.PollReminders();   // lo vencido mientras estaba oculto ya espera en la cola; esto reprograma el siguiente
    }

    private void OnMoveSize(bool moving)
    {
        if (!Alive) return;
        _anim.SetDimmed(moving, _clock.Elapsed);
        Pump();
    }

    private void ReassertTopmost()
    {
        if (!IsHandleCreated || !Visible) return;
        const uint flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE;
        // Sombra primero y pastilla después: cada una sube al frente de los topmost, así la pastilla queda
        // justo encima de su sombra (la sombra también pinta bajo la forma).
        if (_shadow.Handle != IntPtr.Zero) SetWindowPos(_shadow.Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, flags);
    }

    // ---------------------------------------------------------------- contraste adaptativo (F5 · B6)

    /// <summary>Pide un muestreo del fondo (debounce de <see cref="AdaptiveContrast.Debounce"/>; nunca en bucle).</summary>
    private void RequestBackdrop()
    {
        if (!Alive || !_options.Contrast) return;
        _contrast.Request(_clock.Elapsed);
        ArmBackdropTimer();
    }

    private void CancelBackdrop()
    {
        _contrast.Cancel();
        _contrastTimer.Stop();
    }

    private void ArmBackdropTimer()
    {
        _contrastTimer.Stop();
        if (_contrast.NextWakeIn(_clock.Elapsed) is not { } d) return;
        _contrastTimer.Interval = (int)Math.Clamp(Math.Ceiling(d.TotalMilliseconds), 1, int.MaxValue);
        _contrastTimer.Start();
    }

    /// <summary>Muestrea la franja bajo la pastilla de reposo; si el contraste cambia, repinta una vez.</summary>
    private void SampleBackdrop()
    {
        if (!Alive || !IsHandleCreated || !Visible || _hiddenForFullscreen) { CancelBackdrop(); return; }
        var strip = AdaptiveContrast.StripBelow(_topLeft, _surface.Width, NotchLayout.TargetSize(NotchMode.Idle, _scale, 0f));
        if (_contrast.Tick(_clock.Elapsed, strip))
        {
            _painter.Contrast = _contrast.Current;
            RenderFrame();
        }
        else ArmBackdropTimer();   // el timer saltó un pelo antes de plazo: rearma lo que falte (o nada)
    }

    // ---------------------------------------------------------------- colocación y DPI

    private void ScheduleRelayout()
    {
        _relayout.Stop();
        _relayout.Start();
    }

    /// <summary>Recalcula monitor, escala, superficie y posición. Si algo cambia, el notch reaparece
    /// recentrado (nunca se desliza entre monitores) y el renderer recrea su DIB en el siguiente frame.</summary>
    private void Relayout()
    {
        if (!Alive) return;
        var (scale, surface, topLeft) = (_scale, _surface, _topLeft);
        ComputePlacement();
        if (_monitorMissing)
        {
            // Todos los monitores y su monitor ya no está: nada de caer al principal (allí ya hay otra). El host la cierra.
            StopLoop();
            _wake.Stop();
            if (Visible) Hide();
            return;
        }
        bool changed = Math.Abs(scale - _scale) > 0.0001f || surface != _surface || topLeft != _topLeft;
        if (changed && IsHandleCreated)
        {
            SetBounds(_topLeft.X, _topLeft.Y, _surface.Width, _surface.Height);
            _anim.Reset();
        }
        if (changed) UpdateShelfCards();   // otra escala: miniaturas de otro tamaño en px
        _fullscreen.Reevaluate();   // puede haber cambiado de monitor
        Sync();
    }

    private void ComputePlacement()
    {
        var chosen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _options.MonitorDeviceName);
        _monitorMissing = chosen is null && StrictMonitor && !string.IsNullOrEmpty(_options.MonitorDeviceName);
        if (_monitorMissing) return;
        var screen = chosen ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
        _device = screen.DeviceName;
        var b = screen.Bounds;
        _monitor = MonitorFromPoint(new POINT { X = b.Left + b.Width / 2, Y = b.Top + b.Height / 2 }, MONITOR_DEFAULTTONEAREST);
        int dpi = 96;
        try
        {
            if (_monitor != IntPtr.Zero && GetDpiForMonitor(_monitor, MDT_EFFECTIVE_DPI, out uint dx, out _) == 0) dpi = (int)dx;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            dpi = DeviceDpi;   // sin shcore (no debería pasar en Windows 10+): el DPI de la propia ventana
        }
        _scale = NotchPlacement.Scale(dpi, _options.SizePercent, b.Width);
        _surface = NotchPlacement.SurfaceFor(_scale, _surfaceTall);
        _topLeft = NotchPlacement.TopLeft(b, _surface);
    }

    /// <summary>
    /// La superficie solo crece a la alta mientras la página Uso es la activa (objetivo del carrusel o en su cruce) y mientras la
    /// forma de los muelles, o la que retiene la zona de hover, no quepa en la baja (<see cref="NotchPlacement.NeedsTallSurface"/>).
    /// El ancho no cambia (ni la esquina): <c>UpdateLayeredWindow</c> redimensiona la ventana y su sombra en el mismo frame.
    /// </summary>
    private void UpdateSurfaceHeight()
    {
        bool usage = UsageEnabled
            && ((_sm.Mode == NotchMode.Expanded && _carousel.Current == NotchPage.Usage)
                || (_anim.ShownMode == NotchMode.Expanded
                    && (_pageAnim.Current == NotchPage.Usage || (_pageAnim.IsAnimating && _pageAnim.Previous == NotchPage.Usage))));
        bool tall = NotchPlacement.NeedsTallSurface(usage, _anim.Height, _hoverHold, _scale);
        if (tall == _surfaceTall) return;
        _surfaceTall = tall;
        _surface = NotchPlacement.SurfaceFor(_scale, tall);
    }

    /// <summary>¿Superficie alta ahora mismo? (tests)</summary>
    internal bool SurfaceTall => _surfaceTall;

    // ---------------------------------------------------------------- bucle de animación

    /// <summary>Elige el motor del siguiente frame: nada (quieto), DwmFlush en Idle (muelles), o timer
    /// (spinner solo a 30 fps, o respaldo de 16 ms si DWM no compone).</summary>
    private void Pump()
    {
        bool animating = Animating, spinner = SpinnerOn || ArmingOn || ReminderArmingOn || FocusAlertArmingOn || EqualizerOn || LyricsSearchOn;
        if (!Visible || _hiddenForFullscreen || (!animating && !spinner))
        {
            StopLoop();
            _timerLoop = false;   // la próxima animación vuelve a probar DwmFlush
            _fastFlushes = 0;
            return;
        }
        if (animating && !_timerLoop)
        {
            _frameTimer.Stop();
            if (!_idleHooked) { Application.Idle += OnIdle; _idleHooked = true; }
            return;
        }
        Unhook();
        int interval = animating ? NotchMotion.FallbackFrameMs : 1000 / NotchMotion.SpinnerFps;
        if (_frameTimer.Interval != interval) _frameTimer.Interval = interval;
        if (!_frameTimer.Enabled) _frameTimer.Start();
    }

    /// <summary>Bucle en reposo del hilo de UI: un frame por composición de DWM mientras no haya mensajes.</summary>
    private void OnIdle(object? sender, EventArgs e)
    {
        while (_idleHooked && !PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE))
        {
            Frame();
            if (!Animating) { Pump(); return; }

            var t0 = _clock.Elapsed;
            int hr = DwmFlush();
            if (hr < 0 || (_clock.Elapsed - t0).TotalMilliseconds < 1)
            {
                if (++_fastFlushes >= NotchMotion.DwmFlushFastReturns)
                {
                    _timerLoop = true;   // DWM no compone (pantalla apagada, sesión bloqueada, RDP…)
                    Pump();
                    return;
                }
            }
            else _fastFlushes = 0;
        }
    }

    private void Frame()
    {
        if (!Alive || _hiddenForFullscreen) return;
        var now = _clock.Elapsed;
        _anim.Step(now);
        if (_migrateTo is not null && _anim.IsGone)
        {
            FinishMove();   // ya no se ve: al monitor nuevo (Relayout ⇒ Sync ⇒ vuelve a crecer allí)
            return;
        }
        _pageAnim.Step(now);
        _musicMotion.Step(now);
        _iconMotion.Step(now);
        _compactMotion.Step(now);
        // El realce integra el tiempo desde el último paso (acotado: tras un reposo no debe saltar de golpe).
        double dt = Math.Clamp((now - _lastStep).TotalSeconds, 0.0, 1.0 / 30.0);
        _lastStep = now;
        bool shelfSliding = !_shelfScroll.IsSettled;
        if (dt > 0)
        {
            _musicHover.Advance(dt);
            _compactQuota.Advance(dt);
            _shelfHover.Advance(dt);
            _tasksHover.Advance(dt);
            _shelfScroll.Advance(dt);
        }
        UpdateArrows();
        if (dt > 0) _arrows.Advance(dt);
        StepUsage(now, dt);
        StepFocus(dt);
        if (shelfSliding) RehoverShelf();   // la fila se desliza bajo el cursor quieto: el realce la sigue
        UpdateMusicLine(now);
        UpdateCompactLine(now);
        RenderFrame();
        if (_hoverHold.Active && _anim.ShapeSettled && !_hoverSettlePosted)
        {
            _hoverSettlePosted = true;
            BeginInvoke(EndHoverHold);   // fuera del frame: puede acabar en PointerLeave ⇒ Sync
        }
    }

    /// <summary>La forma del cambio de página ya no se mueve: la zona de hover vuelve a ser solo ella. Si el cursor quedó
    /// fuera, sale (la máquina no contrae: espera a que vuelva o al plazo); y la máquina sabe que la forma está asentada.</summary>
    private void EndHoverHold()
    {
        if (!Alive || !_hoverHold.Active || !_anim.ShapeSettled) { _hoverSettlePosted = false; return; }
        _hoverHold = default;
        _hoverSettlePosted = false;
        var c = Cursor.Position;
        if (_pointerInside && !_dragOver && !HitTest(new Point(c.X - _topLeft.X, c.Y - _topLeft.Y))) PointerLeave();
        _sm.OnPageShapeSettled(DateTime.UtcNow);
        Sync();
    }

    private void RenderFrame()
    {
        if (!IsHandleCreated || !Visible || _hiddenForFullscreen) return;
        UpdateSurfaceHeight();
        var shown = _anim.ShownMode;
        var (arm, answer) = PermissionButtonsState();
        var frame = new NotchFrame(_anim.Width, _anim.Height, _scale, _anim.IsGone ? NotchMode.Hidden : shown,
            _anim.ShownAlert, shown == NotchMode.Expanded && _sessionOffset > 0 ? ListedSessions().Skip(_sessionOffset).ToList() : _hub.Sessions,
            _hub.Quota, SpinnerPhase(), _anim.ContentAlpha, _anim.PillAlpha, _anim.ShadowAlpha, _anim.HaloAlpha,
            _anim.Squash, _anim.ContentOffsetY, _options.ReduceMotion, arm, answer,
            _sm.PermissionActionable && _heldRequestId == _sm.ActiveAlert?.RequestId ? _held
                : _sm.FocusAlertActionable ? FocusAlertHeldShown : ReminderHeldShown,
            shown == NotchMode.Expanded ? PagesView() : null, MusicFrame(shown), ShelfFrame(shown), TasksFrame(shown), IdleFrame(shown),
            ReadOnly: (_anim.ShownLayout & LayoutReadOnly) != 0 && shown is NotchMode.Permission or NotchMode.Reminder,
            Update: shown is NotchMode.Idle or NotchMode.Peek or NotchMode.Expanded && _updateView is { } u
                ? u with { Hover = _updateHover, Pressed = _updatePressed && _updateHover } : null,
            Locked: _options.Locked, ProCard: ProCardFrame(shown), Usage: UsageFrame(shown), Focus: FocusFrame(shown), Hold: _hoverHold,
            QuotaAlertPct: _quotaAlertPct);
        _armShown = arm;
        _renderer.Render(Handle, _shadow.Handle, _topLeft, _surface, frame);
        TrackPaintedPermission();
    }

    /// <summary>Primer umbral de aviso de la cuota de 5 h (lo calcula Sync; el frame lo lleva al painter).</summary>
    private double _quotaAlertPct = CompactLyrics.DefaultQuotaAlertPct;

    /// <summary>¿Se mueve algo (forma, contenido, el cambio de página o la fila de la bandeja)? Si no, el bucle se para.</summary>
    private bool Animating => _anim.IsAnimating || _pageAnim.IsAnimating || _musicMotion.IsAnimating
        || _iconMotion.IsAnimating || _compactMotion.IsAnimating || _compactQuota.IsAnimating || _musicHover.IsAnimating
        || _shelfHover.IsAnimating || _tasksHover.IsAnimating || !_shelfScroll.IsSettled || UsageAnimating || _arrows.IsAnimating
        || FocusAnimating;

    /// <summary>Páginas del frame; <c>null</c> con una sola (el expandido de siempre, sin puntos).</summary>
    private NotchPagesView? PagesView() => _pages.Count < 2 ? null
        : new NotchPagesView(_pages, _pageAnim.Current, _pageAnim.Previous, _pageAnim.Blend, _pageAnim.CurrentShift,
            _pageAnim.PreviousShift, _pageAnim.IndicatorPos, _interactive ? _arrows.View : null);

    private bool SpinnerOn => _anim.SpinnerVisible(_hub.Sessions.Any(s => s.Phase.IsActive()),
        sessionsOnScreen: _pageAnim.Current == NotchPage.Claude || (_pageAnim.IsAnimating && _pageAnim.Previous == NotchPage.Claude));

    /// <summary>Botones del Permiso armándose (relleno animado). Con reduce-motion no hay relleno del reloj de la máquina
    /// (basta su plazo), pero el armado de lo PINTADO sí necesita repintar hasta llegar a 1 (también con reduce-motion).</summary>
    private bool ArmingOn => _interactive && _sm.PermissionActionable
        && ((!_options.ReduceMotion && !_sm.PermissionArmed(DateTime.UtcNow))
            || (_armShown < 1f && _sm.ActiveAlert is { } a && PaintedKeyMatches(a)));

    /// <summary>Armado y respuesta de los botones del aviso que SE PINTA (en un fundido puede no ser aún el de la máquina:
    /// entonces se pinta sin armar).</summary>
    private (float Arm, PermissionVerdict Answer) PermissionButtonsState()
    {
        if (_anim.ShownAlert?.Kind == NotchAlertKind.Reminder) return (ReminderArmShown(), PermissionVerdict.None);
        if (_anim.ShownAlert?.Kind == NotchAlertKind.Focus) return (FocusAlertArmShown(), PermissionVerdict.None);
        if (_anim.ShownAlert?.RequestId is null) return (1f, PermissionVerdict.None);
        if (_sm.ActiveAlert is not { } active || !ShownMatches(active)) return (0f, PermissionVerdict.None);
        if (_sm.PermissionAnswer != PermissionVerdict.None) return (1f, _sm.PermissionAnswer);
        // Armado = el de la máquina Y el de lo pintado entero (lo que de verdad ve el usuario), el más lento de los dos.
        float arm = Math.Min(_sm.PermissionArmProgress(DateTime.UtcNow), PaintedArmProgress(active));
        // Armado = max(400 ms, forma asentada): mientras la forma se mueve no se pinta como armado (no acepta clics).
        if (arm >= 1f && _sm.PermissionAnswer == PermissionVerdict.None && !_anim.ShapeSettled) arm = ArmWaitingShape;
        return (arm, _sm.PermissionAnswer);
    }

    /// <summary>Relleno casi completo: el reloj ya armó, pero la forma aún se mueve.</summary>
    private const float ArmWaitingShape = 0.99f;

    private float SpinnerPhase() =>
        (float)(_clock.Elapsed.TotalMilliseconds / NotchMotion.SpinnerPeriod.TotalMilliseconds % 1.0);

    /// <summary>Sesiones en el orden en que las lista el expandido (prioridad de fase, luego la más reciente).</summary>
    private List<SessionState> ListedSessions() => _hub.Sessions.Where(x => x.Phase != SessionPhase.Ended)
        .OrderBy(x => x.Phase.Priority()).ThenByDescending(x => x.LastActivityUtc).ToList();

    private void StopLoop()
    {
        Unhook();
        _frameTimer.Stop();
    }

    private void Unhook()
    {
        if (!_idleHooked) return;
        Application.Idle -= OnIdle;
        _idleHooked = false;
    }

    // ---------------------------------------------------------------- reloj y gato (F5 · B7)

    private void ApplyIdleOptions(NotchOptions o)
    {
        _idleScene.ClockEnabled = o.Clock;
        _idleScene.MascotEnabled = o.Mascot;
        _idleScene.ReduceMotion = o.ReduceMotion;
    }

    /// <summary>Calma del reposo: visible, sin Claude trabajando, sin avisos, sin la canción en el reposo y sin el pomodoro en marcha
    /// (su mini anillo ocupa el reposo: ni reloj ni gato, ni sus despertares). La vista previa y
    /// el expandido cuentan como calma (el gato no se espanta al acercar el ratón).</summary>
    private void UpdateIdleScene(DateTime now)
    {
        bool calm = !_hiddenForFullscreen && _sm.Mode != NotchMode.Hidden && _sm.ActiveAlert is null
            && !_hub.Sessions.Any(s => s.Phase.IsActive()) && !(_options.Music && _idleMusic.Show(now)) && !FocusIdle(now);
        _idleScene.Update(_sm.Mode, calm, now);
    }

    /// <summary>Hora y fecha del reloj: nombres en el idioma de la app y 12/24 h según la configuración de Windows.</summary>
    private (string Time, string Date) ClockNow() =>
        ClockText.Format(DateTime.Now, _painter.Strings.Culture, ClockText.Prefers24h(System.Globalization.CultureInfo.CurrentCulture));

    /// <summary>Ancho en px lógicos de «hora  fecha» (0 si la maquetación no lleva reloj).</summary>
    private float ClockTextWidth(int layout)
    {
        if ((layout & IdleScene.LayoutClock) == 0 || _scale <= 0) return 0f;
        var (time, date) = ClockNow();
        return _painter.MeasureClock(_scale, time, date) / _scale;
    }

    /// <summary>Cursor sobre el notch: los ojos del gato lo siguen (px lógicos desde el centro de los ojos). Solo repinta
    /// (con su plazo) si cambia lo que se ve.</summary>
    private void MascotPointer(Point p)
    {
        var pill = NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface);
        var c = IdleScene.MascotCenter(pill, _anim.ShownMode, _scale);
        var rel = new PointF((p.X + 0.5f - c.X) / _scale, (p.Y + 0.5f - c.Y) / _scale);
        if (_idleScene.OnPointer(rel, DateTime.UtcNow)) Sync();
    }

    /// <summary>El reposo del frame según la maquetación que SE PINTA; <c>null</c> = como siempre.</summary>
    private IdleView? IdleFrame(NotchMode shown)
    {
        if (shown is not (NotchMode.Idle or NotchMode.Minimal or NotchMode.Peek)) return null;
        int layout = _anim.ShownLayout;
        if ((layout & LayoutIdleMusic) != 0) return null;
        bool clock = shown == NotchMode.Idle && (layout & IdleScene.LayoutClock) != 0;
        bool cat = (layout & IdleScene.LayoutMascot) != 0;
        if (!clock && !cat) return null;
        var (time, date) = clock ? ClockNow() : ("", "");
        // Si el gato estaba antes y sigue después en el mismo modo, solo cambia la cara de al lado: sin fundido para él. Al
        // aparecer o irse, con el contenido.
        float alpha = cat && _catSteady && shown == _targetMode ? 1f : _anim.ContentAlpha;
        return new IdleView(clock, time, date, cat ? _idleScene.Look : null, alpha);
    }

    // ---------------------------------------------------------------- música (F5 · B3)

    /// <summary>¿La página Música es la del expandido (objetivo)? Decide el timer de la posición y si se pide la letra.</summary>
    private bool MusicPageVisible => _options.Music && _pages.Count > 1 && !_hiddenForFullscreen
        && _sm.Mode == NotchMode.Expanded && _carousel.Current == NotchPage.Music;

    /// <summary>¿Se PINTA la página Música quieta y entera? Solo entonces aceptan ratón sus controles (lo que se pinta es lo
    /// que se acciona: en un cruce de página o mientras la forma abre, no).</summary>
    private bool MusicPageShown => MusicPageVisible && _anim.ShownMode == NotchMode.Expanded && _anim.ContentAlpha >= 1f
        && _pageAnim.Current == NotchPage.Music && !_pageAnim.IsAnimating && !_proCardOpen;

    /// <summary>Regla de red: la letra solo se pide con el ajuste y, o la página Música visible con la vista de letra abierta,
    /// o la letra en el notch pequeño cuando puede llegar a verse (<see cref="CompactLyrics.CanShow"/>: con pista, el notch a
    /// la vista y sin «Minimalista»; encogido solo sí, porque la letra lo despierta; con el pomodoro en el reposo, no).</summary>
    private bool LyricsWanted => LyricsController.Wanted(_options.Lyrics, MusicPageVisible, _musicMotion.ShowLyrics,
        _options.Music && _options.CompactLyrics,
        CompactLyrics.CanShow(_np is not null, _hiddenForFullscreen, _options.Minimal, _sm.Mode) && !FocusIdle(DateTime.UtcNow));

    // ---- letra compacta (notch pequeño)

    /// <summary>Qué enseña el reposo con música (<paramref name="idleMusic"/> = el reposo enseña la canción).</summary>
    private IdleMusicFace CompactFace(bool idleMusic) => CompactLyrics.Face(idleMusic, _options.Lyrics, _options.CompactLyrics,
        _np, _lyrics.State, _lyrics.Lines.Count);

    private CompactLyricsSlots CompactSlots => new(_options.CompactLyricsArt, _options.CompactLyricsQuota);

    /// <summary>Instante de la LETRA ahora (con el desfase del controlador); null sin música.</summary>
    private TimeSpan? CompactLyricsAt(DateTime utcNow) => _np is { } np ? _lyrics.LyricsPosition(np.PositionAt(utcNow)) : null;

    /// <summary>Índice y texto de la línea que toca ahora («♪» antes de la primera y en las pausas); null sin letra.</summary>
    private (int Index, string Text)? CompactLineNow(DateTime utcNow)
    {
        if (_lyrics.State != LyricsState.Found || _lyrics.Lines.Count == 0 || CompactLyricsAt(utcNow) is not { } at) return null;
        int i = CompactLyrics.LineIndex(_lyrics.Lines, at);
        return (i, CompactLyrics.LineText(_lyrics.Lines, i));
    }

    /// <summary>Ancho extra de la letra compacta para la línea de ahora (medida cacheada por texto y escala).</summary>
    private float CompactExtra(DateTime utcNow)
    {
        if (CompactLineNow(utcNow) is not { } line) return 0f;
        if (!string.Equals(line.Text, _compactMeasured, StringComparison.Ordinal) || Math.Abs(_scale - _compactMeasuredScale) > 0.0001f)
        {
            _compactMeasured = line.Text;
            _compactMeasuredScale = _scale;
            _compactMeasuredWidth = _painter.MeasureCompactLine(line.Text, _scale);
        }
        return CompactLyrics.Extra(_compactMeasuredWidth, CompactSlots);
    }

    /// <summary>Plazo del cambio de línea de la letra compacta: solo si es la maquetación pedida (reposo o la Vista previa
    /// sobre él, sonando, letra sincronizada). En pausa, con otra cara, oculto o tras la última línea, null: nada que
    /// despertar.</summary>
    private TimeSpan? CompactNextLineIn(DateTime utcNow)
    {
        // Con el pomodoro en marcha (o en pausa sin música) el reposo enseña su mini anillo: la letra compacta no se ve.
        if ((_sm.Mode != NotchMode.Idle && !_sm.PeekOverIdle) || _np is not { Playing: true } || FocusIdle(utcNow)) return null;
        bool idleMusic = _options.Music && _idleMusic.Show(utcNow);
        if (CompactFace(idleMusic) != IdleMusicFace.Lyrics || CompactLyricsAt(utcNow) is not { } at) return null;
        return CompactLyrics.NextLineIn(_lyrics.Lines, at);
    }

    /// <summary>Línea de la letra compacta en este frame: con la maquetación a la vista y la misma letra, el cambio de línea
    /// se cruza; letra nueva u otra maquetación, salto. Sin letra se conserva la última (se funde con la maquetación).</summary>
    private void UpdateCompactLine(TimeSpan now)
    {
        if (CompactLineNow(DateTime.UtcNow) is not { } line) return;
        var lines = _lyrics.Lines;
        bool otherLyrics = !ReferenceEquals(lines, _compactLines);
        _compactLines = lines;
        if (otherLyrics || _anim.ShownLayout != LayoutCompactLyrics || _anim.ShownMode != NotchMode.Idle)
            _compactMotion.Jump(line.Index, line.Text);
        else _compactMotion.Show(line.Index, line.Text, now);
    }

    /// <summary>Lo que pinta la letra compacta: la línea (y su cruce) solo si es de la letra ACTUAL
    /// (<paramref name="sameLyrics"/>). Tras un cambio de pista sin letra en caché, la maquetación se funde con la carátula
    /// NUEVA mientras el controlador ya no tiene líneas: la última línea es de otra canción y nunca se pinta; queda «♪»
    /// quieto. PURO.</summary>
    internal static CompactLyricsView CompactViewFor(CompactLyricsMotion motion, bool sameLyrics, bool art, bool quota,
        CompactQuotaRing? ring = null) =>
        sameLyrics && motion.Text is { } line
            ? new CompactLyricsView(line, motion.PreviousText, motion.Alpha, motion.PreviousAlpha, motion.Offset,
                motion.PreviousOffset, art, quota, ring?.Emphasis ?? 0f, ring?.PctReveal ?? 0f)
            : new CompactLyricsView(LrcParser.PauseText, Art: art, Quota: quota, QuotaEmphasis: ring?.Emphasis ?? 0f,
                QuotaReveal: ring?.PctReveal ?? 0f);

    /// <summary>Crea el servicio de música (WinRT, uno para todas las ventanas: <see cref="NotchShared"/>) si el ajuste está
    /// activo. Se llama al mostrarse y al activar el ajuste; llega a <see cref="OnMusic"/> por <see cref="NotchShared.MusicChanged"/>.</summary>
    private Task StartMusicAsync() => Alive ? _shared.StartMusicAsync() : Task.CompletedTask;

    /// <summary>Lo que suena cambió (hilo de UI): reposo con música, letra y repintado.</summary>
    private void OnMusic(NowPlaying? np)
    {
        var previous = _np;
        _np = np;
        // Botón central: con la página a la vista, el cambio de Playing (por el clic o desde el reproductor) se anima;
        // la primera pista o con la página oculta, se coloca sin animación.
        if (np is not null)
        {
            if (previous is null || !MusicPageVisible) _iconMotion.JumpPlaying(np.Playing);
            else _iconMotion.SetPlaying(np.Playing, _clock.Elapsed);
        }
        _idleMusic.Observe(np, DateTime.UtcNow);
        if (_idleMusic.Track is { } t) _idleTrack = t;
        if (Alive) Sync();
    }

    /// <summary>Cambia el controlador de letras sin que su <see cref="LyricsController.Changed"/> lance un Sync (quien llama
    /// sincroniza después).</summary>
    private void QuietLyrics(Action change)
    {
        bool was = _inSync;
        _inSync = true;
        try { change(); }
        finally { _inSync = was; }
    }

    /// <summary>La letra cambió (buscando → letra / sin letra). Dentro de Sync ya se tiene en cuenta; fuera, Sync: la letra
    /// compacta puede cambiar la maquetación y el tamaño del reposo (y el plazo de la siguiente línea).</summary>
    private void OnLyricsChanged()
    {
        if (!Alive || _inSync) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(OnLyricsChanged); }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { /* cerrándose */ }
            return;
        }
        Sync();
    }

    /// <summary>Programa el siguiente repintado de la página Música (posición, tiempos, letra): un solo disparo, solo con la
    /// página visible y sonando. En pausa o con otra página, nada (0 % CPU).</summary>
    private void ScheduleMusicTick()
    {
        _musicTick.Stop();
        if (!MusicPageVisible || !Visible || !IsHandleCreated) return;
        var lines = _musicMotion.ShowLyrics ? ShownLyricLines() : null;
        float barPx = MusicLayout.Compute(MusicInner(), _scale, lyrics: false).Bar.Width;
        if (MusicClock.NextTickIn(_np, DateTime.UtcNow, lines, barPx, LyricShift) is not { } d) return;
        _musicTick.Interval = (int)Math.Clamp(Math.Ceiling(d.TotalMilliseconds), 1, int.MaxValue);
        _musicTick.Start();
    }

    /// <summary>Zona de contenido de la página (la MISMA que pinta <see cref="NotchContentPainter"/>).</summary>
    private RectangleF MusicInner() => NotchContentPainter.PageInner(
        NotchGeometry.PillRect(_anim.Width, _anim.Height, _surface), _scale, TopLineShown, _pages.Count > 1);

    /// <summary>Control de la página Música bajo <paramref name="p"/>; nada si la página no se pinta quieta, sin música o en
    /// pleno cruce de la vista de letra.</summary>
    private MusicHit MusicHitAt(Point p)
    {
        if (!MusicPageShown || _np is null) return default;
        float split = _musicMotion.SplitBlend;
        if (split is > 0f and < 1f) return default;
        bool lyrics = _options.Lyrics && split >= 1f;
        var hit = MusicLayout.HitTest(new PointF(p.X + 0.5f, p.Y + 0.5f), MusicInner(), _scale, lyrics,
            lyrics ? LyricsToolsShown : LyricsTools.None);
        if (hit.Control == MusicControl.Lyrics && !_options.Lyrics) return default;
        if (hit.Control == MusicControl.LyricLine && _lyrics.State != LyricsState.Found) return default;   // plana: sin saltos
        return hit;
    }

    /// <summary>Botones de la vista de letra que se ven (letras v2): el desfase con letra sincronizada; «Otra letra» con
    /// cualquier letra a la vista.</summary>
    private LyricsTools LyricsToolsShown =>
        (_lyrics.CanNudge ? LyricsTools.Nudge : LyricsTools.None)
        | (_lyrics.State is LyricsState.Found or LyricsState.Plain ? LyricsTools.Other : LyricsTools.None);

    /// <summary>Desfase manual de la letra que se ve (solo con sincronizada; positivo = la letra va más tarde).</summary>
    private TimeSpan LyricShift => _lyrics.CanNudge ? TimeSpan.FromSeconds(_lyrics.OffsetSeconds) : TimeSpan.Zero;

    /// <summary>Líneas que pinta la vista de letra: las sincronizadas tal cual o las de la letra plana con tiempos
    /// estimados (<see cref="PlainLyricsTiming"/>, cacheadas); vacías en los demás estados.</summary>
    private IReadOnlyList<LyricLine> ShownLyricLines()
    {
        if (_lyrics.State == LyricsState.Found) return _lyrics.Lines;
        if (_lyrics.State != LyricsState.Plain || _np is null) return Array.Empty<LyricLine>();
        var text = _lyrics.PlainText;
        if (!ReferenceEquals(text, _plainText) || _np.Duration != _plainDuration)
        {
            _plainText = text;
            _plainDuration = _np.Duration;
            _plainLines = PlainLyricsTiming.Estimate(text, _np.Duration);
        }
        return _plainLines;
    }

    /// <summary>Texto de «Otra letra» («LRCLIB 1/3»), rehecho solo cuando cambia.</summary>
    private string SourceLabel()
    {
        var key = (_lyrics.Source, _lyrics.CandidateIndex, _lyrics.CandidateCount, _lyrics.CandidatesLoading);
        if (_sourceLabel is null || key != _sourceKey)
        {
            _sourceKey = key;
            _sourceLabel = MusicFormat.SourceLabel(key.Source, key.CandidateIndex, key.CandidateCount, key.CandidatesLoading,
                _painter.Strings.NotchLyricsOther);
        }
        return _sourceLabel;
    }

    /// <summary>Aviso pequeño de la vista de letra: «letra sin sincronizar» con la plana; el desfase con la sincronizada
    /// si no es 0; nada en lo demás.</summary>
    private string? LyricsNotice()
    {
        var key = (_lyrics.State, _lyrics.CanNudge ? _lyrics.OffsetSeconds : 0d);
        if (_lyricsTextStale || key != _noticeKey)
        {
            _noticeKey = key;
            _lyricsTextStale = false;
            var s = _painter.Strings;
            _notice = key.State == LyricsState.Plain ? s.NotchLyricsUnsynced
                : key.Item2 != 0 ? string.Format(s.Culture, s.NotchLyricsOffsetFmt, MusicFormat.Offset(key.Item2, s.Culture))
                : null;
        }
        return _notice;
    }

    /// <summary>Realce del control bajo el cursor (HoverIn al entrar, Rápido al salir; reduce-motion = salto).</summary>
    private void MusicHover(Point p)
    {
        var hit = MusicHitAt(p);
        bool none = hit.Control == MusicControl.None;
        if (none)
        {
            if (_musicHover.Key is null) return;
            _musicHover.Leave(snap: _options.ReduceMotion);
        }
        else
        {
            string key = $"{hit.Control}:{hit.LineOffset}";
            if (key == _musicHover.Key) return;
            _musicHoverHit = hit with { Fraction = 0f };
            _musicHover.Enter(key, RectangleF.Empty, snap: _options.ReduceMotion);
        }
        Frame();
        Pump();
    }

    /// <summary>WM_LBUTTONDOWN: recuerda el control pulsado (y pinta la pulsación: encoge y oscurece, al instante).</summary>
    private void MusicPress(Point p)
    {
        var hit = MusicHitAt(p);
        if (hit.Control == _musicHeld.Control && hit.LineOffset == _musicHeld.LineOffset) return;
        _musicHeld = hit;
        RenderFrame();
    }

    /// <summary>
    /// Clic al SOLTAR en la página Música, sobre el MISMO control que se pulsó: anterior / pausa / siguiente, «Letra»,
    /// saltar en la barra o a una línea de la letra. Soltar fuera tras pulsar un control lo cancela (sin cerrar el
    /// expandido). <c>false</c> si el clic no era de la página Música (clic normal).
    /// </summary>
    private bool MusicClick(Point p)
    {
        var held = _musicHeld;
        _musicHeld = default;
        var hit = MusicHitAt(p);
        if (hit.Control == MusicControl.None) return held.Control != MusicControl.None && MusicPageVisible;
        if (hit.Control != held.Control || hit.LineOffset != held.LineOffset || _np is not { } np) return true;

        switch (hit.Control)
        {
            case MusicControl.Previous:
                _iconMotion.Skip(next: false, _clock.Elapsed);
                _ = _shared.Media?.PreviousAsync();
                break;
            case MusicControl.PlayPause: _ = _shared.Media?.PlayPauseAsync(); break;   // el morph llega con el Playing nuevo
            case MusicControl.Next:
                _iconMotion.Skip(next: true, _clock.Elapsed);
                _ = _shared.Media?.NextAsync();
                break;
            case MusicControl.Lyrics:
                _musicMotion.SetLyrics(!_musicMotion.ShowLyrics, _clock.Elapsed);
                _musicHover.Reset();   // los controles se recolocan: el realce viejo no sigue en su sitio
                break;
            case MusicControl.Progress when np.Duration > TimeSpan.Zero:
                _ = _shared.Media?.SeekAsync(TimeSpan.FromTicks((long)(np.Duration.Ticks * (double)hit.Fraction)));
                break;
            case MusicControl.LyricLine:
            {
                var lines = _lyrics.State == LyricsState.Found ? _lyrics.Lines : Array.Empty<LyricLine>();
                int i = _musicMotion.Line + hit.LineOffset;
                if (i >= 0 && i < lines.Count)
                {
                    var at = lines[i].At + LyricShift;   // instante del reproductor en que toca esa línea
                    _ = _shared.Media?.SeekAsync(at < TimeSpan.Zero ? TimeSpan.Zero : at);
                }
                break;
            }
            case MusicControl.NudgeBack: _lyrics.NudgeOffset(-LyricsCache.OffsetStep); break;
            case MusicControl.NudgeForward: _lyrics.NudgeOffset(LyricsCache.OffsetStep); break;
            case MusicControl.OtherLyrics: _ = _lyrics.NextCandidate(); break;   // nunca lanza; avisa con Changed
        }
        Sync();
        return true;
    }

    /// <summary>Línea de la letra que toca ahora: otra letra (pista nueva) salta; la siguiente de la misma, con cruce.</summary>
    private void UpdateMusicLine(TimeSpan now)
    {
        var lines = ShownLyricLines();
        int idx = _np is { } np && lines.Count > 0
            ? LrcParser.IndexAt(lines, np.PositionAt(DateTime.UtcNow) - LyricShift, MusicClock.LyricLead) : -1;
        if (!ReferenceEquals(lines, _shownLines))
        {
            _shownLines = lines;
            _musicMotion.JumpLine(idx);
            return;
        }
        if (MusicPageVisible) _musicMotion.OnLine(idx, now);
        else _musicMotion.JumpLine(idx);
    }

    /// <summary>La música del frame: el reposo con la canción (si es la maquetación que se pinta) o la página Música.</summary>
    private MusicView? MusicFrame(NotchMode shown)
    {
        if (!_options.Music) return null;
        if (shown == NotchMode.Idle)
        {
            if ((_idleMusic.Track ?? _idleTrack) is not { } t) return null;
            if (_anim.ShownLayout == LayoutCompactLyrics && _compactMotion.Text is not null)
                return new MusicView(t, TimeSpan.Zero, IdleFace: true, Compact: CompactViewFor(_compactMotion,
                    ReferenceEquals(_compactLines, _lyrics.Lines), _options.CompactLyricsArt, _options.CompactLyricsQuota,
                    _compactQuota));
            if (_anim.ShownLayout != LayoutIdleMusic) return null;
            return new MusicView(t, TimeSpan.Zero, IdleFace: true, Equalizer: EqualizerFor(t));
        }
        if (shown != NotchMode.Expanded || _np is not { } np) return null;
        var hover = _musicHover.Visible ? _musicHoverHit : default;
        // Botones y aviso mientras se PINTE la capa de letra (SplitBlend > 0), no según ShowLyrics: al cerrar se funden con
        // el resto de la columna en vez de desaparecer en el primer frame. El hit-test ya exige split >= 1.
        bool lyricsView = _options.Lyrics && _musicMotion.SplitBlend > 0f;
        var tools = lyricsView ? LyricsToolsShown : LyricsTools.None;
        return new MusicView(np, np.PositionAt(DateTime.UtcNow), false, _lyrics.State, ShownLyricLines(),
            _musicMotion.Line, _musicMotion.PreviousLine, _musicMotion.LineBlend, _options.Lyrics ? _musicMotion.SplitBlend : 0f,
            hover.Control, hover.LineOffset, (float)_musicHover.Intensity, _musicHeld.Control, _options.Lyrics,
            EqualizerFor(np), _iconMotion.IsMorphing ? _iconMotion.Morph : null, _iconMotion.NextOffset,
            _iconMotion.PreviousOffset, tools,
            (tools & LyricsTools.Other) != 0 ? SourceLabel() : null,
            _lyrics.CandidatesLoading || (_lyrics.CandidateCount == 1 && _lyrics.CandidateIndex == 0),
            lyricsView ? LyricsNotice() : null, _clock.Elapsed.TotalSeconds, _options.ReduceMotion);
    }

    /// <summary>Alturas del ecualizador de «está sonando» para esta pista (quieto en pausa o con reduce-motion).</summary>
    private float[] EqualizerFor(NowPlaying t) =>
        EqualizerBars.Heights(_clock.Elapsed.TotalSeconds, t.Playing, still: _options.ReduceMotion);

    /// <summary>El ecualizador se está viendo y se mueve: reposo con música o página Música, sonando, sin reduce-motion.
    /// Mantiene vivo el timer del spinner (≈ SpinnerFps); en pausa o sin música, nada.</summary>
    private bool EqualizerOn =>
        _options.Music && !_options.ReduceMotion && Visible && !_hiddenForFullscreen
        && ((_sm.Mode == NotchMode.Idle && _anim.ShownLayout == LayoutIdleMusic && (_idleMusic.Track ?? _idleTrack) is { Playing: true })
            || (_sm.Mode == NotchMode.Expanded && MusicPageShown && _np is { Playing: true }));

    /// <summary>Buscando la letra con la vista de letra a la vista: las letras que suben mantienen vivo el mismo timer que
    /// el ecualizador (≈ SpinnerFps) hasta que el estado deja de ser <see cref="LyricsState.Loading"/> (OnLyricsChanged ⇒
    /// Pump lo para). Con reduce-motion, el fotograma es fijo y no hace falta.</summary>
    private bool LyricsSearchOn => LyricsSearchParticles.Animates(_lyrics.State,
        _options.Music && _options.Lyrics && _sm.Mode == NotchMode.Expanded && MusicPageShown && _musicMotion.ShowLyrics,
        Visible, _hiddenForFullscreen, _options.ReduceMotion);

    // ---------------------------------------------------------------- bandeja (F5 · B4)

    /// <summary>La bandeja (una para todas las ventanas: <see cref="NotchShared"/>), si ya se cargó.</summary>
    private ShelfStore? Shelf => _shared.Shelf;

    /// <summary>Carga la bandeja la primera vez (lee <c>shelf.json</c>; «vaciar al arrancar» si el ajuste lo pide) y, la primera
    /// vez en esta ventana, toma su foto.</summary>
    private ShelfStore EnsureShelf()
    {
        var shelf = _shared.EnsureShelf();
        if (!_shelfSeen)
        {
            _shelfSeen = true;
            RefreshShelf();
        }
        return shelf;
    }

    /// <summary>La bandeja cambió (soltar, quitar, vaciar): foto nueva y alto nuevo (Sync ⇒ Ajuste en el animador).</summary>
    private void OnShelfChanged()
    {
        if (!Alive) return;
        RefreshShelf();
        Sync();
    }

    /// <summary>Foto de la bandeja (orden y disponibilidad: E/S de disco, UNA vez aquí y no por frame); limpia la selección,
    /// acota el desplazamiento a lo que queda y pide las miniaturas que falten.</summary>
    private void RefreshShelf()
    {
        _shelfSnap = Shelf is not { } shelf ? ShelfSnapshot.Empty : ShelfSnapshot.Take(shelf);
        _shelfSel.Prune(_shelfSnap.Items.Select(i => i.Id));
        ScrollShelfTo(_shelfFirst);   // con menos tarjetas, la última puede verse con menos desplazamiento
        UpdateShelfCards();
    }

    /// <summary>
    /// Con la página Bandeja delante (al mostrarse, al cambiar la bandeja o la escala), pide en segundo plano lo que falte de
    /// las tarjetas: miniatura del sistema, tamaño y si el archivo sigue ahí (<see cref="ShelfThumbnails"/>; lo que la caché
    /// tiene y no cambió no se vuelve a pedir). Después rehace las tarjetas: pedir puede liberar imágenes de la caché.
    /// </summary>
    private void UpdateShelfCards()
    {
        if (ShelfEnabled && _carousel.Current == NotchPage.Shelf) _shelfThumbs.Request(_shelfSnap.Items, ShelfLayout.ThumbPx(_scale));
        RebuildShelfCards();
    }

    /// <summary>Miniaturas y líneas grises de las tarjetas, paralelas a la foto. Se rehacen al cambiar la foto, el idioma o la
    /// escala y al llegar algo del hilo de fondo, nunca por frame (la caché pudo liberar las imágenes de antes).</summary>
    private void RebuildShelfCards()
    {
        var items = _shelfSnap.Items;
        int px = ShelfLayout.ThumbPx(_scale);
        var thumbs = new Bitmap?[items.Count];
        var details = new string[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            bool probes = ShelfThumbPolicy.Probes(items[i]);
            thumbs[i] = probes ? _shelfThumbs.Get(items[i].Value, px) : null;
            details[i] = ShelfCardText.Detail(items[i], probes ? _shelfThumbs.Probe(items[i].Value) : null, Strings);
        }
        _shelfCardThumbs = thumbs;
        _shelfDetails = details;
    }

    /// <summary>Llegó del hilo de fondo la miniatura o lo mirado de <paramref name="path"/> (ya en el hilo de UI). Lo recién
    /// mirado manda sobre la foto, en los dos sentidos: lo que ya no está pasa a «No disponible» y lo que vuelve (un USB
    /// que se conecta de nuevo) deja de estarlo; una unidad que no se toca no cambia nada. Las tarjetas se rehacen y, con
    /// la página delante, se repinta.</summary>
    private void OnShelfThumbs(string path)
    {
        if (!Alive) return;
        if (_shelfThumbs.Probe(path) is { State: not ShelfProbeState.Opaque } probe)
            foreach (var item in _shelfSnap.Items)
                if (ShelfThumbPolicy.Probes(item) && string.Equals(item.Value, path, StringComparison.OrdinalIgnoreCase))
                    _shelfSnap = _shelfSnap.WithAvailability(item.Id, probe.Exists);
        RebuildShelfCards();
        if (ShelfPageVisible) RenderFrame();
    }

    /// <summary>Lleva la fila a <paramref name="first"/> (acotada) con el muelle del carrusel; con reduce-motion o sin la página
    /// delante (nada se mueve si no se ve), de golpe. <c>false</c> si ya iba ahí.</summary>
    private bool ScrollShelfTo(int first)
    {
        first = Math.Clamp(first, 0, ShelfLayout.MaxFirst(_shelfSnap.Items.Count));
        float target = ShelfLayout.ScrollFor(first);
        if (first == _shelfFirst && _shelfScroll.Target == target) return false;
        _shelfFirst = first;
        if (_options.ReduceMotion || !ShelfPageVisible) _shelfScroll.Snap(target);
        else _shelfScroll.SetTarget(target);
        return true;
    }

    /// <summary>La fila vuelve al principio, sin animación (al cerrar, al vaciar o al soltar algo nuevo, que va el primero).</summary>
    private void ResetShelfScroll()
    {
        _shelfFirst = 0;
        _shelfScroll.Snap(0);
    }

    /// <summary>Cambios en <c>shelf.json</c>: un error de disco no tumba la ventana (se anota y la bandeja sigue en memoria).</summary>
    private static void TryShelf(Action change) => NotchShared.TryShelf(change);

    /// <summary>¿La bandeja se usa? (su página en la lista y sin el candado del Pro: con él ni se carga ni acepta arrastres).</summary>
    private bool ShelfEnabled => _pages.Contains(NotchPage.Shelf) && !PageLocked(NotchPage.Shelf);

    /// <summary>¿La página Bandeja es la del expandido (objetivo)?</summary>
    private bool ShelfPageVisible => ShelfEnabled && !_hiddenForFullscreen && _sm.Mode == NotchMode.Expanded
        && _carousel.Current == NotchPage.Shelf;

    /// <summary>¿Se PINTA la página Bandeja quieta y entera? Solo entonces acepta ratón (lo que se pinta es lo que se acciona).</summary>
    private bool ShelfPageShown => ShelfPageVisible && _anim.ShownMode == NotchMode.Expanded && _anim.ContentAlpha >= 1f
        && _pageAnim.Current == NotchPage.Shelf && !_pageAnim.IsAnimating && !_proCardOpen;

    private void ResetShelfPointer()
    {
        _shelfHeld = default;
        _shelfWheelAccum = 0;
        if (_shelfHover.Key is not null || _shelfHover.Visible) _shelfHover.Reset();
    }

    /// <summary>Pasa a la página Bandeja (al arrastrar algo encima). Con el expandido ya abierto, con su cruce; si se está
    /// abriendo, de golpe (aún no se ve) y sin que Sync lo devuelva a Claude.</summary>
    private void ShowShelfPage()
    {
        if (!ShelfEnabled) return;
        EnsureShelf();
        if (GoToRequestedPage(NotchPage.Shelf)) RefreshShelf();   // con cruce, PageChanged ya refresca la bandeja
    }

    /// <summary>
    /// Lleva el carrusel a <paramref name="page"/> pedida desde fuera (anillo, arrastre…) según
    /// <see cref="NotchPages.HowToShow"/>: con el expandido abierto, con su cruce (PageChanged ya entra en la página → false);
    /// si se está abriendo, de golpe y marcando la apertura como suya para que Sync no la devuelva a Claude, también si el
    /// carrusel se quedó en ella al cerrar. true = la página no se ha «entrado»: que lo haga quien llama.
    /// </summary>
    private bool GoToRequestedPage(NotchPage page)
    {
        var how = NotchPages.HowToShow(page, _carousel.Current, _wasExpanded);
        int from = _carousel.Index;
        if (_carousel.Current != page) _carousel.GoTo(page);
        switch (how)
        {
            case PageShow.Slide:
                PageChanged(_carousel.Index > from ? 1 : -1);
                return false;
            case PageShow.Jump:
                _pageAnim.Jump(_carousel.Current, _carousel.Index);
                _wasExpanded = true;   // «cada apertura empieza en Claude» no aplica a esta
                return true;
            default:
                return true;
        }
    }

    /// <summary>Control de la página Bandeja bajo <paramref name="p"/>; nada si la página no se pinta quieta.</summary>
    private ShelfHit ShelfHitAt(Point p)
    {
        if (!ShelfPageShown) return default;
        return ShelfLayout.HitTest(new PointF(p.X + 0.5f, p.Y + 0.5f), MusicInner(), _scale, _shelfSnap.Items.Count,
            (float)_shelfScroll.Value);
    }

    /// <summary>Realce de la tarjeta, la flecha o el botón bajo el cursor (HoverIn al entrar, Rápido al salir; reduce-motion =
    /// salto).</summary>
    private void ShelfHover(Point p)
    {
        if (!UpdateShelfHover(p)) return;
        Frame();
        Pump();
    }

    /// <summary>Lleva el realce al control bajo <paramref name="p"/> (o lo apaga); <c>true</c> si cambió.</summary>
    private bool UpdateShelfHover(Point p)
    {
        var hit = ShelfHitAt(p);
        if (hit.Control == ShelfControl.None)
        {
            if (_shelfHover.Key is null) return false;
            _shelfHover.Leave(snap: _options.ReduceMotion);
            return true;
        }
        string key = $"{hit.Control}:{hit.Index}";
        if (key == _shelfHover.Key) return false;
        _shelfHoverHit = hit;
        _shelfHover.Enter(key, RectangleF.Empty, snap: _options.ReduceMotion);
        return true;
    }

    /// <summary>La fila se movió bajo el cursor quieto (no llega WM_MOUSEMOVE): el realce pasa a lo que ahora tiene debajo.
    /// Mientras algo se arrastra encima manda el arrastre, no el ratón.</summary>
    private void RehoverShelf()
    {
        if (_pointerInside && !_dragOver && IsHandleCreated) UpdateShelfHover(PointToClient(Cursor.Position));
    }

    /// <summary>WM_LBUTTONDOWN (o el doble clic, que solo vale en las flechas: <see cref="ShelfClickRules.Press"/>): recuerda
    /// el control pulsado, dónde (umbral para arrastrar fuera) y si había selección («Vaciar» o «Quitar (N)»).</summary>
    private void ShelfPress(Point p, bool doubleClick)
    {
        var hit = ShelfClickRules.Press(ShelfHitAt(p), doubleClick);
        _shelfPressAt = p;
        _shelfHeldWithSelection = _shelfSel.Count > 0;
        if (hit == _shelfHeld) return;
        _shelfHeld = hit;
        RenderFrame();
    }

    /// <summary>
    /// Clic al SOLTAR en la página Bandeja, sobre el MISMO control que se pulsó. Tarjeta: sin modificadores la abre
    /// (<see cref="ShelfOpen"/>) y quita la selección; con Ctrl/Mayús (del <c>wParam</c> del propio mensaje) selecciona.
    /// «Vaciar» quita todo; con selección, «Quitar (N)» quita solo lo seleccionado. Flechas ‹ ›: una página de tarjetas (en
    /// un extremo no hacen nada, pero el clic es suyo). Soltar fuera tras pulsar un control lo cancela (sin cerrar el
    /// expandido). <c>false</c> si el clic no era de la bandeja (clic normal).
    /// </summary>
    private bool ShelfClick(Point p, long keys)
    {
        var held = _shelfHeld;
        bool heldWithSelection = _shelfHeldWithSelection;
        _shelfHeld = default;
        var hit = ShelfHitAt(p);
        if (hit.Control == ShelfControl.None) return held.Control != ShelfControl.None && ShelfPageVisible;
        if (hit != held || Shelf is not { } shelf) return true;

        switch (hit.Control)
        {
            case ShelfControl.Clear:
            {
                // «Quitar (N)» que, al soltar, ya sería «Vaciar» (o al revés): no hace nada.
                if (!ShelfClickRules.ClearStillMeans(heldWithSelection, _shelfSel.Count)) break;
                var ids = _shelfSel.Ids;
                _shelfSel.Clear();
                ResetShelfScroll();
                _shelfHover.Reset();
                if (ids.Count > 0) TryShelf(() => shelf.Remove(ids));
                else TryShelf(shelf.Clear);
                break;
            }
            case ShelfControl.Previous:
            case ShelfControl.Next:
                ScrollShelfTo(ShelfLayout.Page(_shelfFirst, _shelfSnap.Items.Count, hit.Control == ShelfControl.Next ? 1 : -1));
                break;
            case ShelfControl.Card when hit.Index >= 0 && hit.Index < _shelfSnap.Items.Count:
            {
                var item = _shelfSnap.Items[hit.Index];
                bool ctrl = (keys & MK_CONTROL) != 0, shift = (keys & MK_SHIFT) != 0;
                if (ctrl || shift) _shelfSel.Click(item.Id, _shelfSnap.Items.Select(i => i.Id).ToArray(), ctrl, shift);
                else
                {
                    _shelfSel.Clear();
                    OpenShelfItem(item);
                }
                break;
            }
        }
        Sync();
        return true;
    }

    /// <summary>Abre una entrada: la disponibilidad se mira OTRA vez (pudo borrarse o moverse después de la foto) y, si ya
    /// no está, se marca y no se intenta abrir. Un texto copiado enseña «Copiado» un momento.</summary>
    private void OpenShelfItem(ShelfItem item)
    {
        if (Shelf is not { } store) return;
        bool available = store.IsAvailable(item);
        _shelfSnap = _shelfSnap.WithAvailability(item.Id, available);
        bool copy = ShelfOpen.Decide(item, available) == ShelfOpenAction.CopyText;
        if (ShelfOpen.Run(item, available, _shell) && copy)
        {
            _shelfCopied = item.Id;
            _shelfFlash.Stop();
            _shelfFlash.Start();
        }
    }

    /// <summary>Ratón pulsado sobre una tarjeta y movido más allá del umbral del sistema: arrastrar fuera.</summary>
    private void ShelfMaybeDragOut(Point p)
    {
        if (_shelfHeld.Control != ShelfControl.Card || _selfDrag || !ShelfPageShown) return;
        if (!ShelfDragOut.Exceeded(_shelfPressAt, p, SystemInformation.DragSize)) return;
        var held = _shelfHeld;
        _shelfHeld = default;
        if (Shelf is not { } store || held.Index < 0 || held.Index >= _shelfSnap.Items.Count) return;

        var items = ShelfDragOut.ItemsFor(_shelfSnap.Items[held.Index], _shelfSnap.Items, _shelfSel);
        // Disponibilidad al momento: un archivo que ya no está no viaja (el destino recibiría una ruta rota).
        if (ShelfDragOut.Build(items, store.IsAvailable) is not { } payload)
        {
            RefreshShelf();
            RenderFrame();
            return;
        }
        RenderFrame();
        _selfDrag = true;
        try
        {
            // Bucle modal de OLE: copiar o vincular, nunca mover (la entrada no debe quedarse colgando).
            DoDragDrop(ShelfDataTransfer.Create(payload), (DragDropEffects)(int)ShelfDragOut.Allowed);
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            AppLog.Warn($"notch: bandeja: no se pudo arrastrar: {ex.Message}");
        }
        finally { _selfDrag = false; }

        // OLE se quedó el WM_LBUTTONUP; si el ratón acabó fuera del notch, sale como siempre.
        if (!Alive) return;
        var now = PointToClient(Cursor.Position);
        if (_pointerInside && !HitTest(now)) PointerLeave();
        else RenderFrame();
    }

    /// <summary>La bandeja del frame (solo en el expandido y con la página en la lista).</summary>
    private ShelfView? ShelfFrame(NotchMode shown)
    {
        if (shown != NotchMode.Expanded || !ShelfEnabled) return null;
        var hover = _shelfHover.Visible ? _shelfHoverHit : default;
        return new ShelfView(_shelfSnap.Items, _shelfSnap.Available, _shelfSel.Ids, (float)_shelfScroll.Value, hover,
            (float)_shelfHover.Intensity, _shelfHeld, _dragOver, _shelfCopied, _shelfCardThumbs, _shelfDetails);
    }

    // ---- arrastrar encima (OLE)

    /// <summary>Respuesta al arrastre: nunca «mover» (<see cref="ShelfDrop.Effect"/>); nada si es el nuestro o no hay página.</summary>
    private DragDropEffects DropEffect(DragEventArgs e, ShelfDrop drop) =>
        !ShelfEnabled || _hiddenForFullscreen ? DragDropEffects.None
            : (DragDropEffects)(int)ShelfDrop.Effect((ShelfDropEffect)(int)e.AllowedEffect, drop, _selfDrag);

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        var drop = _selfDrag ? ShelfDrop.None : ShelfDataTransfer.Read(e.Data);
        e.Effect = DropEffect(e, drop);
        if (e.Effect == DragDropEffects.None) return;
        _dragDrop = drop;
        _dragOver = true;
        _proCardOpen = false;   // lo que se arrastra va a la bandeja, no a la tarjeta
        // Cuenta como puntero encima (la máquina abre YA y despierta el encogido); el realce del ratón no aplica.
        _pointerInside = true;
        ResetShelfPointer();
        _sm.OnDragEnter(DateTime.UtcNow);
        ShowShelfPage();
        Sync();
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effect = _dragOver ? DropEffect(e, _dragDrop) : DragDropEffects.None;
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        if (!_dragOver) return;
        _dragOver = false;
        _dragDrop = ShelfDrop.None;
        PointerLeave();   // contrae tras la gracia de siempre
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (!_dragOver) return;
        _dragOver = false;
        // Se relee al soltar (lo de la entrada puede venir incompleto con datos diferidos); si falla, lo de la entrada.
        var drop = ShelfDataTransfer.Read(e.Data);
        if (drop.IsEmpty) drop = _dragDrop;
        _dragDrop = ShelfDrop.None;
        e.Effect = DropEffect(e, drop);
        if (e.Effect != DragDropEffects.None)
        {
            var shelf = EnsureShelf();
            _shelfSel.Clear();
            ResetShelfScroll();   // lo nuevo va el primero
            TryShelf(() => drop.ApplyTo(shelf));
        }
        // El puntero sigue encima: se vuelve a seguir con TrackMouseEvent para enterarse de cuándo sale (si ya no está
        // encima, WM_MOUSELEAVE llega enseguida).
        var tme = new TRACKMOUSEEVENT { cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(), dwFlags = TME_LEAVE, hwndTrack = Handle };
        TrackMouseEvent(ref tme);
        _pointerInside = true;
        _sm.OnPointerEnter(DateTime.UtcNow);
        Sync();
    }

    // ---------------------------------------------------------------- limpieza

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hub.SessionsChanged -= OnSessions;
            _hub.Signal -= OnSignal;
            _hub.QuotaThresholdCrossed -= OnQuotaCrossing;
            _hub.QuotaChanged -= OnQuota;
            _hub.Permissions.Changed -= OnPermissionsChanged;
            Unhook();   // Application.Idle es estático: sin esto la ventana viviría para siempre
            _shared.MusicChanged -= OnMusic;
            _shared.LyricsChanged -= OnLyricsChanged;
            _musicTick.Dispose();
            _shared.ShelfChanged -= OnShelfChanged;
            _shelfFlash.Dispose();
            _shelfThumbs.Changed -= OnShelfThumbs;
            _shelfCardThumbs = Array.Empty<Bitmap?>();
            EndTaskEdit(save: true, restoreFocus: true);   // antes de soltar las tareas: lo escrito se guarda
            DisposeTasks();
            DisposeUsage();
            DisposeFocus();
            _shared.PermissionAnswered -= OnPermissionAnswered;
            // Deja de contar para las letras y para «alguien ve el notch»: si se cierra oculta, el broker no se queda
            // rechazando para un notch que se cree después con el mismo hub.
            _shared.Forget(this);
            // Sin host, los servicios eran suyos: música, letras (escribe lo pendiente de la caché), miniaturas y tareas.
            if (_ownsShared) _shared.Dispose();
            _contrastTimer.Dispose();
            _wake.Dispose();
            _frameTimer.Dispose();
            _relayout.Dispose();
            _fullscreen.Dispose();
            _renderer.Dispose();
            _shadow.Dispose();
        }
        base.Dispose(disposing);
    }
}
