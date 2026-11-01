using System.Text.Json.Nodes;
using ClaudeBarWin.Services;

/// <summary>
/// «Quitar la integración con Claude Code» (Ajustes): de una vez, quita de settings.json solo lo de Muesca (los hooks de las
/// sesiones en vivo y de los permisos, y el enganche de la statusline, que devuelve la del usuario tal cual). Lo ajeno —hooks
/// de otros, otros ajustes, comentarios fuera— se queda. Pensado para antes de desinstalar la edición de la Store, que no
/// ejecuta nada al desinstalar.
/// </summary>
public class ClaudeIntegrationTests
{
    private const string Helper = @"C:\Users\ana\AppData\Local\Microsoft\WindowsApps\Bekay.Muesca_8wekyb3d8bbwe\muesca-helper.exe";

    private static readonly JsonObject ForeignHook = new() { ["type"] = "command", ["command"] = "C:/tools/notify.exe stop" };

    /// <summary>Un settings.json con todo lo nuestro (hooks, permisos, statusline enganchada) y cosas del usuario.</summary>
    private static string Everything(ClaudeShell shell)
    {
        var json = HookInstaller.MergeSettings("""{ "model": "opus", "env": { "X": "1" } }""",
            HookInstaller.HelperHookSpec(true, Helper, shell), HookInstaller.PermissionSpec(true, Helper, shell));
        var root = JsonNode.Parse(json)!.AsObject();
        // Un hook ajeno en Stop, junto a los nuestros.
        root["hooks"]!["Stop"]!.AsArray().Add(new JsonObject { ["matcher"] = "*", ["hooks"] = new JsonArray(ForeignHook.DeepClone()) });
        root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = "ccstatusline", ["padding"] = 0 };
        return StatuslineInstaller.Enable(root.ToJsonString(), Helper, shell);
    }

    [Theory]
    [InlineData(ClaudeShell.GitBash)]
    [InlineData(ClaudeShell.PowerShell)]
    public void Removing_takes_out_hooks_and_statusline_and_nothing_else(ClaudeShell shell)
    {
        var before = Everything(shell);
        Assert.True(ClaudeIntegration.HasAny(before));
        Assert.True(HookInstaller.HasOurHooks(before));
        Assert.Equal(TapState.Tee, StatuslineInstaller.State(before));

        var after = ClaudeIntegration.RemoveAll(before);
        Assert.False(ClaudeIntegration.HasAny(after));
        Assert.False(HookInstaller.HasOurHooks(after));
        Assert.DoesNotContain("muesca-helper", after);

        var root = JsonNode.Parse(after)!.AsObject();
        // La statusline del usuario, exacta y con sus campos.
        Assert.Equal("ccstatusline", root["statusLine"]!["command"]!.GetValue<string>());
        Assert.Equal(0, root["statusLine"]!["padding"]!.GetValue<int>());
        // El hook ajeno y los demás ajustes, intactos.
        Assert.Contains("C:/tools/notify.exe stop", after);
        Assert.Equal("opus", root["model"]!.GetValue<string>());
        Assert.Equal("1", root["env"]!["X"]!.GetValue<string>());
    }

    [Fact]
    public void Our_own_statusline_goes_away_entirely()
    {
        var json = StatuslineInstaller.Enable("""{ "model": "opus" }""", Helper, ClaudeShell.GitBash);
        Assert.True(ClaudeIntegration.HasAny(json));
        var after = JsonNode.Parse(ClaudeIntegration.RemoveAll(json))!.AsObject();
        Assert.False(after.ContainsKey("statusLine"));
        Assert.Equal("opus", after["model"]!.GetValue<string>());
    }

    [Fact]
    public void The_old_statusline_format_and_the_powershell_hook_are_removed_too()
    {
        var old = """
            { "statusLine": { "type": "command", "command": "C:/L/ClaudeBarWin/bin/claudebar-helper.exe statusline --tee | ccstatusline" } }
            """;
        var json = HookInstaller.MergeSettings(old, HookInstaller.HookSpec(execForm: true, @"C:\Users\ana\.claude\hooks\claudebar-hook.ps1"));
        Assert.True(ClaudeIntegration.HasAny(json));
        var after = ClaudeIntegration.RemoveAll(json);
        Assert.False(ClaudeIntegration.HasAny(after));
        Assert.Equal("ccstatusline", JsonNode.Parse(after)!["statusLine"]!["command"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "statusLine": { "type": "command", "command": "ccstatusline" }, "model": "opus" }""")]
    [InlineData("""{ "statusLine": { "type": "command", "command": "C:/tools/not-claudebar-helper.exe statusline" } }""")]
    public void Without_anything_of_ours_there_is_nothing_to_remove(string json)
    {
        Assert.False(ClaudeIntegration.HasAny(json));
        Assert.Equal(JsonNode.Parse(json)!.ToJsonString(), JsonNode.Parse(ClaudeIntegration.RemoveAll(json))!.ToJsonString());
    }

    [Fact]
    public void Removing_twice_is_the_same_as_once()
    {
        var once = ClaudeIntegration.RemoveAll(Everything(ClaudeShell.GitBash));
        Assert.Equal(once, ClaudeIntegration.RemoveAll(once));
    }

    [Fact]
    public void Comments_and_trailing_commas_are_tolerated()
    {
        var json = "{ // mío\n \"statusLine\": { \"type\": \"command\", \"command\": \"" +
                   StatuslineInstaller.TeeCommand(Helper, ClaudeShell.GitBash, "ccstatusline").Replace("\"", "\\\"") + "\", }, }";
        Assert.True(ClaudeIntegration.HasAny(json));
        Assert.Equal("ccstatusline", JsonNode.Parse(ClaudeIntegration.RemoveAll(json))!["statusLine"]!["command"]!.GetValue<string>());
    }
}
