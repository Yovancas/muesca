using System.Globalization;
using System.Text.Json;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Letra elegida a mano para una pista («Otra letra») y su desfase manual en segundos (positivo = la letra
/// va más tarde). <see cref="LyricsCache.SetChoice"/> lo acota con <see cref="LyricsCache.ClampOffset"/>.</summary>
public sealed record LyricsChoice(string Source, string ExternalId, double OffsetSeconds);

/// <summary>Letra guardada en disco. <paramref name="Body"/> es LRC si <paramref name="Synced"/>, si no texto plano.
/// <paramref name="SavedUtc"/> marca la caducidad (<see cref="LyricsCache.MaxAge"/>).</summary>
public sealed record CachedLyrics(string Source, string ExternalId, bool Synced, string Body, TimeSpan Duration, DateTime SavedUtc);

/// <summary>
/// Caché de letras por pista en <c>lyrics-cache.json</c> (escritura atómica con <see cref="AtomicFile"/>), con la
/// elección y el desfase manual de cada pista: sobreviven a reinicios. Escritura: con <c>writeDelay</c> 0 (tests), tras
/// cada cambio; en la app, agrupada en segundo plano a los <c>writeDelay</c> del último cambio (los clics de «±0,5 s» o
/// los aciertos que mueven el LRU no serializan ~1 MB con fsync en el hilo de la UI) y lo pendiente al
/// <see cref="Flush"/> o <see cref="Dispose"/>.
/// <list type="bullet">
/// <item>Clave (<see cref="Key"/>): título y artista normalizados (sin acentos ni mayúsculas) + duración redondeada
/// a 2 s. La app que reproduce no cuenta.</item>
/// <item>LRU de <c>max</c> pistas (300): leer o escribir una pista la pone la primera; se guarda el orden.</item>
/// <item>La letra caduca a los <see cref="MaxAge"/> (30 días) de <see cref="CachedLyrics.SavedUtc"/>; la elección
/// de la pista se conserva.</item>
/// <item>Desfase acotado a ±<see cref="MaxOffsetSeconds"/> s en pasos de <see cref="OffsetStep"/> s.</item>
/// <item>Archivo ausente o corrupto → caché vacía (no peta; se sobrescribe en el siguiente cambio). Un error de disco
/// al guardar se anota y la caché sigue en memoria.</item>
/// </list>
/// Segura entre hilos (un candado): la consultan la UI y las búsquedas en segundo plano.
/// </summary>
public sealed class LyricsCache : IDisposable
{
    public const int DefaultMax = 300;
    public const double MaxOffsetSeconds = 10;
    public const double OffsetStep = 0.5;
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <summary><c>%APPDATA%\ClaudeBarWin\notch\lyrics-cache.json</c>.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeBarWin", "notch", "lyrics-cache.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly string _path;
    private readonly int _max;
    private readonly Func<DateTime> _utcNow;
    private readonly object _gate = new();
    private readonly object _writeGate = new();   // una escritura a la vez y en orden (la instantánea se toma dentro)
    private readonly List<Entry> _items;   // la primera, la usada más recientemente
    private readonly TimeSpan _writeDelay;
    private readonly System.Threading.Timer? _writeTimer;   // solo con writeDelay > 0
    private bool _dirty;
    private bool _disposed;

    /// <summary><paramref name="writeDelay"/> 0 = escribe tras cada cambio; &gt; 0 = agrupa las escrituras (ver la
    /// descripción de la clase).</summary>
    public LyricsCache(string path, int max = DefaultMax, TimeSpan writeDelay = default)
        : this(path, max, static () => DateTime.UtcNow, writeDelay)
    {
    }

    /// <summary>Para tests: reloj inyectable.</summary>
    internal LyricsCache(string path, int max, Func<DateTime> utcNow, TimeSpan writeDelay = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        ArgumentNullException.ThrowIfNull(utcNow);
        ArgumentOutOfRangeException.ThrowIfLessThan(writeDelay, TimeSpan.Zero);
        _path = path;
        _max = max;
        _utcNow = utcNow;
        _writeDelay = writeDelay;
        _items = Load(path, max);
        if (writeDelay > TimeSpan.Zero) _writeTimer = new System.Threading.Timer(_ => FlushFromTimer());
    }

    /// <summary>Clave estable de la pista (ver la descripción de la clase).</summary>
    public static string Key(TrackQuery q)
    {
        ArgumentNullException.ThrowIfNull(q);
        return TrackNameParser.Normalize(q.Title ?? "") + "\u001F" + TrackNameParser.Normalize(q.Artist ?? "") +
               "\u001F" + DurationBucket(q.Duration).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Duración redondeada a 2 s tal como entra en <see cref="Key"/> (0 = desconocida).</summary>
    public static long DurationBucket(TimeSpan duration)
    {
        var seconds = duration > TimeSpan.Zero ? duration.TotalSeconds : 0;
        return (long)Math.Round(seconds / 2, MidpointRounding.AwayFromZero) * 2;
    }

    /// <summary>Desfase acotado a ±<see cref="MaxOffsetSeconds"/> y redondeado al paso de <see cref="OffsetStep"/>
    /// (los medios, hacia fuera). NaN → 0.</summary>
    public static double ClampOffset(double seconds)
    {
        if (double.IsNaN(seconds)) return 0;
        var clamped = Math.Clamp(seconds, -MaxOffsetSeconds, MaxOffsetSeconds);
        var stepped = Math.Round(clamped / OffsetStep, MidpointRounding.AwayFromZero) * OffsetStep;
        return stepped == 0 ? 0 : stepped;   // sin −0
    }

    /// <summary>Letra de la pista si está y no ha caducado (la pone la primera del LRU). Una caducada se borra.</summary>
    public bool TryGet(string trackKey, out CachedLyrics value)
    {
        ArgumentException.ThrowIfNullOrEmpty(trackKey);
        lock (_gate)
        {
            int i = IndexOf(trackKey);
            if (i >= 0 && _items[i].Lyrics is { } lyrics)
            {
                if (_utcNow() - lyrics.SavedUtc > MaxAge)
                {
                    var e = _items[i];
                    if (e.Choice is null) _items.RemoveAt(i);
                    else _items[i] = e with { Lyrics = null };
                    Commit();
                }
                else
                {
                    if (i > 0) { MoveToFront(i); Commit(); }
                    value = lyrics;
                    return true;
                }
            }
        }
        value = null!;
        return false;
    }

    /// <summary>Guarda (o reemplaza) la letra de la pista sin tocar su elección.</summary>
    public void Put(string trackKey, CachedLyrics value)
    {
        ArgumentException.ThrowIfNullOrEmpty(trackKey);
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            Upsert(trackKey, e => e with { Lyrics = value });
            Commit();
        }
    }

    /// <summary>Guarda la elección y el desfase (acotado) de la pista.</summary>
    public void SetChoice(string trackKey, LyricsChoice c)
    {
        ArgumentException.ThrowIfNullOrEmpty(trackKey);
        ArgumentNullException.ThrowIfNull(c);
        var clamped = c with { OffsetSeconds = ClampOffset(c.OffsetSeconds) };
        lock (_gate)
        {
            Upsert(trackKey, e => e with { Choice = clamped });
            Commit();
        }
    }

    /// <summary>Elección guardada de la pista, o <c>null</c>. No caduca ni cambia el orden del LRU.</summary>
    public LyricsChoice? Choice(string trackKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(trackKey);
        lock (_gate)
        {
            int i = IndexOf(trackKey);
            return i >= 0 ? _items[i].Choice : null;
        }
    }

    /// <summary>Escribe ya lo pendiente (nada si no hay cambios). Seguro desde cualquier hilo.</summary>
    public void Flush()
    {
        lock (_writeGate)
        {
            Entry[] snapshot;
            lock (_gate)
            {
                if (!_dirty) return;
                _dirty = false;
                snapshot = _items.ToArray();   // las entradas son inmutables: se serializa fuera del candado
            }
            Write(snapshot);
        }
    }

    /// <summary>Para el temporizador y escribe lo pendiente. Después, cada cambio se escribe al momento.</summary>
    public void Dispose()
    {
        lock (_writeGate)
        {
            if (_disposed) return;
            _disposed = true;
            _writeTimer?.Dispose();
        }
        Flush();
    }

    private int IndexOf(string key) => _items.FindIndex(e => e.Key == key);

    private void MoveToFront(int i)
    {
        var e = _items[i];
        _items.RemoveAt(i);
        _items.Insert(0, e);
    }

    private void Upsert(string key, Func<Entry, Entry> change)
    {
        int i = IndexOf(key);
        var e = i >= 0 ? _items[i] : new Entry(key, null, null);
        if (i >= 0) _items.RemoveAt(i);
        _items.Insert(0, change(e));
        if (_items.Count > _max) _items.RemoveRange(_max, _items.Count - _max);
    }

    // Se llama con _gate tomado. Sin espera (o ya liberada), escribe al momento; si no, marca y (re)programa la escritura.
    private void Commit()
    {
        if (_writeTimer is null || _disposed)
        {
            _dirty = false;
            Write(_items.ToArray());
            return;
        }
        _dirty = true;
        _writeTimer.Change(_writeDelay, Timeout.InfiniteTimeSpan);
    }

    private void FlushFromTimer()
    {
        try
        {
            lock (_writeGate)
            {
                if (_disposed) return;   // Dispose ya escribió (o escribirá) lo pendiente
                Flush();
            }
        }
        catch (Exception ex)   // en el hilo del temporizador una excepción tiraría la app
        {
            AppLog.Warn($"letras: no se pudo guardar la caché ({ex.GetType().Name})");
        }
    }

    private void Write(Entry[] snapshot)
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(new CacheFile(1, snapshot.ToList()), Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"letras: no se pudo guardar la caché: {ex.Message}");
        }
    }

    private static List<Entry> Load(string path, int max)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var f = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), Json);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return f?.Entries?
                .Where(e => e is not null && !string.IsNullOrEmpty(e.Key) && (e.Lyrics is not null || e.Choice is not null))
                .Where(e => seen.Add(e.Key))
                .Select(e => e.Choice is { } c ? e with { Choice = c with { OffsetSeconds = ClampOffset(c.OffsetSeconds) } } : e)
                .Take(max)
                .ToList() ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new();
        }
    }

    private sealed record Entry(string Key, CachedLyrics? Lyrics, LyricsChoice? Choice);

    private sealed record CacheFile(int Version, List<Entry> Entries);
}
