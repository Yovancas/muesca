using System.Security.Cryptography;
using Velopack;
using Velopack.Exceptions;
using Velopack.Logging;
using Velopack.Sources;

namespace ClaudeBarWin.Services.Updates;

/// <summary>
/// <see cref="GithubSource"/> que solo deja pasar las releases cuyo manifiesto (<c>releases.win.json</c>) trae una firma
/// Ed25519 válida (<see cref="ManifestSignature"/>). Repite el <c>GetReleaseFeed</c> de <c>GitBase</c> (Velopack 1.2.161:
/// las 10 últimas releases publicadas, un manifiesto por release) pero verifica cada manifiesto ANTES de leerlo. Una
/// release sin firma, con la firma mala o con el manifiesto tocado se descarta entera y se anota en el log. Descargar es
/// lo de <c>GitBase</c> (solo acepta los assets que salen de aquí), y después se vuelve a comprobar el tamaño y el SHA-256
/// de cada paquete contra el manifiesto firmado, aunque Velopack también lo haga: no depende de que siga haciéndolo.
/// </summary>
public sealed class SignedGithubSource : GithubSource
{
    /// <summary>
    /// El repositorio de las releases: el público, ya con el nombre de Muesca (<see cref="Brand.RepoUrl"/>). Va también en lo
    /// que se firma (<see cref="ManifestSignature.SignedMessage"/>), así que scripts/release-velopack.ps1 firma con el mismo
    /// (<c>-GhRepo</c>, ReleaseScriptTests). Cambió antes de la primera release con Velopack (la 0.5): ninguna app instalada
    /// verifica con el nombre viejo. Las 0.3.9/0.4.0 (NetSparkle) no lo usan: leen el appcast por la URL vieja, que GitHub
    /// redirige al repositorio renombrado (docs/branding.md).
    /// </summary>
    public const string RepoUrl = Brand.RepoUrl;

    /// <summary>Para probar las actualizaciones de punta a punta contra otro repositorio de GitHub (uno de pruebas; ver
    /// docs/velopack-migracion.md). No baja la guardia: sus releases tienen que venir firmadas con la misma clave.</summary>
    public const string RepoOverrideVariable = "CLAUDEBAR_UPDATE_REPO";

    /// <summary>Tope del manifiesto de una release (se baja antes de verificar nada). Uno de vpk lleva unos cientos de bytes
    /// por paquete; un mega da para miles.</summary>
    internal const int MaxManifestBytes = 1024 * 1024;

    private readonly byte[] _publicKey;
    private readonly Action<string> _warn;

    /// <param name="downloader">Inyectable para los tests (por defecto, el HTTP de Velopack con tope para el manifiesto y su
    /// firma: <see cref="BoundedHttpDownloader"/>).</param>
    /// <param name="warn">Dónde se anotan las releases descartadas (por defecto, <see cref="AppLog"/>).</param>
    public SignedGithubSource(string repoUrl, byte[] publicKey, IFileDownloader? downloader = null, Action<string>? warn = null)
        : base(repoUrl, accessToken: null, prerelease: false, downloader ?? new BoundedHttpDownloader())
    {
        _publicKey = publicKey;
        _warn = warn ?? AppLog.Warn;
    }

    /// <summary>Releases descartadas en la última consulta.</summary>
    public IReadOnlyList<RejectedRelease> LastRejected { get; private set; } = Array.Empty<RejectedRelease>();

    /// <summary>Releases con el manifiesto bien firmado en la última consulta. Cero no es «al día»: la app instalada salió de
    /// alguna, así que o se borraron todas o hay demasiadas releases sin Velopack delante (solo se miran las 10 últimas).</summary>
    public int LastVerified { get; private set; }

    public override async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
        Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
    {
        var releases = await GetReleases(Prerelease).ConfigureAwait(false) ?? Array.Empty<GithubRelease>();
        var manifestName = ManifestName(channel);
        var assets = new List<VelopackAsset>();
        var rejected = new List<RejectedRelease>();
        int verified = 0;
        foreach (var release in releases)
        {
            // Sin manifiesto no es una release de Velopack (las de Inno, hasta la 0.4): ni cuenta ni se avisa.
            if (AssetUrl(release, manifestName) is not { } manifestUrl) continue;
            byte[] manifest;
            byte[]? signature = null;
            string? why = null;
            VelopackAsset[]? entries = null;
            try
            {
                manifest = await DownloadAtMost(manifestUrl, MaxManifestBytes).ConfigureAwait(false);
                if (AssetUrl(release, manifestName + ManifestSignature.SignatureSuffix) is { } signatureUrl)
                    signature = await DownloadAtMost(signatureUrl, ManifestSignature.MaxSignatureFileBytes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Un fallo de red (o un archivo por encima del tope) con una release no tumba la consulta entera: solo se
                // descarta esa (sin sus bytes no hay firma que comprobar, así que tampoco pasa nada de ella).
                manifest = Array.Empty<byte>();
                why = $"no se pudo bajar el manifiesto o su firma ({ex.Message})";
            }

            if (why is null)
            {
                var verdict = ManifestSignature.Verify(manifest, signature, _publicKey, RepoUri.AbsolutePath);
                why = verdict == ManifestVerdict.Valid ? null : ManifestSignature.Describe(verdict);
            }
            if (why is null)
            {
                // Firmado, pero que no se pueda leer no tumba la consulta entera: solo se descarta esta release.
                try { entries = VelopackAssetFeed.FromJson(ManifestSignature.ManifestText(manifest)).Assets; }
                catch (Exception ex) { why = $"manifiesto firmado pero ilegible ({ex.Message})"; }
            }
            if (why is not null || entries is null)
            {
                var what = new RejectedRelease(release.Name ?? "(sin nombre)", why ?? "sin entradas");
                rejected.Add(what);
                _warn($"actualizaciones: release descartada, {what}");
                continue;
            }
            verified++;
            foreach (var entry in entries)
                assets.Add(new GitBaseAsset(entry, release));
        }
        LastRejected = rejected;
        LastVerified = verified;
        return new VelopackAssetFeed { Assets = assets.ToArray() };
    }

    /// <summary>El repositorio de las releases: <see cref="RepoUrl"/> o, si <see cref="RepoOverrideVariable"/> trae otro
    /// repositorio de github.com por https (<c>https://github.com/dueño/repo</c>), ese. Cualquier otra cosa se ignora.</summary>
    public static string ResolveRepoUrl(Func<string, string?> env)
    {
        var custom = env(RepoOverrideVariable)?.Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(custom) || !Uri.TryCreate(custom, UriKind.Absolute, out var uri)) return RepoUrl;
        bool github = uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                      && uri.IsDefaultPort && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
                      && uri.AbsolutePath.Trim('/').Split('/') is [{ Length: > 0 }, { Length: > 0 }];
        return github ? custom : RepoUrl;
    }

    /// <summary>Descarga como <c>GitBase</c> y comprueba tamaño y SHA-256 contra el manifiesto firmado (de donde sale
    /// <paramref name="releaseEntry"/>). Si no cuadra, borra lo descargado y lanza <see cref="ChecksumFailedException"/>.</summary>
    public override async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
        Action<int> progress, CancellationToken cancelToken)
    {
        await base.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken).ConfigureAwait(false);
        if (!MatchesManifest(localFile, releaseEntry, out var why))
        {
            try { File.Delete(localFile); } catch { /* best-effort: Velopack limpia los .partial */ }
            _warn($"actualizaciones: {releaseEntry.FileName} no cuadra con el manifiesto firmado ({why})");
            throw new ChecksumFailedException(localFile, why);
        }
    }

    /// <summary>¿Tiene el archivo el tamaño y el SHA-256 del manifiesto? Sin SHA-256 en el manifiesto, no (los de vpk 1.2 siempre
    /// lo traen; el SHA-1 solo no basta).</summary>
    internal static bool MatchesManifest(string file, VelopackAsset entry, out string why)
    {
        var info = new FileInfo(file);
        if (!info.Exists) { why = "no se ha descargado"; return false; }
        if (info.Length != entry.Size) { why = $"tamaño {info.Length} en vez de {entry.Size}"; return false; }
        if (string.IsNullOrWhiteSpace(entry.SHA256)) { why = "el manifiesto no trae SHA-256"; return false; }
        string actual;
        using (var stream = File.OpenRead(file)) actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(entry.SHA256.Trim(), StringComparison.OrdinalIgnoreCase)) { why = "el SHA-256 no coincide"; return false; }
        why = "";
        return true;
    }

    /// <summary>Como <c>CoreUtil.GetVeloReleaseIndexName</c> de Velopack (interno): <c>releases.win.json</c> en Windows.</summary>
    internal static string ManifestName(string? channel) => $"releases.{(string.IsNullOrEmpty(channel) ? "win" : channel)}.json";

    /// <summary>Un archivo pequeño de la release (manifiesto o firma) con un tope de bytes: sin pasar de ahí en memoria si el
    /// downloader lo permite (<see cref="IBoundedDownloader"/>, el de la app); con otro, al menos no se usa algo mayor.</summary>
    private async Task<byte[]> DownloadAtMost(string url, int maxBytes)
    {
        var headers = GetRequestHeaders("application/octet-stream");
        var bytes = Downloader is IBoundedDownloader bounded
            ? await bounded.DownloadBytes(url, headers, maxBytes).ConfigureAwait(false)
            : await Downloader.DownloadBytes(url, headers).ConfigureAwait(false);
        if (bytes.Length > maxBytes) throw BoundedHttpDownloader.TooBig(maxBytes);
        return bytes;
    }

    /// <summary>URL del asset de la release, o null si no lo trae (<c>GetAssetUrlFromName</c> lanza en ese caso).</summary>
    private string? AssetUrl(GithubRelease release, string name)
    {
        try { return GetAssetUrlFromName(release, name); }
        catch (ArgumentException) { return null; }
    }
}

/// <summary>Una release que <see cref="SignedGithubSource"/> no deja pasar, con el motivo.</summary>
public sealed record RejectedRelease(string Name, string Reason)
{
    /// <summary>Versión que dice ser por su nombre (<c>v0.5.2</c>, como la publica scripts/release-velopack.ps1), o null si no
    /// se lee. Sale de una release sin verificar: sirve para decidir si avisar, nunca para instalar nada.</summary>
    public SemanticVersion? ClaimedVersion =>
        SemanticVersion.TryParse(Name.Trim().TrimStart('v', 'V'), out var version) ? version : null;

    public override string ToString() => $"{Name}: {Reason}";
}
