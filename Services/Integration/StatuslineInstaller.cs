using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClaudeBarWin.Services;

public enum TapState { NotInstalled, Tee, Standalone }

public sealed class TapInstallException : Exception
{
    /// <summary>"type" (statusLine no es un comando) o "compound" (&&, ||, ;, saltos de línea).</summary>
    public string Code { get; }
    public TapInstallException(string code, string message) : base(message) => Code = code;
}

/// <summary>
/// v0.4 — engancha claudebar-helper delante de la statusLine del usuario («tee»): el helper guarda los rate_limits y le pasa
/// a la del usuario (C) el mismo JSON, así que su barra se ve igual. Sin statusLine previa crea una propia
/// (<c>helper statusline</c>). Desactivar restaura C exacto. Las transformaciones son puras sobre el texto de settings.json; la
/// E/S con backup la hace <see cref="ClaudeSettingsFile.Rewrite"/>. Un comando compuesto se rechaza: con «tee | a &amp;&amp; b»
/// el stdin solo llegaría a «a».
/// <para><b>Resistente a que falte el helper</b> (docs/msix.md): MSIX no ejecuta nada al desinstalar, y quien quite Muesca sin
/// apagar el enganche dejaría su statusline apuntando a un helper que ya no existe (y sin la suya). Por eso el «tee» comprueba
/// antes que el helper está y, si no, le pasa el JSON directo a C, en la sintaxis del shell con el que lo lanza Claude Code
/// (<see cref="TeeCommand"/>):</para>
/// <list type="bullet">
/// <item>Git Bash: <c>if [ -e H ]; then H statusline --tee; else cat; fi | C</c>.</item>
/// <item>PowerShell: <c>$OutputEncoding = [Text.UTF8Encoding]::new($false); &amp; { if (Test-Path -LiteralPath 'H') { H statusline
/// --tee } else { [IO.StreamReader]::new([Console]::OpenStandardInput()).ReadToEnd() } } | C</c>. Sin el helper, stdin se lee
/// como UTF-8 y se pasa en UTF-8 (PowerShell 5.1 lo pasaría en ASCII); con él, lo que sale del helper ya es ASCII
/// (<c>AsciiJson</c>), igual que antes.</item>
/// </list>
/// El formato de antes (<c>H statusline --tee | C</c>) se sigue reconociendo: el arranque lo pone al día (<see cref="Upgrade"/>)
/// y apagarlo lo quita. La standalone no cambia: sin statusline del usuario no hay nada que conservar.
/// </summary>
public static class StatuslineInstaller
{
    private const string ExeName = HelperCommand.ExePattern;

    // Lo nuestro, con la ruta del helper con nombre EXACTO (HelperCommand) y C capturado tal cual, espacios incluidos:
    // el «tee» resistente de Git Bash, el de PowerShell, y el de antes (o la standalone).
    private const string BashTee =
        @"if \[ -e " + ExeName + @" \]; then " + ExeName + @" statusline --tee; else cat; fi \| (?<orig>.*)";
    private const string PowerShellTee =
        @"\$OutputEncoding = \[Text\.UTF8Encoding\]::new\(\$false\); & \{ if \(Test-Path -LiteralPath " +
        HelperCommand.PowerShellLiteralPattern + @"\) \{ " + ExeName + @" statusline --tee \} else \{ " +
        @"\[IO\.StreamReader\]::new\(\[Console\]::OpenStandardInput\(\)\)\.ReadToEnd\(\) \} \} \| (?<orig>.*)";
    private const string LegacyOrStandalone = ExeName + @"\s+statusline(?:\s+--tee \| (?<orig>.*))?";

    private static readonly Regex Ours = new(
        @"^\s*(?:" + BashTee + "|" + PowerShellTee + "|" + LegacyOrStandalone + @")\s*$",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Compound = new(@"&&|\|\||;|\r|\n", RegexOptions.CultureInvariant);

    /// <summary>Nuestra statusline propia (sin la del usuario): <c>H statusline</c>.</summary>
    internal static string StandaloneCommand(string helperPath, ClaudeShell shell) =>
        ShellQuote.Command(helperPath, shell) + " statusline";

    /// <summary>El «tee» resistente delante de <paramref name="original"/>, en la sintaxis de <paramref name="shell"/> (ver la
    /// clase). PURO.</summary>
    internal static string TeeCommand(string helperPath, ClaudeShell shell, string original)
    {
        var exe = ShellQuote.Command(helperPath, shell);
        return shell == ClaudeShell.GitBash
            ? $"if [ -e {exe} ]; then {exe} statusline --tee; else cat; fi | {original}"
            : "$OutputEncoding = [Text.UTF8Encoding]::new($false); & { if (Test-Path -LiteralPath " + ShellQuote.PowerShellLiteral(helperPath) +
              ") { " + exe + " statusline --tee } else { [IO.StreamReader]::new([Console]::OpenStandardInput()).ReadToEnd() } } | " + original;
    }

    public static TapState State(string json)
    {
        var cmd = Command(ClaudeSettingsFile.ParseRoot(json));
        if (cmd is null) return TapState.NotInstalled;
        var original = OriginalOf(cmd, out bool ours);
        if (!ours) return TapState.NotInstalled;
        return original is null ? TapState.Standalone : TapState.Tee;
    }

    /// <param name="helperPath">Ruta del helper (sin citar: se cita para <paramref name="shell"/>).</param>
    /// <param name="shell">El shell con el que Claude Code lanza la statusline (<see cref="ClaudeShellDetector"/>).</param>
    public static string Enable(string json, string helperPath, ClaudeShell shell)
    {
        var root = ClaudeSettingsFile.ParseRoot(json);
        var standalone = StandaloneCommand(helperPath, shell);
        if (root["statusLine"] is not JsonObject sl)
        {
            root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = standalone };
            return ClaudeSettingsFile.Serialize(root);
        }
        var type = sl["type"] is JsonValue t && t.TryGetValue<string>(out var ts) ? ts : "command";
        if (type != "command") throw new TapInstallException("type", $"statusLine.type = {type}");

        var current = Command(root) ?? "";
        var original = OriginalOf(current, out bool ours);
        if (ours && original is null) // ya era la nuestra standalone: solo refrescar el prefijo
        {
            sl["command"] = standalone;
            return ClaudeSettingsFile.Serialize(root);
        }
        original ??= current;
        if (string.IsNullOrWhiteSpace(original))
        {
            sl["command"] = standalone;
            return ClaudeSettingsFile.Serialize(root);
        }
        if (Compound.IsMatch(original)) throw new TapInstallException("compound", original);
        sl["command"] = TeeCommand(helperPath, shell, original);
        return ClaudeSettingsFile.Serialize(root);
    }

    public static string Disable(string json)
    {
        var root = ClaudeSettingsFile.ParseRoot(json);
        if (root["statusLine"] is not JsonObject sl) return ClaudeSettingsFile.Serialize(root);
        var original = OriginalOf(Command(root) ?? "", out bool ours);
        if (!ours) return ClaudeSettingsFile.Serialize(root);
        if (original is null) root.Remove("statusLine");
        else sl["command"] = original;
        return ClaudeSettingsFile.Serialize(root);
    }

    /// <summary>JSON con nuestro enganche al día (otra ruta, otro shell, el formato de antes), o null si no hace falta.</summary>
    public static string? Upgrade(string json, string helperPath, ClaudeShell shell)
    {
        if (State(json) == TapState.NotInstalled) return null;
        var desired = Enable(Disable(json), helperPath, shell);
        return Command(ClaudeSettingsFile.ParseRoot(desired)) == Command(ClaudeSettingsFile.ParseRoot(json)) ? null : desired;
    }

    private static (DateTime Mtime, long Len, bool On) _cache;

    /// <summary>¿Está enganchada en el settings.json real? Cacheado por (mtime, tamaño): la UI lo consulta al repintar.</summary>
    public static bool IsInstalled()
    {
        try
        {
            var fi = new FileInfo(ClaudeSettingsFile.SettingsPath);
            if (!fi.Exists) return false;
            var c = _cache;
            if (c.Mtime == fi.LastWriteTimeUtc && c.Len == fi.Length) return c.On;
            bool on = State(File.ReadAllText(fi.FullName)) != TapState.NotInstalled;
            _cache = (fi.LastWriteTimeUtc, fi.Length, on);
            return on;
        }
        catch { return false; }
    }

    private static string? Command(JsonObject root) =>
        root["statusLine"] is JsonObject sl && sl["command"] is JsonValue c && c.TryGetValue<string>(out var s) ? s : null;

    /// <summary>C original si el comando es nuestro en forma tee; null en la standalone. <paramref name="ours"/> = es nuestro.</summary>
    private static string? OriginalOf(string command, out bool ours)
    {
        var m = HelperCommand.Mentions(command) ? Ours.Match(command) : Match.Empty;
        ours = m.Success;
        return m.Success && m.Groups["orig"].Success ? m.Groups["orig"].Value : null;
    }
}
