using System.Drawing;
using System.Globalization;
using ClaudeBarWin.Config;

namespace ClaudeBarWin.Services.Notch;

/// <summary>
/// Foto de un monitor para ponerle nombre. La rellena <see cref="Platform.MonitorCatalog"/> con
/// <c>Screen.AllScreens</c> + EDID; los tests la construyen a mano.
/// </summary>
/// <param name="DeviceName">Nombre GDI (<c>\\.\DISPLAY2</c>): lo que se guarda en <c>AppConfig.NotchMonitor</c>.</param>
/// <param name="Bounds">Rect del monitor en el escritorio virtual (el principal empieza en 0,0).</param>
/// <param name="IsPrimary">Es el monitor principal de Windows.</param>
/// <param name="FriendlyName">Nombre del EDID («LG ULTRAGEAR»); null/vacío si la API falla o el monitor no lo da.</param>
public readonly record struct MonitorInfo(string DeviceName, Rectangle Bounds, bool IsPrimary, string? FriendlyName);

/// <summary>Dónde está un monitor respecto al principal.</summary>
public enum MonitorSide { Primary, Left, Right, Above, Below }

/// <summary>
/// Nombre legible de cada monitor: «LG ULTRAGEAR · 2560×1440 · derecha» (o «Monitor 2 · 1920×1080 · izquierda» si no hay
/// nombre del EDID). Dos monitores con el mismo nombre se numeran («DELL U2419H 1», «DELL U2419H 2»). PURA: los monitores y
/// los textos llegan por parámetro.
/// </summary>
public static class MonitorNames
{
    /// <summary>Etiqueta de cada monitor, en el mismo orden que <paramref name="monitors"/>.</summary>
    public static IReadOnlyList<string> Labels(IReadOnlyList<MonitorInfo> monitors, Strings s)
    {
        var primary = PrimaryBounds(monitors);
        var names = new string[monitors.Count];
        for (int i = 0; i < monitors.Count; i++)
        {
            var friendly = monitors[i].FriendlyName?.Trim();
            names[i] = string.IsNullOrEmpty(friendly)
                ? string.Format(s.Culture, s.NotchMonitorFallbackFmt, MonitorNumber(monitors[i].DeviceName, i))
                : friendly;
        }

        // Nombres repetidos (dos monitores iguales): se numeran por orden de aparición.
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var total = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var labels = new string[monitors.Count];
        for (int i = 0; i < monitors.Count; i++)
        {
            var name = names[i];
            if (total[name] > 1)
            {
                seen[name] = seen.TryGetValue(name, out var k) ? k + 1 : 1;
                name = $"{name} {seen[name]}";
            }
            var b = monitors[i].Bounds;
            var side = Side(monitors[i], primary);
            labels[i] = $"{name} · {b.Width}×{b.Height} · {SideText(side, s)}";
        }
        return labels;
    }

    /// <summary>Etiqueta del monitor con ese nombre GDI, o null si no está conectado.</summary>
    public static string? LabelFor(string? deviceName, IReadOnlyList<MonitorInfo> monitors, Strings s)
    {
        int i = IndexOf(deviceName, monitors);
        return i < 0 ? null : Labels(monitors, s)[i];
    }

    /// <summary>Posición del monitor de cuyo nombre GDI coincide (sin distinguir mayúsculas), o -1.</summary>
    public static int IndexOf(string? deviceName, IReadOnlyList<MonitorInfo> monitors)
    {
        if (string.IsNullOrEmpty(deviceName)) return -1;
        for (int i = 0; i < monitors.Count; i++)
            if (string.Equals(monitors[i].DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>
    /// Lado respecto al principal: si no se solapan en horizontal manda el eje X (izquierda/derecha), si no el Y
    /// (arriba/abajo); solapados (no debería pasar) decide el eje con más distancia entre centros.
    /// </summary>
    public static MonitorSide Side(MonitorInfo m, Rectangle primary)
    {
        if (m.IsPrimary) return MonitorSide.Primary;
        var b = m.Bounds;
        if (b.Right <= primary.Left) return MonitorSide.Left;
        if (b.Left >= primary.Right) return MonitorSide.Right;
        if (b.Bottom <= primary.Top) return MonitorSide.Above;
        if (b.Top >= primary.Bottom) return MonitorSide.Below;
        int dx = (b.Left + b.Width / 2) - (primary.Left + primary.Width / 2);
        int dy = (b.Top + b.Height / 2) - (primary.Top + primary.Height / 2);
        if (Math.Abs(dx) >= Math.Abs(dy)) return dx < 0 ? MonitorSide.Left : MonitorSide.Right;
        return dy < 0 ? MonitorSide.Above : MonitorSide.Below;
    }

    private static string SideText(MonitorSide side, Strings s) => side switch
    {
        MonitorSide.Primary => s.NotchMonitorIsPrimary,
        MonitorSide.Left => s.NotchMonitorLeft,
        MonitorSide.Right => s.NotchMonitorRight,
        MonitorSide.Above => s.NotchMonitorAbove,
        _ => s.NotchMonitorBelow,
    };

    /// <summary>El principal; si ninguno viene marcado, el que empieza en 0,0 (o el primero).</summary>
    private static Rectangle PrimaryBounds(IReadOnlyList<MonitorInfo> monitors)
    {
        foreach (var m in monitors) if (m.IsPrimary) return m.Bounds;
        foreach (var m in monitors) if (m.Bounds.Location == Point.Empty) return m.Bounds;
        return monitors.Count > 0 ? monitors[0].Bounds : Rectangle.Empty;
    }

    /// <summary>El número del nombre GDI (<c>\\.\DISPLAY2</c> → 2, el que ya veía el usuario); si no lo tiene, su posición + 1.</summary>
    private static int MonitorNumber(string deviceName, int index)
    {
        int end = deviceName?.Length ?? 0, start = end;
        while (start > 0 && char.IsAsciiDigit(deviceName![start - 1])) start--;
        return start < end && int.TryParse(deviceName.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0
            ? n
            : index + 1;
    }
}

/// <summary>Un elemento del submenú «Monitor del notch»: <see cref="DeviceName"/> "" = seguir al principal.</summary>
public readonly record struct MonitorMenuItem(string DeviceName, string Text, bool Checked);

/// <summary>Un modo del submenú «Monitor del notch» (opción de radio: uno marcado).</summary>
public readonly record struct MonitorModeMenuItem(NotchMonitorMode Mode, string Text, bool Checked);

/// <summary>
/// Modelo del submenú de la bandeja para elegir el monitor del notch (como Nusa, pero sin reiniciar: el notch se mueve
/// en vivo). PURO: la bandeja solo lo pinta y guarda el <see cref="MonitorMenuItem.DeviceName"/> elegido.
/// </summary>
public static class NotchMonitorMenu
{
    /// <summary>Solo con el notch encendido y 2+ monitores (con uno no hay nada que elegir).</summary>
    public static bool IsVisible(bool notchEnabled, int monitorCount) => notchEnabled && monitorCount >= 2;

    /// <summary>
    /// «Principal (seguir al principal)» + un elemento por monitor, con check en el actual. Un monitor guardado que ya no
    /// está conectado marca «Principal», que es donde lo pone el notch de hecho.
    /// </summary>
    public static IReadOnlyList<MonitorMenuItem> Items(string? current, IReadOnlyList<MonitorInfo> monitors, Strings s)
    {
        int sel = MonitorNames.IndexOf(current, monitors);
        var labels = MonitorNames.Labels(monitors, s);
        var items = new List<MonitorMenuItem>(monitors.Count + 1) { new("", s.NotchMonitorFollowPrimary, sel < 0) };
        for (int i = 0; i < monitors.Count; i++)
            items.Add(new MonitorMenuItem(monitors[i].DeviceName, labels[i], i == sel));
        return items;
    }

    /// <summary>Lo que se guarda al elegir un elemento (mismo campo que cicla Ajustes → Notch).</summary>
    public static void Choose(AppConfig config, MonitorMenuItem item) => config.NotchMonitor = item.DeviceName ?? "";

    /// <summary>Los tres modos, arriba del submenú, con el actual marcado (uno desconocido marca Fijo, que es lo que hace).</summary>
    public static IReadOnlyList<MonitorModeMenuItem> Modes(NotchMonitorMode current, Strings s)
    {
        if (!Enum.IsDefined(current)) current = NotchMonitorMode.Fixed;
        return new[]
        {
            new MonitorModeMenuItem(NotchMonitorMode.Fixed, s.NotchMonitorModeFixed, current == NotchMonitorMode.Fixed),
            new MonitorModeMenuItem(NotchMonitorMode.FollowActive, s.NotchMonitorModeFollow, current == NotchMonitorMode.FollowActive),
            new MonitorModeMenuItem(NotchMonitorMode.AllMonitors, s.NotchMonitorModeAll, current == NotchMonitorMode.AllMonitors),
        };
    }

    /// <summary>La lista de monitores solo se puede elegir en Fijo (en los otros modos el monitor lo decide la ventana activa,
    /// o hay uno en cada monitor; el elegido se guarda igual: es el de casa).</summary>
    public static bool MonitorsEnabled(NotchMonitorMode mode) => mode == NotchMonitorMode.Fixed || !Enum.IsDefined(mode);

    /// <summary>Lo que se guarda al elegir un modo (mismo campo que Ajustes → Notch → Monitor).</summary>
    public static void ChooseMode(AppConfig config, NotchMonitorMode mode) =>
        config.NotchMonitorMode = Enum.IsDefined(mode) ? mode : NotchMonitorMode.Fixed;
}
