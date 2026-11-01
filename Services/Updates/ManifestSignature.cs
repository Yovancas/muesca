using System.Text;
using Chaos.NaCl;

namespace ClaudeBarWin.Services.Updates;

/// <summary>Qué pasa con la firma del manifiesto de una release.</summary>
public enum ManifestVerdict
{
    Valid,
    /// <summary>La release no trae <c>releases.win.json.sig</c>.</summary>
    MissingSignature,
    /// <summary>El .sig no es una firma Ed25519 en base64 (64 bytes).</summary>
    MalformedSignature,
    /// <summary>La firma no es de nuestra clave, o el manifiesto (o la firma) se tocó después de firmarlo.</summary>
    BadSignature,
}

/// <summary>
/// Firma Ed25519 del manifiesto de cada release de Velopack (<c>releases.win.json</c>). Velopack solo comprueba que cada
/// paquete descargado (completo o delta) cuadre con el SHA-256 y el tamaño que dice el manifiesto; con el manifiesto
/// firmado, todo lo que se descarga queda atado a nuestra clave, como cuando NetSparkle verificaba el instalador. Es la
/// misma clave que hasta la 0.4. Se firman los bytes tal cual se sirven, sin normalizar el JSON (nada se interpreta antes
/// de verificar), detrás de un contexto: <c>claudebar/velopack-manifest/v1\n&lt;dueño/repo&gt;\n</c>
/// (<see cref="SignedMessage"/>). Con él, nada firmado con la misma clave para otra cosa (un appcast de NetSparkle, el
/// manifiesto de un repositorio de pruebas) vale como manifiesto de este repositorio.
/// </summary>
public static class ManifestSignature
{
    /// <summary>Clave pública Ed25519 (la privada NUNCA va al repo). La misma que usaba NetSparkle hasta la 0.4; también la
    /// lleva scripts/release-velopack.ps1, que se niega a firmar con una privada que no le corresponda.</summary>
    public const string PublicKeyBase64 = "UxMaJjAdtg6sQZrydaUvC0N088ZIheKsFw+HjQCsLS8=";

    /// <summary>La firma va junto al manifiesto con este sufijo: <c>releases.win.json.sig</c>.</summary>
    public const string SignatureSuffix = ".sig";

    /// <summary>Primera línea de lo que se firma (scripts/release-velopack.ps1 lleva la misma; ReleaseScriptTests lo vigila).</summary>
    public const string SigningContext = "claudebar/velopack-manifest/v1";

    /// <summary>Una firma en base64 son 88 caracteres; con un salto de línea o un BOM, poco más. Algo mayor no es una firma.</summary>
    internal const int MaxSignatureFileBytes = 256;

    public static byte[] PublicKey => Convert.FromBase64String(PublicKeyBase64);

    /// <summary>Verifica <paramref name="manifest"/> (bytes tal cual se descargaron) del repositorio <paramref name="repo"/>
    /// con el contenido del .sig (null = la release no lo trae).</summary>
    public static ManifestVerdict Verify(byte[] manifest, byte[]? signatureFile, byte[] publicKey, string repo)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(publicKey);
        if (publicKey.Length != Ed25519.PublicKeySizeInBytes)
            throw new ArgumentException($"la clave pública Ed25519 tiene {Ed25519.PublicKeySizeInBytes} bytes", nameof(publicKey));
        if (signatureFile is null) return ManifestVerdict.MissingSignature;
        if (!TryParseSignature(signatureFile, out var signature)) return ManifestVerdict.MalformedSignature;
        return Ed25519.Verify(signature, SignedMessage(repo, manifest), publicKey) ? ManifestVerdict.Valid : ManifestVerdict.BadSignature;
    }

    /// <summary>Lo que se firma: <c>claudebar/velopack-manifest/v1\n&lt;dueño/repo en minúsculas&gt;\n</c> y los bytes del
    /// manifiesto.</summary>
    public static byte[] SignedMessage(string repo, byte[] manifest)
    {
        var header = Encoding.UTF8.GetBytes($"{SigningContext}\n{NormalizeRepo(repo)}\n");
        var message = new byte[header.Length + manifest.Length];
        header.CopyTo(message, 0);
        manifest.CopyTo(message, header.Length);
        return message;
    }

    /// <summary><c>https://github.com/Dueño/Repo</c> o <c>Dueño/Repo</c> → <c>dueño/repo</c>.</summary>
    public static string NormalizeRepo(string repo)
    {
        var r = repo.Trim();
        if (Uri.TryCreate(r, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            r = uri.AbsolutePath;
        r = r.Trim('/');
        if (r.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) r = r[..^4];
        return r.ToLowerInvariant();
    }

    /// <summary>El .sig lleva la firma (64 bytes) en base64, como los <c>.signature</c> de NetSparkle. Se toleran espacios,
    /// saltos de línea y un BOM alrededor.</summary>
    internal static bool TryParseSignature(byte[] file, out byte[] signature)
    {
        signature = Array.Empty<byte>();
        if (file.Length == 0 || file.Length > MaxSignatureFileBytes) return false;
        var text = Encoding.UTF8.GetString(file).Trim().TrimStart('\uFEFF').Trim();
        try { signature = Convert.FromBase64String(text); }
        catch (FormatException) { return false; }
        return signature.Length == Ed25519.SignatureSizeInBytes;
    }

    /// <summary>El manifiesto ya verificado, como texto para Velopack (sin el BOM, si lo trae).</summary>
    internal static string ManifestText(byte[] manifest) => Encoding.UTF8.GetString(manifest).TrimStart('\uFEFF');

    /// <summary>Para el log.</summary>
    internal static string Describe(ManifestVerdict verdict) => verdict switch
    {
        ManifestVerdict.Valid => "firma válida",
        ManifestVerdict.MissingSignature => "sin firma (" + SignatureSuffix + ")",
        ManifestVerdict.MalformedSignature => "firma ilegible",
        _ => "la firma no cuadra con el manifiesto",
    };
}
