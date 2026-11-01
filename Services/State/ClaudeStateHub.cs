using ClaudeBarWin.Models;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Services.State;

/// <summary>
/// API única del estado de Claude Code para el notch (y, más adelante, globos e hitos): sesiones vivas,
/// señales de transición (<see cref="SessionTransitionDetector"/>), cuota y cruces de umbral
/// (<see cref="QuotaThresholdTracker"/>) y peticiones de permiso del helper (<see cref="Permissions"/>, F2).
///
/// Sin hilos ni timers propios: el llamador (TrayAppContext) ya está en el hilo de UI y los eventos se
/// disparan síncronos dentro de <see cref="PublishSessions"/> / <see cref="PublishQuota"/> / <see cref="SyncPermissions"/>.
///
/// <para>Permisos: el broker se toca desde el pipe (otro hilo), así que el hub NO se suscribe a su
/// <see cref="PermissionBroker.Changed"/>; quien lo escucha (el notch) llama a <see cref="SyncPermissions"/> en el
/// hilo de UI y el hub traduce la diferencia en <see cref="PermissionPending"/> (con <c>RequestId</c>) y
/// <see cref="PermissionRequestClosed"/>. Al publicar sesiones, la actividad de una sesión (herramienta, Stop, salir
/// de «espera tu OK», sesión cerrada) cancela sus peticiones: ya se contestó en la terminal.</para>
/// </summary>
public sealed class ClaudeStateHub
{
    private readonly SessionTransitionDetector _detector = new();
    private readonly QuotaThresholdTracker _quotaTracker;
    private readonly Dictionary<string, PermissionRequestInfo> _announced = new(StringComparer.Ordinal);

    /// <param name="quotaTracker">Umbrales de cuota; por defecto 80 / 95 % con histéresis de 3 puntos.</param>
    /// <param name="permissions">Peticiones de permiso (las comparte el servidor del pipe); por defecto uno nuevo de 25 s.</param>
    public ClaudeStateHub(QuotaThresholdTracker? quotaTracker = null, PermissionBroker? permissions = null)
    {
        _quotaTracker = quotaTracker ?? new QuotaThresholdTracker(new[] { 80, 95 });
        Permissions = permissions ?? new PermissionBroker(PermissionBroker.DefaultTtl);
    }

    /// <summary>Peticiones de permiso pendientes del helper (F2). El notch las contesta con <see cref="PermissionBroker.Resolve"/>.</summary>
    public PermissionBroker Permissions { get; }

    /// <summary>Umbrales de los avisos de cuota (<c>NotchQuotaThresholds</c>), ascendentes.</summary>
    public IReadOnlyList<int> QuotaThresholds => _quotaTracker.Thresholds;

    /// <summary>Snapshot nuevo de sesiones (se dispara antes que sus <see cref="Signal"/>).</summary>
    public event Action<IReadOnlyList<SessionState>>? SessionsChanged;
    /// <summary>Una transición de sesión (herramienta, terminado, permiso, sesión cerrada).</summary>
    public event Action<ClaudeSignal>? Signal;
    /// <summary>Lectura nueva de cuota (se dispara antes que sus <see cref="QuotaThresholdCrossed"/>).</summary>
    public event Action<RealUsage>? QuotaChanged;
    /// <summary>Una ventana de cuota acaba de cruzar un umbral.</summary>
    public event Action<QuotaCrossing>? QuotaThresholdCrossed;

    /// <summary>Último snapshot publicado (vacío hasta el primero).</summary>
    public IReadOnlyList<SessionState> Sessions { get; private set; } = Array.Empty<SessionState>();
    /// <summary>Última cuota publicada (<c>null</c> hasta la primera).</summary>
    public RealUsage? Quota { get; private set; }

    public void PublishSessions(IReadOnlyList<SessionState> snapshot, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        // El estado se calcula entero antes de avisar: un suscriptor que lea Sessions ya ve el nuevo.
        var signals = _detector.Diff(snapshot, nowUtc);
        // Actividad de la sesión ⇒ su petición ya no espera al notch. PermissionPending NO cuenta: el hook de sesión
        // y el helper de permisos van por pipes distintos y el primero puede llegar después del segundo.
        foreach (var s in signals)
            if (s is ToolActivity or TaskCompleted or PermissionCleared or SessionGone) Permissions.CancelSession(s.SessionId);
        Sessions = snapshot;
        SessionsChanged?.Invoke(snapshot);
        foreach (var s in signals) Signal?.Invoke(s);
        SyncPermissions(nowUtc);
    }

    /// <summary>
    /// Traduce el estado del broker en señales (hilo de UI): petición nueva ⇒ <see cref="PermissionPending"/> con su
    /// <c>RequestId</c>; petición que ya no está ⇒ <see cref="PermissionRequestClosed"/>. Sin cambios, sin señales.
    /// Solo una petición con <see cref="PermissionRequestInfo.PreviewComplete"/> se anuncia (= botones): otra se cierra sin
    /// decisión en el broker (el servidor ya no las registra; esto es la segunda barrera).
    /// </summary>
    public void SyncPermissions(DateTime nowUtc)
    {
        foreach (var p in Permissions.Pending)
            if (!p.PreviewComplete) Permissions.Cancel(p.RequestId);
        var pending = Permissions.Pending;
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in pending) live.Add(p.RequestId);

        var signals = new List<ClaudeSignal>();
        foreach (var (id, p) in _announced.Where(kv => !live.Contains(kv.Key)).ToList())
        {
            _announced.Remove(id);
            signals.Add(new PermissionRequestClosed(p.SessionId, p.ProjectName, nowUtc, id));
        }
        foreach (var p in pending)
            if (_announced.TryAdd(p.RequestId, p))
                signals.Add(new PermissionPending(p.SessionId, p.ProjectName, nowUtc, p.Tool, p.Preview, p.RequestId));
        foreach (var s in signals) Signal?.Invoke(s);
    }

    /// <summary>
    /// Nueva cuota para el notch. Con <paramref name="observe"/> = false (caché del arranque o la última buena mientras la
    /// API devuelve 429) solo se ENSEÑA: no pasa por el tracker de umbrales, cuya gracia de arranque necesita una lectura
    /// real para no avisar de algo que ya estaba por encima.
    /// </summary>
    public void PublishQuota(RealUsage usage, bool observe = true)
    {
        ArgumentNullException.ThrowIfNull(usage);
        var crossings = observe ? _quotaTracker.Observe(usage) : Array.Empty<QuotaCrossing>();
        Quota = usage;
        QuotaChanged?.Invoke(usage);
        foreach (var c in crossings) QuotaThresholdCrossed?.Invoke(c);
    }
}
