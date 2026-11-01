namespace ClaudeBarWin.Services;

/// <summary>
/// v0.4 — cómo se reconoce el helper en un comando de settings.json. Solo vale la gramática que genera
/// <see cref="ShellQuote"/> y el nombre de ejecutable EXACTO <c>claudebar-helper(.exe)</c>: un «contiene la marca»
/// hacía nuestros a <c>not-claudebar-helper.exe</c> o a un hook que solo la mencionaba en un argumento, y al
/// desactivar/desinstalar ClaudeBar se los llevaba por delante (V04-03/04 de la revisión de Codex).
/// <para>Edición de la Store: el helper se lanza por el alias del paquete, <c>muesca-helper.exe</c>
/// (<see cref="Packaging.StoreEdition.HelperAlias"/>), que se reconoce igual, con nombre exacto.</para>
/// </summary>
internal static class HelperCommand
{
    /// <summary>Nombres del helper sin «.exe»: el exe de siempre y el alias del paquete de la Store.</summary>
    internal static readonly string[] Names = { "claudebar-helper", "muesca-helper" };

    /// <summary>
    /// Ruta del helper: sin comillas, entre '…' de Git Bash (con <c>'\''</c> dentro) o <c>&amp; '…'</c> de PowerShell
    /// (con <c>''</c> dentro). El directorio es opcional y, si está, acaba en / o \ justo antes del nombre.
    /// </summary>
    internal const string ExePattern =
        @"(?:&\s*)?(?:'(?:(?:[^']|'\\''|'')*[/\\])?(?:claudebar|muesca)-helper(?:\.exe)?'|(?:[^\s']*[/\\])?(?:claudebar|muesca)-helper(?:\.exe)?)";

    /// <summary>
    /// La ruta del helper como cadena literal de PowerShell (<c>'…'</c> con <c>''</c> dentro, la de
    /// <see cref="ShellQuote.PowerShellLiteral"/>), con el nombre EXACTO: la usa el enganche resistente de la statusline en
    /// <c>Test-Path -LiteralPath</c>.
    /// </summary>
    internal const string PowerShellLiteralPattern =
        @"'(?:(?:[^']|'')*[/\\])?(?:claudebar|muesca)-helper(?:\.exe)?'";

    /// <summary>¿Es esta ruta (exec form, sin shell) exactamente nuestro helper?</summary>
    public static bool IsHelperExePath(string path)
    {
        var name = Path.GetFileName(path.Trim().Trim('"', '\'').Replace('/', '\\'));
        foreach (var helper in Names)
            if (name.Equals(helper + ".exe", StringComparison.OrdinalIgnoreCase) || name.Equals(helper, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>¿Menciona el texto alguno de los nombres del helper? Solo un filtro rápido antes de las expresiones exactas.</summary>
    public static bool Mentions(string text)
    {
        foreach (var helper in Names)
            if (text.Contains(helper, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
