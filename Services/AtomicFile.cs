using System.Text;

namespace ClaudeBarWin.Services;

/// <summary>
/// Escritura atómica de archivos de texto pequeños (config, caché, settings.json de Claude Code).
/// Escribe a un tmp ÚNICO en el mismo directorio (mismo volumen), fuerza los datos a disco y lo
/// mueve sobre el destino con reemplazo (MoveFileEx + REPLACE_EXISTING, atómico en NTFS). El
/// destino nunca queda truncado ni a medias: o se ve el contenido viejo o el nuevo.
/// Sin <c>Flush(true)</c> el rename podría persistir antes que los datos tras un corte de luz.
/// No bloquea el destino en exclusiva: Claude Code no coopera con locks y un FileShare.None solo
/// haría fallar sus lecturas (auditoría cruzada Claude+Codex, v0.3.10).
/// </summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string contents) => TryWriteAllText(path, contents, static () => true);

    /// <summary>
    /// Como <see cref="WriteAllText"/>, pero comprueba <paramref name="stillValid"/> JUSTO antes de cada intento de
    /// reemplazo, con el temporal ya escrito y en disco. Si deja de valer (otro proceso cambió el destino mientras se
    /// preparaba el temporal) no escribe nada y devuelve false para que el llamador rehaga su cambio sobre lo nuevo
    /// (v0.4, V04-01 de la revisión de Codex: antes la relectura iba ANTES de preparar el temporal).
    /// </summary>
    public static bool TryWriteAllText(string path, string contents, Func<bool> stillValid)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Utf8NoBom.GetBytes(contents);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            // Un lector que abrió el destino sin FILE_SHARE_DELETE (antivirus, otro proceso) hace
            // fallar el reemplazo con IOException/UnauthorizedAccess: reintento corto y acotado.
            for (int attempt = 1; ; attempt++)
            {
                if (!stillValid())
                {
                    File.Delete(tmp);
                    return false;
                }
                try
                {
                    File.Move(tmp, full, overwrite: true);
                    return true;
                }
                catch (Exception ex) when (attempt < 4 && ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(40 * attempt);
                }
            }
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best-effort: no dejar tmp huérfano */ }
            throw;
        }
    }
}
