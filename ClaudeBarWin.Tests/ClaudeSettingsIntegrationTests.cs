using System.Text.Json.Nodes;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

// Integración con la configuración de Claude Code: propiedad de la statusline y de los hooks
// (StatuslineInstaller/HookInstaller), reescritura atómica de settings.json (ClaudeSettingsFile),
// snapshots huérfanos (QuotaPolicy) y despliegue del helper (HelperDeployer).
// Origen: revisión cruzada de la v0.4, en la que Codex demostró con estos tests los hallazgos
// V04-01..V04-05 (los que tenían prueba). V04-05 y V04-07 llevan además tests propios.
// En serie porque uno de ellos cambia CLAUDE_CONFIG_DIR, que es global al proceso.
[CollectionDefinition("ClaudeSettingsIntegrationSerial", DisableParallelization = true)]
public sealed class ClaudeSettingsIntegrationSerialCollection;

[Collection("ClaudeSettingsIntegrationSerial")]
public sealed class ClaudeSettingsIntegrationTests
{
    private static string WithStatusline(string command) => new JsonObject
    {
        ["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = command },
    }.ToJsonString();

    // V04-03: una statusline ajena cuyo exe solo CONTIENE la marca no es de ClaudeBar (Disable la borraría).
    [Theory]
    [InlineData("C:/tools/not-claudebar-helper.exe statusline")]
    [InlineData("'C:/tools/not-claudebar-helper.exe' statusline --tee | ccstatusline")]
    [InlineData("& 'C:/tools/my claudebar-helper.exe' statusline")]
    public void Statusline_command_with_similar_executable_name_is_not_owned_by_claudebar(string userCommand)
    {
        Assert.Equal(TapState.NotInstalled, StatuslineInstaller.State(WithStatusline(userCommand)));
        Assert.Equal(userCommand, JsonNode.Parse(StatuslineInstaller.Disable(WithStatusline(userCommand)))!["statusLine"]!["command"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("claudebar-helper.exe statusline")]
    [InlineData("C:/L/claudebar-helper statusline")]
    [InlineData(@"'C:/Users/O'\''Brien/claudebar-helper.exe' statusline")]
    public void Statusline_with_the_exact_helper_basename_is_ours(string command) =>
        Assert.Equal(TapState.Standalone, StatuslineInstaller.State(WithStatusline(command)));

    [Theory]
    [InlineData("  node C:/Proyectos/espanol/statusline.mjs  ")]
    [InlineData("& 'C:/Users/O''Brien/statusline.exe'   ")]
    [InlineData("bash ~/.claude/statusline.sh | head -1  ")]
    public void Ordinary_statusline_round_trip_preserves_the_exact_command(string original)
    {
        const string helper = @"C:\Users\O'Brien\claudebar-helper.exe";
        foreach (var shell in new[] { ClaudeShell.GitBash, ClaudeShell.PowerShell })
        {
            var restored = StatuslineInstaller.Disable(StatuslineInstaller.Enable(WithStatusline(original), helper, shell));
            Assert.Equal(original, JsonNode.Parse(restored)!["statusLine"]!["command"]!.GetValue<string>());
        }
    }

    // V04-02: activar SOLO la cuota no puede hacer que el arranque instale los 8 hooks.
    [Fact]
    public void Statusline_tap_alone_does_not_trigger_automatic_hook_installation()
    {
        var settings = WithStatusline("C:/ClaudeBar/bin/claudebar-helper.exe statusline");
        var desiredHook = HookInstaller.HelperHookSpec(true, @"C:\ClaudeBar\bin\claudebar-helper.exe", ClaudeShell.PowerShell);
        Assert.Null(HookInstaller.UpgradedSettings(settings, desiredHook));
        Assert.False(HookInstaller.HasOurHooks(settings));
    }

    [Fact]
    public void Our_helper_hooks_are_detected_structurally()
    {
        var exec = HookInstaller.MergeSettings("{}", HookInstaller.HelperHookSpec(true, @"C:\L\claudebar-helper.exe", ClaudeShell.GitBash));
        var shell = HookInstaller.MergeSettings("{}", HookInstaller.HelperHookSpec(false, @"C:\Users\John O'Brien\claudebar-helper.exe", ClaudeShell.GitBash));
        var legacy = HookInstaller.MergeSettings("{}", HookInstaller.HookSpec(true, @"C:\u\.claude\hooks\claudebar-hook.ps1"));
        Assert.True(HookInstaller.HasOurHooks(exec));
        Assert.True(HookInstaller.HasOurHooks(shell));
        Assert.True(HookInstaller.HasOurHooks(legacy));
    }

    // V04-04: RemoveHooks no puede llevarse hooks ajenos que solo contienen la marca.
    [Theory]
    [InlineData("""{ "type": "command", "command": "C:/tools/not-claudebar-helper.exe", "args": ["hook"] }""")]
    [InlineData("""{ "type": "command", "command": "C:/tools/logger.exe", "args": ["--tag", "claudebar-helper"] }""")]
    [InlineData("""{ "type": "command", "command": "C:/L/claudebar-helper.exe", "args": ["statusline"] }""")]
    [InlineData("""{ "type": "command", "command": "echo claudebar-helper hook" }""")]
    public void RemoveHooks_preserves_foreign_hooks_that_only_mention_the_marker(string foreignHook)
    {
        var settings = $$"""{ "hooks": { "Stop": [ { "matcher": "*", "hooks": [ {{foreignHook}} ] } ] } }""";
        var after = HookInstaller.RemoveHooks(settings);
        Assert.Equal(
            JsonNode.Parse(settings)!["hooks"]!["Stop"]!.ToJsonString(),
            JsonNode.Parse(after)!["hooks"]!["Stop"]!.ToJsonString());
        Assert.False(HookInstaller.HasOurHooks(settings));
    }

    // V04-05: con el enganche quitado (p. ej. /statusline), un snapshot huérfano fresco no puede frenar la API.
    [Fact]
    public void Orphan_snapshots_are_ignored_when_the_tap_is_not_installed()
    {
        var now = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var snap = new RateLimitSnapshot("s", now.AddSeconds(-30), now.AddSeconds(-30),
            new Dictionary<string, RateWindowObs> { ["five_hour"] = new(50, new DateTimeOffset(now).AddHours(2), now.AddSeconds(-30), true) });
        Assert.Empty(QuotaPolicy.UsableSnapshots(tapInstalled: false, new[] { snap }));
        Assert.Single(QuotaPolicy.UsableSnapshots(tapInstalled: true, new[] { snap }));
    }

    // V04-01: la relectura optimista tiene que ir DESPUÉS de preparar el temporal, pegada al reemplazo.
    [Fact]
    public void Rewrite_does_not_lose_external_settings_write_after_optimistic_check()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "codex-v04-rewrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", root);
        var settingsPath = Path.Combine(root, "settings.json");
        File.WriteAllText(settingsPath, """{"model":"initial"}""");

        try
        {
            using var externalWritten = new ManualResetEventSlim();
            using var watcher = new FileSystemWatcher(root, ".settings.json.*.tmp")
            {
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            int handled = 0;
            watcher.Created += (_, _) =>
            {
                if (Interlocked.Exchange(ref handled, 1) != 0) return;
                // Claude Code escribe justo cuando aparece el temporal de ClaudeBar y retiene el archivo un momento.
                using (var fs = new FileStream(settingsPath, FileMode.Create, FileAccess.Write, FileShare.Read))
                using (var sw = new StreamWriter(fs))
                {
                    sw.Write("{\"model\":\"written-by-claude-code\"}");
                    sw.Flush();
                    fs.Flush(flushToDisk: true);
                    externalWritten.Set();
                    Thread.Sleep(100);
                }
            };

            ClaudeSettingsFile.Rewrite("codex-review", current =>
            {
                var json = ClaudeSettingsFile.ParseRoot(current);
                json["reviewPayload"] = new string('x', 8 * 1024 * 1024); // alarga la ventana del temporal
                return ClaudeSettingsFile.Serialize(json);
            });

            Assert.True(externalWritten.Wait(TimeSpan.FromSeconds(5)), "The controlled external write did not run");
            var final = ClaudeSettingsFile.ParseRoot(File.ReadAllText(settingsPath));
            Assert.Equal("written-by-claude-code", final["model"]!.GetValue<string>());
            Assert.NotNull(final["reviewPayload"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previous);
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    // V04-07: si falla el paso de promover el helper nuevo, el viejo vuelve a su sitio (settings.json apunta ahí).
    [Fact]
    public void HelperDeployer_restores_the_old_helper_when_promotion_fails()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cbd-v0407-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var target = Path.Combine(dir, "bin", HelperDeployer.ExeName);
            var src1 = Path.Combine(dir, "v1.exe"); File.WriteAllText(src1, "v1");
            var src2 = Path.Combine(dir, "v2.exe"); File.WriteAllText(src2, "v2");
            HelperDeployer.Ensure(src1, target);

            // El antivirus bloquea el .new justo al promoverlo.
            void FailingMove(string from, string to)
            {
                if (from.EndsWith(".new", StringComparison.OrdinalIgnoreCase)) throw new IOException("bloqueado");
                File.Move(from, to);
            }

            var result = HelperDeployer.Ensure(src2, target, FailingMove);
            Assert.Equal(target, result);
            Assert.Equal("v1", File.ReadAllText(target));
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.new"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
