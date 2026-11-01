using System.Text.Json.Nodes;
using ClaudeBarWin.Services;

/// <summary>
/// El enganche de la statusline (v0.4): el helper delante de la statusline del usuario. Desde la edición de la Store es
/// <b>resistente a que falte el helper</b> (MSIX no ejecuta nada al desinstalar: si se quita Muesca sin apagarlo, settings.json
/// seguiría apuntando a un helper que ya no existe): si no está, el JSON va directo al comando del usuario, en Git Bash
/// (<c>if [ -e H ]; then H statusline --tee; else cat; fi | C</c>) y en PowerShell (<c>Test-Path</c>, y si no, stdin tal
/// cual). El formato de antes (<c>H statusline --tee | C</c>) se sigue reconociendo para quitarlo o ponerlo al día.
/// </summary>
public class StatuslineInstallerTests
{
    private const string H = "C:/L/ClaudeBarWin/bin/claudebar-helper.exe";
    private const string Spaced = @"C:\Users\John Smith\claudebar-helper.exe";
    private const string Apostrophe = @"C:\Users\O'Brien\claudebar-helper.exe";

    private static string Cmd(string json) => JsonNode.Parse(json)!["statusLine"]!["command"]!.GetValue<string>();

    private static string WithCommand(string command) =>
        new JsonObject { ["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = command } }.ToJsonString();

    private static string BashTee(string exe, string original) =>
        $"if [ -e {exe} ]; then {exe} statusline --tee; else cat; fi | {original}";

    private static string PsTee(string literal, string exe, string original) =>
        "$OutputEncoding = [Text.UTF8Encoding]::new($false); & { if (Test-Path -LiteralPath " + literal + ") { " + exe +
        " statusline --tee } else { [IO.StreamReader]::new([Console]::OpenStandardInput()).ReadToEnd() } } | " + original;

    public static IEnumerable<object[]> Helpers()
    {
        foreach (var path in new[] { H, Spaced, Apostrophe })
            foreach (var shell in new[] { ClaudeShell.GitBash, ClaudeShell.PowerShell })
                yield return new object[] { path, shell };
    }

    // ---------------------------------------------------------------- el formato

    [Fact]
    public void In_Git_Bash_the_tee_falls_back_to_the_original_when_the_helper_is_missing()
    {
        var outJson = StatuslineInstaller.Enable(WithCommand("ccstatusline"), H, ClaudeShell.GitBash);
        Assert.Equal(BashTee(H, "ccstatusline"), Cmd(outJson));

        var quoted = StatuslineInstaller.Enable(WithCommand("ccstatusline"), Apostrophe, ClaudeShell.GitBash);
        Assert.Equal(BashTee(@"'C:/Users/O'\''Brien/claudebar-helper.exe'", "ccstatusline"), Cmd(quoted));
    }

    [Fact]
    public void In_PowerShell_the_tee_falls_back_to_the_original_when_the_helper_is_missing()
    {
        var outJson = StatuslineInstaller.Enable(WithCommand("ccstatusline"), H, ClaudeShell.PowerShell);
        Assert.Equal(PsTee($"'{H}'", H, "ccstatusline"), Cmd(outJson));

        var quoted = StatuslineInstaller.Enable(WithCommand("ccstatusline"), Apostrophe, ClaudeShell.PowerShell);
        Assert.Equal(PsTee("'C:/Users/O''Brien/claudebar-helper.exe'", "& 'C:/Users/O''Brien/claudebar-helper.exe'", "ccstatusline"),
            Cmd(quoted));
    }

    [Fact]
    public void Enable_wraps_existing_command_and_keeps_other_fields()
    {
        var src = """{ "statusLine": { "type": "command", "command": "ccstatusline", "padding": 0, "refreshInterval": 5 }, "model": "opus" }""";
        var outJson = StatuslineInstaller.Enable(src, H, ClaudeShell.GitBash);
        var sl = JsonNode.Parse(outJson)!["statusLine"]!;
        Assert.Equal(0, sl["padding"]!.GetValue<int>());
        Assert.Equal(5, sl["refreshInterval"]!.GetValue<int>());
        Assert.Equal("opus", JsonNode.Parse(outJson)!["model"]!.GetValue<string>());
        Assert.Equal(TapState.Tee, StatuslineInstaller.State(outJson));
    }

    [Theory]
    [InlineData("ccstatusline")]
    [InlineData("npx -y ccstatusline@latest")]
    [InlineData("powershell -NoProfile -File C:/Users/u/.claude/statusline.ps1")]
    [InlineData("bash ~/.claude/statusline.sh | head -1")]
    [InlineData("node \"C:/Users/John Smith/sl.mjs\"")]
    [InlineData("& 'C:/Users/O''Brien/statusline.exe'   ")]
    public void Disable_restores_the_exact_original(string original)
    {
        foreach (var helper in Helpers())
        {
            var on = StatuslineInstaller.Enable(WithCommand(original), (string)helper[0], (ClaudeShell)helper[1]);
            Assert.Equal(TapState.Tee, StatuslineInstaller.State(on));
            var restored = StatuslineInstaller.Disable(on);
            Assert.Equal(original, Cmd(restored));
            Assert.Equal(TapState.NotInstalled, StatuslineInstaller.State(restored));
        }
    }

    [Fact]
    public void Enable_without_statusline_creates_standalone_and_disable_removes_it()
    {
        var outJson = StatuslineInstaller.Enable("""{ "model": "opus" }""", H, ClaudeShell.GitBash);
        Assert.Equal($"{H} statusline", Cmd(outJson));
        Assert.Equal(TapState.Standalone, StatuslineInstaller.State(outJson));
        var back = JsonNode.Parse(StatuslineInstaller.Disable(outJson))!.AsObject();
        Assert.False(back.ContainsKey("statusLine"));
        Assert.Equal("opus", back["model"]!.GetValue<string>());
    }

    [Fact]
    public void Enable_with_empty_command_creates_standalone()
    {
        var outJson = StatuslineInstaller.Enable("""{ "statusLine": { "type": "command", "command": "  " } }""", Spaced, ClaudeShell.PowerShell);
        Assert.Equal("& 'C:/Users/John Smith/claudebar-helper.exe' statusline", Cmd(outJson));
        Assert.Equal(TapState.Standalone, StatuslineInstaller.State(outJson));
    }

    [Theory]
    [MemberData(nameof(Helpers))]
    public void Enable_is_idempotent(string helper, ClaudeShell shell)
    {
        var once = StatuslineInstaller.Enable(WithCommand("ccstatusline"), helper, shell);
        Assert.Equal(Cmd(once), Cmd(StatuslineInstaller.Enable(once, helper, shell)));
        Assert.Null(StatuslineInstaller.Upgrade(once, helper, shell));
    }

    [Theory]
    [InlineData("a && b")]
    [InlineData("a || b")]
    [InlineData("a; b")]
    [InlineData("a\nb")]
    public void Enable_refuses_compound_commands(string original)
    {
        foreach (var shell in new[] { ClaudeShell.GitBash, ClaudeShell.PowerShell })
        {
            var ex = Assert.Throws<TapInstallException>(() => StatuslineInstaller.Enable(WithCommand(original), H, shell));
            Assert.Equal("compound", ex.Code);
        }
    }

    [Fact]
    public void Enable_refuses_non_command_type()
    {
        var ex = Assert.Throws<TapInstallException>(() =>
            StatuslineInstaller.Enable("""{ "statusLine": { "type": "http", "url": "x" } }""", H, ClaudeShell.GitBash));
        Assert.Equal("type", ex.Code);
    }

    // ---------------------------------------------------------------- de una versión a otra

    [Theory]
    [InlineData("C:/old/claudebar-helper.exe statusline --tee | ccstatusline")]
    [InlineData("'C:/Users/John Smith/claudebar-helper.exe' statusline --tee | ccstatusline")]
    [InlineData("& 'C:/Users/O''Brien/claudebar-helper.exe' statusline --tee | ccstatusline")]
    public void The_old_tee_is_still_ours_and_upgrades_to_the_resilient_one(string old)
    {
        var json = WithCommand(old);
        Assert.Equal(TapState.Tee, StatuslineInstaller.State(json));
        Assert.Equal("ccstatusline", Cmd(StatuslineInstaller.Disable(json)));

        var up = StatuslineInstaller.Upgrade(json, H, ClaudeShell.GitBash);
        Assert.Equal(BashTee(H, "ccstatusline"), Cmd(up!));
        Assert.Null(StatuslineInstaller.Upgrade(up!, H, ClaudeShell.GitBash));
    }

    [Fact]
    public void Upgrade_changes_only_the_prefix_and_follows_the_shell()
    {
        var old = StatuslineInstaller.Enable(WithCommand("ccstatusline"), "C:/old/claudebar-helper.exe", ClaudeShell.GitBash);
        var up = StatuslineInstaller.Upgrade(old, H, ClaudeShell.GitBash);
        Assert.Equal(BashTee(H, "ccstatusline"), Cmd(up!));
        // Git Bash desinstalado: Claude Code pasa a PowerShell y el enganche cambia de sintaxis con él.
        var ps = StatuslineInstaller.Upgrade(up!, H, ClaudeShell.PowerShell);
        Assert.Equal(PsTee($"'{H}'", H, "ccstatusline"), Cmd(ps!));
        Assert.Null(StatuslineInstaller.Upgrade(WithCommand("ccstatusline"), H, ClaudeShell.GitBash));
    }

    // ---------------------------------------------------------------- lo ajeno no es nuestro

    [Fact]
    public void User_replacing_statusline_reads_as_not_installed() =>
        Assert.Equal(TapState.NotInstalled, StatuslineInstaller.State(WithCommand("my-new-line")));

    [Theory]
    [InlineData("if [ -e C:/x/not-claudebar-helper.exe ]; then C:/x/not-claudebar-helper.exe statusline --tee; else cat; fi | ccstatusline")]
    [InlineData("if [ -e C:/x/claudebar-helper.exe ]; then rm -rf ~; else cat; fi | ccstatusline")]
    [InlineData("if [ -e C:/x/claudebar-helper.exe ]; then C:/x/claudebar-helper.exe statusline --tee; else evil; fi | ccstatusline")]
    [InlineData("$OutputEncoding = [Text.UTF8Encoding]::new($false); & { if (Test-Path -LiteralPath 'C:/x/my claudebar-helper.exe') { & 'C:/x/my claudebar-helper.exe' statusline --tee } else { [IO.StreamReader]::new([Console]::OpenStandardInput()).ReadToEnd() } } | ccstatusline")]
    [InlineData("$OutputEncoding = [Text.UTF8Encoding]::new($false); & { if (Test-Path -LiteralPath 'C:/x/claudebar-helper.exe') { Remove-Item ~ } else { [IO.StreamReader]::new([Console]::OpenStandardInput()).ReadToEnd() } } | ccstatusline")]
    public void Lookalikes_of_the_resilient_tee_are_not_ours(string command)
    {
        Assert.Equal(TapState.NotInstalled, StatuslineInstaller.State(WithCommand(command)));
        Assert.Equal(command, Cmd(StatuslineInstaller.Disable(WithCommand(command))));
    }

    [Fact]
    public void Tolerates_comments_and_trailing_commas()
    {
        var src = "{ // mío\n \"statusLine\": { \"type\": \"command\", \"command\": \"ccstatusline\", }, }";
        Assert.Equal(TapState.Tee, StatuslineInstaller.State(StatuslineInstaller.Enable(src, H, ClaudeShell.GitBash)));
    }

    [Fact]
    public void Disable_without_marker_changes_nothing_semantically() =>
        Assert.Equal("ccstatusline", Cmd(StatuslineInstaller.Disable(WithCommand("ccstatusline"))));

    [Fact]
    public void Keeps_non_ascii_of_other_settings_unescaped()
    {
        var outJson = StatuslineInstaller.Enable("""{ "statusLine": { "type": "command", "command": "ccstatusline" }, "nota": "español 🚀" }""", H,
            ClaudeShell.GitBash);
        Assert.Contains("español", outJson); // tildes intactas en el archivo
        // Fuera del BMP (emoji) System.Text.Json escribe siempre 🚀: mismo valor al leerlo.
        Assert.Equal("español 🚀", JsonNode.Parse(outJson)!["nota"]!.GetValue<string>());
    }

    // ---------------------------------------------------------------- HelperCommand

    [Theory]
    [InlineData(@"C:\L\ClaudeBarWin\bin\claudebar-helper.exe", "'C:/L/ClaudeBarWin/bin/claudebar-helper.exe'")]
    [InlineData(@"C:\Users\O'Brien\AppData\Local\Microsoft\WindowsApps\Bekay.Muesca_8wekyb3d8bbwe\muesca-helper.exe",
        "'C:/Users/O''Brien/AppData/Local/Microsoft/WindowsApps/Bekay.Muesca_8wekyb3d8bbwe/muesca-helper.exe'")]
    public void The_PowerShell_literal_of_the_helper_is_recognised_exactly(string path, string literal)
    {
        Assert.Equal(literal, ShellQuote.PowerShellLiteral(path));
        Assert.Matches("^" + HelperCommand.PowerShellLiteralPattern + "$", literal);
    }

    [Theory]
    [InlineData("'C:/x/not-claudebar-helper.exe'")]
    [InlineData("'C:/x/claudebar-helper.exe.bak'")]
    [InlineData("'C:/x/claudebar-helper.exe' + 'x'")]
    [InlineData("C:/x/claudebar-helper.exe")]
    public void Lookalike_PowerShell_literals_are_not_the_helper(string literal) =>
        Assert.DoesNotMatch("^" + HelperCommand.PowerShellLiteralPattern + "$", literal);
}
