using System.Text.Json;
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>Tarifa de un modelo en USD por 1.000.000 de tokens.</summary>
public readonly record struct ModelRate(
    double Input, double Output, double CacheWrite5m, double CacheWrite1h, double CacheRead);

/// <summary>
/// Coste estimado de un turno + si hubo tarifa real (T13b). <c>IsPriced=false</c> significa que el
/// modelo no aparece en NINGUNA fuente del catálogo: la UI enseña "—" con sufijo localizado y el
/// total NO lo suma (no se miente con $0).
/// </summary>
public readonly record struct PricedCost(double Usd, bool IsPriced);

/// <summary>
/// Catálogo de tarifas SIN hardcodear (T13b — sustituye la tabla fija de <see cref="Pricing"/>).
/// Cadena de fuentes, de más fresca a más estable:
/// <list type="number">
///   <item>(a) caché en disco <c>%APPDATA%\ClaudeBarWin\models-pricing.json</c> — si parsea se usa
///   (aunque esté caducada: dato real &gt; snapshot); su EDAD (&lt;7 días) solo decide si toca refrescar.</item>
///   <item>(b) refresh asíncrono desde https://models.dev/api.json — GET sin NINGÚN dato del usuario
///   (ni query ni headers identificativos: solo el User-Agent "ClaudeBarWin/&lt;ver&gt;"), timeout 5 s,
///   fire-and-forget en background (<see cref="RefreshInBackground"/> JAMÁS bloquea el refresh de UI).
///   Al éxito reescribe la caché con swap atómico .tmp+Replace (patrón <see cref="CredentialsWriter"/>).
///   Con el toggle de privacidad OFF (<c>AppConfig.PricingOnlineUpdate=false</c>) NO se toca la red.</item>
///   <item>(c) snapshot embebido como recurso del ensamblado (Resources\models-pricing-snapshot.json,
///   provider anthropic de models.dev recortado a id→cost): la app funciona offline para siempre.</item>
/// </list>
/// Matching: id exacto (case-insensitive) → si no, mejor match por familia (mismo token de familia de
/// <see cref="ModelFamily.FromId"/>, versión más alta disponible). Modelo en ninguna fuente ⇒
/// <see cref="PricedCost.IsPriced"/> = false.
/// </summary>
public sealed class PricingCatalog
{
    public const string SourceUrl = "https://models.dev/api.json";
    /// <summary>Edad máxima de la caché antes de intentar un refresh online.</summary>
    public static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(7);
    /// <summary>Espera mínima entre intentos de refresh online tras un intento (fallido o no).</summary>
    public static readonly TimeSpan FailureRetryDelay = TimeSpan.FromHours(6);
    private DateTime _retryAfterUtc = DateTime.MinValue;
    /// <summary>Timeout corto del GET a models.dev: si la red está mal, ni se nota (hay snapshot).</summary>
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Instancia de la app: caché real en %APPDATA% + fetcher HTTP real.</summary>
    public static PricingCatalog Default { get; } = new();

    public static string DefaultCachePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeBarWin", "models-pricing.json");

    private readonly string _cachePath;
    private readonly Func<CancellationToken, Task<string>> _fetcher;
    private readonly Func<DateTime> _utcNow;
    private int _refreshing; // un refresh en vuelo a la vez (Interlocked)

    /// <summary>Tablas inmutables que se sustituyen ATÓMICAMENTE al refrescar (lectores sin lock).</summary>
    private sealed record Tables(
        Dictionary<string, ModelRate> ById,
        Dictionary<string, ModelRate> ByFamily);

    private volatile Tables _tables;

    /// <summary>
    /// <paramref name="cachePath"/> y <paramref name="fetcher"/> inyectables para test (caja de arena
    /// en %TEMP% + fetcher fake que cuenta llamadas / devuelve un golden file / falla como "sin red").
    /// </summary>
    public PricingCatalog(string? cachePath = null,
        Func<CancellationToken, Task<string>>? fetcher = null,
        Func<DateTime>? utcNow = null)
    {
        _cachePath = cachePath ?? DefaultCachePath;
        _fetcher = fetcher ?? FetchFromModelsDevAsync;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _tables = BuildTables(LoadInitialTable());
    }

    // ---------------- resolución ----------------

    /// <summary>Coste estimado del turno. Sin tarifa en ninguna fuente ⇒ (0, IsPriced=false).</summary>
    public PricedCost CostFor(UsageRecord r)
    {
        var rate = RateFor(r.Model);
        if (rate is null) return new PricedCost(0, IsPriced: false);
        double usd = r.InputTokens / 1_000_000.0 * rate.Value.Input
                   + r.OutputTokens / 1_000_000.0 * rate.Value.Output
                   + r.CacheCreate5mTokens / 1_000_000.0 * rate.Value.CacheWrite5m
                   + r.CacheCreate1hTokens / 1_000_000.0 * rate.Value.CacheWrite1h
                   + r.CacheReadTokens / 1_000_000.0 * rate.Value.CacheRead;
        return new PricedCost(usd, IsPriced: true);
    }

    /// <summary>Id exacto (case-insensitive) → familia con versión más alta → null (sin tarifa).</summary>
    internal ModelRate? RateFor(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return null;
        var t = _tables;
        if (t.ById.TryGetValue(model.Trim(), out var exact)) return exact;
        var family = ModelFamily.FromId(model);
        if (family != ModelFamily.Other && t.ByFamily.TryGetValue(family, out var fam)) return fam;
        return null;
    }

    // ---------------- refresh online (opt-out, nunca bloquea) ----------------

    /// <summary>Fire-and-forget: el refresh de UI NUNCA espera a la red. No-op con el toggle OFF
    /// o con la caché fresca; los errores se tragan (la cadena caché+snapshot sigue sirviendo).</summary>
    public void RefreshInBackground(bool onlineEnabled) => _ = RefreshAsync(onlineEnabled);

    internal async Task<bool> RefreshAsync(bool onlineEnabled, CancellationToken ct = default)
    {
        if (!onlineEnabled) return false;            // privacidad: OFF ⇒ ni un byte a la red
        if (CacheIsFresh()) return false;            // <7 días ⇒ nada que refrescar
        if (_utcNow() < _retryAfterUtc) return false; // falló hace poco: no reintentar en cada tick
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return false; // ya hay uno en vuelo
        try
        {
            // Se arma el enfriamiento ANTES de ir a la red: sin él, un fallo persistente (sin red, feed
            // roto) reintentaba cada 60 s descargando ~5 MB de models.dev (auditoría v0.3.10).
            _retryAfterUtc = _utcNow() + FailureRetryDelay;
            string json = await _fetcher(ct).ConfigureAwait(false);
            var fetched = ParseModels(json);
            if (fetched is null || fetched.Count == 0) return false; // feed corrupto: no pisar nada

            WriteCacheAtomic(SerializeTrimmed(fetched, _utcNow()));

            // El feed pisa al snapshot, pero un modelo retirado del feed conserva la tarifa embebida.
            var table = ParseModels(LoadSnapshotJson()) ?? NewTable();
            foreach (var kv in fetched) table[kv.Key] = kv.Value;
            _tables = BuildTables(table);
            return true;
        }
        catch
        {
            return false; // sin red / timeout / IO: la app sigue con caché+snapshot
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    /// <summary>La caché cuenta como fresca si existe y tiene menos de <see cref="CacheMaxAge"/>.</summary>
    internal bool CacheIsFresh()
    {
        try
        {
            return File.Exists(_cachePath)
                && _utcNow() - File.GetLastWriteTimeUtc(_cachePath) < CacheMaxAge;
        }
        catch
        {
            return false;
        }
    }

    // ---------------- carga / parseo ----------------

    private static Dictionary<string, ModelRate> NewTable() => new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Snapshot embebido como base + overlay de la caché en disco si parsea (merge, no
    /// sustitución: lo que la caché no trae sigue resolviendo desde el snapshot).</summary>
    private Dictionary<string, ModelRate> LoadInitialTable()
    {
        var table = ParseModels(LoadSnapshotJson()) ?? NewTable();
        try
        {
            if (File.Exists(_cachePath) && ParseModels(File.ReadAllText(_cachePath)) is { } cached)
                foreach (var kv in cached) table[kv.Key] = kv.Value;
        }
        catch
        {
            // caché ilegible: se queda el snapshot (el próximo refresh la reescribe entera)
        }
        return table;
    }

    /// <summary>JSON del recurso embebido (generado desde models.dev, provider anthropic, id→cost).</summary>
    internal static string LoadSnapshotJson()
    {
        var asm = typeof(PricingCatalog).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("models-pricing-snapshot.json", StringComparison.OrdinalIgnoreCase));
        if (name is null) return "{}";
        using var stream = asm.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Parsea cualquiera de los dos formatos a id→tarifa:
    /// (1) api.json COMPLETO de models.dev (raíz = providers): se toma SOLO <c>anthropic.models</c>;
    /// (2) recortado (caché/snapshot): raíz con <c>models</c> directo, cada entrada id→cost.
    /// Derivaciones cuando el feed no trae el campo (proporciones públicas de Anthropic):
    /// cache_write 5m = input×1.25 · cache_write 1h = input×2.0 · cache_read = input×0.1.
    /// models.dev publica <c>cache_write</c> = la tarifa de escritura 5m.
    /// Devuelve null si el JSON no parsea o no tiene la forma esperada.
    /// </summary>
    internal static Dictionary<string, ModelRate>? ParseModels(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            JsonElement models;
            if (root.TryGetProperty("models", out var direct) && direct.ValueKind == JsonValueKind.Object)
                models = direct;   // formato recortado (caché / snapshot)
            else if (root.TryGetProperty("anthropic", out var anth) && anth.ValueKind == JsonValueKind.Object
                     && anth.TryGetProperty("models", out var am) && am.ValueKind == JsonValueKind.Object)
                models = am;       // api.json completo: SOLO el provider anthropic
            else
                return null;

            var table = NewTable();
            foreach (var prop in models.EnumerateObject())
            {
                var el = prop.Value;
                if (el.ValueKind != JsonValueKind.Object) continue;
                // api.json anida los precios bajo "cost"; el formato recortado los trae directos.
                if (el.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Object)
                    el = cost;

                double? input = GetNumber(el, "input");
                double? output = GetNumber(el, "output");
                if (input is null || output is null) continue; // sin input/output no hay tarifa útil

                table[prop.Name] = new ModelRate(
                    Input: input.Value,
                    Output: output.Value,
                    CacheWrite5m: GetNumber(el, "cache_write") ?? input.Value * 1.25,
                    CacheWrite1h: GetNumber(el, "cache_write_1h") ?? input.Value * 2.0,
                    CacheRead: GetNumber(el, "cache_read") ?? input.Value * 0.1);
            }
            return table;
        }
        catch
        {
            return null;
        }
    }

    private static double? GetNumber(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;

    // ---------------- índice por familia (mejor versión) ----------------

    private static Tables BuildTables(Dictionary<string, ModelRate> byId)
    {
        // Por familia gana la VERSIÓN más alta listada: claude-opus-9 (aún no listado) resuelve a la
        // tarifa del Opus más nuevo, no a la de un opus-4-0 antiguo con otra lista de precios.
        var best = new Dictionary<string, (long[] Key, string Id, ModelRate Rate)>(StringComparer.Ordinal);
        foreach (var (id, rate) in byId)
        {
            var family = ModelFamily.FromId(id);
            if (family == ModelFamily.Other) continue;
            var key = VersionKey(id);
            if (!best.TryGetValue(family, out var cur)) { best[family] = (key, id, rate); continue; }
            int cmp = CompareVersion(key, cur.Key);
            if (cmp > 0 || (cmp == 0 && string.CompareOrdinal(id, cur.Id) > 0))
                best[family] = (key, id, rate);
        }
        return new Tables(byId,
            best.ToDictionary(kv => kv.Key, kv => kv.Value.Rate, StringComparer.Ordinal));
    }

    /// <summary>
    /// Clave de versión de un id: sus tokens PURAMENTE numéricos en orden, EXCLUYENDO los sellos de
    /// fecha (≥ 10.000.000, p.ej. 20250514) — "claude-opus-4-8" → [4,8] gana a
    /// "claude-opus-4-20250514" → [4] (la fecha no es una versión 20 millones).
    /// </summary>
    internal static long[] VersionKey(string id)
    {
        var nums = new List<long>();
        foreach (var token in id.Split('-'))
            if (token.Length > 0 && token.All(char.IsAsciiDigit)
                && long.TryParse(token, out var n) && n < 10_000_000)
                nums.Add(n);
        return nums.ToArray();
    }

    /// <summary>Comparación elemento a elemento; el componente ausente cuenta como -1 ([4,8] &gt; [4]).</summary>
    internal static int CompareVersion(long[] a, long[] b)
    {
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            long av = i < a.Length ? a[i] : -1;
            long bv = i < b.Length ? b[i] : -1;
            if (av != bv) return av.CompareTo(bv);
        }
        return 0;
    }

    // ---------------- caché en disco (escritura atómica, patrón CredentialsWriter) ----------------

    /// <summary>Formato recortado y loss-less (incluye los derivados 5m/1h/read): re-parseable
    /// por <see cref="ParseModels"/> sin red ("models" directo, cada id→tarifa).</summary>
    private static string SerializeTrimmed(Dictionary<string, ModelRate> models, DateTime utcNow)
    {
        var doc = new Dictionary<string, object>
        {
            ["_source"] = SourceUrl,
            ["_refreshed_utc"] = utcNow.ToString("O"),
            ["models"] = models.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, double>
                {
                    ["input"] = kv.Value.Input,
                    ["output"] = kv.Value.Output,
                    ["cache_read"] = kv.Value.CacheRead,
                    ["cache_write"] = kv.Value.CacheWrite5m,
                    ["cache_write_1h"] = kv.Value.CacheWrite1h,
                })
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Swap atómico .tmp+Replace (mismo patrón que <see cref="CredentialsWriter"/>): la caché
    /// nunca queda escrita a medias aunque el proceso muera en mitad del refresh.</summary>
    private void WriteCacheAtomic(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
        string tmp = _cachePath + ".tmp";
        File.WriteAllText(tmp, json);
        try
        {
            if (File.Exists(_cachePath))
                File.Replace(tmp, _cachePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else
                File.Move(tmp, _cachePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best-effort: no dejar .tmp huérfano */ }
            throw;
        }
    }

    // ---------------- fetcher real ----------------

    /// <summary>GET anónimo a models.dev: SIN datos del usuario (ni query ni headers identificativos);
    /// el único header propio es el User-Agent del producto. Timeout corto (5 s).</summary>
    private static async Task<string> FetchFromModelsDevAsync(CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = FetchTimeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"ClaudeBarWin/{AppVersion()}");
        return await http.GetStringAsync(SourceUrl, ct).ConfigureAwait(false);
    }

    private static string AppVersion()
    {
        var v = typeof(PricingCatalog).Assembly.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
