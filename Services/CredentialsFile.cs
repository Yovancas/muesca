using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeBarWin.Services;

/// <summary>
/// Token de acceso OAuth de Claude Code, en memoria y solo mientras dura una petición: se lee justo antes de llamar a
/// la API y nadie lo guarda en un campo. Clase y no <c>record</c>: un record generaría <c>ToString</c>/<c>PrintMembers</c>
/// con el token dentro. En logs, excepciones, JSON o el depurador sale como «Creds(***)».
/// </summary>
[DebuggerDisplay("Creds(***)")]
internal sealed class Creds
{
    internal Creds(string accessToken, long? expiresAtMs)
    {
        AccessToken = accessToken;
        ExpiresAtMs = expiresAtMs;
    }

    /// <summary>El secreto. Solo lo lee <see cref="OAuthTransport"/> para poner la cabecera Authorization.</summary>
    [JsonIgnore, DebuggerBrowsable(DebuggerBrowsableState.Never)]
    internal string AccessToken { get; }

    /// <summary><c>expiresAt</c> en ms Unix; null si el archivo no lo trae.</summary>
    internal long? ExpiresAtMs { get; }

    public override string ToString() => "Creds(***)";
}

/// <summary>
/// Único sitio que abre <c>~/.claude/.credentials.json</c>. Ese archivo lo escribe Claude Code, en claro: ClaudeBar NO
/// lo cifra (Claude Code dejaría de poder leerlo y tendrías que volver a iniciar sesión), no lo escribe y no hace
/// copias de él.
/// <para>
/// Lista blanca: del bloque <c>claudeAiOauth</c> solo se miran <c>accessToken</c>, <c>expiresAt</c>,
/// <c>subscriptionType</c> y <c>rateLimitTier</c>. Todo lo demás (<c>refreshToken</c> incluido) se salta sin
/// convertirlo nunca en texto, y el token solo se decodifica si se pide (<see cref="ReadToken"/>); para el plan basta
/// con saber si hay uno. Los bytes se leen sin el búfer interno de FileStream y se ponen a cero al terminar.
/// </para>
/// </summary>
internal static class CredentialsFile
{
    /// <summary>El archivo real pesa menos de 1 KB; algo mucho mayor no es el de Claude Code y no se lee.</summary>
    internal const int MaxBytes = 64 * 1024;

    /// <summary>Lo que se saca del bloque <c>claudeAiOauth</c>. Struct sin record: su ToString es el nombre del tipo.</summary>
    private struct OAuthFields
    {
        public bool HasBlock;
        public bool HasToken;
        public string? Token;
        public long? ExpiresAtMs;
        public string? SubscriptionType;
        public string? RateLimitTier;
    }

    /// <summary>Huella del archivo (fecha de escritura y tamaño) para saber si cambió SIN abrirlo. Null si no existe.</summary>
    internal static (DateTime WrittenUtc, long Length)? Stamp(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? (fi.LastWriteTimeUtc, fi.Length) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Token de acceso y caducidad, para una petición. Null si no hay archivo, no hay sesión o no se puede leer ahora
    /// mismo (a medio escribir, bloqueado…). Nunca lanza ni registra nada.
    /// </summary>
    internal static Creds? ReadToken(string path)
    {
        if (!TryReadFile(path, decodeToken: true, out var f) || string.IsNullOrEmpty(f.Token)) return null;
        return new Creds(f.Token, f.ExpiresAtMs);
    }

    /// <summary>
    /// Plan + si hay sesión iniciada (sin decodificar el token: solo si su valor no está vacío) + cuándo se escribió.
    /// Sin archivo = sesión cerrada. Null = ilegible ahora mismo (p. ej. Claude Code reescribiéndolo): no es un
    /// cambio de cuenta, el llamador debe seguir con lo último que sabía.
    /// </summary>
    internal static CredentialsState? ReadState(string path)
    {
        try
        {
            if (!File.Exists(path)) return new CredentialsState(new PlanInfo("", ""), HasToken: false, WrittenUtc: DateTime.MinValue);
            var written = File.GetLastWriteTimeUtc(path);
            if (!TryReadFile(path, decodeToken: false, out var f)) return null;
            if (!f.HasBlock) return new CredentialsState(new PlanInfo("", ""), HasToken: false, written); // /logout deja el archivo sin el bloque
            return new CredentialsState(new PlanInfo(f.SubscriptionType ?? "", f.RateLimitTier ?? ""), f.HasToken, written);
        }
        catch
        {
            return null; // a medio escribir, bloqueado… — el plan es cosmético
        }
    }

    private static bool TryReadFile(string path, bool decodeToken, out OAuthFields fields)
    {
        fields = default;
        byte[]? buf = null;
        try
        {
            // Lectura compartida: Claude Code puede estar reescribiéndolo. bufferSize 0 = sin el búfer interno de
            // FileStream (que no se limpia): los bytes solo pasan por `buf`, que se pone a cero en el finally.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0);
            if (fs.Length > MaxBytes) return false;
            buf = new byte[MaxBytes + 1];
            int len = 0, n;
            while (len < buf.Length && (n = fs.Read(buf, len, buf.Length - len)) > 0) len += n;
            if (len > MaxBytes) return false; // creció mientras se leía
            return TryScan(buf.AsSpan(0, len), decodeToken, out fields);
        }
        catch
        {
            return false; // no existe, bloqueado, sin permiso…: sin log, el llamador ya sabe qué hacer sin token
        }
        finally
        {
            if (buf is not null) CryptographicOperations.ZeroMemory(buf);
        }
    }

    /// <summary>Recorre el JSON sin materializar nada fuera de la lista blanca. False si no es un objeto JSON válido.</summary>
    private static bool TryScan(ReadOnlySpan<byte> utf8, bool decodeToken, out OAuthFields f)
    {
        f = default;
        if (utf8.StartsWith("\uFEFF"u8)) utf8 = utf8[3..]; // BOM
        var r = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        try
        {
            if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return false;
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                if (r.ValueTextEquals("claudeAiOauth"u8))
                {
                    r.Read();
                    if (r.TokenType == JsonTokenType.StartObject)
                    {
                        f = default; // si se repite la clave, manda la última (como JsonDocument)
                        f.HasBlock = true;
                        ScanOAuth(ref r, decodeToken, ref f);
                    }
                    else r.Skip();
                }
                else r.Skip(); // otras claves de primer nivel (mcpOAuth…): ni se miran
            }
            if (r.TokenType != JsonTokenType.EndObject) return false;
            return !r.Read(); // nada detrás del objeto (lo que no es JSON lanza y cae al catch)
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            f = default;
            return false; // a medio escribir o roto
        }
    }

    private static void ScanOAuth(ref Utf8JsonReader r, bool decodeToken, ref OAuthFields f)
    {
        while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
        {
            if (r.ValueTextEquals("accessToken"u8))
            {
                r.Read();
                if (r.TokenType == JsonTokenType.String)
                {
                    // Vacío = sin sesión. Se mira la longitud de los bytes, sin convertirlos en texto.
                    f.HasToken = (r.HasValueSequence ? r.ValueSequence.Length : r.ValueSpan.Length) > 0;
                    f.Token = decodeToken && f.HasToken ? r.GetString() : null;
                }
                else r.Skip();
            }
            else if (r.ValueTextEquals("expiresAt"u8))
            {
                r.Read();
                if (r.TokenType == JsonTokenType.Number && r.TryGetInt64(out var ms)) f.ExpiresAtMs = ms;
                else r.Skip();
            }
            else if (r.ValueTextEquals("subscriptionType"u8)) f.SubscriptionType = ReadString(ref r);
            else if (r.ValueTextEquals("rateLimitTier"u8)) f.RateLimitTier = ReadString(ref r);
            else r.Skip(); // refreshToken, scopes… se saltan sin decodificarlos
        }
    }

    private static string? ReadString(ref Utf8JsonReader r)
    {
        r.Read();
        if (r.TokenType == JsonTokenType.String) return r.GetString();
        r.Skip();
        return null;
    }
}
