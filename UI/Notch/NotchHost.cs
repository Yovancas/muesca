using System.Diagnostics;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Las ventanas del notch según <see cref="NotchMonitorMode"/> (decisión del 29-oct: «haz que se pueda decidir»), todas sobre
/// el MISMO <see cref="ClaudeStateHub"/> y el mismo <see cref="NotchShared"/>. Capa Win32 fina: las reglas son de
/// <see cref="NotchMonitorPolicy"/>, <see cref="ForegroundDebounce"/> y <see cref="NotchMonitorChoice"/>.
///
/// <list type="bullet">
/// <item><b>Fijo:</b> una ventana en <see cref="NotchOptions.MonitorDeviceName"/> ("" = principal). Sin hooks ni nada más:
/// exactamente como antes.</item>
/// <item><b>Seguir a la ventana activa:</b> una ventana que se muda (<see cref="NotchForm.MoveToMonitor"/>, encoger, mover y
/// crecer) al monitor de la ventana en primer plano cuando se asienta (300 ms) y si no está retenida
/// (<see cref="NotchForm.HoldsPlace"/>: expandida, con el puntero encima o con un Permiso o Recordatorio a la vista); si lo
/// está, espera a que acabe.</item>
/// <item><b>Todos los monitores:</b> una ventana por monitor (<see cref="NotchForm.StrictMonitor"/>), reflejando lo mismo; solo
/// la del principal efectivo (el monitor de la ventana activa, con la misma señal; o el del último clic en un aviso de solo
/// lectura) es <see cref="NotchForm.Interactive"/>. Al conectar o desconectar monitores (<c>WM_DISPLAYCHANGE</c>, 400 ms
/// después) se crean o cierran ventanas sin tocar las demás. Pantalla completa: cada una mira su monitor.</item>
/// </list>
/// La señal de la ventana activa es la de los hooks compartidos (<see cref="WinEventHooks"/>: primer plano, fin de un
/// arrastre y la ventana que deja de moverse): sin hooks nuevos ni sondeos; en reposo, nada despierta. Una ventana que se
/// cierra sola (Alt+F4 con el foco en ella) sale de la lista y se vuelve a crear (<see cref="OnWindowDisposed"/>). Hilo
/// de UI.
/// </summary>
public sealed class NotchHost : IDisposable
{
    /// <summary>Espera tras un cambio de pantallas antes de crear o cerrar ventanas (llegan en ráfaga; como el notch).</summary>
    private const int DisplayDebounceMs = 400;

    private readonly ClaudeStateHub _hub;
    private readonly NotchShared _shared;
    private readonly Action<Action> _post;
    private readonly List<NotchForm> _windows = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly ForegroundDebounce _debounce = new();
    private readonly System.Windows.Forms.Timer _foregroundTimer = new();   // un disparo: se asienta la ventana activa
    private readonly System.Windows.Forms.Timer _displayTimer = new() { Interval = DisplayDebounceMs };
    private NotchOptions _options;
    private NotchMonitorMode _mode;
    private Strings _strings;
    // Seguir a la ventana activa: el monitor del notch. Todos los monitores: el principal efectivo elegido.
    private NotchMonitorChoice _choice = new("");
    private string? _previousFollow;   // seguir a la ventana activa: de dónde vino (para volver si allí se tapa)
    private bool _hooked;
    private bool _rolesQueued;
    private bool _ending;              // la sesión o la app se cierran: las ventanas que caen no se recrean
    private bool _disposed;
    private readonly bool _show;
    private UpdateOfferView? _updateOffer;   // la versión nueva: la llevan todas, el botón solo la interactiva
    private NotchProOffer? _proOffer;        // el Pro: lo llevan todas, los botones de la tarjeta solo la interactiva
    private string? _appearSettledOn;        // relevo del asistente: la ventana de ese monitor aparece ya asentada

    public NotchHost(ClaudeStateHub hub, NotchOptions options, NotchMonitorMode mode, Strings strings)
        : this(hub, options, mode, strings, show: true)
    {
    }

    /// <param name="appearSettledOn">Monitor (nombre GDI) donde el asistente de bienvenida acaba de encogerse hasta el notch
    /// de reposo: la ventana de ese monitor aparece ya asentada, encima de él (el relevo). null = como siempre.</param>
    public NotchHost(ClaudeStateHub hub, NotchOptions options, NotchMonitorMode mode, Strings strings, string? appearSettledOn)
        : this(hub, options, mode, strings, show: true, appearSettledOn: appearSettledOn)
    {
    }

    /// <param name="show">false = crea las ventanas sin mostrarlas (tests: sin nada en pantalla).</param>
    /// <param name="post">Lleva trabajo al siguiente mensaje del hilo de UI; por defecto, su contexto de sincronización (los
    /// tests lo guardan y lo ejecutan a mano, sin bombear mensajes).</param>
    /// <param name="appearSettledOn">Ver el constructor público: solo cuenta para las ventanas que se crean aquí.</param>
    internal NotchHost(ClaudeStateHub hub, NotchOptions options, NotchMonitorMode mode, Strings strings, bool show,
        Action<Action>? post = null, string? appearSettledOn = null)
    {
        _appearSettledOn = appearSettledOn;
        _show = show;
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        if (post is null)
        {
            var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            post = work => ui.Post(_ => work(), null);
        }
        _post = post;
        _shared = new NotchShared(hub, options, _post);
        _shared.UsageSeriesWanted += OnUsageSeriesWanted;
        _shared.FocusSaved += OnFocusSaved;
        _shared.UsageViewSaved += OnUsageViewSaved;
        _foregroundTimer.Tick += (_, _) => OnForegroundTimer();
        _displayTimer.Tick += (_, _) => { _displayTimer.Stop(); Reconcile(); };
        _mode = Enum.IsDefined(mode) ? mode : NotchMonitorMode.Fixed;
        EnterMode();
        Reconcile();
        _appearSettledOn = null;   // solo las ventanas del arranque: las que vengan luego aparecen como siempre
    }

    /// <summary>Ventanas vivas (una, o una por monitor).</summary>
    internal IReadOnlyList<NotchForm> Windows => _windows;

    internal NotchMonitorMode Mode => _mode;

    /// <summary>¿Escucha la ventana activa? (solo siguiéndola o con uno por monitor; en Fijo, ningún hook propio).</summary>
    internal bool FollowsForeground => _hooked;

    /// <summary>
    /// Versión nueva (la conecta la bandeja a <c>IAppUpdates</c>): todas las ventanas enseñan el punto en el reposo y la línea
    /// en el expandido; el botón «Actualizar», solo la interactiva (como los botones del Permiso). null la quita. Las ventanas
    /// que se creen después (otro monitor, una que se cerró sola) la reciben al nacer.
    /// </summary>
    public void SetUpdateOffer(UpdateOfferView? offer)
    {
        if (_disposed) return;
        _updateOffer = offer;
        foreach (var w in _windows.Where(IsAlive).ToArray()) w.SetUpdateOffer(offer);
    }

    /// <summary>Clic en «Actualizar» en la ventana interactiva: la bandeja llama a <c>IAppUpdates.ApplyAndRestartAsync</c>.</summary>
    public event Action? UpdateRequested;

    /// <summary>La oferta que tienen las ventanas (null sin oferta).</summary>
    internal UpdateOfferView? UpdateOffer => _updateOffer;

    /// <summary>
    /// El Pro de ahora (la conecta la bandeja a la licencia): todas las ventanas lo tienen para su tarjeta; los botones, solo la
    /// interactiva. Las ventanas que se creen después lo reciben al nacer.
    /// </summary>
    public void SetProOffer(NotchProOffer? offer)
    {
        if (_disposed) return;
        _proOffer = offer;
        foreach (var w in _windows.Where(IsAlive).ToArray()) w.SetProOffer(offer);
    }

    /// <summary>La oferta del Pro que tienen las ventanas.</summary>
    internal NotchProOffer? ProOffer => _proOffer;

    /// <summary>Clic en «Comprar», «Restaurar compra» o «Conseguir Pro…» en la ventana interactiva.</summary>
    public event Action<NotchProAction>? ProActionRequested;

    /// <summary>
    /// Lo último de la bandeja para la página Uso (cuota, ritmo, gasto, salud y plan), en cada refresco: lo tienen todas las
    /// ventanas (<see cref="NotchShared.Usage"/>), también las que se creen después.
    /// </summary>
    public void SetUsage(UsageInput input)
    {
        if (_disposed) return;
        _shared.SetUsage(input);
    }

    /// <summary>Una ventana necesita los datos de la gráfica de un rango (como mucho uno en vuelo por rango): la bandeja los lee
    /// en segundo plano y responde con <see cref="SetUsageSeries"/> o <see cref="UsageSeriesFailed"/> en el hilo de UI.</summary>
    public event Action<ChartRange>? UsageSeriesRequested;

    /// <summary>Los datos de la gráfica de un rango (hilo de UI).</summary>
    public void SetUsageSeries(ChartRange range, UsageSeriesData data)
    {
        if (_disposed) return;
        _shared.SetUsageSeries(range, data);
    }

    /// <summary>No se pudieron leer los datos de un rango: se volverán a pedir al hacer falta.</summary>
    public void UsageSeriesFailed(ChartRange range)
    {
        if (_disposed) return;
        _shared.UsageSeriesFailed(range);
    }

    private void OnUsageSeriesWanted(ChartRange range)
    {
        if (!_disposed) UsageSeriesRequested?.Invoke(range);
    }

    /// <summary>
    /// El pomodoro guardado en <c>config.json</c> (la bandeja lo pasa al crear el notch, también al rehacerlo): sigue donde iba,
    /// contando el tiempo con la app cerrada. Suscribirse antes a <see cref="FocusStateChanged"/>: lo que acabó mientras tanto
    /// se cierra ya y se pide guardar.
    /// </summary>
    public void RestoreFocus(FocusState? state)
    {
        if (_disposed) return;
        _shared.RestoreFocus(state);
    }

    /// <summary>Hay que guardar el pomodoro (cada acción, el fin de una fase, un cambio de hora). Hilo de UI.</summary>
    public event Action<FocusState>? FocusStateChanged;

    private void OnFocusSaved(FocusState state)
    {
        if (!_disposed) FocusStateChanged?.Invoke(state);
    }

    /// <summary>Modo, ventana y rango de la página Uso guardados en <c>config.json</c> (null = lo de serie): las ventanas, también
    /// las que se creen después, abren la página como se dejó.</summary>
    public void RestoreUsageView(UsageViewState? state)
    {
        if (_disposed) return;
        _shared.RestoreUsageView(state);
    }

    /// <summary>Hay que guardar lo elegido en la página Uso (otro modo, ventana o rango). Hilo de UI; la bandeja lo escribe en
    /// segundo plano.</summary>
    public event Action<UsageViewState>? UsageViewChanged;

    /// <summary>Lo que recuerda ahora la página Uso (la bandeja se lo pasa al notch que rehaga).</summary>
    public UsageViewState UsageView => _shared.UsageView;

    private void OnUsageViewSaved(UsageViewState state)
    {
        if (!_disposed) UsageViewChanged?.Invoke(state);
    }

    /// <summary>El pomodoro compartido (los tests y la bandeja, que al rehacer el notch pasa al nuevo el estado vivo).</summary>
    internal PomodoroTimer Focus => _shared.Focus;

    /// <summary>Abre la tarjeta del Pro en la ventana interactiva («ClaudeBar Pro…» de la bandeja). false si no se pudo (sin
    /// ventana a la vista o sin oferta): entonces la bandeja actúa sin notch.</summary>
    public bool ShowProCard()
    {
        if (_disposed) return false;
        return InteractiveWindow() is { } w && w.OpenProCard(linger: true);
    }

    /// <summary>El aviso del fin de la prueba ya se ve en una ventana (entonces se apunta que salió).</summary>
    public event Action? ProNoticeShown;

    private void OnProNoticeShown(NotchForm w)
    {
        if (!_disposed && IsAlive(w)) ProNoticeShown?.Invoke();
    }

    /// <summary>El único aviso «tu prueba de Pro ha terminado», en la ventana interactiva (aunque esté oculta por pantalla
    /// completa: su cola lo guarda y sale al volver). false si no hay ninguna viva.</summary>
    public bool ShowProNotice()
    {
        if (_disposed || _windows.FirstOrDefault(w => IsAlive(w) && w.Interactive) is not { } w) return false;
        w.ShowProNotice();
        return true;
    }

    /// <summary>La ventana interactiva si está viva y a la vista (la dueña de la ventana de compra de la Store, que se pide
    /// desde su tarjeta); <see cref="IntPtr.Zero"/> si no. Hilo de UI.</summary>
    internal IntPtr PurchaseOwner() =>
        !_disposed && InteractiveWindow() is { IsHandleCreated: true, Visible: true } w ? w.Handle : IntPtr.Zero;
    /// <summary>La ventana con los botones (con una sola, ella), viva y a la vista.</summary>
    private NotchForm? InteractiveWindow() =>
        _windows.FirstOrDefault(w => IsAlive(w) && w.Interactive && !w.HiddenForFullscreen);

    /// <summary>Ajustes, modo e idioma nuevos, en caliente: las ventanas que siguen no se recrean.</summary>
    public void Apply(NotchOptions options, NotchMonitorMode mode, Strings strings)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(strings);
        if (_disposed) return;
        _options = options;
        _strings = strings;
        if (!Enum.IsDefined(mode)) mode = NotchMonitorMode.Fixed;
        if (mode != _mode)
        {
            _mode = mode;
            EnterMode();
        }
        Reconcile();
    }

    // ---------------------------------------------------------------- ventanas

    /// <summary>Lleva las ventanas al modo: las que sobran se cierran, las que faltan se crean y las demás reciben los ajustes
    /// (y su monitor). Luego, quién es la interactiva.</summary>
    private void Reconcile()
    {
        if (_disposed) return;
        // Red de seguridad: una ventana ya muerta no se configura ni cuenta como la de su monitor (OnWindowDisposed la quita).
        foreach (var dead in _windows.Where(w => !IsAlive(w)).ToList()) Detach(dead);
        var connected = Screens();
        if (_mode == NotchMonitorMode.AllMonitors)
        {
            _choice.Retarget(connected, Home(connected));
            var (remove, add) = NotchMonitorPolicy.Reconcile(_windows.Select(w => w.MonitorDevice).ToList(),
                NotchMonitorPolicy.Desired(_mode, connected, ""));
            foreach (var w in _windows.Where(w => NotchMonitorChoice.Contains(remove, w.MonitorDevice)).ToList()) Close(w);
            foreach (var w in _windows)
            {
                w.StrictMonitor = true;
                Configure(w, w.MonitorDevice);
            }
            foreach (var device in add) Create(device, strict: true);
        }
        else
        {
            string target;
            if (_mode == NotchMonitorMode.FollowActive)
            {
                // El monitor que se sigue se desconectó: al de casa, sin animar (la ventana ya cayó al principal).
                bool gone = _choice.Retarget(connected, Home(connected));
                target = _choice.Current;
                if (gone && _windows.Count > 0) _windows[0].MoveToMonitor(target, animate: false);
            }
            else target = _options.MonitorDeviceName ?? "";

            int keep = NotchMonitorPolicy.KeepForSingle(_windows.Select(w => w.MonitorDevice).ToList(), Resolve(target, connected));
            for (int i = _windows.Count - 1; i >= 0; i--)
                if (i != keep) Close(_windows[i]);
            if (_windows.Count == 0) Create(target, strict: false);
            else
            {
                var w = _windows[0];
                w.StrictMonitor = false;
                Configure(w, target);
            }
        }
        UpdateRoles();
    }

    /// <summary>Ajustes (con su monitor) e idioma de una ventana que sigue; lo que no cambia no se vuelve a aplicar. Fuera de
    /// «seguir a la ventana activa», una mudanza a medias se cancela: si acabara, la ventana llegaría a un monitor que ya tiene
    /// la suya (dos interactivas, dos campanitas) o saldría del monitor fijo.</summary>
    private void Configure(NotchForm w, string device)
    {
        if (_mode != NotchMonitorMode.FollowActive) w.CancelMove();
        var o = _options with { MonitorDeviceName = device };
        if (o != w.Options) w.ApplyOptions(o);
        if (!Equals(w.Strings, _strings)) w.Strings = _strings;
    }

    private void Create(string device, bool strict)
    {
        var w = new NotchForm(_hub, _options with { MonitorDeviceName = device }, _strings, _shared) { StrictMonitor = strict };
        // Todos los monitores: no es la interactiva hasta que UpdateRoles lo diga (y entonces lo que ya está a la vista, que ya
        // sonó en otra, no vuelve a sonar).
        if (_mode == NotchMonitorMode.AllMonitors) w.Interactive = false;
        if (_appearSettledOn is { } on && Same(w.MonitorDevice, on)) w.AppearSettled = true;
        w.SetUpdateOffer(_updateOffer);
        w.SetProOffer(_proOffer);
        w.PlaceChanged += OnPlaceChanged;
        w.PromoteRequested += OnPromote;
        w.UpdateClicked += OnUpdateClicked;
        w.ProActionRequested += OnProAction;
        w.ProNoticeShown += OnProNoticeShown;
        w.DisplayChanged += OnDisplayChanged;
        w.FormClosed += OnWindowClosing;
        w.Disposed += OnWindowDisposed;
        _windows.Add(w);
        if (_show) w.Show();
    }

    private void Close(NotchForm w)
    {
        Detach(w);   // antes de cerrarla: así OnWindowDisposed sabe que la cerró el host
        try { w.Close(); w.Dispose(); }
        catch (Exception ex) { AppLog.Warn($"cerrando un notch: {ex.Message}"); }
    }

    /// <summary>Fuera de la lista y sin sus eventos.</summary>
    private void Detach(NotchForm w)
    {
        w.PlaceChanged -= OnPlaceChanged;
        w.PromoteRequested -= OnPromote;
        w.UpdateClicked -= OnUpdateClicked;
        w.ProActionRequested -= OnProAction;
        w.ProNoticeShown -= OnProNoticeShown;
        w.DisplayChanged -= OnDisplayChanged;
        w.FormClosed -= OnWindowClosing;
        w.Disposed -= OnWindowDisposed;
        _windows.Remove(w);
    }

    /// <summary>Cerrar sesión, apagar o salir de la app: lo que se cierre por eso no se recrea.</summary>
    private void OnWindowClosing(object? sender, FormClosedEventArgs e)
    {
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.ApplicationExitCall or CloseReason.TaskManagerClosing)
            _ending = true;
    }

    /// <summary>
    /// Una ventana se cerró sin que la cerrase el host: Alt+F4 con el foco en ella (editando una tarea; el notch no cancela
    /// su cierre). Antes <c>TrayAppContext</c> miraba <c>IsDisposed</c> y la recreaba; con el host nadie se enteraba: en Fijo
    /// o siguiendo a la ventana activa el notch desaparecía hasta reiniciar, y con uno por monitor la muerta podía quedarse
    /// con los botones. Se quita ya y, en el siguiente mensaje (fuera de su Dispose), se vuelve a crear lo que falte y se
    /// reparten los papeles. Se escucha <c>Disposed</c> y no <c>FormClosed</c>: llega por cualquier camino, también si se
    /// libera sin haberse mostrado. Al cerrar la sesión, apagar o salir de la app no se recrea (<see cref="OnWindowClosing"/>).
    /// </summary>
    private void OnWindowDisposed(object? sender, EventArgs e)
    {
        if (sender is not NotchForm w || !_windows.Contains(w)) return;   // la cerró el host: ya no estaba en la lista
        Detach(w);
        if (_disposed || _ending) return;
        AppLog.Info($"notch: la ventana de {w.MonitorDevice} se cerró sola; se vuelve a crear");
        _post(() =>
        {
            if (!_disposed && !_ending) Reconcile();
        });
    }

    private static bool IsAlive(NotchForm w) => !w.IsDisposed && !w.Disposing;

    /// <summary>Quién lleva los botones y suena: con una sola ventana, ella; con una por monitor, el principal efectivo (primero
    /// se apagan las demás: nunca dos interactivas a la vez).</summary>
    private void UpdateRoles()
    {
        if (_disposed) return;
        var alive = _windows.Where(IsAlive).ToArray();   // una muerta nunca se queda con los botones
        if (_mode != NotchMonitorMode.AllMonitors)
        {
            foreach (var w in alive) w.Interactive = true;
            return;
        }
        var slots = alive.Select(w => new NotchSlot(w.MonitorDevice, w.HiddenForFullscreen, w.InUse)).ToList();
        string? primary = NotchMonitorPolicy.EffectivePrimary(slots, _choice.Current, Home(Screens()));
        foreach (var w in alive)
            if (!Same(w.MonitorDevice, primary)) w.Interactive = false;
        foreach (var w in alive)
            if (Same(w.MonitorDevice, primary)) w.Interactive = true;
    }

    /// <summary>Los roles se recalculan en el siguiente mensaje: los avisos llegan desde dentro del Sync de una ventana.</summary>
    private void QueueRoles()
    {
        if (_rolesQueued || _disposed) return;
        _rolesQueued = true;
        _post(() =>
        {
            _rolesQueued = false;
            UpdateRoles();
        });
    }

    private void OnPlaceChanged(NotchForm w)
    {
        if (_disposed) return;
        if (_mode == NotchMonitorMode.FollowActive)
        {
            // Siguió a una ventana que luego pasó a pantalla completa y se ocultó: de vuelta al monitor de antes (a la vista).
            if (NotchMonitorPolicy.StepBackFrom(w.HiddenForFullscreen, _options.HideInFullscreen, _previousFollow,
                    _choice.Current, Screens()) is { } back)
            {
                _post(() =>
                {
                    if (_disposed || _mode != NotchMonitorMode.FollowActive || _windows.Count == 0 || !_windows[0].HiddenForFullscreen) return;
                    _previousFollow = null;
                    _choice.Force(back);
                    _windows[0].MoveToMonitor(back, animate: false);
                });
                return;
            }
            // Lo que esperaba (el notch estaba en uso) se aplica cuando se suelta.
            if (_choice.Pending is null) return;
            _post(() =>
            {
                if (_disposed || _mode != NotchMonitorMode.FollowActive || _windows.Count == 0) return;
                var win = _windows[0];
                string from = _choice.Current;
                if (!win.HoldsPlace && _choice.Release()) MoveFollower(win, from);
            });
        }
        else if (_mode == NotchMonitorMode.AllMonitors)
        {
            if (_choice.Pending is not null && PrimaryWindow() is not { InUse: true }) _choice.Release();
            QueueRoles();   // también cambia con «oculta por pantalla completa»
        }
    }

    /// <summary>Clic en un aviso de solo lectura: ese notch pasa a ser el principal efectivo, al momento.</summary>
    private void OnPromote(NotchForm w)
    {
        if (_disposed || _mode != NotchMonitorMode.AllMonitors) return;
        _choice.Force(w.MonitorDevice);
        QueueRoles();
    }

    private NotchForm? PrimaryWindow() => _windows.FirstOrDefault(w => w.Interactive);

    /// <summary>«Actualizar»: solo cuenta el de la ventana interactiva y viva (nunca dos actualizaciones a la vez).</summary>
    private void OnUpdateClicked(NotchForm w)
    {
        if (_disposed || !IsAlive(w) || !w.Interactive || _updateOffer is not { Ready: true }) return;
        UpdateRequested?.Invoke();
    }

    /// <summary>Un botón de la tarjeta del Pro: solo cuenta el de la ventana interactiva y viva, sin una compra en marcha
    /// (nunca dos compras a la vez).</summary>
    private void OnProAction(NotchForm w, NotchProAction action)
    {
        if (_disposed || !IsAlive(w) || !w.Interactive || _proOffer is not { Busy: false }) return;
        ProActionRequested?.Invoke(action);
    }

    // ---------------------------------------------------------------- ventana activa

    /// <summary>Al entrar en un modo: hooks compartidos solo si se sigue a la ventana activa, y punto de partida (la ventana
    /// activa de ahora si cuenta; si no, el monitor de casa).</summary>
    private void EnterMode()
    {
        bool follow = _mode is NotchMonitorMode.FollowActive or NotchMonitorMode.AllMonitors;
        if (follow && !_hooked)
        {
            var hooks = WinEventHooks.Shared;
            hooks.Foreground += OnForegroundEvent;
            hooks.MoveSize += OnMoveSize;
            hooks.LocationSettled += OnForegroundEvent;
            hooks.Acquire();
            _hooked = true;
        }
        else if (!follow) Unhook();

        _foregroundTimer.Stop();
        _previousFollow = null;
        _debounce.Seed(follow ? FullscreenWatcher.DescribeForeground() : default);
        var connected = Screens();
        string start = _mode == NotchMonitorMode.FollowActive && _windows.Count > 0
            ? _windows[0].MonitorDevice                         // sin saltos al cambiar de modo: se mueve con la próxima ventana
            : _debounce.Settled ?? Home(connected);
        _choice = new NotchMonitorChoice(Resolve(start, connected));
    }

    private void OnMoveSize(bool moving)
    {
        if (!moving) OnForegroundEvent();   // la ventana activa pudo acabar en otro monitor
    }

    private void OnForegroundEvent()
    {
        if (_disposed || !_hooked) return;
        if (_debounce.OnForeground(FullscreenWatcher.DescribeForeground(), _clock.Elapsed)) ArmForegroundTimer();
        else _foregroundTimer.Stop();
    }

    private void ArmForegroundTimer()
    {
        _foregroundTimer.Stop();
        if (_debounce.NextWakeIn(_clock.Elapsed) is not { } d) return;
        _foregroundTimer.Interval = (int)Math.Clamp(Math.Ceiling(d.TotalMilliseconds), 1, int.MaxValue);
        _foregroundTimer.Start();
    }

    private void OnForegroundTimer()
    {
        _foregroundTimer.Stop();
        if (_disposed) return;
        if (_debounce.Tick(_clock.Elapsed) is not { } monitor)
        {
            ArmForegroundTimer();   // el timer saltó un pelo antes de plazo
            return;
        }
        OnActiveMonitor(monitor);
    }

    /// <summary>Se asentó la ventana activa en <paramref name="monitor"/>.</summary>
    private void OnActiveMonitor(string monitor)
    {
        if (!NotchMonitorChoice.Contains(Screens(), monitor)) return;
        if (_mode == NotchMonitorMode.FollowActive && _windows.Count > 0)
        {
            // A un monitor tapado por un juego no: allí no se vería (y nadie contestaría sus permisos).
            if (!NotchMonitorPolicy.FollowsInto(monitor, FullscreenWatcher.FullscreenMonitorDevice(), _options.HideInFullscreen))
                return;
            var w = _windows[0];
            string from = _choice.Current;
            if (_choice.Request(monitor, busy: w.HoldsPlace)) MoveFollower(w, from);
        }
        else if (_mode == NotchMonitorMode.AllMonitors)
        {
            if (_choice.Request(monitor, busy: PrimaryWindow() is { InUse: true })) UpdateRoles();
        }
    }

    /// <summary>Seguir a la ventana activa: se muda al monitor elegido y recuerda de dónde viene.</summary>
    private void MoveFollower(NotchForm w, string from)
    {
        _previousFollow = from;
        w.MoveToMonitor(_choice.Current);
    }

    private void OnDisplayChanged()
    {
        // Fijo: la ventana se recoloca sola (su WM_DISPLAYCHANGE), como siempre.
        if (_disposed || _mode == NotchMonitorMode.Fixed) return;
        _displayTimer.Stop();
        _displayTimer.Start();
    }

    // ---------------------------------------------------------------- monitores

    private static IReadOnlyList<string> Screens()
    {
        try { return Screen.AllScreens.Select(s => s.DeviceName).ToList(); }
        catch (Exception ex)
        {
            AppLog.Warn($"no se pudieron leer los monitores: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>Monitor de casa: el de Fijo si está conectado; si no, el principal.</summary>
    private string Home(IReadOnlyList<string> connected) => Resolve(_options.MonitorDeviceName ?? "", connected);

    /// <summary>Nombre GDI concreto: el pedido si está conectado; "" o desconectado, el principal.</summary>
    private static string Resolve(string device, IReadOnlyList<string> connected)
    {
        if (!string.IsNullOrEmpty(device) && NotchMonitorChoice.Contains(connected, device)) return device;
        try { return Screen.PrimaryScreen?.DeviceName ?? connected.FirstOrDefault() ?? ""; }
        catch (Exception) { return connected.FirstOrDefault() ?? ""; }
    }

    private static bool Same(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- limpieza

    private void Unhook()
    {
        if (!_hooked) return;
        var hooks = WinEventHooks.Shared;
        hooks.Foreground -= OnForegroundEvent;
        hooks.MoveSize -= OnMoveSize;
        hooks.LocationSettled -= OnForegroundEvent;
        hooks.Release();
        _hooked = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Unhook();
        _foregroundTimer.Dispose();
        _displayTimer.Dispose();
        foreach (var w in _windows.ToArray()) Close(w);
        _shared.UsageSeriesWanted -= OnUsageSeriesWanted;
        _shared.FocusSaved -= OnFocusSaved;
        _shared.UsageViewSaved -= OnUsageViewSaved;
        _shared.Dispose();
    }
}
