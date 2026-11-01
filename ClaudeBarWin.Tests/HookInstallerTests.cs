using System.Text.Json;
using ClaudeBarWin.Services;
using System.Text.Json.Nodes;

namespace ClaudeBarWin.Tests;

public class HookInstallerTests
{
    private const string Cmd = "powershell -NoProfile -File \"C:\\x\\claudebar-hook.ps1\"";

    [Fact]
    public void Merge_into_empty_settings_adds_all_events()
    {
        var merged = HookInstaller.MergeSettings("{}", Cmd);
        using var doc = JsonDocument.Parse(merged);
        var hooks = doc.RootElement.GetProperty("hooks");
        Assert.True(hooks.TryGetProperty("PostToolUse", out _));
        Assert.True(hooks.TryGetProperty("PermissionRequest", out _));
        // v0.3.10: PreToolUse y SubagentStop ya no se instalan (duplicaban el coste por tool call).
        Assert.False(hooks.TryGetProperty("PreToolUse", out _));
        Assert.False(hooks.TryGetProperty("SubagentStop", out _));
        Assert.Contains("claudebar-hook.ps1", merged);
    }

    // ---- v0.3.10: exec form, migración desde 0.3.9, JSON del usuario intacto ----

    private const string V039Settings = """
        {
          // comentario que alguien puso a mano
          "language": "español",
          "hooks": {
            "PreToolUse": [
              { "matcher": "Skill", "hooks": [ { "type": "command", "command": "ccstatusline --hook" } ] },
              { "matcher": "*", "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\Users\\z\\.claude\\hooks\\claudebar-hook.ps1\"" } ] }
            ],
            "SubagentStop": [
              { "matcher": "*", "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\Users\\z\\.claude\\hooks\\claudebar-hook.ps1\"" } ] }
            ],
            "Stop": [
              { "matcher": "*", "hooks": [ { "type": "command", "command": "powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\\Users\\z\\.claude\\hooks\\claudebar-hook.ps1\"" } ] }
            ],
          },
        }
        """;

    [Fact]
    public void Exec_form_spec_launches_powershell_directly_with_the_script_in_args()
    {
        var spec = HookInstaller.HookSpec(execForm: true, scriptPath: @"C:\x\claudebar-hook.ps1");
        Assert.EndsWith("powershell.exe", spec["command"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        var args = spec["args"]!.AsArray().Select(a => a!.GetValue<string>()).ToList();
        Assert.Equal(@"C:\x\claudebar-hook.ps1", args[^1]);
        Assert.Equal("-File", args[^2]);
    }

    [Fact]
    public void Upgrade_from_v039_drops_old_events_keeps_foreign_hooks_and_is_then_stable()
    {
        var spec = HookInstaller.HookSpec(execForm: true, scriptPath: @"C:\x\claudebar-hook.ps1");
        var upgraded = HookInstaller.UpgradedSettings(V039Settings, spec);
        Assert.NotNull(upgraded);

        using var doc = JsonDocument.Parse(upgraded!);
        var hooks = doc.RootElement.GetProperty("hooks");
        // El PreToolUse ajeno (ccstatusline) sobrevive; el nuestro se va.
        var pre = hooks.GetProperty("PreToolUse");
        Assert.Equal(1, pre.GetArrayLength());
        Assert.Contains("ccstatusline", pre.GetRawText());
        Assert.False(hooks.TryGetProperty("SubagentStop", out _));
        // Nuestras entradas nuevas, en exec form.
        Assert.Contains("-NonInteractive", hooks.GetProperty("Stop").GetRawText());
        Assert.True(hooks.TryGetProperty("SessionEnd", out _));
        // Tildes sin re-escapar.
        Assert.Contains("\"español\"", upgraded);

        Assert.Null(HookInstaller.UpgradedSettings(upgraded!, spec)); // idempotente
    }

    [Fact]
    public void Upgrade_does_nothing_when_not_installed()
        => Assert.Null(HookInstaller.UpgradedSettings("""{"model":"opus"}""", HookInstaller.HookSpec(true)));

    [Fact]
    public void Remove_also_finds_exec_form_entries()
    {
        var spec = HookInstaller.HookSpec(execForm: true, scriptPath: @"C:\x\claudebar-hook.ps1");
        var merged = HookInstaller.MergeSettings("""{"model":"opus"}""", spec);
        var removed = HookInstaller.RemoveHooks(merged);
        Assert.DoesNotContain("claudebar-hook.ps1", removed);
        Assert.Contains("\"model\"", removed);
    }

    [Theory]
    [InlineData("2.1.283 (Claude Code)", 2, 1, 283)]
    [InlineData("claude 2.1.139", 2, 1, 139)]
    public void ParseVersion_reads_claude_version_output(string output, int a, int b, int c)
        => Assert.Equal(new Version(a, b, c), HookInstaller.ParseVersion(output));

    [Fact]
    public void ParseVersion_returns_null_for_garbage()
        => Assert.Null(HookInstaller.ParseVersion("command not found"));

    [Fact]
    public void Merge_is_idempotent()
    {
        var once = HookInstaller.MergeSettings("{}", Cmd);
        var twice = HookInstaller.MergeSettings(once, Cmd);
        // No debe duplicar nuestra entrada: contar ocurrencias de la marca por evento sigue siendo 1 cada uno.
        var count = twice.Split("claudebar-hook.ps1").Length - 1;
        var onceCount = once.Split("claudebar-hook.ps1").Length - 1;
        Assert.Equal(onceCount, count);
    }

    [Fact]
    public void Merge_preserves_foreign_hooks()
    {
        var existing = """
        {"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"echo cron-setup"}]}]}}
        """;
        var merged = HookInstaller.MergeSettings(existing, Cmd);
        Assert.Contains("echo cron-setup", merged); // el hook del Asistente sobrevive
        Assert.Contains("claudebar-hook.ps1", merged);
    }

    [Fact]
    public void Remove_strips_only_our_hooks()
    {
        var existing = """
        {"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"echo cron-setup"}]}]}}
        """;
        var merged = HookInstaller.MergeSettings(existing, Cmd);
        var removed = HookInstaller.RemoveHooks(merged);
        Assert.Contains("echo cron-setup", removed);
        Assert.DoesNotContain("claudebar-hook.ps1", removed);
    }

    [Fact]
    public void Remove_preserves_non_hook_settings()
    {
        var existing = """{"model":"opus","hooks":{}}""";
        var merged = HookInstaller.MergeSettings(existing, Cmd);
        var removed = HookInstaller.RemoveHooks(merged);
        Assert.Contains("\"model\"", removed);
    }

    // ---------------- v0.4: hooks con claudebar-helper ----------------

    [Fact]
    public void HelperHookSpec_exec_form_uses_raw_path_and_hook_arg()
    {
        var spec = HookInstaller.HelperHookSpec(true, @"C:\Users\John Smith\claudebar-helper.exe", ClaudeShell.GitBash);
        Assert.Equal(@"C:\Users\John Smith\claudebar-helper.exe", spec["command"]!.GetValue<string>());
        Assert.Equal("hook", spec["args"]![0]!.GetValue<string>());
    }

    [Fact]
    public void HelperHookSpec_shell_form_quotes()
    {
        var spec = HookInstaller.HelperHookSpec(false, @"C:\Users\John Smith\claudebar-helper.exe", ClaudeShell.GitBash);
        Assert.Equal("'C:/Users/John Smith/claudebar-helper.exe' hook", spec["command"]!.GetValue<string>());
        Assert.Null(spec["args"]);
    }

    [Fact]
    public void Upgrade_migrates_ps1_entries_to_helper_and_keeps_foreign_hooks()
    {
        var ps1 = HookInstaller.MergeSettings(
            """{ "hooks": { "Stop": [ { "matcher": "*", "hooks": [ { "type": "command", "command": "mine.cmd" } ] } ] } }""",
            HookInstaller.HookSpec(true, @"C:\u\.claude\hooks\claudebar-hook.ps1"));
        var helper = HookInstaller.HelperHookSpec(true, @"C:\L\claudebar-helper.exe", ClaudeShell.GitBash);
        var up = HookInstaller.UpgradedSettings(ps1, helper)!;
        Assert.DoesNotContain("claudebar-hook.ps1", up);
        Assert.Contains("claudebar-helper.exe", up);
        Assert.Contains("mine.cmd", up);
        Assert.Null(HookInstaller.UpgradedSettings(up, helper));
    }

    [Fact]
    public void RemoveHooks_removes_helper_entries_too()
    {
        var helper = HookInstaller.HelperHookSpec(true, @"C:\L\claudebar-helper.exe", ClaudeShell.GitBash);
        var json = HookInstaller.MergeSettings("{}", helper);
        Assert.DoesNotContain("claudebar-helper", HookInstaller.RemoveHooks(json));
    }

    [Fact]
    public void Merge_is_idempotent_with_helper_entries()
    {
        var helper = HookInstaller.HelperHookSpec(true, @"C:\L\claudebar-helper.exe", ClaudeShell.GitBash);
        var once = HookInstaller.MergeSettings("{}", helper);
        Assert.Equal(once, HookInstaller.MergeSettings(once, helper));
    }

    [Fact]
    public void DesiredSpec_falls_back_to_ps1_without_helper()
    {
        Assert.Contains("claudebar-hook.ps1", HookInstaller.DesiredSpec(helperPath: null).ToJsonString());
        Assert.Contains("claudebar-hook.ps1", HookInstaller.DesiredSpec(@"C:\no\existe\claudebar-helper.exe").ToJsonString());
    }

    // ---------------- F2: entrada «permission» opt-in (solo en PermissionRequest) ----------------

    private const string HelperExe = @"C:\L\claudebar-helper.exe";
    private static JsonObject HookHelper() => HookInstaller.HelperHookSpec(true, HelperExe, ClaudeShell.GitBash);
    private static JsonObject PermHelper() => HookInstaller.PermissionSpec(true, HelperExe, ClaudeShell.GitBash);

    /// <summary>Cada hook de las entradas de un evento, como JSON compacto.</summary>
    private static List<string> Commands(string json, string ev)
    {
        if (ClaudeSettingsFile.ParseRoot(json)["hooks"]?[ev] is not JsonArray arr) return new();
        return arr.OfType<JsonObject>()
            .SelectMany(e => e["hooks"] as JsonArray ?? new JsonArray())
            .Select(h => h!.ToJsonString())
            .ToList();
    }

    [Fact]
    public void PermissionSpec_exec_and_shell_forms_carry_permission_arg_and_timeout_45()
    {
        var exec = HookInstaller.PermissionSpec(true, @"C:\Users\John Smith\claudebar-helper.exe", ClaudeShell.GitBash);
        Assert.Equal(@"C:\Users\John Smith\claudebar-helper.exe", exec["command"]!.GetValue<string>());
        Assert.Equal("permission", Assert.Single(exec["args"]!.AsArray())!.GetValue<string>());
        Assert.Equal(45, exec["timeout"]!.GetValue<int>());

        var shell = HookInstaller.PermissionSpec(false, @"C:\Users\John Smith\claudebar-helper.exe", ClaudeShell.GitBash);
        Assert.Equal("'C:/Users/John Smith/claudebar-helper.exe' permission", shell["command"]!.GetValue<string>());
        Assert.Null(shell["args"]);
        Assert.Equal(45, shell["timeout"]!.GetValue<int>());
        Assert.Null(shell["onFailure"]);   // nunca «block»: sin decisión, Claude Code muestra su diálogo
    }

    [Fact]
    public void Permissions_on_adds_second_entry_only_in_PermissionRequest_with_timeout_45()
    {
        var json = HookInstaller.MergeSettings("{}", HookHelper(), PermHelper());
        var perm = Commands(json, "PermissionRequest");
        Assert.Equal(2, perm.Count);
        Assert.Contains(perm, c => c.Contains("\"hook\""));
        var p = Assert.Single(perm, c => c.Contains("\"permission\""));
        Assert.Contains("\"timeout\":45", p);
        // Dos entradas separadas (la de permisos con su propio timeout), no un mismo bloque con dos hooks.
        Assert.Equal(2, ((JsonArray)ClaudeSettingsFile.ParseRoot(json)["hooks"]!["PermissionRequest"]!).Count);
        foreach (var ev in HookInstaller.Events.Where(e => e != "PermissionRequest"))
        {
            var cmds = Commands(json, ev);
            Assert.Single(cmds);
            Assert.DoesNotContain(cmds, c => c.Contains("permission"));
        }
        Assert.True(HookInstaller.HasOurHooks(json));
    }

    [Fact]
    public void Permissions_off_removes_permission_entry_and_keeps_hook_entry()
    {
        var on = HookInstaller.MergeSettings("{}", HookHelper(), PermHelper());
        var off = HookInstaller.UpgradedSettings(on, HookHelper(), permissionSpec: null);
        Assert.NotNull(off);
        var perm = Commands(off!, "PermissionRequest");
        Assert.Contains(perm, c => c.Contains("\"hook\""));
        Assert.DoesNotContain(perm, c => c.Contains("permission"));
        // Y al revés: activarlo sobre una instalación sin la entrada la añade.
        var back = HookInstaller.UpgradedSettings(off!, HookHelper(), PermHelper());
        Assert.NotNull(back);
        Assert.Contains(Commands(back!, "PermissionRequest"), c => c.Contains("\"permission\""));
        // Merge sin permisos sobre un JSON que los tenía: también los quita (el ajuste manda).
        Assert.DoesNotContain(Commands(HookInstaller.MergeSettings(on, HookHelper(), null), "PermissionRequest"),
            c => c.Contains("permission"));
    }

    [Fact]
    public void Uninstall_removes_both_and_keeps_foreign_hooks()
    {
        const string foreign = """
            { "hooks": { "PermissionRequest": [ { "matcher": "Bash", "hooks": [ { "type": "command", "command": "guard.cmd" } ] } ] } }
            """;
        var json = HookInstaller.MergeSettings(foreign, HookHelper(), PermHelper());
        Assert.Equal(3, Commands(json, "PermissionRequest").Count);
        var removed = HookInstaller.RemoveHooks(json);
        Assert.DoesNotContain("claudebar-helper", removed);
        Assert.Contains("guard.cmd", removed);
        Assert.False(HookInstaller.HasOurHooks(removed));
        // También en forma shell (PowerShell y Git Bash).
        foreach (var sh in new[] { ClaudeShell.PowerShell, ClaudeShell.GitBash })
        {
            var shell = HookInstaller.MergeSettings(foreign,
                HookInstaller.HelperHookSpec(false, @"C:\Users\John O'Brien\claudebar-helper.exe", sh),
                HookInstaller.PermissionSpec(false, @"C:\Users\John O'Brien\claudebar-helper.exe", sh));
            Assert.Equal(3, Commands(shell, "PermissionRequest").Count);
            var removedShell = HookInstaller.RemoveHooks(shell);
            Assert.DoesNotContain("claudebar-helper", removedShell);
            Assert.Contains("guard.cmd", removedShell);
        }
    }

    [Theory]
    [InlineData("""{ "type": "command", "command": "C:\\x\\my-tool.exe", "args": ["permission"] }""")]
    [InlineData("""{ "type": "command", "command": "C:\\x\\not-claudebar-helper.exe", "args": ["permission"] }""")]
    [InlineData("""{ "type": "command", "command": "C:\\L\\claudebar-helper.exe", "args": ["permission", "--x"] }""")]
    [InlineData("""{ "type": "command", "command": "C:\\L\\claudebar-helper.exe", "args": ["permissions"] }""")]
    [InlineData("""{ "type": "command", "command": "echo claudebar-helper permission" }""")]
    [InlineData("""{ "type": "command", "command": "C:/L/claudebar-helper.exe permission && rm -rf x" }""")]
    [InlineData("""{ "type": "command", "command": "audit.cmd --log permission" }""")]
    public void Foreign_hook_mentioning_permission_is_not_ours(string hook)
    {
        var json = $$"""{ "hooks": { "PermissionRequest": [ { "matcher": "*", "hooks": [ {{hook}} ] } ] } }""";
        Assert.False(HookInstaller.HasOurHooks(json));
        Assert.Single(Commands(HookInstaller.RemoveHooks(json), "PermissionRequest"));   // no se borra
        var merged = HookInstaller.MergeSettings(json, HookHelper(), null);              // ni lo quita el «off»
        Assert.Equal(2, Commands(merged, "PermissionRequest").Count);
    }

    [Fact]
    public void Permission_entry_in_the_wrong_event_is_cleaned_up_by_merge()
    {
        const string stray = """
            { "hooks": { "Stop": [ { "matcher": "*", "hooks": [ { "type": "command", "command": "C:\\L\\claudebar-helper.exe", "args": ["permission"] } ] } ] } }
            """;
        Assert.True(HookInstaller.HasOurHooks(stray));
        var merged = HookInstaller.MergeSettings(stray, HookHelper(), PermHelper());
        Assert.DoesNotContain(Commands(merged, "Stop"), c => c.Contains("permission"));
        Assert.Single(Commands(merged, "PermissionRequest"), c => c.Contains("permission"));
    }

    [Fact]
    public void Merge_is_idempotent_with_permissions()
    {
        var once = HookInstaller.MergeSettings("{}", HookHelper(), PermHelper());
        Assert.Equal(once, HookInstaller.MergeSettings(once, HookHelper(), PermHelper()));
        Assert.Null(HookInstaller.UpgradedSettings(once, HookHelper(), PermHelper()));
        var off = HookInstaller.MergeSettings("{}", HookHelper(), null);
        Assert.Equal(off, HookInstaller.MergeSettings(off, HookHelper(), null));
        Assert.Null(HookInstaller.UpgradedSettings(off, HookHelper(), null));
        Assert.Null(HookInstaller.UpgradedSettings(off, HookHelper()));            // la firma antigua = permisos apagados
    }

    [Fact]
    public void PermissionEntryUpdated_touches_only_the_permission_entry()
    {
        // Instalación con el .ps1 (sin helper): activar/desactivar el ajuste no debe migrar ni reescribir las de sesiones.
        var ps1 = HookInstaller.MergeSettings(
            """{ "hooks": { "Stop": [ { "matcher": "*", "hooks": [ { "type": "command", "command": "mine.cmd" } ] } ] } }""",
            HookInstaller.HookSpec(true, @"C:\u\.claude\hooks\claudebar-hook.ps1"));
        var on = HookInstaller.PermissionEntryUpdated(ps1, PermHelper());
        Assert.NotNull(on);
        Assert.Equal(HookInstaller.RemoveHooks(ps1), HookInstaller.RemoveHooks(on!));          // lo ajeno, intacto
        Assert.Equal(HookInstaller.Events.Length, on!.Split("claudebar-hook.ps1").Length - 1);  // las .ps1 siguen todas
        Assert.Single(Commands(on, "PermissionRequest"), c => c.Contains("\"permission\""));
        Assert.Null(HookInstaller.PermissionEntryUpdated(on, PermHelper()));                    // idempotente

        var off = HookInstaller.PermissionEntryUpdated(on, null);
        Assert.NotNull(off);
        Assert.DoesNotContain("permission\"", off!);
        Assert.Equal(HookInstaller.Events.Length, off.Split("claudebar-hook.ps1").Length - 1);
        Assert.Null(HookInstaller.PermissionEntryUpdated(off, null));
        Assert.Null(HookInstaller.PermissionEntryUpdated("""{"model":"opus"}""", PermHelper()));   // sin hooks: no se instala
    }

    [Fact]
    public void DesiredPermissionSpec_requires_setting_and_deployed_helper()
    {
        Assert.Null(HookInstaller.DesiredPermissionSpec(@"C:\no\existe\claudebar-helper.exe", permissions: true));
        Assert.Null(HookInstaller.DesiredPermissionSpec(null, permissions: true));
        var dir = Path.Combine(Path.GetTempPath(), "cb-perm-" + Guid.NewGuid().ToString("N"));
        var exe = Path.Combine(dir, "claudebar-helper.exe");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(exe, "");
            Assert.Null(HookInstaller.DesiredPermissionSpec(exe, permissions: false));
            var spec = HookInstaller.DesiredPermissionSpec(exe, permissions: true);
            Assert.NotNull(spec);
            Assert.Equal(45, spec!["timeout"]!.GetValue<int>());
            Assert.Contains("permission", spec.ToJsonString());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
    // ---------------- Revisión API (punto 4): hooks ajenos en el MISMO bloque que el nuestro ----------------

    private const string SharedBlock = """
        { "hooks": { "PermissionRequest": [ { "matcher": "*", "hooks": [
            { "type": "command", "command": "C:\\L\\claudebar-helper.exe", "args": ["hook"] },
            { "type": "command", "command": "C:\\L\\claudebar-helper.exe", "args": ["permission"], "timeout": 45 },
            { "type": "command", "command": "guard.cmd" }
        ] } ] } }
        """;

    [Fact]
    public void RemoveHooks_keeps_foreign_hook_in_the_same_block_and_drops_empty_blocks()
    {
        var removed = HookInstaller.RemoveHooks(SharedBlock);
        var arr = (JsonArray)ClaudeSettingsFile.ParseRoot(removed)["hooks"]!["PermissionRequest"]!;
        var block = Assert.Single(arr)!.AsObject();
        Assert.Equal("*", block["matcher"]!.GetValue<string>());
        var only = Assert.Single(block["hooks"]!.AsArray())!;
        Assert.Equal("guard.cmd", only["command"]!.GetValue<string>());
        Assert.False(HookInstaller.HasOurHooks(removed));

        // Bloque solo con lo nuestro: se va entero (y el evento, si queda vacío).
        var ours = HookInstaller.RemoveHooks(HookInstaller.MergeSettings("{}", HookHelper(), PermHelper()));
        Assert.Null(ClaudeSettingsFile.ParseRoot(ours)["hooks"]!["PermissionRequest"]);
    }

    [Fact]
    public void Permissions_off_keeps_foreign_and_hook_entries_in_the_same_block()
    {
        var off = HookInstaller.PermissionEntryUpdated(SharedBlock, null);
        Assert.NotNull(off);
        var cmds = Commands(off!, "PermissionRequest");
        Assert.Equal(2, cmds.Count);
        Assert.Contains(cmds, c => c.Contains("guard.cmd"));
        Assert.Contains(cmds, c => c.Contains("\"hook\""));
        Assert.DoesNotContain(cmds, c => c.Contains("permission"));

        var merged = HookInstaller.MergeSettings(SharedBlock, HookHelper(), null);
        Assert.Contains(Commands(merged, "PermissionRequest"), c => c.Contains("guard.cmd"));
        Assert.DoesNotContain(Commands(merged, "PermissionRequest"), c => c.Contains("permission"));
    }

    [Fact]
    public void Upgrade_keeps_foreign_hook_of_a_shared_block_and_is_then_stable()
    {
        var up = HookInstaller.UpgradedSettings(SharedBlock, HookHelper(), PermHelper());
        Assert.NotNull(up);   // las nuestras pasan a sus propios bloques: se reescribe UNA vez
        var cmds = Commands(up!, "PermissionRequest");
        Assert.Single(cmds, c => c.Contains("guard.cmd"));
        Assert.Single(cmds, c => c.Contains("\"hook\""));
        Assert.Single(cmds, c => c.Contains("\"permission\""));
        Assert.Null(HookInstaller.UpgradedSettings(up!, HookHelper(), PermHelper()));
    }
}
