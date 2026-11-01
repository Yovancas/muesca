using System.Drawing.Drawing2D;

namespace ClaudeBarWin.UI;

/// <summary>
/// Núcleo PURO de la caché de recursos (F3 Task 3): desechables por clave. Nunca libera nada a mitad
/// de un frame —un pincel ya entregado puede seguir en uso—; si se pasa del tope, se vacía entero en
/// <see cref="TrimIfOverflowed"/>, que el dueño llama al terminar de pintar. Así colores animados
/// (alfas del hover, crossfades) no hacen crecer la caché sin límite.
/// </summary>
internal sealed class DisposableCache<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, IDisposable> _items = new();
    private readonly int _maxEntries;

    public DisposableCache(int maxEntries) => _maxEntries = maxEntries;

    public int Count => _items.Count;

    public T Get<T>(TKey key, Func<T> create) where T : class, IDisposable
    {
        if (_items.TryGetValue(key, out var hit)) return (T)hit;
        var made = create();
        _items[key] = made;
        return made;
    }

    /// <summary>Vacía la caché si superó el tope. Solo entre frames.</summary>
    public void TrimIfOverflowed()
    {
        if (_items.Count > _maxEntries) Clear();
    }

    /// <summary>Libera todo (cambio de tema/DPI, cierre).</summary>
    public void Clear()
    {
        foreach (var d in _items.Values) d.Dispose();
        _items.Clear();
    }
}

/// <summary>
/// Pinceles, plumas y fuentes cacheados (F3 Task 3): el pintado creaba y liberaba decenas de objetos
/// GDI+ por frame (y dos <c>new Font</c>), también a ~30 fps con Claude trabajando. Una instancia por
/// formulario; <see cref="Invalidate"/> al cambiar de tema o de DPI. Los objetos devueltos son de la
/// caché: el llamador NO los libera.
/// <para>Los renderers estáticos (cabecera, secciones, mascota, barras) no reciben la caché por
/// parámetro: el formulario la publica como <see cref="Current"/> mientras mide/pinta
/// (<see cref="Use"/>) y ellos piden <see cref="BrushFor"/>/<see cref="PenFor"/>/<see cref="FontFor"/>
/// dentro de un <c>using</c>. Sin caché ambiente (tests, render offline) el préstamo crea el objeto y lo
/// libera al salir del <c>using</c>, exactamente como antes.</para>
/// </summary>
internal sealed class GdiCache : IDisposable
{
    /// <summary>Tope de entradas antes de vaciar entre frames (colores animados).</summary>
    internal const int MaxEntries = 256;

    [ThreadStatic] private static GdiCache? _current;

    private readonly DisposableCache<(int Kind, int Argb, float Size, int Style, string Family)> _items = new(MaxEntries);

    /// <summary>Caché ambiente del hilo de UI durante una medida/pintado (null fuera).</summary>
    public static GdiCache? Current => _current;

    /// <summary>Publica <paramref name="cache"/> como ambiente hasta el Dispose del token (anidable).</summary>
    public static Scope Use(GdiCache cache)
    {
        var prev = _current;
        _current = cache;
        return new Scope(prev);
    }

    public SolidBrush Brush(Color c)
        => _items.Get((0, c.ToArgb(), 0f, 0, ""), () => new SolidBrush(c));

    public Pen Pen(Color c, float width, LineCap cap = LineCap.Flat)
        => _items.Get((1, c.ToArgb(), width, (int)cap, ""),
            () => new Pen(c, width) { StartCap = cap, EndCap = cap });

    public Font Font(string family, float size, FontStyle style)
        => _items.Get((2, 0, size, (int)style, family), () => new Font(family, size, style));

    /// <summary>Libera todo; lo siguiente que se pida se crea de nuevo (tema o DPI cambiados).</summary>
    public void Invalidate() => _items.Clear();

    /// <summary>Fin de frame: vacía si los colores animados desbordaron el tope.</summary>
    public void EndFrame() => _items.TrimIfOverflowed();

    public void Dispose() => _items.Clear();

    public static Lease<SolidBrush> BrushFor(Color c)
        => _current is { } k ? new(k.Brush(c), owned: false) : new(new SolidBrush(c), owned: true);

    public static Lease<Pen> PenFor(Color c, float width = 1f, LineCap cap = LineCap.Flat)
        => _current is { } k ? new(k.Pen(c, width, cap), owned: false)
            : new(new Pen(c, width) { StartCap = cap, EndCap = cap }, owned: true);

    public static Lease<Font> FontFor(string family, float size, FontStyle style)
        => _current is { } k ? new(k.Font(family, size, style), owned: false)
            : new(new Font(family, size, style), owned: true);

    /// <summary>Restaura la caché ambiente anterior.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly GdiCache? _prev;
        internal Scope(GdiCache? prev) => _prev = prev;
        public void Dispose() => _current = _prev;
    }

    /// <summary>Préstamo: libera el objeto solo si no es de la caché. Se convierte solo a su tipo.</summary>
    public readonly struct Lease<T> : IDisposable where T : class, IDisposable
    {
        private readonly bool _owned;
        public T Value { get; }
        internal Lease(T value, bool owned) { Value = value; _owned = owned; }
        public void Dispose() { if (_owned) Value.Dispose(); }
        public static implicit operator T(Lease<T> l) => l.Value;
    }
}
