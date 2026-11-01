using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class SessionRegistryTests
{
    // procStart real observado (FILETIME en texto) y su equivalente UTC.
    private const string ProcStartText = "134376766475057104";
    private static readonly DateTime ProcStart = DateTime.FromFileTimeUtc(134376766475057104);

    private static string Json(int pid = 108116, string id = "57ddfa2e-45c2-4608-bdd6-a651935e2f9e",
        string status = "busy", string entrypoint = "cli", string? procStart = ProcStartText)
        => "{\"pid\":" + pid + ",\"sessionId\":\"" + id + "\",\"cwd\":\"C:\\\\Users\\\\zorro\\\\Asistente\","
           + "\"startedAt\":1793203050475,"
           + (procStart is null ? "" : "\"procStart\":\"" + procStart + "\",")
           + "\"version\":\"2.1.312\",\"peerFeatures\":[\"notify_idle\"],\"kind\":\"interactive\","
           + "\"entrypoint\":\"" + entrypoint + "\",\"name\":\"asistente-98\",\"status\":\"" + status + "\","
           + "\"updatedAt\":1793242584325,\"statusUpdatedAt\":1793242584325}";

    // ---------- Parseo ----------

    [Fact]
    public void Parse_reads_a_valid_registry_file()
    {
        var e = SessionRegistry.Parse(Json());
        Assert.NotNull(e);
        Assert.Equal(108116, e!.Pid);
        Assert.Equal("57ddfa2e-45c2-4608-bdd6-a651935e2f9e", e.SessionId);
        Assert.Equal(@"C:\Users\zorro\Asistente", e.Cwd);
        Assert.Equal("asistente-98", e.Name);
        Assert.Equal("cli", e.Entrypoint);
        Assert.Equal("interactive", e.Kind);
        Assert.True(e.IsBusy);
        Assert.Equal(ProcStart, e.ProcStartUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793203050475).UtcDateTime, e.StartedAtUtc);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1793242584325).UtcDateTime, e.StatusSinceUtc);
    }

    [Fact]
    public void Parse_tolerates_trailing_data_after_the_first_object()
    {
        // Archivo a medio reescribir: el objeto nuevo y restos del anterior detrás.
        var e = SessionRegistry.Parse(Json() + "ion\":\"2.1.300\",\"status\":\"idle\"}");
        Assert.NotNull(e);
        Assert.True(e!.IsBusy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{\"pid\":12")]
    [InlineData("[1,2,3]")]
    [InlineData("no es json")]
    [InlineData("{\"sessionId\":\"abc\",\"status\":\"idle\"}")]   // sin pid
    [InlineData("{\"pid\":42,\"status\":\"idle\"}")]                // sin sessionId
    [InlineData("{\"pid\":0,\"sessionId\":\"abc\"}")]              // pid inválido
    public void Parse_returns_null_for_unusable_text(string text)
        => Assert.Null(SessionRegistry.Parse(text));

    [Fact]
    public void Parse_accepts_missing_optional_fields()
    {
        var e = SessionRegistry.Parse("{\"pid\":7,\"sessionId\":\"s7\"}");
        Assert.NotNull(e);
        Assert.Equal("", e!.Cwd);
        Assert.Equal("", e.Status);
        Assert.Null(e.ProcStartUtc);
        Assert.Null(e.StartedAtUtc);
        Assert.Null(e.Entrypoint);
        Assert.False(e.IsBusy);
        Assert.False(e.IsIdle);
    }

    [Fact]
    public void Parse_keeps_unknown_status_as_neither_busy_nor_idle()
    {
        var e = SessionRegistry.Parse(Json(status: "thinking"));
        Assert.NotNull(e);
        Assert.False(e!.IsBusy);
        Assert.False(e.IsIdle);
    }

    [Theory]
    [InlineData("cli", true)]
    [InlineData("sdk-ts", true)]
    [InlineData(null, true)]
    [InlineData("claude-desktop", false)]
    public void IsSupportedEntrypoint_accepts_cli_and_sdk(string? ep, bool expected)
        => Assert.Equal(expected, SessionRegistry.IsSupportedEntrypoint(ep));

    // ---------- Vivas / huérfanas ----------

    private static RegistryEntry Entry(int pid, string id, DateTime? procStart, string status = "idle",
        string? entrypoint = "cli", DateTime? startedAt = null)
        => new(pid, id, @"C:\p\" + id, status, procStart, startedAt, null, null, entrypoint, "interactive");

    [Fact]
    public void FilterLive_discards_orphans_whose_process_is_gone()
    {
        var entries = new[] { Entry(108116, "vivo", ProcStart), Entry(9616, "huerfano", ProcStart) };
        var live = SessionRegistry.FilterLive(entries, pid => pid == 108116 ? ProcStart : null);
        Assert.Equal("vivo", Assert.Single(live).SessionId);
    }

    [Fact]
    public void FilterLive_discards_reused_pid_with_different_start()
    {
        var entries = new[] { Entry(500, "viejo", ProcStart) };
        var live = SessionRegistry.FilterLive(entries, _ => ProcStart.AddMinutes(10));
        Assert.Empty(live);
    }

    [Fact]
    public void FilterLive_falls_back_to_startedAt_when_procStart_is_missing()
    {
        var started = ProcStart.AddSeconds(3); // Claude Code escribe startedAt ~3 s tras arrancar
        var ok = SessionRegistry.FilterLive(new[] { Entry(1, "a", null, startedAt: started) }, _ => ProcStart);
        Assert.Single(ok);
        Assert.Equal(ProcStart, ok[0].ProcStartUtc); // se rellena con la hora real para vigilar su muerte

        var reused = SessionRegistry.FilterLive(new[] { Entry(1, "a", null, startedAt: started) },
            _ => started.AddHours(1));
        Assert.Empty(reused);
    }

    [Fact]
    public void FilterLive_ignores_unsupported_entrypoints()
    {
        var live = SessionRegistry.FilterLive(new[] { Entry(1, "a", ProcStart, entrypoint: "claude-desktop") },
            _ => ProcStart);
        Assert.Empty(live);
    }

    [Fact]
    public void Reader_reads_live_json_files_and_never_the_key_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cbw-reg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "108116.json"), Json());
            File.WriteAllText(Path.Combine(dir, "9616.json"), Json(pid: 9616, id: "huerfano"));
            File.WriteAllText(Path.Combine(dir, "77.json"), "{roto");
            // Un .key con JSON válido: si se leyera, aparecería una sesión de más.
            File.WriteAllText(Path.Combine(dir, "108116.abc.key"), Json(pid: 108116, id: "clave"));

            var reader = new SessionRegistryReader(dir, pid => pid == 108116 ? ProcStart : null);
            var live = reader.ReadLive();
            Assert.NotNull(live);
            Assert.Equal("57ddfa2e-45c2-4608-bdd6-a651935e2f9e", Assert.Single(live!).SessionId);

            // A medio escribir: se reutiliza la última lectura buena (la sesión no parpadea).
            File.WriteAllText(Path.Combine(dir, "108116.json"), "{\"pid\":108");
            Assert.Single(reader.ReadLive()!);

            // Borrado → desaparece.
            File.Delete(Path.Combine(dir, "108116.json"));
            Assert.Empty(reader.ReadLive()!);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Reader_returns_empty_when_folder_does_not_exist()
    {
        var reader = new SessionRegistryReader(Path.Combine(Path.GetTempPath(), "cbw-no-" + Guid.NewGuid()), _ => null);
        Assert.Empty(reader.ReadLive()!);
    }

    // ---------- Fusión en SessionStore ----------

    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);

    private static RegistryEntry Live(string id, string status, DateTime? statusSince = null, int pid = 100,
        string cwd = @"C:\Users\z\Proyectos\phoenix")
        => new(pid, id, cwd, status, ProcStart, null, statusSince ?? T0.AddMinutes(-5), "n", "cli", "interactive");

    private static HookEvent Ev(string id, string ev, string status)
        => new() { SessionId = id, Cwd = @"C:\Users\z\Proyectos\phoenix", Event = ev, Status = status };

    [Fact]
    public void Sync_adds_unknown_idle_session()
    {
        var s = new SessionStore();
        s.SyncRegistry(new[] { Live("a", "idle") }, T0);
        var sess = Assert.Single(s.Snapshot());
        Assert.Equal("a", sess.SessionId);
        Assert.Equal("phoenix", sess.ProjectName);
        Assert.Equal(SessionPhase.Idle, sess.Phase);
        Assert.Equal(100, sess.ClaudeProcess?.Pid);
        Assert.True(sess.InRegistry);
    }

    [Fact]
    public void Sync_adds_unknown_busy_session_as_processing()
    {
        var s = new SessionStore();
        s.SyncRegistry(new[] { Live("a", "busy") }, T0);
        Assert.Equal(SessionPhase.Processing, Assert.Single(s.Snapshot()).Phase);
    }

    [Fact]
    public void Sync_does_not_override_waiting_for_approval_from_hooks()
    {
        var s = new SessionStore();
        s.Apply(Ev("a", "PermissionRequest", "waiting_for_approval"), T0);
        s.SyncRegistry(new[] { Live("a", "busy", statusSince: T0.AddSeconds(5)) }, T0.AddSeconds(6));
        s.SyncRegistry(new[] { Live("a", "idle", statusSince: T0.AddSeconds(7)) }, T0.AddSeconds(8));
        var sess = Assert.Single(s.Snapshot());
        Assert.Equal(SessionPhase.WaitingForApproval, sess.Phase);
        Assert.True(sess.InRegistry);
    }

    [Fact]
    public void Sync_busy_moves_idle_hook_session_to_processing_only_if_newer()
    {
        var s = new SessionStore();
        s.Apply(Ev("a", "SessionStart", "idle"), T0);   // fase por hook: Idle desde T0
        Assert.Equal(SessionPhase.Idle, s.Snapshot()[0].Phase);

        // Un "busy" ANTERIOR al último cambio de fase por hook no la pisa.
        s.SyncRegistry(new[] { Live("a", "busy", statusSince: T0.AddSeconds(-1)) }, T0.AddSeconds(1));
        Assert.Equal(SessionPhase.Idle, s.Snapshot()[0].Phase);

        s.SyncRegistry(new[] { Live("a", "busy", statusSince: T0.AddSeconds(2)) }, T0.AddSeconds(3));
        Assert.Equal(SessionPhase.Processing, s.Snapshot()[0].Phase);
    }

    [Fact]
    public void Sync_does_not_override_waiting_for_input_from_hooks()
    {
        var s = new SessionStore();
        s.Apply(Ev("a", "UserPromptSubmit", "processing"), T0);
        s.Apply(Ev("a", "Notification", "waiting_for_input"), T0.AddSeconds(1));
        s.SyncRegistry(new[] { Live("a", "busy", statusSince: T0.AddSeconds(5)) }, T0.AddSeconds(6));
        s.SyncRegistry(new[] { Live("a", "idle", statusSince: T0.AddSeconds(7)) }, T0.AddSeconds(8));
        Assert.Equal(SessionPhase.WaitingForInput, s.Snapshot()[0].Phase);
    }

    [Fact]
    public void Sync_idle_ends_a_stale_processing_phase()
    {
        var s = new SessionStore();
        s.SyncRegistry(new[] { Live("a", "busy", statusSince: T0) }, T0);
        s.SyncRegistry(new[] { Live("a", "idle", statusSince: T0.AddSeconds(30)) }, T0.AddSeconds(31));
        Assert.Equal(SessionPhase.Idle, s.Snapshot()[0].Phase);
    }

    [Fact]
    public void Sync_removes_registry_session_when_it_disappears()
    {
        var s = new SessionStore();
        s.SyncRegistry(new[] { Live("a", "idle"), Live("b", "idle", pid: 200) }, T0);
        s.Apply(Ev("solo-hook", "PreToolUse", "running_tool"), T0);
        s.SyncRegistry(new[] { Live("b", "idle", pid: 200) }, T0.AddSeconds(1), _ => false);
        var ids = s.Snapshot().Select(x => x.SessionId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "b", "solo-hook" }, ids); // la que solo conoce por hook no la toca
    }

    [Fact]
    public void Sessions_in_registry_are_not_pruned_by_ttl()
    {
        var s = new SessionStore();
        s.SyncRegistry(new[] { Live("a", "idle") }, T0);
        s.Apply(Ev("b", "Stop", "waiting_for_input"), T0);
        var removed = s.Prune(T0.AddDays(2), _ => true);
        Assert.Equal(1, removed);
        Assert.Equal("a", Assert.Single(s.Snapshot()).SessionId);
    }

    [Fact]
    public void Registry_session_whose_process_died_is_pruned()
    {
        var s = new SessionStore();
        s.SyncRegistry(new[] { Live("a", "idle") }, T0);
        Assert.Equal(1, s.Prune(T0.AddSeconds(1), _ => false));
        Assert.Empty(s.Snapshot());
    }

    [Fact]
    public void Sync_raises_Changed_only_when_something_changed()
    {
        var s = new SessionStore();
        var fired = 0;
        s.Changed += () => fired++;
        var live = new[] { Live("a", "idle") };
        Assert.True(s.SyncRegistry(live, T0));
        Assert.Equal(1, fired);
        Assert.False(s.SyncRegistry(live, T0.AddSeconds(5)));
        Assert.False(s.SyncRegistry(live, T0.AddSeconds(10)));
        Assert.Equal(1, fired);
        Assert.False(new SessionStore().SyncRegistry(Array.Empty<RegistryEntry>(), T0));
    }

    [Fact]
    public void Sync_does_not_resurrect_an_ended_session_still_listed()
    {
        var s = new SessionStore();
        s.SyncRegistry(new[] { Live("a", "idle") }, T0);
        s.Apply(Ev("a", "SessionEnd", "ended"), T0.AddSeconds(1));
        s.Prune(T0.AddSeconds(2), _ => true);
        Assert.Empty(s.Snapshot());
        s.SyncRegistry(new[] { Live("a", "idle") }, T0.AddSeconds(3)); // el archivo aún no se borró
        Assert.Empty(s.Snapshot());
    }

    [Fact]
    public void Sync_updates_cwd_of_known_session()
    {
        var s = new SessionStore();
        s.SyncRegistry(new[] { Live("a", "idle") }, T0);
        s.SyncRegistry(new[] { Live("a", "idle", cwd: @"C:\Users\z\Proyectos\tkb") }, T0.AddSeconds(1));
        Assert.Equal("tkb", s.Snapshot()[0].ProjectName);
    }

    [Fact]
    public void Stop_after_registry_idle_still_becomes_waiting_for_input()
    {
        // Carrera: la pasada del registro ("idle", más nuevo) llega antes que el hook Stop.
        var s = new SessionStore();
        s.Apply(Ev("a", "UserPromptSubmit", "processing"), T0);
        s.SyncRegistry(new[] { Live("a", "idle", statusSince: T0.AddSeconds(5)) }, T0.AddSeconds(5));
        Assert.Equal(SessionPhase.Idle, s.Snapshot()[0].Phase);
        s.Apply(Ev("a", "Stop", "waiting_for_input"), T0.AddSeconds(6));
        Assert.Equal(SessionPhase.WaitingForInput, s.Snapshot()[0].Phase);
    }

    [Fact]
    public void Idle_prompt_after_registry_idle_becomes_waiting_for_input()
    {
        var s = new SessionStore();
        s.Apply(Ev("a", "UserPromptSubmit", "processing"), T0);
        s.SyncRegistry(new[] { Live("a", "idle", statusSince: T0.AddSeconds(5)) }, T0.AddSeconds(5));
        s.Apply(Ev("a", "Notification", "waiting_for_input"), T0.AddSeconds(65));
        Assert.Equal(SessionPhase.WaitingForInput, s.Snapshot()[0].Phase);
    }

    [Fact]
    public void Hook_idle_session_does_not_jump_to_waiting_for_input()
    {
        // Sin el registro de por medio la regla de siempre sigue: Idle (por hook) → WaitingForInput no vale.
        var s = new SessionStore();
        s.Apply(Ev("a", "SessionStart", "idle"), T0);
        s.Apply(Ev("a", "Notification", "waiting_for_input"), T0.AddSeconds(60));
        Assert.Equal(SessionPhase.Idle, s.Snapshot()[0].Phase);
    }

    [Fact]
    public void Sync_keeps_session_missing_once_while_its_process_lives()
    {
        var s = new SessionStore();
        s.SyncRegistry(new[] { Live("a", "busy", statusSince: T0) }, T0);
        s.Apply(Ev("a", "PermissionRequest", "waiting_for_approval"), T0.AddSeconds(1));

        // Una lectura fallida la saca del registro una vez: se conserva con su permiso pendiente.
        s.SyncRegistry(Array.Empty<RegistryEntry>(), T0.AddSeconds(2), _ => true);
        Assert.Equal(SessionPhase.WaitingForApproval, Assert.Single(s.Snapshot()).Phase);

        // Vuelve a aparecer: se olvida la falta, y otra falta suelta tampoco la quita.
        s.SyncRegistry(new[] { Live("a", "busy", statusSince: T0) }, T0.AddSeconds(3), _ => true);
        s.SyncRegistry(Array.Empty<RegistryEntry>(), T0.AddSeconds(4), _ => true);
        Assert.Single(s.Snapshot());

        // Dos pasadas seguidas sin ella: fuera.
        s.SyncRegistry(Array.Empty<RegistryEntry>(), T0.AddSeconds(5), _ => true);
        Assert.Empty(s.Snapshot());
    }

    [Fact]
    public void Hook_ancestor_does_not_override_registry_process()
    {
        var s = new SessionStore();
        s.SyncRegistry(new[] { Live("a", "idle") }, T0);
        var e = Ev("a", "PreToolUse", "running_tool");
        e.Ancestors = new[] { new ProcessInfo(4242, "node.exe", T0) };
        s.Apply(e, T0.AddSeconds(1));
        Assert.Equal(100, s.Snapshot()[0].ClaudeProcess?.Pid);
        // La pasada siguiente no tiene nada que corregir.
        Assert.False(s.SyncRegistry(new[] { Live("a", "busy", statusSince: T0.AddSeconds(-1)) }, T0.AddSeconds(2)));
    }
}
