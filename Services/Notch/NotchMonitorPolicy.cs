using ClaudeBarWin.Config;

namespace ClaudeBarWin.Services.Notch;

/// <summary>
/// Foto de la ventana en primer plano para decidir a qué monitor va el notch (seguir a la ventana activa) o cuál es el
/// principal efectivo (todos los monitores). La rellena <see cref="FullscreenWatcher.DescribeForeground"/> con Win32; los
/// tests la construyen a mano.
/// </summary>
/// <param name="Handle">HWND (solo se compara: la misma ventana que vuelve no es información nueva).</param>
/// <param name="Monitor">Nombre GDI del monitor donde está la mayor parte (<c>\\.\DISPLAY2</c>); null si ninguno.</param>
/// <param name="ClassName">Clase de la ventana (<c>Shell_TrayWnd</c>, <c>Progman</c>…).</param>
/// <param name="Visible"><c>IsWindowVisible</c>.</param>
/// <param name="Minimized"><c>IsIconic</c>: minimizada no está en ningún monitor.</param>
/// <param name="Cloaked">DWM la tiene oculta (otro escritorio virtual, app UWP suspendida…).</param>
/// <param name="OwnProcess">Es una ventana de ClaudeBar (el panel, un diálogo, el propio notch al editar una tarea).</param>
public readonly record struct ForegroundWindow(IntPtr Handle, string? Monitor, string? ClassName, bool Visible,
    bool Minimized, bool Cloaked, bool OwnProcess);

/// <summary>
/// Qué ventanas activas cuentan para mover el notch. PURA. Las ignoradas no lo mueven: el notch se queda donde estaba.
/// </summary>
public static class ForegroundFilter
{
    /// <summary>
    /// El escritorio y las barras de tareas (las mismas de <see cref="FullscreenWatcher"/>) y superficies de paso del shell:
    /// Alt+Tab y la vista de tareas, Inicio/búsqueda/centro de notificaciones (<c>Windows.UI.Core.CoreWindow</c> de primer
    /// nivel: las apps UWP van dentro de <c>ApplicationFrameWindow</c>), el desbordamiento de la bandeja y la ventana de
    /// transición del primer plano. Abrir Inicio en otro monitor no es «trabajar» allí.
    /// </summary>
    public static readonly IReadOnlySet<string> ShellClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW",
        "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "TaskSwitcherWnd", "ForegroundStaging",
        "Windows.UI.Core.CoreWindow", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    /// <summary>¿Cuenta esta ventana? No: propia, del shell, invisible, minimizada, camuflada o sin monitor.</summary>
    public static bool Counts(ForegroundWindow w) =>
        w.Handle != IntPtr.Zero && !string.IsNullOrEmpty(w.Monitor) && w.Visible && !w.Minimized && !w.Cloaked
        && !w.OwnProcess && !(w.ClassName is { } c && ShellClasses.Contains(c));
}

/// <summary>
/// Debounce de la ventana activa: el monitor solo «se asienta» tras <see cref="Delay"/> sin otra ventana que cuente (Alt+Tab
/// pasando por varias ventanas no pasea el notch). PURO: el instante llega por parámetro (el <c>Stopwatch</c> del host) y no
/// hay timers dentro; quien llama arma un disparo único a <see cref="NextWakeIn"/> y llama a <see cref="Tick"/>.
///
/// <list type="bullet">
/// <item>Las ventanas que no cuentan (<see cref="ForegroundFilter"/>) no cambian nada: ni cancelan ni reinician la espera.</item>
/// <item>Volver a la ventana ya asentada (misma ventana y mismo monitor) cancela lo pendiente y no es un evento nuevo: un
/// clic que hizo principal a otro notch sobrevive a que la ventana activa se mueva un píxel o reciba el foco otra vez.</item>
/// <item>Otra ventana en el mismo monitor SÍ es un evento nuevo (todos los monitores: vuelve a mandar la ventana activa).</item>
/// </list>
/// </summary>
public sealed class ForegroundDebounce
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private (IntPtr Handle, string Monitor)? _settled;
    private (IntPtr Handle, string Monitor)? _pending;
    private TimeSpan _due;

    /// <summary>Monitor de la última ventana asentada (null hasta la primera).</summary>
    public string? Settled => _settled?.Monitor;

    /// <summary>¿Hay una ventana esperando a asentarse?</summary>
    public bool IsPending => _pending is not null;

    /// <summary>Punto de partida sin esperar (al entrar en el modo): la ventana activa de ahora si cuenta; si no (el panel de
    /// Ajustes de ClaudeBar, el escritorio), ninguna, y quien llama usa su monitor de casa.</summary>
    public void Seed(ForegroundWindow w)
    {
        _pending = null;
        _settled = ForegroundFilter.Counts(w) ? (w.Handle, w.Monitor!) : null;
    }

    /// <summary>Cambió la ventana activa (o se movió). Devuelve si queda algo pendiente (armar el disparo).</summary>
    public bool OnForeground(ForegroundWindow w, TimeSpan now)
    {
        if (!ForegroundFilter.Counts(w)) return _pending is not null;
        var key = (w.Handle, w.Monitor!);
        if (_settled is { } s && s.Handle == key.Handle && Same(s.Monitor, key.Item2))
        {
            _pending = null;
            return false;
        }
        _pending = key;
        _due = now + Delay;
        return true;
    }

    /// <summary>Hasta que se asiente lo pendiente (0 si ya toca); null si no hay nada.</summary>
    public TimeSpan? NextWakeIn(TimeSpan now) =>
        _pending is null ? null : _due > now ? _due - now : TimeSpan.Zero;

    /// <summary>Si ya pasó la espera, asienta lo pendiente y devuelve su monitor; si no, null.</summary>
    public string? Tick(TimeSpan now)
    {
        if (_pending is not { } p || now < _due) return null;
        _pending = null;
        _settled = p;
        return p.Monitor;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Un monitor elegido que no cambia mientras «se está usando»: el del notch que sigue a la ventana activa y el principal
/// efectivo de todos los monitores. PURO. Pedir otro monitor con el notch ocupado lo deja <see cref="Pending"/> (gana el
/// último pedido) y se aplica al soltarse (<see cref="Release"/>); un clic (<see cref="Force"/>) manda al momento.
/// </summary>
public sealed class NotchMonitorChoice
{
    public NotchMonitorChoice(string current) => Current = current ?? "";

    /// <summary>Monitor elegido (nombre GDI; "" = el principal).</summary>
    public string Current { get; private set; }

    /// <summary>Monitor pedido mientras estaba ocupado; null si nada espera.</summary>
    public string? Pending { get; private set; }

    /// <summary>Pide <paramref name="monitor"/>. Devuelve si <see cref="Current"/> cambia YA (con <paramref name="busy"/>,
    /// espera; pedir el actual cancela lo que esperara).</summary>
    public bool Request(string monitor, bool busy)
    {
        if (Same(monitor, Current))
        {
            Pending = null;
            return false;
        }
        if (busy)
        {
            Pending = monitor;
            return false;
        }
        Pending = null;
        Current = monitor;
        return true;
    }

    /// <summary>Dejó de estar ocupado: si algo esperaba, se aplica. Devuelve si <see cref="Current"/> cambió.</summary>
    public bool Release()
    {
        if (Pending is not { } p) return false;
        Pending = null;
        if (Same(p, Current)) return false;
        Current = p;
        return true;
    }

    /// <summary>Elección explícita (clic en un notch): al momento, aunque algo esperara. Devuelve si cambió.</summary>
    public bool Force(string monitor)
    {
        Pending = null;
        if (Same(monitor, Current)) return false;
        Current = monitor;
        return true;
    }

    /// <summary>Cambiaron los monitores conectados: lo pendiente de uno que ya no está se olvida y, si el elegido se fue, pasa a
    /// <paramref name="fallback"/> sin esperar (su sitio ya no existe). Devuelve si <see cref="Current"/> cambió.</summary>
    public bool Retarget(IReadOnlyCollection<string> connected, string fallback)
    {
        if (Pending is { } p && !Contains(connected, p)) Pending = null;
        if (Current.Length == 0 || Contains(connected, Current)) return false;
        Current = fallback ?? "";
        return true;
    }

    internal static bool Contains(IEnumerable<string> list, string device) =>
        list.Any(d => string.Equals(d, device, StringComparison.OrdinalIgnoreCase));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Una ventana del notch vista por el host: su monitor, si está oculta por pantalla completa y si se está usando
/// (puntero encima, vista previa o expandido).</summary>
public readonly record struct NotchSlot(string Monitor, bool Hidden, bool InUse);

/// <summary>
/// Reglas del modo de monitor del notch. PURAS: el host (Win32) solo junta los datos y aplica lo que dicen.
/// </summary>
public static class NotchMonitorPolicy
{
    /// <summary>
    /// Seguir a la ventana activa: el notch NO se muda mientras está expandido o en vista previa, con el puntero encima o con
    /// un Permiso o un Recordatorio a la vista (espera a que acabe; ir con el ratón a un botón no debe llevárselo).
    /// </summary>
    public static bool HoldsPlace(NotchMode mode, bool pointerInside) =>
        pointerInside || mode is NotchMode.Expanded or NotchMode.Peek or NotchMode.Permission or NotchMode.Reminder;

    /// <summary>
    /// Seguir a la ventana activa: no se muda a un monitor que está a pantalla completa (un juego) si el notch se oculta en ella:
    /// allí no lo vería nadie y, siendo la única ventana, el broker rechazaría los permisos que en el otro monitor se pueden
    /// contestar. Se queda donde está, a la vista. <paramref name="fullscreenMonitor"/>: el de
    /// <see cref="FullscreenPolicy.FullscreenMonitor"/> (null si ninguno).
    /// </summary>
    public static bool FollowsInto(string monitor, string? fullscreenMonitor, bool hideInFullscreen) =>
        !(hideInFullscreen && fullscreenMonitor is not null
          && string.Equals(monitor, fullscreenMonitor, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Seguir a la ventana activa: el notch siguió a una ventana que DESPUÉS pasó a pantalla completa (el juego arranca en
    /// ventana) y se ocultó. Vuelve al monitor de antes (si sigue conectado y es otro), sin animar: está oculto. Null = se
    /// queda (un solo monitor, o no se oculta en pantalla completa).
    /// </summary>
    public static string? StepBackFrom(bool hiddenForFullscreen, bool hideInFullscreen, string? previous, string current,
        IReadOnlyCollection<string> connected) =>
        hiddenForFullscreen && hideInFullscreen && !string.IsNullOrEmpty(previous)
        && !string.Equals(previous, current, StringComparison.OrdinalIgnoreCase) && NotchMonitorChoice.Contains(connected, previous)
            ? previous : null;

    /// <summary>
    /// Todos los monitores: el principal efectivo no cambia mientras se usa (puntero encima, vista previa o expandido). Un aviso
    /// a la vista no lo retiene: se ve en todos, y sus botones van con la ventana en la que se trabaja.
    /// </summary>
    public static bool InUse(NotchMode mode, bool pointerInside) =>
        pointerInside || mode is NotchMode.Expanded or NotchMode.Peek;

    /// <summary>
    /// Principal efectivo (el ÚNICO notch con botones en el Permiso y el Recordatorio, y el único que suena): el elegido (por la
    /// ventana activa o un clic) si tiene ventana y se ve; si no, el de casa (<paramref name="home"/>, el monitor de Fijo o el
    /// principal); si no, el primero que se vea. Con todos ocultos (pantalla completa en todos), ninguno: nadie lo vería.
    /// </summary>
    public static string? EffectivePrimary(IReadOnlyList<NotchSlot> slots, string? chosen, string? home)
    {
        foreach (var want in new[] { chosen, home })
        {
            if (string.IsNullOrEmpty(want)) continue;
            foreach (var s in slots)
                if (!s.Hidden && string.Equals(s.Monitor, want, StringComparison.OrdinalIgnoreCase)) return s.Monitor;
        }
        foreach (var s in slots)
            if (!s.Hidden) return s.Monitor;
        return null;
    }

    /// <summary>¿Qué monitores llevan ventana? Todos los monitores: uno por monitor conectado, en su orden; los otros dos
    /// modos: una sola, en <paramref name="single"/>.</summary>
    public static IReadOnlyList<string> Desired(NotchMonitorMode mode, IReadOnlyList<string> connected, string single) =>
        mode == NotchMonitorMode.AllMonitors
            ? connected.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : new[] { single ?? "" };

    /// <summary>
    /// Altas y bajas de ventanas (todos los monitores, al conectar o desconectar uno): qué ventanas sobran (su monitor ya no
    /// se quiere) y qué monitores necesitan una nueva. Las que siguen no se tocan (ni se recrean ni pierden sus avisos).
    /// </summary>
    public static (IReadOnlyList<string> Remove, IReadOnlyList<string> Add) Reconcile(IReadOnlyList<string> existing,
        IReadOnlyList<string> desired)
    {
        var remove = existing.Where(e => !NotchMonitorChoice.Contains(desired, e)).ToList();
        var add = desired.Where(d => !NotchMonitorChoice.Contains(existing, d)).ToList();
        return (remove, add);
    }

    /// <summary>
    /// Para los modos de UNA ventana: de las que hay, cuál se queda (la que ya está en <paramref name="target"/>, o la primera;
    /// -1 si no hay ninguna). El resto se cierra. Así, al volver de «todos los monitores» no se recrea nada.
    /// </summary>
    public static int KeepForSingle(IReadOnlyList<string> existing, string target)
    {
        for (int i = 0; i < existing.Count; i++)
            if (string.Equals(existing[i], target, StringComparison.OrdinalIgnoreCase)) return i;
        return existing.Count > 0 ? 0 : -1;
    }
}

/// <summary>
/// Reparto de los avisos con botones entre las ventanas de todos los monitores. PURO. En el principal efectivo el Permiso y
/// el Recordatorio llevan sus botones y suenan; en los demás salen en solo lectura («Espera tu OK · proyecto», sin botones) y
/// callan. Con una sola ventana (fijo o siguiendo a la ventana activa) esa es siempre la interactiva: como siempre.
/// </summary>
public static class NotchAlertRouting
{
    /// <summary>¿Es un aviso que se contesta con botones? (Permiso del helper, Recordatorio o el fin de fase del pomodoro.)</summary>
    public static bool HasButtons(NotchAlert? alert) =>
        alert is { Kind: NotchAlertKind.Reminder or NotchAlertKind.Focus } or { Kind: NotchAlertKind.Permission, RequestId: not null };

    /// <summary>¿Lleva botones aquí? Solo en la ventana interactiva.</summary>
    public static bool ButtonsHere(NotchAlert? alert, bool interactive) => interactive && HasButtons(alert);

    /// <summary>¿Se enseña aquí en solo lectura un aviso que en el principal lleva botones? (clic ⇒ este pasa a principal).</summary>
    public static bool ReadOnlyHere(NotchAlert? alert, bool interactive) => !interactive && HasButtons(alert);

    /// <summary>¿Puede sonar aquí? Solo la interactiva: nunca dos campanitas por el mismo aviso. El resto de la decisión
    /// (ajuste, pantalla completa, tipo de aviso) es de <see cref="NotchSound.ShouldPlay(NotchAlert, bool, bool)"/>.</summary>
    public static bool MayChime(bool interactive) => interactive;

    /// <summary>¿Cuenta como pantalla completa para sonar? La de su monitor o la del de cualquier otra ventana del notch: con
    /// uno por monitor, un juego en el monitor 1 oculta ese notch y los botones pasan al del 2, y antes este sonaba en plena
    /// partida (con una sola ventana, en ese caso no sonaba nunca).</summary>
    public static bool QuietForFullscreen(bool here, bool anyWindow) => here || anyWindow;
}
