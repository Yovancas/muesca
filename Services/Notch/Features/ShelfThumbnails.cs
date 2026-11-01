using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Clave de una miniatura de la bandeja (F5 · B4): ruta (sin distinguir mayúsculas, como Windows), tamaño en px FÍSICOS y
/// fecha de modificación (ticks UTC). Si el archivo cambia, cambia la clave y la imagen vieja ya no vale; a otra escala,
/// otra imagen. PURA.
/// </summary>
public readonly record struct ShelfThumbKey(string Path, int SizePx, long Stamp)
{
    public static ShelfThumbKey For(string path, int sizePx, DateTime modifiedUtc) =>
        new(path, sizePx, (modifiedUtc.Kind == DateTimeKind.Local ? modifiedUtc.ToUniversalTime() : modifiedUtc).Ticks);

    public bool Equals(ShelfThumbKey other) => SizePx == other.SizePx && Stamp == other.Stamp
        && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Path ?? ""), SizePx, Stamp);
}

/// <summary>
/// Caché LRU de tamaño fijo para valores que hay que liberar. Al pasarse de <see cref="Capacity"/> sale la menos usada y se
/// libera; sustituir o quitar una entrada también libera su valor (salvo que sea el mismo objeto). <see cref="TryGet"/> la
/// pone la primera; <see cref="Peek"/> no cambia el orden. No es segura entre hilos. PURA.
/// </summary>
public sealed class LruCache<TKey, TValue> : IDisposable where TKey : notnull where TValue : class, IDisposable
{
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new();   // la primera, la más reciente

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(comparer);
    }

    public int Capacity { get; }

    public int Count => _map.Count;

    /// <summary>Claves de la más reciente a la menos.</summary>
    public IEnumerable<TKey> Keys => _order.Select(e => e.Key);

    public bool Contains(TKey key) => _map.ContainsKey(key);

    /// <summary>El valor sin tocar el orden; <c>null</c> si no está.</summary>
    public TValue? Peek(TKey key) => _map.TryGetValue(key, out var node) ? node.Value.Value : null;

    /// <summary>El valor, y la entrada pasa a ser la más reciente.</summary>
    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (!_map.TryGetValue(key, out var node))
        {
            value = null;
            return false;
        }
        Promote(node);
        value = node.Value.Value;
        return true;
    }

    /// <summary>Añade o sustituye (el valor anterior se libera) y saca, liberándolas, las menos usadas que sobren.</summary>
    public void Add(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_map.TryGetValue(key, out var node))
        {
            var old = node.Value.Value;
            node.Value = new KeyValuePair<TKey, TValue>(key, value);
            Promote(node);
            if (!ReferenceEquals(old, value)) old.Dispose();
            return;
        }
        _map[key] = _order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
        while (_map.Count > Capacity && _order.Last is { } last)
        {
            _order.RemoveLast();
            _map.Remove(last.Value.Key);
            last.Value.Value.Dispose();
        }
    }

    /// <summary>Quita la entrada y libera su valor. <c>false</c> si no estaba.</summary>
    public bool Remove(TKey key)
    {
        if (!_map.Remove(key, out var node)) return false;
        _order.Remove(node);
        node.Value.Value.Dispose();
        return true;
    }

    /// <summary>Vacía la caché liberando todo.</summary>
    public void Clear()
    {
        foreach (var e in _order) e.Value.Dispose();
        _order.Clear();
        _map.Clear();
    }

    public void Dispose() => Clear();

    private void Promote(LinkedListNode<KeyValuePair<TKey, TValue>> node)
    {
        if (node == _order.First) return;
        _order.Remove(node);
        _order.AddFirst(node);
    }
}

/// <summary>Qué hay en una ruta según el hilo de fondo.</summary>
public enum ShelfProbeState { Missing, File, Folder, Opaque }

/// <summary>
/// Lo que el hilo de fondo averigua de una ruta: si sigue ahí, si es archivo o carpeta, su tamaño y su fecha de
/// modificación. <see cref="ShelfProbeState.Opaque"/>: unidad que no se toca (red o sin montar,
/// <see cref="ShelfThumbPolicy.Touches"/>): ni tamaño ni miniatura, y se da por disponible.
/// </summary>
public readonly record struct ShelfFileProbe(ShelfProbeState State, long Length = 0, DateTime ModifiedUtc = default)
{
    public static readonly ShelfFileProbe Missing = new(ShelfProbeState.Missing);
    public static readonly ShelfFileProbe Opaque = new(ShelfProbeState.Opaque);

    public bool Exists => State is ShelfProbeState.File or ShelfProbeState.Folder;
}

/// <summary>
/// Lo que <see cref="ShelfThumbnails"/> pide al sistema, SIEMPRE desde el hilo de fondo (doble en los tests): mirar la ruta y
/// pedir su imagen de <paramref name="sizePx"/> px como mucho. Nada lanza: un fallo es <see cref="ShelfFileProbe.Missing"/>
/// o <c>null</c> (la página pinta entonces el icono genérico).
/// </summary>
public interface IShelfThumbnailSource
{
    ShelfFileProbe Probe(string path);

    Bitmap? Extract(string path, int sizePx);
}

/// <summary>Icono dibujado de una tarjeta mientras no hay miniatura (o si no la habrá).</summary>
public enum ShelfGlyph { File, Folder, Link, Text }

/// <summary>
/// Qué entradas de la bandeja se miran en segundo plano y a cuáles se les pide la imagen al shell (F5 · B4). PURA.
/// <list type="bullet">
/// <item>Enlaces y textos guardados: nunca se tocan; llevan su glifo.</item>
/// <item>Archivos y carpetas: solo rutas absolutas locales (ni red ni dispositivo, como <see cref="ShelfStore"/>, ni flujos
/// alternativos de NTFS).</item>
/// <item>Accesos directos e internet (<c>.url</c>, <c>.website</c>, <c>.lnk</c>, <c>.library-ms</c>,
/// <c>.searchconnector-ms</c>, <c>.scf</c>) y temas (<c>.theme</c>, <c>.themepack</c>, <c>.deskthemepack</c>, CVE-2024-21320):
/// su icono o su vista previa puede apuntar a otra ruta (<c>IconFile=\\host\…</c>) y pedírsela al shell haría que Windows
/// se autenticara por NTLM contra ese host al pintar la bandeja. Se mira el archivo (tamaño) pero llevan glifo propio:
/// los de internet, el de enlace. Lo mismo una carpeta cuyo <c>desktop.ini</c> saca el icono de otra máquina o lo deja a
/// un controlador COM (<see cref="UnsafeFolderIcon"/>).</item>
/// </list>
/// </summary>
public static class ShelfThumbPolicy
{
    private static readonly HashSet<string> NoShellImage = new(StringComparer.OrdinalIgnoreCase)
    {
        ".url", ".website", ".lnk", ".library-ms", ".searchconnector-ms", ".scf", ".theme", ".themepack", ".deskthemepack",
    };

    // Claves de desktop.ini que apuntan a un archivo de iconos o de imagen.
    private static readonly HashSet<string> FolderIconKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "IconResource", "IconFile", "Logo", "IconArea_Image",
    };

    // Claves de desktop.ini que dejan la carpeta (y su icono) a un controlador COM: haga lo que haga, no se le pide.
    private static readonly HashSet<string> FolderHandlerKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "CLSID", "CLSID2", "UICLSID",
    };

    private static readonly HashSet<string> InternetShortcuts = new(StringComparer.OrdinalIgnoreCase) { ".url", ".website" };

    public static ShelfGlyph GlyphFor(ShelfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Kind switch
        {
            ShelfKind.Folder => ShelfGlyph.Folder,
            ShelfKind.Link => ShelfGlyph.Link,
            ShelfKind.Text => ShelfGlyph.Text,
            _ => InternetShortcuts.Contains(ShelfOpen.EffectiveExtension(item.Value) ?? "") ? ShelfGlyph.Link : ShelfGlyph.File,
        };
    }

    /// <summary>¿Se mira en segundo plano (existencia, tamaño, fecha)? Archivos y carpetas con ruta absoluta local.</summary>
    public static bool Probes(ShelfItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Kind is not (ShelfKind.File or ShelfKind.Folder)) return false;
        var p = item.Value;
        if (string.IsNullOrWhiteSpace(p) || ShelfStore.IsUncOrDevicePath(p)) return false;
        try
        {
            // Un «:» después de la letra de unidad es un flujo alternativo (informe.pdf:x): no es un archivo que enseñar.
            return Path.IsPathFullyQualified(p) && p.IndexOf(':', 2) < 0;
        }
        catch (ArgumentException) { return false; }
    }

    /// <summary>¿Se pide su imagen al shell, según lo que HAY en <paramref name="path"/> (<paramref name="state"/>, recién
    /// mirado) y no el tipo con que entró en la bandeja? Una carpeta sí (su <c>desktop.ini</c> lo mira la fuente justo antes);
    /// un archivo, salvo los tipos con glifo propio: un <c>.url</c> donde antes había una carpeta tampoco pasa.</summary>
    public static bool WantsShellImage(string path, ShelfProbeState state) => state switch
    {
        ShelfProbeState.Folder => true,
        ShelfProbeState.File => !NoShellImage.Contains(ShelfOpen.EffectiveExtension(path) ?? ""),
        _ => false,
    };

    /// <summary>
    /// ¿El <c>desktop.ini</c> de una carpeta (su texto) hace que pedir su icono al shell no sea seguro? Lo es si saca el icono o
    /// la imagen de otra máquina (<c>IconResource=\\host\…</c>, <c>\\?\…</c>, <c>file://host/…</c>), también tras expandir
    /// las variables de entorno con <paramref name="expand"/> (<c>%LOGONSERVER%\a.ico</c>; por defecto
    /// <see cref="Environment.ExpandEnvironmentVariables(string)"/>), o si deja la carpeta a un controlador COM
    /// (<c>CLSID</c>, <c>CLSID2</c>, <c>UICLSID</c>). Entonces lleva el glifo: pedírsela al shell podría autenticar por NTLM
    /// contra ese host. Las rutas locales (<c>%SystemRoot%\…</c>, las de las carpetas especiales) valen.
    /// </summary>
    public static bool UnsafeFolderIcon(string? desktopIni, Func<string, string>? expand = null)
    {
        if (string.IsNullOrEmpty(desktopIni)) return false;
        expand ??= Environment.ExpandEnvironmentVariables;
        foreach (var raw in desktopIni.Split('\n'))
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            if (FolderHandlerKeys.Contains(key)) return true;
            if (!FolderIconKeys.Contains(key)) continue;
            var value = line[(eq + 1)..].Trim().Trim('"').Trim();
            if (RemoteValue(value) || (value.Contains('%') && RemoteValue(SafeExpand(expand, value)))) return true;
        }
        return false;
    }

    private static bool RemoteValue(string value) =>
        ShelfStore.IsUncOrDevicePath(value) || value.Contains("://", StringComparison.Ordinal)
        || value.StartsWith("file:", StringComparison.OrdinalIgnoreCase);

    private static string SafeExpand(Func<string, string> expand, string value)
    {
        try { return (expand(value) ?? value).Trim().Trim('"').Trim(); }
        catch (ArgumentException) { return value; }
    }

    /// <summary>¿El hilo de fondo toca una unidad de este tipo? Las de red (SMB: NTLM y cuelgues de decenas de segundos que
    /// retendrían la cola) y las letras sin montar, no. Extraíbles y CD sí: es hardware local, como mucho tarda en despertar
    /// (en el hilo de fondo; el de UI sigue sin tocarlas, <see cref="ShelfDefaults.UntouchedDrive"/>).</summary>
    public static bool Touches(DriveType type) => type is not (DriveType.Network or DriveType.NoRootDirectory);
}

/// <summary>
/// Miniaturas e información (existencia, tamaño) de las entradas de la bandeja (F5 · B4).
/// <list type="bullet">
/// <item>Lo caro va SIEMPRE en segundo plano (<c>background</c>): mirar el archivo y pedir la imagen al shell. El trabajo de
/// fondo no toca el estado de aquí: recibe los datos, y el resultado vuelve al hilo de UI con <c>post</c> (el
/// <c>BeginInvoke</c> de la ventana), que lo guarda y avisa con <see cref="Changed"/> para repintar.</item>
/// <item>Caché LRU de <see cref="Capacity"/> imágenes por <see cref="ShelfThumbKey"/> (ruta, px físicos, fecha de
/// modificación): si el archivo no cambió, volver a mirarlo no vuelve a pedir la imagen.</item>
/// <item>Al pintar solo se lee (<see cref="Get"/> y <see cref="Probe"/>): sin E/S ni bitmaps nuevos.</item>
/// <item>Archivo que ya no está: sin imagen (la página pinta el icono genérico) y <see cref="ShelfProbeState.Missing"/>.</item>
/// <item>Una petición en vuelo caduca a los <see cref="PendingTimeout"/> (el trabajo se colgó o su resultado no pudo volver):
/// la siguiente <see cref="Request"/> la repite una vez; si vuelve a caducar, esa ruta ya no se pide (glifo). Cada petición
/// lleva su generación y solo cuenta el resultado de la vigente: lo que devuelva tarde un trabajo abandonado se descarta.</item>
/// </list>
/// Hilo de UI. Los <see cref="Bitmap"/> de <see cref="Get"/> los libera la caché dentro de <see cref="Request"/>, al llegar
/// un resultado (antes de <see cref="Changed"/>) y en <see cref="Dispose"/>: quien los guarde los vuelve a pedir tras cada uno.
/// </summary>
public sealed class ShelfThumbnails : IDisposable
{
    public const int Capacity = 64;

    /// <summary>Lo que puede tardar una petición en volver antes de darla por perdida (algo más que el vigilante del hilo de
    /// fondo, que a los 10 s sigue en otro hilo).</summary>
    public static readonly TimeSpan PendingTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Intentos por ruta y tamaño cuando caducan: tras el segundo, no se insiste.</summary>
    public const int MaxAttempts = 2;

    private readonly IShelfThumbnailSource _source;
    private readonly Action<Action> _background;
    private readonly Action<Action> _post;
    private readonly LruCache<ShelfThumbKey, Bitmap> _cache;
    private readonly Dictionary<Slot, ShelfThumbKey> _current = new();   // última imagen buena de cada ruta y tamaño
    private readonly Dictionary<string, ShelfFileProbe> _probes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Slot, InFlight> _pending = new();
    private readonly Func<DateTime> _clock;
    private long _generation;
    private bool _disposed;

    /// <param name="source">Disco y shell (llamados solo desde <paramref name="background"/>).</param>
    /// <param name="background">Encola trabajo en el hilo de fondo.</param>
    /// <param name="post">Lleva el resultado al hilo de UI; si lanza (ventana cerrándose), la imagen se libera.</param>
    /// <param name="capacity">Tope de la caché.</param>
    /// <param name="clock">Reloj (UTC) de la caducidad de lo que va en vuelo; por defecto, <see cref="DateTime.UtcNow"/>.</param>
    public ShelfThumbnails(IShelfThumbnailSource source, Action<Action> background, Action<Action> post, int capacity = Capacity,
        Func<DateTime>? clock = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _background = background ?? throw new ArgumentNullException(nameof(background));
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _cache = new LruCache<ShelfThumbKey, Bitmap>(capacity);
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>Llegó el resultado de la ruta que se pasa (imagen, tamaño o «ya no está»): hay que repintar. Lo de
    /// <see cref="Probe"/> para OTRAS rutas puede ser de una vuelta anterior; el de esta acaba de mirarse. Hilo de UI.</summary>
    public event Action<string>? Changed;

    /// <summary>Peticiones en vuelo, también las que caducaron sin vuelta (tests).</summary>
    public int Pending => _pending.Count;

    /// <summary>Imágenes en la caché (tests).</summary>
    public int Cached => _cache.Count;

    /// <summary>
    /// Mira en segundo plano las entradas de <paramref name="items"/> que lo admiten (<see cref="ShelfThumbPolicy.Probes"/>) y
    /// pide la imagen de <paramref name="sizePx"/> px de las que la llevan, salvo si la de la caché sigue valiendo. Una ruta
    /// que ya va de camino no se repite, salvo si caducó (<see cref="PendingTimeout"/>, hasta <see cref="MaxAttempts"/>). Lo
    /// que ya no está en la lista se olvida (y su imagen se libera).
    /// </summary>
    public void Request(IReadOnlyList<ShelfItem> items, int sizePx)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (_disposed || sizePx <= 0) return;
        var alive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = _clock();
        foreach (var item in items)
        {
            if (!ShelfThumbPolicy.Probes(item)) continue;
            alive.Add(item.Value);
            var slot = new Slot(item.Value, sizePx);
            ShelfThumbKey? cached = null;
            if (_current.TryGetValue(slot, out var known) && _cache.TryGet(known, out _)) cached = known;   // sigue en uso
            int attempt = 1;
            if (_pending.TryGetValue(slot, out var inFlight))
            {
                if (now - inFlight.Since < PendingTimeout) continue;     // va de camino
                if (inFlight.Attempt >= MaxAttempts) continue;           // se perdió otra vez: no se insiste (glifo)
                attempt = inFlight.Attempt + 1;                           // colgada o perdida: otra vez, con otra generación
            }
            long gen = ++_generation;
            _pending[slot] = new InFlight(gen, now, attempt);
            string path = item.Value;
            _background(() => Work(path, sizePx, cached, gen));
        }
        Forget(alive);
    }

    /// <summary>Imagen de <paramref name="path"/> a <paramref name="sizePx"/> px, o <c>null</c> (aún no llegó, no hay o ya no
    /// está). Solo lee: se puede llamar al pintar.</summary>
    public Bitmap? Get(string path, int sizePx) =>
        !_disposed && path is not null && _current.TryGetValue(new Slot(path, sizePx), out var key) ? _cache.Peek(key) : null;

    /// <summary>Lo último que se supo de <paramref name="path"/>, o <c>null</c> si aún no se miró.</summary>
    public ShelfFileProbe? Probe(string path) =>
        path is not null && _probes.TryGetValue(path, out var p) ? p : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cache.Clear();
        _current.Clear();
        _probes.Clear();
        _pending.Clear();
        Changed = null;
    }

    // Hilo de fondo: solo usa lo recibido y la fuente; el resultado vuelve por _post.
    private void Work(string path, int sizePx, ShelfThumbKey? cached, long gen)
    {
        var probe = ShelfFileProbe.Missing;
        ShelfThumbKey? key = null;
        Bitmap? bmp = null;
        try
        {
            probe = _source.Probe(path);
            if (ShelfThumbPolicy.WantsShellImage(path, probe.State))   // lo que hay ahora, no el tipo con que entró
            {
                key = ShelfThumbKey.For(path, sizePx, probe.ModifiedUtc);
                if (cached != key) bmp = _source.Extract(path, sizePx);
            }
        }
        catch (Exception ex)
        {
            // La fuente no debería lanzar; si lo hace, sin imagen (y «no está» si falló al mirar) y la cola sigue.
            AppLog.Warn($"notch: bandeja: miniatura: {ex.GetType().Name}: {ex.Message}");
            key = null;
        }
        try { _post(() => Complete(path, sizePx, gen, probe, key, bmp)); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // La ventana se cierra (o aún no tiene asa): nadie recogerá la imagen. La petición caduca y se repite.
            bmp?.Dispose();
            AppLog.Warn($"notch: bandeja: miniatura sin vuelta al hilo de UI: {ex.Message}");
        }
    }

    // Hilo de UI.
    private void Complete(string path, int sizePx, long gen, ShelfFileProbe probe, ShelfThumbKey? key, Bitmap? bmp)
    {
        var slot = new Slot(path, sizePx);
        if (_disposed || !_pending.TryGetValue(slot, out var inFlight) || inFlight.Generation != gen)
        {
            bmp?.Dispose();   // de una petición que ya no cuenta: caducada y repetida, olvidada o tras Dispose
            return;
        }
        _pending.Remove(slot);
        _probes[path] = probe;
        if (key is not { } k) Drop(slot);                     // ya no está, no se puede mirar o no lleva imagen
        else if (bmp is not null)
        {
            if (_current.TryGetValue(slot, out var old) && old != k) _cache.Remove(old);
            _cache.Add(k, bmp);
            _current[slot] = k;
        }
        else if (!(_current.TryGetValue(slot, out var cur) && cur == k && _cache.Contains(k)))
            Drop(slot);                                       // cambió y el shell no dio imagen: la vieja ya no vale
        Changed?.Invoke(path);
    }

    private void Drop(Slot slot)
    {
        if (_current.Remove(slot, out var key)) _cache.Remove(key);
    }

    /// <summary>Olvida las rutas que ya no están en la bandeja (y libera sus imágenes; lo que vuelva de ellas se descarta).</summary>
    private void Forget(HashSet<string> alive)
    {
        foreach (var slot in _current.Keys.Where(s => !alive.Contains(s.Path)).ToArray()) Drop(slot);
        foreach (var path in _probes.Keys.Where(p => !alive.Contains(p)).ToArray()) _probes.Remove(path);
        foreach (var slot in _pending.Keys.Where(s => !alive.Contains(s.Path)).ToArray()) _pending.Remove(slot);
    }

    /// <summary>Petición en vuelo: su generación, desde cuándo y qué intento es.</summary>
    private readonly record struct InFlight(long Generation, DateTime Since, int Attempt);

    /// <summary>Ruta (sin distinguir mayúsculas) y tamaño: una imagen por cada par.</summary>
    private readonly record struct Slot(string Path, int SizePx)
    {
        public bool Equals(Slot other) =>
            SizePx == other.SizePx && string.Equals(Path, other.Path, StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Path ?? ""), SizePx);
    }
}
