using System.Text.Json;

namespace ClaudeBarWin.Services.Notch.Features;

public enum ShelfKind { File, Folder, Link, Text }

/// <summary>Entrada de la bandeja. <paramref name="Value"/> es la ruta, la URL o el texto.</summary>
public sealed record ShelfItem(Guid Id, ShelfKind Kind, string Value, string Display, DateTime AddedAt);

/// <summary>
/// Bandeja de archivos del notch, persistida en <c>shelf.json</c> con <see cref="AtomicFile"/>.
/// <list type="bullet">
/// <item>NO copia archivos: guarda rutas. Si luego se borran o mueven, <see cref="IsAvailable"/> da <c>false</c>
/// (la UI la marca y no intenta abrirla); la entrada no desaparece sola.</item>
/// <item>Máx. <see cref="MaxItems"/> entradas: al pasarse salen las más antiguas.</item>
/// <item>Sin duplicados: rutas sin distinguir mayúsculas ni barra final (Windows), enlaces y textos idénticos.</item>
/// <item>Solo rutas locales: las de red (UNC, <c>\\host\share</c>) y las de dispositivo (<c>\\?\</c>, <c>\\.\</c>) se
/// rechazan sin tocar el disco (<see cref="IsUncOrDevicePath"/>): abrirlas o enseñarlas haría que Windows se autenticara
/// por NTLM contra ese host, y comprobarlas desde el hilo de UI puede colgar la app si el recurso está caído.</item>
/// <item>Texto máx. <see cref="MaxText"/> caracteres. Enlaces solo <c>http(s)://</c> absolutos (nunca
/// <c>file:</c>/<c>javascript:</c>: se abren con el shell).</item>
/// <item>Si no se puede guardar, el cambio se deshace en memoria y la excepción llega al llamador.</item>
/// </list>
/// Existencia, tipo (carpeta) y reloj se inyectan para que la lógica sea testeable; por defecto,
/// <see cref="Directory.Exists"/> y <see cref="DateTime.Now"/>. No es seguro entre hilos: hilo de UI.
/// </summary>
public sealed class ShelfStore
{
    public const int MaxItems = 30;
    public const int MaxText = 10_000;
    public const int MaxDisplay = 60;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Func<string, bool> _exists;
    private readonly Func<string, bool> _isDirectory;
    private readonly Func<DateTime> _clock;
    private readonly List<ShelfItem> _items;

    public ShelfStore(string path, Func<string, bool> exists, Func<string, bool>? isDirectory = null, Func<DateTime>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(exists);
        _path = path;
        _exists = exists;
        _isDirectory = isDirectory ?? Directory.Exists;
        _clock = clock ?? (() => DateTime.Now);
        _items = Load(path);
    }

    public event Action? Changed;

    /// <summary>De la más antigua a la más nueva.</summary>
    public IReadOnlyList<ShelfItem> Items => _items.ToArray();

    /// <summary>Añade archivos o carpetas (soltados en el notch). Devuelve solo las entradas nuevas que quedan.</summary>
    public IReadOnlyList<ShelfItem> AddPaths(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var before = _items.ToList();
        var added = new List<ShelfItem>();
        var now = _clock();
        foreach (var raw in paths)
        {
            var p = NormalizePath(raw);
            if (p.Length == 0 || IsUncOrDevicePath(Unquote(raw)) || IsUncOrDevicePath(p) || FindPath(p) is not null) continue;
            var kind = SafeIsDirectory(p) ? ShelfKind.Folder : ShelfKind.File;
            var item = new ShelfItem(Guid.NewGuid(), kind, p, LastSegment(p), now);
            _items.Add(item);
            added.Add(item);
        }
        if (added.Count == 0) return Array.Empty<ShelfItem>();
        Commit(before);
        return added.Where(_items.Contains).ToArray();
    }

    public ShelfItem AddText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Texto vacío.", nameof(text));
        var value = Cap(text, MaxText);
        var dup = _items.Find(i => i.Kind == ShelfKind.Text && i.Value == value);
        if (dup is not null) return dup;

        var firstLine = value.Split('\n').Select(l => l.Trim()).First(l => l.Length > 0);
        return AddOne(new ShelfItem(Guid.NewGuid(), ShelfKind.Text, value, Ellipsis(firstLine), _clock()));
    }

    public ShelfItem AddLink(string url)
    {
        var u = (url ?? "").Trim();
        if (!Uri.TryCreate(u, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Solo enlaces http(s)://.", nameof(url));
        var dup = _items.Find(i => i.Kind == ShelfKind.Link && i.Value == u);
        if (dup is not null) return dup;

        var display = uri.Host + (uri.AbsolutePath == "/" ? "" : uri.AbsolutePath);
        return AddOne(new ShelfItem(Guid.NewGuid(), ShelfKind.Link, u, Ellipsis(display), _clock()));
    }

    public void Remove(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var set = ids.ToHashSet();
        var before = _items.ToList();
        if (_items.RemoveAll(i => set.Contains(i.Id)) > 0) Commit(before);
    }

    public void Clear()
    {
        var before = _items.ToList();
        _items.Clear();
        Commit(before);
    }

    /// <summary>Archivos y carpetas: si la ruta sigue existiendo (un error al comprobarlo cuenta como no
    /// disponible). Enlaces y textos: siempre.</summary>
    public bool IsAvailable(ShelfItem i)
    {
        ArgumentNullException.ThrowIfNull(i);
        if (i.Kind is ShelfKind.Link or ShelfKind.Text) return true;
        try { return _exists(i.Value); }
        catch { return false; }
    }

    private ShelfItem AddOne(ShelfItem item)
    {
        var before = _items.ToList();
        _items.Add(item);
        Commit(before);
        return item;
    }

    private ShelfItem? FindPath(string p) =>
        _items.Find(i => i.Kind is ShelfKind.File or ShelfKind.Folder && string.Equals(i.Value, p, StringComparison.OrdinalIgnoreCase));

    private bool SafeIsDirectory(string p)
    {
        try { return _isDirectory(p); }
        catch { return false; }
    }

    /// <summary>Guarda y avisa. Si el disco falla, la lista vuelve a <paramref name="before"/> y la excepción sigue su
    /// camino: lo que se muestra nunca difiere de lo guardado (el llamador anota el error).</summary>
    private void Commit(List<ShelfItem> before)
    {
        if (_items.Count > MaxItems) _items.RemoveRange(0, _items.Count - MaxItems);
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(new ShelfFile(1, _items), Json));
        }
        catch
        {
            _items.Clear();
            _items.AddRange(before);
            throw;
        }
        Changed?.Invoke();
    }

    private static List<ShelfItem> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var f = JsonSerializer.Deserialize<ShelfFile>(File.ReadAllText(path), Json);
            var items = f?.Items?
                .Where(i => i is not null && Enum.IsDefined(i.Kind) && !string.IsNullOrEmpty(i.Value))
                .ToList() ?? new();
            if (items.Count > MaxItems) items.RemoveRange(0, items.Count - MaxItems);
            return items;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new();
        }
    }

    /// <summary>
    /// ¿Ruta de red o de dispositivo? Todo lo que empieza por dos separadores (<c>\\host\share</c>, <c>//host/share</c>,
    /// <c>\\?\…</c>, <c>\\.\…</c>, mezclas como <c>\/host</c>) y el prefijo NT <c>\??\</c>. PURO: no toca el disco.
    /// </summary>
    public static bool IsUncOrDevicePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var p = path.TrimStart().TrimStart('"').TrimStart();   // como NormalizePath: sin espacios ni comillas delante
        static bool Sep(char c) => c is '\\' or '/';
        if (p.Length >= 2 && Sep(p[0]) && Sep(p[1])) return true;
        return p.StartsWith(@"\??\", StringComparison.Ordinal);
    }

    private static string Unquote(string? raw) => (raw ?? "").Trim().Trim('"').Trim();

    // Sin espacios ni comillas alrededor y sin barra final (salvo la raíz «C:\»).
    private static string NormalizePath(string? raw)
    {
        var p = Unquote(raw);
        if (p.Length == 0) return p;
        var t = p.TrimEnd('\\', '/');
        if (t.Length == 0) return p[..1];
        return t.EndsWith(':') ? t + "\\" : t;
    }

    private static string LastSegment(string p)
    {
        var t = p.TrimEnd('\\', '/');
        int i = t.LastIndexOfAny(new[] { '\\', '/' });
        var seg = i >= 0 ? t[(i + 1)..] : t;
        return Ellipsis(seg.Length == 0 || seg.EndsWith(':') ? p : seg);
    }

    private static string Ellipsis(string s) => s.Length <= MaxDisplay ? s : Cap(s, MaxDisplay - 1) + "…";

    // Recorta a max caracteres sin partir un par sustituto.
    private static string Cap(string s, int max)
    {
        if (s.Length <= max) return s;
        int cut = max;
        if (char.IsHighSurrogate(s[cut - 1])) cut--;
        return s[..cut];
    }

    private sealed record ShelfFile(int Version, List<ShelfItem> Items);
}
