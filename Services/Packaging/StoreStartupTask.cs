using System.Diagnostics;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace ClaudeBarWin.Services.Packaging;

/// <summary>
/// «Iniciar con Windows» en la edición de la Store: la tarea de inicio del paquete (<c>desktop:StartupTask</c>
/// <see cref="StoreEdition.StartupTaskId"/> en installer/msix/Package.appxmanifest, con <c>Enabled="false"</c>: solo se
/// enciende si el usuario lo elige). La clave Run de la edición de GitHub no sirve aquí: lo que una app empaquetada escribe
/// en HKCU va a una copia privada del paquete. Según la documentación de <c>StartupTask</c>:
/// <list type="bullet">
/// <item>«If RequestEnableAsync is called from a packaged desktop app, no user-consent dialog is shown».</item>
/// <item>«If a user disables a task, you can't programmatically re-enable it»: con <see cref="StartupTaskState.DisabledByUser"/>
/// solo se puede encender desde Configuración › Aplicaciones › Inicio (o el Administrador de tareas).</item>
/// </list>
/// <see cref="State"/> no espera nunca (los ajustes lo consultan al pintar): devuelve el último estado leído y, si ha
/// caducado (<see cref="CacheFor"/>), lo vuelve a leer en segundo plano; si la lectura falla, no reintenta hasta pasado
/// <see cref="RetryAfterFailure"/>. Hasta la primera lectura buena el estado es <b>desconocido</b>, no «apagado»: la bienvenida
/// no aplica nada desde ahí y el interruptor dice «comprobando…». Encender y apagar, que pide el usuario, tampoco esperan:
/// van al pool (<see cref="SingleFlight"/>, uno a la vez), el interruptor dice «cambiando…» y <see cref="Changed"/> avisa al
/// acabar; cada llamada a WinRT tiene su plazo (<see cref="Timeout"/>).
/// </summary>
public static class StoreStartupTask
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    /// <summary>La página de Configuración donde el usuario enciende las apps de inicio.</summary>
    public const string SettingsUri = "ms-settings:startupapps";

    private static readonly object Gate = new();
    private static DateTime _nextRefreshAt = DateTime.MinValue;
    private static StartupTaskState? _cached;
    private static bool _known;   // ¿ha habido ya una lectura buena?
    private static Task? _refreshing;
    private static readonly SingleFlight Change = new();

    /// <summary>Cambió lo que hay que pintar: la primera lectura (u otra que cambie el estado), o un cambio que empieza o
    /// acaba. Llega desde el pool: quien repinte, que pase al hilo de UI.</summary>
    public static event Action? Changed;

    /// <summary>Qué hacer para encenderla según su estado.</summary>
    public enum EnableStep
    {
        /// <summary>Apagada por la app: <c>RequestEnableAsync</c> (sin diálogo en una app de escritorio empaquetada).</summary>
        Request,
        /// <summary>Ya encendida (por la app o por una directiva).</summary>
        AlreadyOn,
        /// <summary>La apagó el usuario: solo él puede encenderla, desde Configuración.</summary>
        OnlyInSettings,
        /// <summary>Apagada por una directiva, o no se pudo leer: nada que hacer.</summary>
        Blocked,
    }

    /// <summary>¿Arranca con Windows? PURO.</summary>
    internal static bool IsOn(StartupTaskState? state) => state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    /// <summary>Qué hace <see cref="Enable"/> con ese estado. PURO.</summary>
    internal static EnableStep NextStep(StartupTaskState? state) => state switch
    {
        StartupTaskState.Disabled => EnableStep.Request,
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => EnableStep.AlreadyOn,
        StartupTaskState.DisabledByUser => EnableStep.OnlyInSettings,
        _ => EnableStep.Blocked,
    };

    /// <summary>Lo que se pinta y lo que se puede aplicar: cambiando, desconocido (aún sin ninguna lectura buena) o el último
    /// estado leído. PURO.</summary>
    internal static StartupState Describe(bool changing, bool known, StartupTaskState? cached) =>
        new(known ? IsOn(cached) : null, changing);

    /// <summary>¿Hace algo el interruptor? Solo con un estado conocido y sin otro cambio en marcha. PURO.</summary>
    internal static bool AcceptsToggle(StartupState state) => state.Applicable is not null;

    /// <summary>Lee el estado en segundo plano (al arrancar la edición de la Store, para que los ajustes ya lo tengan).</summary>
    public static void Prime() => RefreshIfStale();

    /// <summary>El último estado leído (o desconocido, o cambiando); nunca espera (ver la clase).</summary>
    public static StartupState State()
    {
        RefreshIfStale();
        bool changing = Change.Busy;
        lock (Gate) return Describe(changing, _known, _cached);
    }

    /// <summary>La enciende si se puede (la elige el usuario en la bienvenida o en los ajustes). Con
    /// <paramref name="openSettingsIfUserDisabled"/> y la tarea apagada por el usuario, abre Configuración › Inicio. No
    /// espera: el resultado llega con <see cref="Changed"/>.</summary>
    public static void Enable(bool openSettingsIfUserDisabled = false) =>
        Run(task => EnableCoreAsync(task, openSettingsIfUserDisabled), "encender");

    /// <summary>La apaga si está encendida. No espera.</summary>
    public static void Disable() => Run(task => { DisableCore(task); return Task.CompletedTask; }, "apagar");

    /// <summary>El interruptor de los ajustes (lo pulsa el usuario: si la apagó él, se le lleva a Configuración). Decide con el
    /// estado de ahora, no con el de la caché. Con el estado desconocido o con otro cambio en marcha, no hace nada.</summary>
    public static void Toggle()
    {
        var state = State();
        if (!AcceptsToggle(state))
        {
            AppLog.Info($"inicio con Windows (Store): {(state.Changing ? "ya se está cambiando" : "aún no se sabe cómo está")}; el interruptor espera");
            return;
        }
        Run(async task =>
        {
            if (IsOn(task.State)) DisableCore(task);
            else await EnableCoreAsync(task, openSettingsIfUserDisabled: true).ConfigureAwait(false);
        }, "cambiar");
    }

    /// <summary>Espera, con plazo, a que acabe un cambio en marcha (la CLI de <c>--apply-setup</c> antes de salir).</summary>
    public static bool WaitForPendingChange(TimeSpan timeout) => Change.WaitIdle(timeout);

    /// <summary>¿Esta ejecución la lanzó la tarea de inicio? (<c>ActivationKind.StartupTask</c>.) Ante cualquier fallo, no.</summary>
    public static bool LaunchedAtLogon()
    {
        try { return AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask; }
        catch (Exception ex)
        {
            AppLog.Warn($"inicio con Windows (Store): no se pudo saber cómo se abrió la app: {ex.Message}");
            return false;
        }
    }

    private static async Task EnableCoreAsync(StartupTask task, bool openSettingsIfUserDisabled)
    {
        switch (NextStep(task.State))
        {
            case EnableStep.Request:
                try
                {
                    var result = await task.RequestEnableAsync().AsTask().WaitAsync(Timeout).ConfigureAwait(false);
                    AppLog.Info($"inicio con Windows (Store): {result}");
                }
                catch (TimeoutException) { AppLog.Warn("inicio con Windows (Store): la tarea de inicio no respondió"); }
                break;
            case EnableStep.OnlyInSettings:
                AppLog.Info("inicio con Windows (Store): lo apagó el usuario; solo se enciende desde Configuración › Aplicaciones › Inicio");
                if (openSettingsIfUserDisabled) OpenSettings();
                break;
            case EnableStep.Blocked:
                AppLog.Info($"inicio con Windows (Store): no se puede encender ({task.State})");
                break;
        }
    }

    private static void DisableCore(StartupTask task)
    {
        if (IsOn(task.State)) task.Disable();
    }

    /// <summary>Lo que pide el usuario: en el pool (WinRT no se espera en el hilo de UI), sin esperar, uno a la vez; al acabar
    /// deja el estado nuevo en la caché. El interruptor dice «cambiando…» mientras tanto (<see cref="Changed"/>).</summary>
    private static void Run(Func<StartupTask, Task> action, string what)
    {
        bool started = Change.TryStart(async () =>
        {
            var task = await GetAsync().ConfigureAwait(false);
            if (task is null) return;
            await action(task).ConfigureAwait(false);
            Remember(task.State, ok: true);
        }, RaiseChanged);
        if (!started) AppLog.Info($"inicio con Windows (Store): ya hay un cambio en marcha; no se vuelve a {what}");
    }

    private static void RefreshIfStale()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow < _nextRefreshAt || _refreshing is { IsCompleted: false }) return;
            _refreshing = Task.Run(async () =>
            {
                var task = await GetAsync().ConfigureAwait(false);
                StartupTaskState? state = null;
                try { state = task?.State; }
                catch (Exception ex) { AppLog.Warn($"inicio con Windows (Store): no se pudo leer el estado: {ex.Message}"); task = null; }
                Remember(state, ok: task is not null);
            });
        }
    }

    /// <summary>Guarda una lectura (o un fallo) y avisa si cambia lo que se pinta.</summary>
    private static void Remember(StartupTaskState? state, bool ok)
    {
        bool changed;
        lock (Gate)
        {
            var before = Describe(false, _known, _cached);
            if (ok)
            {
                _cached = state;
                _known = true;
            }
            _nextRefreshAt = DateTime.UtcNow + (ok ? CacheFor : RetryAfterFailure);
            changed = before != Describe(false, _known, _cached);
        }
        if (changed) RaiseChanged();
    }

    private static void RaiseChanged()
    {
        try { Changed?.Invoke(); }
        catch (Exception ex) { AppLog.Warn($"inicio con Windows (Store): aviso de cambio: {ex.Message}"); }
    }

    /// <summary>La tarea de inicio del paquete, con plazo; null si no responde o falla. Se llama desde el pool.</summary>
    private static async Task<StartupTask?> GetAsync()
    {
        try { return await StartupTask.GetAsync(StoreEdition.StartupTaskId).AsTask().WaitAsync(Timeout).ConfigureAwait(false); }
        catch (TimeoutException) { AppLog.Warn("inicio con Windows (Store): la tarea de inicio no respondió"); }
        catch (Exception ex) { AppLog.Warn($"inicio con Windows (Store): no se pudo leer la tarea de inicio: {ex.GetBaseException().Message}"); }
        return null;
    }

    private static void OpenSettings()
    {
        try { Process.Start(new ProcessStartInfo(SettingsUri) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Warn($"inicio con Windows (Store): no se pudo abrir Configuración: {ex.Message}"); }
    }
}
