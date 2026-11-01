using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="PermissionBroker"/>: PURO (el instante llega por parámetro). Guarda las peticiones de permiso
/// pendientes y completa la tarea de cada una con la decisión del notch, o con <see cref="PermissionVerdict.None"/>
/// si caduca, se cancela su sesión o el helper se va (fallo seguro: Claude Code muestra su diálogo).
/// </summary>
public class PermissionBrokerTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(25);

    /// <summary>Veredicto de una tarea que YA debe estar completada (sin bloquear: falla si no lo está).</summary>
    private static PermissionVerdict Done(Task<PermissionVerdict> t)
    {
        Assert.True(t.IsCompletedSuccessfully, "la tarea del broker debería estar completada");
        return t.IsCompletedSuccessfully ? t.Result : (PermissionVerdict)(-1);
    }

    private static PermissionRequestInfo R(string id, string session = "s1", DateTime? at = null) =>
        new(id, session, "proj", "Bash", "echo hola", at ?? T0, PreviewComplete: true);

    [Fact]
    public void Resolve_completes_task_with_verdict()
    {
        var b = new PermissionBroker(Ttl);
        var allow = b.Register(R("r1"));
        var deny = b.Register(R("r2", "s2"));

        Assert.True(b.Resolve("r1", PermissionVerdict.Allow, T0.AddSeconds(1)));
        Assert.True(b.Resolve("r2", PermissionVerdict.Deny, T0.AddSeconds(1)));

        Assert.True(allow.IsCompleted);
        Assert.Equal(PermissionVerdict.Allow, Done(allow));
        Assert.Equal(PermissionVerdict.Deny, Done(deny));
        Assert.Empty(b.Pending);
    }

    [Fact]
    public void Resolve_after_ttl_returns_false_and_task_is_None()
    {
        var b = new PermissionBroker(Ttl);
        var t = b.Register(R("r1"));

        Assert.False(b.Resolve("r1", PermissionVerdict.Allow, T0 + Ttl));   // justo en el plazo ya no vale

        Assert.True(t.IsCompleted);
        Assert.Equal(PermissionVerdict.None, Done(t));
        Assert.Empty(b.Pending);
    }

    [Fact]
    public void Resolve_twice_or_unknown_returns_false_and_keeps_first_verdict()
    {
        var b = new PermissionBroker(Ttl);
        var t = b.Register(R("r1"));
        Assert.True(b.Resolve("r1", PermissionVerdict.Deny, T0));
        Assert.False(b.Resolve("r1", PermissionVerdict.Allow, T0));
        Assert.False(b.Resolve("nope", PermissionVerdict.Allow, T0));
        Assert.Equal(PermissionVerdict.Deny, Done(t));
    }

    [Fact]
    public void CancelSession_resolves_only_that_session_with_None()
    {
        var b = new PermissionBroker(Ttl);
        var a1 = b.Register(R("r1", "a"));
        var a2 = b.Register(R("r2", "a"));
        var other = b.Register(R("r3", "b"));

        b.CancelSession("a");

        Assert.Equal(PermissionVerdict.None, Done(a1));
        Assert.Equal(PermissionVerdict.None, Done(a2));
        Assert.False(other.IsCompleted);
        Assert.Equal(new[] { "r3" }, b.Pending.Select(p => p.RequestId));
        Assert.False(b.Resolve("r1", PermissionVerdict.Allow, T0));        // ya no se puede contestar
    }

    [Fact]
    public void Two_requests_are_independent()
    {
        var b = new PermissionBroker(Ttl);
        var one = b.Register(R("r1", "a"));
        var two = b.Register(R("r2", "b"));

        Assert.True(b.Resolve("r2", PermissionVerdict.Allow, T0));

        Assert.False(one.IsCompleted);
        Assert.Equal(PermissionVerdict.Allow, Done(two));
        Assert.Equal(new[] { "r1" }, b.Pending.Select(p => p.RequestId));
    }

    [Fact]
    public void Duplicate_request_id_is_single_use()
    {
        var b = new PermissionBroker(Ttl);
        var first = b.Register(R("r1"));
        var dup = b.Register(R("r1", "s2"));

        Assert.True(dup.IsCompleted);
        Assert.Equal(PermissionVerdict.None, Done(dup));
        Assert.False(first.IsCompleted);
        Assert.Single(b.Pending);

        // Tampoco se reutiliza un id ya resuelto.
        b.Resolve("r1", PermissionVerdict.Allow, T0);
        Assert.Equal(PermissionVerdict.None, Done(b.Register(R("r1"))));
    }

    [Fact]
    public void Expire_closes_only_the_stale_ones_with_None()
    {
        var b = new PermissionBroker(Ttl);
        var old = b.Register(R("r1", at: T0));
        var fresh = b.Register(R("r2", at: T0.AddSeconds(10)));

        b.Expire(T0 + Ttl);

        Assert.Equal(PermissionVerdict.None, Done(old));
        Assert.False(fresh.IsCompleted);
        Assert.Equal(new[] { "r2" }, b.Pending.Select(p => p.RequestId));
    }

    [Fact]
    public void Cancel_by_id_closes_with_None()
    {
        var b = new PermissionBroker(Ttl);
        var t = b.Register(R("r1"));
        Assert.True(b.Cancel("r1"));
        Assert.False(b.Cancel("r1"));
        Assert.Equal(PermissionVerdict.None, Done(t));
    }

    [Fact]
    public void Changed_fires_on_every_change_and_Pending_keeps_arrival_order()
    {
        var b = new PermissionBroker(Ttl);
        int changes = 0;
        b.Changed += () => changes++;

        b.Register(R("r1", "a"));
        b.Register(R("r2", "b"));
        Assert.Equal(new[] { "r1", "r2" }, b.Pending.Select(p => p.RequestId));
        b.Resolve("r1", PermissionVerdict.Allow, T0);
        b.CancelSession("nobody");                                          // sin cambios: sin aviso
        b.Expire(T0);                                                       // nada caducado: sin aviso
        Assert.Equal(3, changes);
    }

    // ── Notch oculto (revisión cruzada, punto 3) ─────────────────────────────────────────────────────

    [Fact]
    public void Accepts_requests_by_default()
    {
        var b = new PermissionBroker(Ttl);
        Assert.True(b.Accepting);
        Assert.False(b.Register(R("r1")).IsCompleted);
    }

    [Fact]
    public void Not_accepting_closes_everything_pending_without_decision()
    {
        var b = new PermissionBroker(Ttl);
        var r1 = b.Register(R("r1", "a"));
        var r2 = b.Register(R("r2", "b"));
        int changes = 0;
        b.Changed += () => changes++;

        b.SetAccepting(false);

        Assert.False(b.Accepting);
        Assert.Equal(PermissionVerdict.None, Done(r1));
        Assert.Equal(PermissionVerdict.None, Done(r2));
        Assert.Empty(b.Pending);
        Assert.Equal(1, changes);                                           // un solo aviso para todo
        Assert.False(b.Resolve("r1", PermissionVerdict.Allow, T0));         // un clic tardío no decide
    }

    [Fact]
    public void While_not_accepting_Register_answers_None_at_once()
    {
        var b = new PermissionBroker(Ttl);
        b.SetAccepting(false);
        int changes = 0;
        b.Changed += () => changes++;

        var t = b.Register(R("r1"));
        Assert.Equal(PermissionVerdict.None, Done(t));                      // al instante, sin esperar el plazo
        Assert.Empty(b.Pending);
        Assert.Equal(0, changes);                                           // el notch ni se entera
        b.SetAccepting(false);                                              // repetirlo no cambia nada
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Accepting_again_registers_new_requests_as_usual()
    {
        var b = new PermissionBroker(Ttl);
        var old = b.Register(R("r1"));
        b.SetAccepting(false);
        b.SetAccepting(true);

        Assert.True(b.Accepting);
        Assert.Equal(PermissionVerdict.None, Done(old));                    // la cerrada no vuelve
        var t = b.Register(R("r2"));
        Assert.False(t.IsCompleted);
        Assert.True(b.Resolve("r2", PermissionVerdict.Deny, T0));
        Assert.Equal(PermissionVerdict.Deny, Done(t));
    }

    [Fact]
    public void Continuations_do_not_run_inline_inside_Resolve()
    {
        // El servidor del pipe espera la tarea; su continuación no debe ejecutarse dentro del clic (hilo de UI).
        var b = new PermissionBroker(Ttl);
        var t = b.Register(R("r1"));
        int resolvingThread = Environment.CurrentManagedThreadId, continuationThread = resolvingThread;
        using var ran = new ManualResetEventSlim();
        // Se espera a un evento (no a la tarea): Task.Wait podría ejecutar la continuación en línea aquí mismo.
        _ = t.ContinueWith(_ => { continuationThread = Environment.CurrentManagedThreadId; ran.Set(); },
            TaskContinuationOptions.ExecuteSynchronously);
        b.Resolve("r1", PermissionVerdict.Allow, T0);
        Assert.True(ran.Wait(2000));
        Assert.NotEqual(resolvingThread, continuationThread);
    }
}
