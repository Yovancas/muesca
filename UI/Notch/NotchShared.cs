using System.Diagnostics;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Notch.Platform;
using ClaudeBarWin.Services.State;
using Microsoft.Win32;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Servicios del notch que existen UNA vez aunque haya una ventana por monitor (modo «todos los monitores»): música
/// (sesión de medios de Windows), letras (fuentes, caché en disco y controlador), bandeja (<c>shelf.json</c> y sus
/// miniaturas en un hilo STA), tareas y recordatorios (<c>tasks.json</c>, el temporizador del próximo aviso, la medianoche y
/// los avisos del sistema), la memoria de campanitas por petición y si el broker de permisos admite peticiones. Cada
/// <see cref="NotchForm"/> se suscribe a sus eventos y guarda solo su estado de vista (animaciones, puntero, página).
///
/// <para>Decisión (29-oct): contenedor compartido y no ventanas de «solo espejo». Con espejos, la ventana dueña de los
/// servicios tendría que ser siempre la misma (y entonces un monitor desconectado se llevaría la música y la bandeja) o
/// mudarlos con el principal efectivo; y las secundarias no podrían expandirse con sus páginas, que es lo que se pide
/// (expandir donde está el puntero). Dos instancias de lo que hay aquí sí serían errores: dos escritores de
/// <c>shelf.json</c>, <c>tasks.json</c> y la caché de letras, recordatorios por duplicado, la letra pedida (y cancelada) a
/// la vez desde dos ventanas. El reloj y el gato NO están aquí: son estado de vista puro (<see cref="IdleScene"/>, sin
/// timers ni E/S) que depende del puntero y del modo de CADA ventana; con las mismas entradas, los notch se ven iguales.</para>
///
/// <list type="bullet">
/// <item><b>Letras:</b> cada ventana dice si la quiere (<see cref="UpdateLyrics"/>); se pide si la quiere ALGUNA. Sin esto
/// una ventana sin la página Música cancelaría la petición de la que la enseña.</item>
/// <item><b>Recordatorios:</b> vencen aquí UNA vez (<see cref="ReminderDue"/>) y se apuntan en disco una vez; contestar o
/// descartar en una ventana los quita de todas (<see cref="ReminderCleared"/>).</item>
/// <item><b>Permisos:</b> el broker admite peticiones mientras alguna ventana se ve (<see cref="SetHidden"/>); con una sola,
/// como siempre: oculta por pantalla completa, no.</item>
/// <item><b>Sonido:</b> nada suena si hay pantalla completa en el monitor de ALGUNA ventana (<see cref="AnyFullscreen"/>): con
/// un juego en el monitor 1, ese notch se oculta y los botones pasan al del 2, pero sonar seguiría siendo en plena partida.</item>
/// <item><b>Uso:</b> la última foto de la bandeja (<see cref="Usage"/>) y los datos de la gráfica por rango (<see cref="UsageFeed"/>):
/// una petición a la bandeja por rango aunque lo enseñen varias ventanas, y el mismo color por familia en todas.</item>
/// <item><b>Foco:</b> UN pomodoro (<see cref="Focus"/>) con UN disparo al fin de la fase: cada fase acaba una vez
/// (<see cref="FocusEnded"/>) y se apunta una vez en su tarea; cualquier botón, en cualquier ventana, cambia el de todas
/// (<see cref="FocusChanged"/>). Lo que hay que guardar sale por <see cref="FocusSaved"/> (la bandeja lo escribe en
/// <c>config.json</c>).</item>
/// </list>
/// Hilo de UI. Con una sola ventana se comporta como cuando todo vivía en ella.
/// </summary>
public sealed class NotchShared : IDisposable
{
    private const int MaxChimedRequests = 64;

    private readonly ClaudeStateHub _hub;
    private readonly Action<Action> _post;
    private NotchOptions _options;
    private bool _disposed;

    // ---- música
    private MediaService? _media;
    private bool _mediaStarting;

    // ---- letras: qué ventana la quiere y reenvío de Changed fuera de las actualizaciones
    private readonly Dictionary<object, bool> _lyricsWanted = new(ReferenceEqualityComparer.Instance);
    private int _lyricsUpdating, _lyricsQuiet;
    private bool _lyricsChangedInUpdate;

    // ---- bandeja
    private readonly StaWorker _thumbWorker = new("notch: miniaturas");
    // «Vaciar al arrancar»: solo con lo pedido al crearse (el arranque) y una vez. Sin el Pro llega apagado
    // (NotchProGating): comprar después no vacía lo que se guardó mientras tanto.
    private bool _clearShelfOnLoad;

    // ---- tareas y recordatorios
    private readonly System.Windows.Forms.Timer _reminderTimer = new();   // un disparo a la próxima hora de aviso
    private readonly System.Windows.Forms.Timer _midnightTimer = new();   // un disparo a la próxima medianoche (con tareas)
    private readonly Dictionary<string, string> _activeReminders = new(StringComparer.Ordinal);   // vencidos sin contestar
    private bool _systemEventsHooked;
    private bool _markingNotified;                       // guardando «ya avisó»: su Changed no vuelve a sondear

    // ---- pomodoro (página Foco)
    private readonly Stopwatch _mono = Stopwatch.StartNew();              // el reloj monotónico del pomodoro
    private readonly System.Windows.Forms.Timer _focusTimer = new();      // un disparo al fin de la fase que corre
    private PomodoroTimer _focus;
    private readonly List<Guid> _pendingTaskFocuses = new();             // focos de una tarea enlazada antes de cargar las tareas

    // ---- sonido y visibilidad
    // Peticiones de permiso que ya sonaron (F2): un Permiso que se vuelve a armar es otra instancia del aviso pero la misma
    // petición. Acotado: se vacía al pasar de MaxChimedRequests (un id viejo que vuelva sonaría otra vez, nada más).
    private readonly HashSet<string> _chimedRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<object, bool> _hidden = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _fullscreen = new(ReferenceEqualityComparer.Instance);

    /// <param name="hub">El del notch (su broker de permisos).</param>
    /// <param name="options">Ajustes de partida (los globales: música, letras, tareas, bandeja).</param>
    /// <param name="post">Lleva trabajo al hilo de UI (miniaturas que vuelven del hilo de fondo, avisos del sistema).</param>
    public NotchShared(ClaudeStateHub hub, NotchOptions options, Action<Action> post)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _clearShelfOnLoad = options.ShelfClearOnStart;
        // Letras v2: LRCLIB (creada al primer uso) y caché en disco (letra, elección y desfase por pista).
        LyricsSource = new LazyLyricsCatalog();
        // Escrituras de la caché agrupadas en segundo plano: los clics de «±0,5 s» no escriben ~1 MB en el hilo de la UI.
        LyricsCache = new LyricsCache(LyricsCache.DefaultPath, LyricsCache.DefaultMax, TimeSpan.FromSeconds(1.5));
        // Lo guardado de NetEase o Musixmatch (retiradas) no se enseña: se vuelve a pedir a LRCLIB.
        Lyrics = new LyricsController(LyricsSource, LyricsCache, LazyLyricsCatalog.Supports);
        Lyrics.Changed += OnLyricsChanged;
        // Lo que se mira en el hilo de fondo vuelve al de UI por _post tal cual: si lanza (cerrándose), la imagen se suelta.
        ShelfThumbs = new ShelfThumbnails(new ShellThumbnailSource(), _thumbWorker.Post, post);
        _reminderTimer.Tick += OnReminderTimer;
        _midnightTimer.Tick += OnMidnightTimer;
        _focus = new PomodoroTimer(options.FocusOrDefault.Durations);
        _focusTimer.Tick += OnFocusTimer;
    }

    // ---------------------------------------------------------------- ajustes

    /// <summary>Ajustes nuevos (cada ventana los pasa; solo actúa lo que cambia). Música y tareas las
    /// enciende o apaga la ventana (<see cref="StartMusicAsync"/>, <see cref="StopMusic"/>, <see cref="EnsureTasks"/>,
    /// <see cref="UnloadTasks"/>), en el mismo orden que siempre (p. ej. guardar la tarea que se edita antes de descargar).</summary>
    public void ApplyOptions(NotchOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        _options = o;
        if (!o.ShelfClearOnStart) _clearShelfOnLoad = false;   // apagado antes de cargar: ya no se vacía
        // Duraciones nuevas (Ajustes o el Pro que se va): la fase parada se ajusta; la que corre sigue con la suya.
        if (_focus.SetDurations(o.FocusOrDefault.Durations)) FocusCommit(save: true);
    }

    // ---------------------------------------------------------------- pomodoro (página Foco)

    /// <summary>El pomodoro (uno para todas las ventanas).</summary>
    public PomodoroTimer Focus => _focus;

    /// <summary>El instante del pomodoro: pared en UTC, el monotónico de este proceso y la hora local.</summary>
    public FocusNow FocusNow() => FocusClock?.Invoke() ?? new(DateTime.UtcNow, _mono.Elapsed, DateTime.Now);

    /// <summary>Reloj del pomodoro para los tests (null = el de verdad).</summary>
    internal Func<FocusNow>? FocusClock { get; set; }

    /// <summary>El pomodoro cambió (un botón en cualquier ventana, el fin de una fase, duraciones, la tarea…). Hilo de UI.</summary>
    public event Action? FocusChanged;

    /// <summary>Acabó una fase (una vez para todas las ventanas: cada una enseña su aviso). Hilo de UI.</summary>
    public event Action<FocusEnd>? FocusEnded;

    /// <summary>Hay que guardar el pomodoro (lo reenvía <see cref="NotchHost"/>; la bandeja lo escribe en <c>config.json</c>).</summary>
    public event Action<FocusState>? FocusSaved;

    /// <summary>El aviso del fin de fase se descartó (clic fuera de su botón en una ventana): fuera de la cola de todas.</summary>
    public event Action? FocusAlertCleared;

    /// <summary>Descarta el aviso del fin de fase en todas las ventanas (la oferta sigue en la página Foco).</summary>
    public void ClearFocusAlert()
    {
        if (!_disposed) FocusAlertCleared?.Invoke();
    }

    /// <summary>¿Se puede enlazar el foco con una tarea? (Pro y las tareas encendidas y desbloqueadas.)</summary>
    public bool FocusLinkAllowed => _options.Tasks && (_options.Locked & NotchProLocks.FocusPro) == 0;

    /// <summary>La tarea enlazada que se ve (null sin enlace, sin el Pro o si la tarea ya no está).</summary>
    public NotchTask? FocusTask => FocusLinkAllowed && _focus.TaskId is { } id && Tasks is { } tasks
        ? tasks.Items.FirstOrDefault(t => t.Id == id) : null;

    /// <summary>
    /// El pomodoro guardado en otra ejecución (la bandeja lo pasa al crear el notch). Lo que acabó con la app cerrada se cierra
    /// ya (y se avisa si fue hace poco: <see cref="PomodoroTimer.StaleAfter"/>); una oferta muy vieja caduca.
    /// </summary>
    public void RestoreFocus(FocusState? state)
    {
        if (_disposed) return;
        var now = FocusNow();
        _focus = PomodoroTimer.Restore(state, _options.FocusOrDefault.Durations, now);
        if (_focus.Active) HookSystemEvents();
        FocusTick(now);   // lo que acabó con la app cerrada se cierra; lo que caducó, caduca (y se guarda)
        FocusChanged?.Invoke();
    }

    /// <summary>El botón principal: empieza, pausa o reanuda (con la oferta a la vista, empieza la fase que ofrece).</summary>
    public void FocusToggle() => FocusAct(t => t.Toggle(FocusNow()));

    /// <summary>«Empezar descanso» / «Empezar foco» del aviso del fin de fase (sin pausar si ya corre).</summary>
    public void FocusStart() => FocusAct(t => t.Start(FocusNow()));

    /// <summary>Saltar a la siguiente fase sin contar esta.</summary>
    public void FocusSkip() => FocusAct(t => t.Skip(FocusNow()));

    /// <summary>Reiniciar la fase (o, parado, el ciclo entero).</summary>
    public void FocusReset() => FocusAct(t => t.Reset(FocusNow()));

    /// <summary>Enlaza el foco con la tarea <paramref name="taskId"/> (null = ninguna). Solo con <see cref="FocusLinkAllowed"/>.</summary>
    public void FocusLink(Guid? taskId)
    {
        if (_disposed || (!FocusLinkAllowed && taskId is not null)) return;
        if (_focus.Link(taskId)) FocusCommit(save: true);
    }

    /// <summary>Al mostrarse la página Foco: una oferta de hace más de <see cref="PomodoroTimer.StaleAfter"/> caduca.</summary>
    public void FocusRefresh()
    {
        if (_disposed) return;
        FocusTick(FocusNow());
    }

    private void FocusAct(Action<PomodoroTimer> act)
    {
        if (_disposed) return;
        int before = _focus.Version;
        act(_focus);
        if (_focus.Active) HookSystemEvents();
        if (_focus.Version != before) FocusCommit(save: true);
    }

    /// <summary>Se reprograma el disparo, se avisa a las ventanas y, con <paramref name="save"/>, se pide guardar.</summary>
    private void FocusCommit(bool save)
    {
        ArmFocusTimer(FocusNow());
        FocusChanged?.Invoke();
        if (save) FocusSaved?.Invoke(_focus.Save());
    }

    /// <summary>Si la fase que corre ya acabó, la cierra: se apunta en su tarea, se avisa (una vez) y se guarda. Siempre
    /// reprograma el disparo.</summary>
    private void FocusTick(FocusNow now)
    {
        int before = _focus.Version;
        _focus.Expire(now);   // una oferta o una pausa olvidadas: el ciclo vuelve a empezar
        var end = _focus.Advance(now);
        if (end is null)
        {
            ArmFocusTimer(now);
            // Acabó hace tanto que no se cuenta, o caducó: el ciclo vuelve a empezar y eso también se guarda y se ve (sin el
            // aviso, las ventanas seguirían con el mini anillo de una fase que ya no corre).
            if (_focus.Version == before) return;
            FocusSaved?.Invoke(_focus.Save());
            FocusChanged?.Invoke();
            return;
        }
        if (end.Counted && end.TaskId is { } id && FocusLinkAllowed)
        {
            if (Tasks is not { } tasks) _pendingTaskFocuses.Add(id);   // acabó con la app cerrada: se apunta al cargar las tareas
            else if (tasks.Items.Any(t => t.Id == id)) TryTasks(() => tasks.AddFocus(id));
            else _focus.Link(null);   // la tarea ya no está
        }
        ArmFocusTimer(now);
        FocusSaved?.Invoke(_focus.Save());
        FocusEnded?.Invoke(end);
        FocusChanged?.Invoke();
    }

    /// <summary>UN disparo al fin de la fase que corre o, parado o en pausa, a cuando caduca (horas: nada más despierta).</summary>
    private void ArmFocusTimer(FocusNow now)
    {
        _focusTimer.Stop();
        if (_disposed || (_focus.NextEndIn(now) ?? _focus.ExpiresIn(now)) is not { } d) return;
        _focusTimer.Interval = (int)Math.Clamp(Math.Ceiling(d.TotalMilliseconds) + 1, 1, int.MaxValue);
        _focusTimer.Start();
    }

    private void OnFocusTimer(object? sender, EventArgs e)
    {
        _focusTimer.Stop();
        if (!_disposed) FocusTick(FocusNow());   // si saltó un pelo antes de plazo, solo se rearma
    }

    /// <summary>Vuelta de suspender (<paramref name="resume"/>) o cambio de hora del sistema: se realinean las anclas del pomodoro,
    /// se cierra lo que acabó mientras tanto y se guarda la hora de fin nueva.</summary>
    private void FocusClockJump(bool resume)
    {
        if (_disposed) return;
        var now = FocusNow();
        if (_focus.Run != FocusRun.Running)
        {
            FocusTick(now);   // lo parado o en pausa pudo caducar durmiendo
            return;
        }
        if (resume) _focus.OnResume(now);
        else _focus.OnClockChanged(now);
        FocusSaved?.Invoke(_focus.Save());
        FocusTick(now);
        FocusChanged?.Invoke();
    }

    // ---------------------------------------------------------------- página Uso

    private readonly UsageFeed _usageFeed = new();

    /// <summary>Lo último de la bandeja para la página Uso (null hasta el primer refresco).</summary>
    public UsageInput? Usage { get; private set; }

    /// <summary>Slot de color estable de cada familia de modelo en la gráfica (el mismo en todas las ventanas).</summary>
    public ChartSeriesAssigner UsageSlots { get; } = new();

    /// <summary>Llegó una foto nueva de la bandeja (hilo de UI).</summary>
    public event Action? UsageChanged;

    /// <summary>Llegaron los datos de la gráfica de un rango (hilo de UI).</summary>
    public event Action<ChartRange>? UsageSeriesChanged;

    /// <summary>Hay que pedir a la bandeja los datos de un rango (lo reenvía <see cref="NotchHost"/>).</summary>
    public event Action<ChartRange>? UsageSeriesWanted;

    /// <summary>La foto nueva de la bandeja.</summary>
    public void SetUsage(UsageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (_disposed) return;
        Usage = input;
        UsageChanged?.Invoke();
    }

    /// <summary>Los datos guardados de <paramref name="range"/> (null si aún no llegaron).</summary>
    public UsageSeriesData? UsageSeries(ChartRange range) => _usageFeed.Get(range);

    /// <summary>Pide los datos de <paramref name="range"/> si lo guardado es viejo y no hay ya una petición en vuelo.</summary>
    public void RequestUsageSeries(ChartRange range, DateTime nowUtc)
    {
        if (_disposed || !_usageFeed.ShouldRequest(range, nowUtc)) return;
        _usageFeed.MarkRequested(range, nowUtc);
        UsageSeriesWanted?.Invoke(range);
    }

    /// <summary>Llegaron los datos de <paramref name="range"/> (hilo de UI).</summary>
    public void SetUsageSeries(ChartRange range, UsageSeriesData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (_disposed) return;
        _usageFeed.Store(range, data);
        UsageSeriesChanged?.Invoke(range);
    }

    /// <summary>Falló la lectura de los datos de un rango (hilo de UI): las ventanas lo dicen y lo reintentan.</summary>
    public event Action<ChartRange>? UsageSeriesError;

    /// <summary>¿Falló la última petición de <paramref name="range"/>?</summary>
    public bool UsageSeriesHasFailed(ChartRange range) => _usageFeed.HasFailed(range);

    /// <summary>Lo que la página Uso recuerda entre aperturas (modo, ventana y rango; lo último que eligió cualquier ventana).
    /// Las ventanas nuevas empiezan con ello.</summary>
    public UsageViewState UsageView { get; private set; } = UsageViewState.Default;

    /// <summary>La bandeja restauró lo guardado (<see cref="RestoreUsageView"/>): las ventanas lo toman.</summary>
    public event Action? UsageViewRestored;

    /// <summary>Una ventana eligió otro modo, ventana o rango: hay que guardarlo (lo reenvía <see cref="NotchHost"/>).</summary>
    public event Action<UsageViewState>? UsageViewSaved;

    /// <summary>Lo guardado en <c>config.json</c> (null = lo de serie), normalizado; no se vuelve a pedir guardarlo.</summary>
    public void RestoreUsageView(UsageViewState? state)
    {
        if (_disposed) return;
        UsageView = (state ?? UsageViewState.Default).Normalize();
        UsageViewRestored?.Invoke();
    }

    /// <summary>Lo que acaba de elegir una ventana: se recuerda y, si cambió, se pide guardarlo.</summary>
    public void SaveUsageView(UsageViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_disposed || state == UsageView) return;
        UsageView = state;
        UsageViewSaved?.Invoke(state);
    }

    /// <summary>La petición de <paramref name="range"/> falló: se podrá repetir (lo guardado se sigue enseñando) y las
    /// ventanas se enteran (si no, una que espera ese rango se quedaría con la gráfica del anterior hasta la próxima foto).</summary>
    public void UsageSeriesFailed(ChartRange range)
    {
        if (_disposed) return;
        _usageFeed.Failed(range);
        UsageSeriesError?.Invoke(range);
    }

    // ---------------------------------------------------------------- música

    /// <summary>Lo que suena ahora (null sin reproductor o con la música apagada).</summary>
    public NowPlaying? NowPlaying { get; private set; }

    /// <summary>El servicio de música, si está en marcha (controles de la página Música).</summary>
    public MediaService? Media => _media;

    /// <summary>Lo que suena cambió (hilo de UI).</summary>
    public event Action<NowPlaying?>? MusicChanged;

    /// <summary>Crea el servicio de música (WinRT) si el ajuste está activo y no lo estaba. Lo llama la ventana al mostrarse y
    /// al activar el ajuste; varias a la vez, una sola sesión.</summary>
    public async Task StartMusicAsync()
    {
        if (_media is not null || _mediaStarting || !_options.Music || _disposed) return;
        _mediaStarting = true;
        MediaService? m = null;
        try
        {
            m = await MediaService.TryCreateAsync();   // vuelve al hilo de UI (contexto capturado)
        }
        catch (Exception ex)
        {
            AppLog.Warn($"notch: no se pudo arrancar la música: {ex.Message}");
        }
        finally
        {
            _mediaStarting = false;
        }
        if (m is null) return;
        if (_disposed || !_options.Music || _media is not null)
        {
            m.Dispose();
            return;
        }
        _media = m;
        m.Changed += OnMedia;
        OnMedia(m.Current);
    }

    /// <summary>Para la música (ajuste apagado o cierre): todas las ventanas reciben «nada suena».</summary>
    public void StopMusic()
    {
        var m = _media;
        _media = null;
        if (m is not null)
        {
            m.Changed -= OnMedia;
            m.Dispose();
        }
        OnMedia(null);
    }

    private void OnMedia(NowPlaying? np)
    {
        NowPlaying = np;
        MusicChanged?.Invoke(np);
    }

    // ---------------------------------------------------------------- letras

    public LazyLyricsCatalog LyricsSource { get; }
    public LyricsCache LyricsCache { get; }
    public LyricsController Lyrics { get; }

    /// <summary>La letra cambió (buscando → letra / sin letra…). Lo que cambia DENTRO de <see cref="UpdateLyrics"/> se avisa al
    /// acabar la actualización (el controlador ya está entero); la ventana que la pidió lo ignora, porque ya lo tiene en
    /// cuenta.</summary>
    public event Action? LyricsChanged;

    /// <summary>
    /// Lleva el controlador a lo que suena y pide la letra si ALGUNA ventana la quiere (<paramref name="wanted"/> es lo de
    /// <paramref name="window"/>, con su regla de red de siempre: <see cref="LyricsController.Wanted"/>). Con
    /// <paramref name="quiet"/> no se avisa a nadie (quien llama sincroniza después).
    /// </summary>
    public void UpdateLyrics(object window, bool wanted, bool quiet = false)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_disposed) return;
        _lyricsWanted[window] = wanted;
        bool any = _lyricsWanted.Values.Any(w => w);
        if (quiet) _lyricsQuiet++;
        _lyricsUpdating++;
        try { Lyrics.Update(NowPlaying, any); }
        finally
        {
            _lyricsUpdating--;
            if (quiet) _lyricsQuiet--;
        }
        if (_lyricsUpdating == 0 && _lyricsChangedInUpdate)
        {
            _lyricsChangedInUpdate = false;
            if (!quiet && _lyricsQuiet == 0) LyricsChanged?.Invoke();
        }
    }

    private void OnLyricsChanged()
    {
        if (_lyricsQuiet > 0) return;
        if (_lyricsUpdating > 0)
        {
            _lyricsChangedInUpdate = true;
            return;
        }
        LyricsChanged?.Invoke();
    }

    // ---------------------------------------------------------------- bandeja

    /// <summary>La bandeja, si ya se cargó (<see cref="EnsureShelf"/>).</summary>
    public ShelfStore? Shelf { get; private set; }

    /// <summary>Miniaturas, tamaños y existencia de las entradas, en un hilo STA de fondo, con su caché.</summary>
    public ShelfThumbnails ShelfThumbs { get; }

    /// <summary>La bandeja cambió (soltar, quitar, vaciar) en cualquier ventana.</summary>
    public event Action? ShelfChanged;

    /// <summary>Crea la bandeja la primera vez (lee <c>shelf.json</c>; «vaciar al arrancar» si el ajuste lo pide).</summary>
    public ShelfStore EnsureShelf()
    {
        if (Shelf is not null) return Shelf;
        var shelf = new ShelfStore(ShelfDefaults.StorePath, ShelfDefaults.Exists);
        Shelf = shelf;
        if (_clearShelfOnLoad && shelf.Items.Count > 0) TryShelf(shelf.Clear);
        _clearShelfOnLoad = false;
        shelf.Changed += OnShelfChanged;
        return shelf;
    }

    private void OnShelfChanged()
    {
        if (!_disposed) ShelfChanged?.Invoke();
    }

    /// <summary>Cambios en <c>shelf.json</c>: un error de disco no tumba la ventana (se anota y la bandeja sigue en memoria).</summary>
    internal static void TryShelf(Action change)
    {
        try { change(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn($"notch: bandeja: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- tareas y recordatorios

    /// <summary>Las tareas, si están cargadas (ajuste «Tareas» activo y alguna ventana ya se mostró).</summary>
    public TaskStore? Tasks { get; private set; }

    /// <summary>Recordatorios ya vencidos (cada uno una vez) y su temporizador. Rearmar = «+10 min».</summary>
    public TaskReminderClock ReminderClock { get; } = new();

    /// <summary>Las tareas cambiaron (o se cargaron / descargaron).</summary>
    public event Action? TasksChanged;

    /// <summary>Vence un recordatorio: (sesión <c>task:&lt;id&gt;</c>, título). Cada ventana lo encola en su máquina.</summary>
    public event Action<string, string>? ReminderDue;

    /// <summary>Un recordatorio se contestó, se descartó o su tarea ya no está: fuera de la cola de todas las ventanas.</summary>
    public event Action<string>? ReminderCleared;

    /// <summary>Recordatorios vencidos y sin contestar (sesión → título): una ventana nueva (monitor conectado) los encola.</summary>
    public IReadOnlyDictionary<string, string> ActiveReminders => _activeReminders;

    /// <summary>Carga <c>tasks.json</c> la primera vez (al mostrarse con el ajuste «Tareas» activo, esté o no la página en
    /// la lista), purga las hechas de días anteriores y programa el primer aviso y la medianoche. Se engancha a la
    /// suspensión y al cambio de hora del sistema.</summary>
    public void EnsureTasks()
    {
        if (Tasks is not null || !_options.Tasks || _disposed) return;
        var tasks = new TaskStore(TaskDefaults.StorePath);
        Tasks = tasks;
        tasks.Changed += OnTasksChanged;
        TryTasks(() => tasks.Purge(DateTime.Now));
        HookSystemEvents();
        // Focos de la tarea enlazada que acabaron antes de cargarlas (al restaurar el pomodoro): se apuntan ahora.
        foreach (var id in _pendingTaskFocuses.Where(id => tasks.Items.Any(t => t.Id == id)).ToList()) TryTasks(() => tasks.AddFocus(id));
        _pendingTaskFocuses.Clear();
        TasksChanged?.Invoke();
        PollReminders();
        ArmMidnight();
    }

    /// <summary>Ajuste «Tareas» apagado: fuera los avisos pendientes de todas las colas y los temporizadores; el archivo se
    /// queda como está (al volver a encenderlo se relee).</summary>
    public void UnloadTasks()
    {
        if (Tasks is not { } tasks) return;
        foreach (var t in tasks.Items) ClearReminder(TaskAlerts.SessionId(t.Id));
        tasks.Changed -= OnTasksChanged;
        Tasks = null;
        _activeReminders.Clear();
        _reminderTimer.Stop();
        _midnightTimer.Stop();
        TasksChanged?.Invoke();
    }

    /// <summary>Las máquinas de modos se rehicieron (Ajustes) y los avisos en cola se perdieron: tampoco se encolan en una
    /// ventana que se cree después.</summary>
    public void ForgetActiveReminders() => _activeReminders.Clear();

    /// <summary>Quita el recordatorio de esa sesión de todas las ventanas (contestado, descartado, tarea hecha o quitada).</summary>
    public void ClearReminder(string sessionId)
    {
        _activeReminders.Remove(sessionId);
        ReminderCleared?.Invoke(sessionId);
    }

    /// <summary>Encola los recordatorios vencidos (cada uno una vez; con el notch oculto esperan en la cola) y programa el
    /// siguiente con un único disparo. Sin tareas con hora pendientes, ningún temporizador.</summary>
    public void PollReminders()
    {
        _reminderTimer.Stop();
        if (Tasks is not { } tasks || _disposed) return;
        var now = DateTime.Now;
        var items = tasks.Items;
        var due = ReminderClock.Poll(items, now);
        foreach (var t in due)
        {
            string id = TaskAlerts.SessionId(t.Id);
            _activeReminders[id] = t.Title;
            ReminderDue?.Invoke(id, t.Title);
        }
        if (due.Count > 0)
        {
            // Apuntado en disco: tras reiniciar la app (o el PC) el mismo aviso no vuelve a sonar.
            _markingNotified = true;
            try { TryTasks(() => tasks.MarkNotified(due.Select(t => t.Id))); }
            finally { _markingNotified = false; }
        }
        if (ReminderClock.NextWakeIn(items, now) is not { } d) return;
        _reminderTimer.Interval = (int)Math.Clamp(Math.Ceiling(d.TotalMilliseconds), 1, int.MaxValue);
        _reminderTimer.Start();
    }

    private void OnTasksChanged()
    {
        if (_disposed) return;
        if (!_markingNotified)
        {
            // Lo guarda PollReminders mismo: sin sondeo anidado.
            PollReminders();
            ArmMidnight();
        }
        // La tarea enlazada al foco se quitó: fuera el enlace.
        if (_focus.TaskId is { } linked && Tasks is { } tasks && tasks.Items.All(t => t.Id != linked)) FocusLink(null);
        TasksChanged?.Invoke();
    }

    private void OnReminderTimer(object? sender, EventArgs e)
    {
        _reminderTimer.Stop();
        PollReminders();
    }

    /// <summary>Disparo único a la próxima medianoche, solo con tareas (sin ellas no hay nada que purgar ni que recalcular).</summary>
    private void ArmMidnight()
    {
        _midnightTimer.Stop();
        if (Tasks is not { } tasks || _disposed || tasks.Items.Count == 0) return;
        var d = TaskTimes.UntilMidnight(DateTime.Now);
        // +1 s: que el reloj ya marque el día nuevo al despertar (el temporizador puede adelantarse unos ms).
        _midnightTimer.Interval = (int)Math.Clamp(Math.Ceiling(d.TotalMilliseconds) + 1000, 1, int.MaxValue);
        _midnightTimer.Start();
    }

    /// <summary>Medianoche: purga las hechas de ayer y avisa (las etiquetas «mañana»/vencidas dependen del día).</summary>
    private void OnMidnightTimer(object? sender, EventArgs e)
    {
        _midnightTimer.Stop();
        OnSystemClockJumpNow();
    }

    /// <summary>Suspensión y hora del sistema (tareas y pomodoro): una vez, la primera que lo necesite.</summary>
    private void HookSystemEvents()
    {
        if (_systemEventsHooked || _disposed) return;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnSystemTimeChanged;
        _systemEventsHooked = true;
    }

    /// <summary>Al volver de suspender (el temporizador no corre dormido) o si cambia la hora del sistema: el pomodoro se realinea
    /// y las tareas purgan y sondean. Estos eventos pueden llegar desde otro hilo: al de UI con <see cref="Post"/>.</summary>
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) OnSystemClockJump(resume: true);
    }

    private void OnSystemTimeChanged(object? sender, EventArgs e) => OnSystemClockJump(resume: false);

    private void OnSystemClockJump(bool resume)
    {
        if (_disposed) return;
        // Un cambio de zona horaria llega por aquí también: sin vaciar la caché, DateTime.Now seguiría con la zona vieja (el
        // «hoy» del pomodoro, la medianoche de las tareas).
        TimeZoneInfo.ClearCachedData();
        Post(() =>
        {
            FocusClockJump(resume);
            OnSystemClockJumpNow();
        });
    }

    private void OnSystemClockJumpNow()
    {
        if (_disposed || Tasks is not { } tasks) return;
        TryTasks(() => tasks.Purge(DateTime.Now));
        PollReminders();
        ArmMidnight();
        TasksChanged?.Invoke();
    }

    /// <summary>Cambios en <c>tasks.json</c>: un error de disco no tumba la ventana (se anota; la lista sigue en memoria).</summary>
    internal static void TryTasks(Action change)
    {
        try { change(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Warn($"notch: tareas: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- permisos, campanitas y visibilidad

    /// <summary>La ventana interactiva contestó una petición (quién, sesión, petición, respuesta): las de solo lectura enseñan la
    /// misma confirmación y lo quitan a la vez, en vez de quedarse con el aviso hasta que Claude siga.</summary>
    public event Action<object, string, string, PermissionVerdict>? PermissionAnswered;

    public void AnnouncePermissionAnswer(object origin, string sessionId, string requestId, PermissionVerdict verdict) =>
        PermissionAnswered?.Invoke(origin, sessionId, requestId, verdict);

    /// <summary>Un Permiso suena UNA vez por petición aunque se vuelva a armar, o cambie la ventana que lo enseña con botones.
    /// <c>true</c> = es la primera vez (puede sonar).</summary>
    public bool TryChimeRequest(string key)
    {
        if (_chimedRequests.Contains(key)) return false;
        if (_chimedRequests.Count >= MaxChimedRequests) _chimedRequests.Clear();
        _chimedRequests.Add(key);
        return true;
    }

    /// <summary>Una ventana se oculta (pantalla completa) o vuelve. El broker admite peticiones mientras alguna se vea (nadie
    /// mirando: lo pendiente se cierra sin decisión y Claude Code enseña su diálogo sin esperar el plazo).</summary>
    public void SetHidden(object window, bool hidden)
    {
        _hidden[window] = hidden;
        // Oculta no pinta letra: deja de quererla (si no, otra ventana la pediría por ella). Al volver, su Sync la pide.
        if (hidden && _lyricsWanted.ContainsKey(window)) _lyricsWanted[window] = false;
        UpdateAccepting();
    }

    /// <summary>Hay (o deja de haber) pantalla completa o presentación en el monitor de una ventana, se oculte o no.</summary>
    public void SetFullscreen(object window, bool fullscreen)
    {
        if (fullscreen) _fullscreen.Add(window);
        else _fullscreen.Remove(window);
    }

    /// <summary>¿Pantalla completa en el monitor de alguna ventana? Entonces no suena ninguna (la interactiva puede estar en
    /// otro monitor). Con una sola ventana, lo mismo que su propio vigilante: como siempre.</summary>
    public bool AnyFullscreen => _fullscreen.Count > 0;

    /// <summary>La ventana se cierra: deja de contar para las letras, la visibilidad y la pantalla completa.</summary>
    public void Forget(object window)
    {
        bool hadWanted = _lyricsWanted.Remove(window, out bool wanted) && wanted;
        _hidden.Remove(window);
        _fullscreen.Remove(window);
        UpdateAccepting();
        if (hadWanted && !_disposed) Lyrics.Update(NowPlaying, _lyricsWanted.Values.Any(w => w));   // nada en vuelo para nadie
    }

    /// <summary>Sin ventanas (o al cerrarse la última oculta), vuelve a admitir: el broker no se queda rechazando para un
    /// notch que se cree después con el mismo hub.</summary>
    private void UpdateAccepting() =>
        _hub.Permissions.SetAccepting(_hidden.Count == 0 || _hidden.Values.Any(h => !h));

    // ---------------------------------------------------------------- limpieza

    private void Post(Action work)
    {
        try { _post(work); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { /* cerrándose */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopMusic();
        _disposed = true;
        Lyrics.Changed -= OnLyricsChanged;
        Lyrics.Dispose();
        LyricsCache.Dispose();   // escribe lo pendiente
        LyricsSource.Dispose();
        if (Shelf is not null) Shelf.Changed -= OnShelfChanged;
        _thumbWorker.Dispose();  // lo pendiente ya no se pide
        ShelfThumbs.Dispose();   // lo que esté en marcha, al volver, encuentra la caché cerrada y suelta su imagen
        if (_systemEventsHooked)
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnSystemTimeChanged;
            _systemEventsHooked = false;
        }
        if (Tasks is not null) Tasks.Changed -= OnTasksChanged;
        _reminderTimer.Tick -= OnReminderTimer;
        _reminderTimer.Dispose();
        _midnightTimer.Tick -= OnMidnightTimer;
        _midnightTimer.Dispose();
        _focusTimer.Tick -= OnFocusTimer;
        _focusTimer.Dispose();
        _hidden.Clear();
        _fullscreen.Clear();
        UpdateAccepting();
    }
}
