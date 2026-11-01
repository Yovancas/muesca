namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Páginas del notch expandido (Uso y Foco, al final del enum para no cambiar los valores de las demás).</summary>
public enum NotchPage { Claude, Music, Tasks, Shelf, Usage, Focus }

/// <summary>
/// Carrusel de páginas del expandido, movido por la rueda o por clic en los puntos (<see cref="GoTo"/>).
/// PURO: el instante llega por parámetro.
///
/// <list type="bullet">
/// <item>Acumula <c>delta / 120</c> (una muesca = 1; un trackpad manda trozos pequeños) y cambia de página
/// cuando |acumulado| ≥ <see cref="Threshold"/>. Rueda hacia abajo (delta &lt; 0) → siguiente.</item>
/// <item><b>Una página por gesto</b>: tras cambiar, se ignora la rueda mientras siga llegando con pausas de
/// ≤ <see cref="GestureGap"/> (180 ms) entre eventos; la rueda libre no salta varias páginas.</item>
/// <item>Una pausa &gt; <see cref="GestureGap"/> empieza un gesto nuevo con el acumulador a cero.</item>
/// <item>Envuelve: después de la última viene la primera, y al revés.</item>
/// </list>
/// </summary>
public sealed class PageCarousel
{
    public const double Threshold = 0.9;
    public const int WheelDelta = 120;
    public static readonly TimeSpan GestureGap = TimeSpan.FromMilliseconds(180);

    private readonly NotchPage[] _pages;
    private double _acc;
    private DateTime? _lastWheel;
    private bool _consumed;   // este gesto ya cambió de página

    public PageCarousel(IReadOnlyList<NotchPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (pages.Count == 0) throw new ArgumentException("Hace falta al menos una página.", nameof(pages));
        if (pages.Distinct().Count() != pages.Count) throw new ArgumentException("Páginas repetidas.", nameof(pages));
        _pages = pages.ToArray();
    }

    public int Index { get; private set; }

    public NotchPage Current => _pages[Index];

    /// <summary>Un evento de rueda (<paramref name="delta"/> de WM_MOUSEWHEEL). Devuelve <c>true</c> si cambió de página.</summary>
    public bool OnWheel(int delta, DateTime now)
    {
        bool sameGesture = _lastWheel is { } last && now >= last && now - last <= GestureGap;
        _lastWheel = now;
        if (!sameGesture)
        {
            _acc = 0;
            _consumed = false;
        }
        if (_consumed || _pages.Length < 2) return false;

        _acc += (double)delta / WheelDelta;
        if (Math.Abs(_acc) < Threshold) return false;

        int step = _acc < 0 ? 1 : -1;
        Index = (Index + step + _pages.Length) % _pages.Length;
        _acc = 0;
        _consumed = true;
        return true;
    }

    /// <summary>Salta a <paramref name="p"/> (clic en su punto) y descarta la rueda pendiente.</summary>
    public void GoTo(NotchPage p)
    {
        int i = Array.IndexOf(_pages, p);
        if (i < 0) throw new ArgumentException($"La página {p} no está en el carrusel.", nameof(p));
        Index = i;
        _acc = 0;
        _lastWheel = null;
        _consumed = false;
    }
}
