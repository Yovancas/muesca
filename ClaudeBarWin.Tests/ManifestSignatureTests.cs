using System.Text;
using Chaos.NaCl;
using ClaudeBarWin.Services.Updates;

namespace ClaudeBarWin.Tests;

/// <summary>
/// La firma Ed25519 del manifiesto de cada release de Velopack (releases.win.json + .sig): buena, mala, ausente o manipulada.
/// Con una clave de pruebas fija (semilla determinista), que no tiene nada que ver con la de verdad.
/// </summary>
public class ManifestSignatureTests
{
    private static readonly byte[] Seed = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] PublicKey = Ed25519.PublicKeyFromSeed(Seed);
    private static readonly byte[] PrivateKey = Ed25519.ExpandedPrivateKeyFromSeed(Seed);

    internal static readonly byte[] Manifest = Encoding.UTF8.GetBytes(
        """{"Assets":[{"PackageId":"ClaudeBarWin","Version":"0.5.1","Type":"Full","FileName":"ClaudeBarWin-0.5.1-full.nupkg","SHA1":"a","SHA256":"b","Size":3}]}""");

    /// <summary>El repositorio de las pruebas (el de la app).</summary>
    internal const string TestRepo = "Yovancas/muesca";

    /// <summary>Un .sig como el que deja scripts/release-velopack.ps1: la firma (con el contexto y el repositorio) en base64,
    /// sin salto de línea.</summary>
    internal static byte[] SigFile(byte[] manifest, byte[]? privateKey = null, string around = "", string repo = TestRepo) =>
        Encoding.UTF8.GetBytes(around + Convert.ToBase64String(
            Ed25519.Sign(ManifestSignature.SignedMessage(repo, manifest), privateKey ?? PrivateKey)) + around);

    private static ManifestVerdict Verify(byte[] manifest, byte[]? sig, byte[]? publicKey = null) =>
        ManifestSignature.Verify(manifest, sig, publicKey ?? PublicKey, TestRepo);

    internal static byte[] TestPublicKey => PublicKey;

    [Fact]
    public void A_good_signature_is_valid()
    {
        Assert.Equal(ManifestVerdict.Valid, Verify(Manifest, SigFile(Manifest)));
    }

    [Fact]
    public void A_release_without_signature_is_rejected()
    {
        Assert.Equal(ManifestVerdict.MissingSignature, Verify(Manifest, null));
    }

    [Fact]
    public void A_signature_made_with_another_key_is_rejected()
    {
        var otherSeed = Enumerable.Repeat((byte)7, 32).ToArray();
        var other = Ed25519.ExpandedPrivateKeyFromSeed(otherSeed);
        Assert.Equal(ManifestVerdict.BadSignature, Verify(Manifest, SigFile(Manifest, other)));
    }

    [Fact]
    public void A_manifest_touched_after_signing_is_rejected()
    {
        var sig = SigFile(Manifest);
        // Otra versión, otro hash o un byte cualquiera: la firma ya no vale.
        var otherVersion = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Manifest).Replace("0.5.1", "9.9.9"));
        var otherHash = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Manifest).Replace("\"SHA256\":\"b\"", "\"SHA256\":\"c\""));
        var flipped = (byte[])Manifest.Clone();
        flipped[^2] ^= 0x01;
        Assert.Equal(ManifestVerdict.BadSignature, Verify(otherVersion, sig));
        Assert.Equal(ManifestVerdict.BadSignature, Verify(otherHash, sig));
        Assert.Equal(ManifestVerdict.BadSignature, Verify(flipped, sig));
        // Ni un espacio de más al final.
        Assert.Equal(ManifestVerdict.BadSignature, Verify([.. Manifest, (byte)' '], sig));
    }

    [Fact]
    public void A_signature_touched_after_signing_is_rejected()
    {
        var raw = Ed25519.Sign(ManifestSignature.SignedMessage(TestRepo, Manifest), PrivateKey);
        raw[10] ^= 0x40;
        var sig = Encoding.UTF8.GetBytes(Convert.ToBase64String(raw));
        Assert.Equal(ManifestVerdict.BadSignature, Verify(Manifest, sig));
    }

    [Theory]
    [InlineData("")]
    [InlineData("esto no es base64 !!")]
    [InlineData("AAAA")]                                  // base64 válido, pero 3 bytes
    public void Something_that_is_not_an_Ed25519_signature_is_malformed(string content)
    {
        Assert.Equal(ManifestVerdict.MalformedSignature, Verify(Manifest, Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void A_signature_of_the_wrong_length_or_a_huge_file_is_malformed()
    {
        var sig63 = Encoding.UTF8.GetBytes(Convert.ToBase64String(Ed25519.Sign(Manifest, PrivateKey)[..63]));
        var huge = Encoding.UTF8.GetBytes(new string('A', 4096));
        Assert.Equal(ManifestVerdict.MalformedSignature, Verify(Manifest, sig63));
        Assert.Equal(ManifestVerdict.MalformedSignature, Verify(Manifest, huge));
    }

    [Fact]
    public void Line_breaks_spaces_and_a_BOM_around_the_signature_are_tolerated()
    {
        Assert.Equal(ManifestVerdict.Valid, Verify(Manifest, SigFile(Manifest, around: "\r\n")));
        Assert.Equal(ManifestVerdict.Valid, Verify(Manifest, SigFile(Manifest, around: "  ")));
        byte[] bom = [0xEF, 0xBB, 0xBF];
        Assert.Equal(ManifestVerdict.Valid, Verify(Manifest, [.. bom, .. SigFile(Manifest)]));
    }

    [Fact]
    public void The_signature_covers_the_bytes_as_served_BOM_included()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Manifest];
        Assert.Equal(ManifestVerdict.Valid, Verify(withBom, SigFile(withBom)));
        Assert.Equal(ManifestVerdict.BadSignature, Verify(Manifest, SigFile(withBom)));
        // Lo que se le pasa a Velopack ya va sin el BOM.
        Assert.StartsWith("{", ManifestSignature.ManifestText(withBom));
    }

    [Fact]
    public void Rfc8032_test_vector_1_verifies()
    {
        // RFC 8032, 7.1, TEST 1 (mensaje vacío): la librería es Ed25519 estándar, la misma que usaba NetSparkle.
        var publicKey = Convert.FromHexString("d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
        var signature = Convert.FromHexString(
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");
        var seed = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        Assert.Equal(publicKey, Ed25519.PublicKeyFromSeed(seed));
        Assert.True(Ed25519.Verify(signature, [], publicKey));
        Assert.Equal(signature, Ed25519.Sign([], Ed25519.ExpandedPrivateKeyFromSeed(seed)));
    }

    [Fact]
    public void The_public_key_is_the_one_NetSparkle_used_until_0_4()
    {
        Assert.Equal("UxMaJjAdtg6sQZrydaUvC0N088ZIheKsFw+HjQCsLS8=", ManifestSignature.PublicKeyBase64);
        Assert.Equal(32, ManifestSignature.PublicKey.Length);
        // Y con ella, nuestra firma de pruebas no vale.
        Assert.Equal(ManifestVerdict.BadSignature, Verify(Manifest, SigFile(Manifest), ManifestSignature.PublicKey));
    }

    [Fact]
    public void A_manifest_signed_for_another_repository_is_rejected()
    {
        // El de un repositorio de pruebas, firmado con la misma clave, no vale en el de la app (ni al revés).
        var test = SigFile(Manifest, repo: "Yovancas/claudebar-win-test");
        Assert.Equal(ManifestVerdict.BadSignature, Verify(Manifest, test));
        Assert.Equal(ManifestVerdict.Valid, ManifestSignature.Verify(Manifest, test, PublicKey, "https://github.com/Yovancas/claudebar-win-test"));
    }

    [Fact]
    public void Something_else_signed_with_the_same_key_is_not_a_manifest()
    {
        // NetSparkle firmaba los bytes sueltos (appcast.xml + .signature) con esta misma clave: sin el contexto, no vale.
        var bare = Encoding.UTF8.GetBytes(Convert.ToBase64String(Ed25519.Sign(Manifest, PrivateKey)));
        Assert.Equal(ManifestVerdict.BadSignature, Verify(Manifest, bare));
    }

    [Theory]
    [InlineData("Yovancas/muesca")]
    [InlineData("yovancas/MUESCA")]
    [InlineData("/Yovancas/muesca/")]
    [InlineData("https://github.com/Yovancas/muesca")]
    [InlineData("https://github.com/Yovancas/muesca.git")]
    public void The_repository_is_compared_without_case_or_URL_noise(string repo)
    {
        Assert.Equal("yovancas/muesca", ManifestSignature.NormalizeRepo(repo));
        Assert.Equal(ManifestVerdict.Valid, ManifestSignature.Verify(Manifest, SigFile(Manifest), PublicKey, repo));
    }

    [Fact]
    public void The_signed_message_is_the_context_the_repository_and_the_bytes()
    {
        var message = ManifestSignature.SignedMessage("https://github.com/Yovancas/muesca", [1, 2]);
        byte[] expected = [.. "claudebar/velopack-manifest/v1\nyovancas/muesca\n"u8.ToArray(), 1, 2];
        Assert.Equal(expected, message);
    }

    [Fact]
    public void A_public_key_of_the_wrong_size_is_a_programming_error()
    {
        Assert.Throws<ArgumentException>(() => Verify(Manifest, SigFile(Manifest), new byte[31]));
    }
}
