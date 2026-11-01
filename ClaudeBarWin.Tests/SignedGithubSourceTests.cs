using System.Text;
using System.Text.Json;
using Chaos.NaCl;
using ClaudeBarWin.Services.Updates;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="SignedGithubSource"/> contra un GitHub falso (un <see cref="IFileDownloader"/> en memoria): solo llegan a Velopack
/// los assets de las releases con el manifiesto bien firmado; las demás se descartan y se anotan.
/// </summary>
public class SignedGithubSourceTests
{
    private const string Repo = "https://github.com/Yovancas/muesca";

    /// <summary>GitHub en memoria: la API de releases devuelve <see cref="ReleasesJson"/>; cada asset es una URL de
    /// <see cref="Files"/>. Lo que no esté, 404.</summary>
    private sealed class FakeGitHub : IFileDownloader
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public readonly List<string> Requested = new();
        public string ReleasesJson = "[]";

        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Requested.Add(url);
            Assert.StartsWith("https://api.github.com/repos/Yovancas/muesca/releases", url);
            return Task.FromResult(ReleasesJson);
        }

        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Requested.Add(url);
            return Files.TryGetValue(url, out var bytes)
                ? Task.FromResult(bytes)
                : Task.FromException<byte[]>(new HttpRequestException("404 " + url));
        }

        public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null,
            double timeout = 30, CancellationToken cancelToken = default)
        {
            Requested.Add(url);
            File.WriteAllBytes(targetFile, Files[url]);
            progress(100);
            return Task.CompletedTask;
        }

        public static string Url(string tag, string asset) => $"https://github.com/Yovancas/muesca/releases/download/{tag}/{asset}";

        /// <summary>Publica una release con estos assets (nombre → contenido).</summary>
        public void Release(string tag, DateTime published, params (string Name, byte[] Content)[] assets)
        {
            foreach (var (name, content) in assets) Files[Url(tag, name)] = content;
            var list = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(ReleasesJson)!;
            list.Add(new Dictionary<string, object>
            {
                ["name"] = tag,
                ["prerelease"] = false,
                ["published_at"] = published.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["assets"] = assets.Select(a => new Dictionary<string, object>
                {
                    ["name"] = a.Name,
                    ["browser_download_url"] = Url(tag, a.Name),
                    ["url"] = $"https://api.github.com/assets/{tag}/{a.Name}",
                }).ToArray(),
            });
            ReleasesJson = JsonSerializer.Serialize(list);
        }
    }

    private sealed class NoLog : IVelopackLogger
    {
        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception) { }
    }

    private static byte[] Feed(string version, long size = 3, string sha256 = "b") => Encoding.UTF8.GetBytes(
        $$"""{"Assets":[{"PackageId":"ClaudeBar","Version":"{{version}}","Type":"Full","FileName":"ClaudeBar-{{version}}-full.nupkg","SHA1":"a","SHA256":"{{sha256}}","Size":{{size}}}]}""");

    /// <summary>Manifiesto con el tamaño y el SHA-256 de verdad de <paramref name="package"/>.</summary>
    private static byte[] FeedFor(string version, byte[] package) =>
        Feed(version, package.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package)));

    private static (string, byte[]) Manifest(byte[] content) => ("releases.win.json", content);
    private static (string, byte[]) Sig(byte[] sigFile) => ("releases.win.json.sig", sigFile);

    private static (SignedGithubSource Source, List<string> Warnings) Source(FakeGitHub gh)
    {
        var warnings = new List<string>();
        return (new SignedGithubSource(Repo, ManifestSignatureTests.TestPublicKey, gh, warnings.Add), warnings);
    }

    [Fact]
    public async Task Only_releases_with_a_valid_signature_reach_Velopack()
    {
        var gh = new FakeGitHub();
        var t = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var good = Feed("0.5.1");
        // La de Inno (hasta la 0.4): sin manifiesto de Velopack, ni cuenta ni se avisa.
        gh.Release("v0.4.0", t, ("appcast.xml", "<rss/>"u8.ToArray()), ("ClaudeBarWin-Setup-0.4.0.exe", [1, 2]));
        gh.Release("v0.5.1", t.AddDays(1), Manifest(good), Sig(ManifestSignatureTests.SigFile(good)));
        // Una más nueva, pero firmada con otra clave.
        var forged = Feed("0.5.2");
        var otherKey = Ed25519.ExpandedPrivateKeyFromSeed(Enumerable.Repeat((byte)9, 32).ToArray());
        gh.Release("v0.5.2", t.AddDays(2), Manifest(forged), Sig(ManifestSignatureTests.SigFile(forged, otherKey)));

        var (source, warnings) = Source(gh);
        var feed = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        var asset = Assert.Single(feed.Assets);
        Assert.Equal("0.5.1", asset.Version.ToString());
        Assert.Equal("ClaudeBar-0.5.1-full.nupkg", asset.FileName);
        var rejected = Assert.Single(source.LastRejected);
        Assert.Equal("v0.5.2", rejected.Name);
        Assert.Equal(1, source.LastVerified);
        Assert.Contains("v0.5.2", Assert.Single(warnings));
        // Solo se piden la API, los manifiestos y sus firmas: ningún paquete antes de verificar.
        Assert.DoesNotContain(gh.Requested, u => u.EndsWith(".nupkg") || u.EndsWith(".exe") || u.EndsWith(".xml"));
    }

    [Fact]
    public async Task A_release_without_signature_is_dropped()
    {
        var gh = new FakeGitHub();
        gh.Release("v0.5.1", DateTime.UtcNow, Manifest(Feed("0.5.1")));
        var (source, warnings) = Source(gh);

        var feed = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        Assert.Empty(feed.Assets);
        Assert.Contains("sin firma", Assert.Single(source.LastRejected).ToString());
        Assert.Single(warnings);
    }

    [Fact]
    public async Task A_manifest_swapped_after_signing_is_dropped()
    {
        var gh = new FakeGitHub();
        var signed = Feed("0.5.1");
        // Mismo manifiesto con otro tamaño de paquete (p. ej. para colar otro binario): la firma ya no cuadra.
        gh.Release("v0.5.1", DateTime.UtcNow, Manifest(Feed("0.5.1", size: 4)), Sig(ManifestSignatureTests.SigFile(signed)));
        var (source, _) = Source(gh);

        var feed = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        Assert.Empty(feed.Assets);
        Assert.Contains("no cuadra", Assert.Single(source.LastRejected).ToString());
    }

    [Fact]
    public async Task A_manifest_signed_for_another_repository_is_dropped()
    {
        // Copiado tal cual (manifiesto, firma y paquetes) desde un repositorio de pruebas firmado con la misma clave.
        var gh = new FakeGitHub();
        var feed = Feed("9.9.9");
        gh.Release("v9.9.9", DateTime.UtcNow, Manifest(feed), Sig(ManifestSignatureTests.SigFile(feed, repo: "Yovancas/claudebar-win-test")));
        var (source, _) = Source(gh);

        var result = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        Assert.Empty(result.Assets);
        Assert.Equal(0, source.LastVerified);
        Assert.Contains("no cuadra", Assert.Single(source.LastRejected).ToString());
    }

    [Fact]
    public async Task A_signed_manifest_that_does_not_parse_drops_only_its_release()
    {
        var gh = new FakeGitHub();
        var t = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var good = Feed("0.5.1");
        var junk = "<rss>no es JSON</rss>"u8.ToArray();
        gh.Release("v0.5.1", t, Manifest(good), Sig(ManifestSignatureTests.SigFile(good)));
        gh.Release("v0.5.2", t.AddDays(1), Manifest(junk), Sig(ManifestSignatureTests.SigFile(junk)));
        var (source, _) = Source(gh);

        var result = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        Assert.Equal("0.5.1", Assert.Single(result.Assets).Version.ToString());
        Assert.Equal(1, source.LastVerified);
        Assert.Contains("ilegible", Assert.Single(source.LastRejected).ToString());
    }

    [Fact]
    public async Task A_network_failure_on_one_release_drops_only_that_release()
    {
        var gh = new FakeGitHub();
        var t = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var good = Feed("0.5.1");
        gh.Release("v0.5.1", t, Manifest(good), Sig(ManifestSignatureTests.SigFile(good)));
        var other = Feed("0.5.2");
        gh.Release("v0.5.2", t.AddDays(1), Manifest(other), Sig(ManifestSignatureTests.SigFile(other)));
        // Su firma no llega (un 404, o la red que se corta a medias).
        gh.Files.Remove(FakeGitHub.Url("v0.5.2", "releases.win.json.sig"));
        var (source, warnings) = Source(gh);

        var feed = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        Assert.Equal("0.5.1", Assert.Single(feed.Assets).Version.ToString());
        Assert.Equal(1, source.LastVerified);
        var rejected = Assert.Single(source.LastRejected);
        Assert.Equal("v0.5.2", rejected.Name);
        Assert.Contains("no se pudo bajar", rejected.Reason);
        Assert.Contains("v0.5.2", Assert.Single(warnings));
    }

    [Fact]
    public async Task A_network_failure_on_a_manifest_never_lets_its_release_through()
    {
        var gh = new FakeGitHub();
        var feedBytes = Feed("0.5.2");
        gh.Release("v0.5.2", DateTime.UtcNow, Manifest(feedBytes), Sig(ManifestSignatureTests.SigFile(feedBytes)));
        gh.Files.Remove(FakeGitHub.Url("v0.5.2", "releases.win.json"));
        var (source, _) = Source(gh);

        var feed = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        Assert.Empty(feed.Assets);
        Assert.Equal(0, source.LastVerified);
        Assert.Contains("no se pudo bajar", Assert.Single(source.LastRejected).Reason);
    }

    [Fact]
    public async Task An_oversized_manifest_or_signature_drops_only_its_release()
    {
        var gh = new FakeGitHub();
        var t = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var good = Feed("0.5.1");
        gh.Release("v0.5.1", t, Manifest(good), Sig(ManifestSignatureTests.SigFile(good)));
        // Un manifiesto de más de un mega y una «firma» de un kilo (con un downloader sin tope, como este).
        gh.Release("v0.5.2", t.AddDays(1), Manifest(new byte[SignedGithubSource.MaxManifestBytes + 1]), Sig([1]));
        gh.Release("v0.5.3", t.AddDays(2), Manifest(Feed("0.5.3")), Sig(new byte[1024]));
        var (source, _) = Source(gh);

        var feed = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        Assert.Equal("0.5.1", Assert.Single(feed.Assets).Version.ToString());
        Assert.Equal(new[] { "v0.5.2", "v0.5.3" }, source.LastRejected.Select(r => r.Name).Order());
        Assert.All(source.LastRejected, r => Assert.Contains("pasa de", r.Reason));
    }

    /// <summary>Un downloader con tope (como el de la app): anota el tope con que se pide cada archivo.</summary>
    private sealed class BoundedFake(FakeGitHub inner) : IFileDownloader, IBoundedDownloader
    {
        public readonly Dictionary<string, int> Limits = new();

        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string> headers, int maxBytes)
        {
            Limits[url] = maxBytes;
            return inner.DownloadBytes(url, headers);
        }

        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
            throw new InvalidOperationException("el manifiesto y la firma se piden con tope");

        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
            inner.DownloadString(url, headers, timeout);

        public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null,
            double timeout = 30, CancellationToken cancelToken = default) =>
            inner.DownloadFile(url, targetFile, progress, headers, timeout, cancelToken);
    }

    [Fact]
    public async Task With_a_bounded_downloader_the_manifest_and_signature_are_asked_with_their_limits()
    {
        var gh = new FakeGitHub();
        var good = Feed("0.5.1");
        gh.Release("v0.5.1", DateTime.UtcNow, Manifest(good), Sig(ManifestSignatureTests.SigFile(good)));
        var bounded = new BoundedFake(gh);
        var source = new SignedGithubSource(Repo, ManifestSignatureTests.TestPublicKey, bounded, _ => { });

        var feed = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        Assert.Equal("0.5.1", Assert.Single(feed.Assets).Version.ToString());
        Assert.Equal(SignedGithubSource.MaxManifestBytes, bounded.Limits[FakeGitHub.Url("v0.5.1", "releases.win.json")]);
        Assert.Equal(256, bounded.Limits[FakeGitHub.Url("v0.5.1", "releases.win.json.sig")]);
    }

    [Theory]
    [InlineData("v0.5.2", "0.5.2")]
    [InlineData("V1.0.0-beta.1", "1.0.0-beta.1")]
    [InlineData(" 0.6.0 ", "0.6.0")]
    [InlineData("ClaudeBar 0.5.2", null)]
    [InlineData("(sin nombre)", null)]
    public void A_rejected_release_claims_its_version_by_name(string name, string? version)
    {
        Assert.Equal(version, new RejectedRelease(name, "x").ClaimedVersion?.ToString());
    }

    [Fact]
    public void Only_a_rejected_release_newer_than_the_installed_one_is_not_up_to_date()
    {
        var installed = SemanticVersion.Parse("0.5.1");
        RejectedRelease R(string name) => new(name, "la firma no cuadra");

        // Una vieja con la firma mala (o que no se pudo bajar) no tapa nada: al día.
        Assert.Null(VelopackEngine.NotUpToDateReason([R("v0.5.0")], verified: 1, installed));
        Assert.Null(VelopackEngine.NotUpToDateReason([R("v0.5.1")], verified: 1, installed));
        Assert.Null(VelopackEngine.NotUpToDateReason([], verified: 1, installed));
        // Una más nueva, o una cuyo nombre no dice qué versión es, sí: podría ser la actualización.
        Assert.Contains("v0.5.2", VelopackEngine.NotUpToDateReason([R("v0.5.0"), R("v0.5.2")], verified: 1, installed));
        Assert.DoesNotContain("v0.5.0", VelopackEngine.NotUpToDateReason([R("v0.5.0"), R("v0.5.2")], verified: 1, installed));
        Assert.NotNull(VelopackEngine.NotUpToDateReason([R("release rara")], verified: 1, installed));
        // Sin versión instalada que comparar, toda descartada cuenta.
        Assert.NotNull(VelopackEngine.NotUpToDateReason([R("v0.1.0")], verified: 1, installed: null));
        // Y sin ninguna release firmada a la vista, tampoco es «al día».
        Assert.Contains("ninguna release", VelopackEngine.NotUpToDateReason([R("v0.5.0")], verified: 0, installed));
    }

    [Fact]
    public async Task Releases_without_Velopack_do_not_count_as_verified()
    {
        var gh = new FakeGitHub();
        gh.Release("v0.4.0", DateTime.UtcNow, ("appcast.xml", "<rss/>"u8.ToArray()));
        var (source, warnings) = Source(gh);

        var result = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        Assert.Empty(result.Assets);
        Assert.Equal(0, source.LastVerified);   // VelopackEngine lo trata como fallo, no como «al día»
        Assert.Empty(source.LastRejected);
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task A_signature_that_is_not_one_is_dropped()
    {
        var gh = new FakeGitHub();
        gh.Release("v0.5.1", DateTime.UtcNow, Manifest(Feed("0.5.1")), Sig("hola"u8.ToArray()));
        var (source, _) = Source(gh);

        var feed = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        Assert.Empty(feed.Assets);
        Assert.Contains("ilegible", Assert.Single(source.LastRejected).ToString());
    }

    [Fact]
    public async Task The_verified_assets_download_through_Velopack_as_usual()
    {
        // DownloadReleaseEntry es el de GitBase: solo acepta los assets que salen de GetReleaseFeed (con su release).
        byte[] package = [7, 8, 9];
        var gh = new FakeGitHub();
        var feedBytes = FeedFor("0.5.1", package);
        gh.Release("v0.5.1", DateTime.UtcNow, Manifest(feedBytes), Sig(ManifestSignatureTests.SigFile(feedBytes)),
            ("ClaudeBar-0.5.1-full.nupkg", package));
        var (source, _) = Source(gh);
        var asset = Assert.Single((await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win")).Assets);

        var tmp = TempFile();
        try
        {
            await source.DownloadReleaseEntry(new NoLog(), asset, tmp, _ => { }, CancellationToken.None);
            Assert.Equal(package, File.ReadAllBytes(tmp));
        }
        finally { File.Delete(tmp); }

        // Un asset que no viene de un manifiesto verificado no se descarga.
        var stray = new VelopackAsset { PackageId = "ClaudeBar", FileName = "ClaudeBar-0.5.1-full.nupkg" };
        await Assert.ThrowsAsync<ArgumentException>(() => source.DownloadReleaseEntry(new NoLog(), stray, "x", _ => { }, CancellationToken.None));
    }

    [Fact]
    public async Task A_package_swapped_after_signing_is_refused_and_deleted()
    {
        // El manifiesto firmado es el bueno, pero el paquete de la release se cambió por otro del mismo tamaño.
        byte[] good = [7, 8, 9], evil = [6, 6, 6];
        var gh = new FakeGitHub();
        var feedBytes = FeedFor("0.5.1", good);
        gh.Release("v0.5.1", DateTime.UtcNow, Manifest(feedBytes), Sig(ManifestSignatureTests.SigFile(feedBytes)),
            ("ClaudeBar-0.5.1-full.nupkg", evil));
        var (source, warnings) = Source(gh);
        var asset = Assert.Single((await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win")).Assets);

        var tmp = TempFile();
        var ex = await Assert.ThrowsAsync<Velopack.Exceptions.ChecksumFailedException>(() =>
            source.DownloadReleaseEntry(new NoLog(), asset, tmp, _ => { }, CancellationToken.None));
        Assert.False(File.Exists(tmp));
        Assert.Contains("SHA-256", ex.Message);
        Assert.Contains("no cuadra", Assert.Single(warnings));
    }

    [Fact]
    public void Size_and_SHA256_must_both_match_the_signed_manifest()
    {
        byte[] package = [1, 2, 3, 4];
        var tmp = TempFile();
        File.WriteAllBytes(tmp, package);
        try
        {
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(package));
            VelopackAsset Entry(long size, string? hash) => new() { FileName = "p.nupkg", Size = size, SHA256 = hash! };

            Assert.True(SignedGithubSource.MatchesManifest(tmp, Entry(4, sha), out _));
            Assert.True(SignedGithubSource.MatchesManifest(tmp, Entry(4, sha.ToLowerInvariant()), out _));
            Assert.False(SignedGithubSource.MatchesManifest(tmp, Entry(5, sha), out var why));
            Assert.Contains("tamaño", why);
            Assert.False(SignedGithubSource.MatchesManifest(tmp, Entry(4, new string('0', 64)), out _));
            Assert.False(SignedGithubSource.MatchesManifest(tmp, Entry(4, null), out why));   // solo SHA-1: no basta
            Assert.Contains("SHA-256", why);
            Assert.False(SignedGithubSource.MatchesManifest(tmp + ".no", Entry(4, sha), out _));
        }
        finally { File.Delete(tmp); }
    }

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"claudebar-vp-{Guid.NewGuid():N}.nupkg");

    [Fact]
    public async Task A_manifest_served_with_a_BOM_still_reads()
    {
        var gh = new FakeGitHub();
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Feed("0.5.1")];
        gh.Release("v0.5.1", DateTime.UtcNow, Manifest(withBom), Sig(ManifestSignatureTests.SigFile(withBom)));
        var (source, _) = Source(gh);

        var feed = await source.GetReleaseFeed(new NoLog(), "ClaudeBar", "win");

        Assert.Equal("0.5.1", Assert.Single(feed.Assets).Version.ToString());
        Assert.Empty(source.LastRejected);
    }

    [Theory]
    [InlineData(null, Repo)]
    [InlineData("", Repo)]
    [InlineData("https://github.com/Yovancas/claudebar-win-test", "https://github.com/Yovancas/claudebar-win-test")]
    [InlineData("  https://github.com/Yovancas/claudebar-win-test/ ", "https://github.com/Yovancas/claudebar-win-test")]
    [InlineData("http://github.com/Yovancas/claudebar-win-test", Repo)]          // sin https, no
    [InlineData("https://evil.example/Yovancas/claudebar-win", Repo)]            // fuera de github.com, no
    [InlineData("https://github.com.evil.example/a/b", Repo)]
    [InlineData("https://github.com/Yovancas", Repo)]                            // sin repositorio
    [InlineData("https://github.com/a/b/releases", Repo)]
    [InlineData("https://github.com:8443/a/b", Repo)]
    [InlineData("https://github.com/a/b?x=1", Repo)]
    [InlineData("no es una url", Repo)]
    public void The_test_repository_override_only_accepts_a_github_repo(string? value, string expected)
    {
        Assert.Equal(expected, SignedGithubSource.ResolveRepoUrl(name =>
            name == SignedGithubSource.RepoOverrideVariable ? value : null));
    }

    [Fact]
    public void The_manifest_name_follows_the_channel()
    {
        Assert.Equal("releases.win.json", SignedGithubSource.ManifestName("win"));
        Assert.Equal("releases.win.json", SignedGithubSource.ManifestName(null));
        Assert.Equal("releases.beta.json", SignedGithubSource.ManifestName("beta"));
    }
}
