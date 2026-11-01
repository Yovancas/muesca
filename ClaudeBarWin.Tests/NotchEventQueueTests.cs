using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="NotchEventQueue"/>: cola de avisos del notch por prioridad (Permiso &gt; Aviso 95 &gt;
/// Terminado &gt; HUD 80), fusión por (tipo, sesión) y caducidad medida desde que el aviso se muestra.
/// </summary>
public class NotchEventQueueTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private static NotchAlert A(NotchAlertKind k, string id, DateTime at, string title = "p") =>
        new(k, id, title, null, at, NotchAlert.DefaultDuration(k));

    [Fact]
    public void Permission_beats_Completed()
    {
        var q = new NotchEventQueue();
        q.Enqueue(A(NotchAlertKind.Completed, "a", T0));
        q.Enqueue(A(NotchAlertKind.Permission, "b", T0.AddSeconds(1)));
        Assert.Equal(NotchAlertKind.Permission, q.Current(T0.AddSeconds(1))!.Kind);
        Assert.Equal(2, q.Count);
    }

    [Fact]
    public void Same_kind_and_session_merges()
    {
        var q = new NotchEventQueue();
        q.Enqueue(A(NotchAlertKind.Permission, "a", T0, "viejo"));
        q.Enqueue(A(NotchAlertKind.Permission, "a", T0.AddSeconds(1), "nuevo"));
        Assert.Equal(1, q.Count);
        Assert.Equal("nuevo", q.Current(T0.AddSeconds(1))!.Title);
        // Otro tipo de la misma sesión NO se fusiona.
        q.Enqueue(A(NotchAlertKind.Completed, "a", T0.AddSeconds(2)));
        Assert.Equal(2, q.Count);
    }

    [Fact]
    public void Completed_expires_after_4s_from_becoming_current()
    {
        var q = new NotchEventQueue();
        q.Enqueue(A(NotchAlertKind.Permission, "p", T0));
        q.Enqueue(A(NotchAlertKind.Completed, "a", T0));
        Assert.Equal(NotchAlertKind.Permission, q.Current(T0)!.Kind);

        // El permiso tapa al Terminado 10 s: su reloj no corre mientras no se ve.
        q.Remove(NotchAlertKind.Permission, "p");
        var shown = T0.AddSeconds(10);
        Assert.Equal(NotchAlertKind.Completed, q.Current(shown)!.Kind);
        Assert.Equal(shown + NotchTiming.Completed, q.CurrentExpiresAtUtc);
        Assert.NotNull(q.Current(shown + NotchTiming.Completed - TimeSpan.FromMilliseconds(100)));
        Assert.Null(q.Current(shown + NotchTiming.Completed));
        Assert.Equal(0, q.Count);
        Assert.Null(q.CurrentExpiresAtUtc);
    }

    [Fact]
    public void Permission_never_expires_until_removed()
    {
        var q = new NotchEventQueue();
        q.Enqueue(A(NotchAlertKind.Permission, "a", T0));
        Assert.NotNull(q.Current(T0));
        Assert.Null(q.CurrentExpiresAtUtc);
        Assert.NotNull(q.Current(T0.AddHours(3)));
        q.Remove(NotchAlertKind.Permission, "a");
        Assert.Null(q.Current(T0.AddHours(3)));
    }

    [Fact]
    public void Three_sessions_completing_yield_three_alerts_in_order()
    {
        var q = new NotchEventQueue();
        q.Enqueue(A(NotchAlertKind.Completed, "a", T0));
        q.Enqueue(A(NotchAlertKind.Completed, "b", T0));
        q.Enqueue(A(NotchAlertKind.Completed, "c", T0));

        var seen = new List<string>();
        var now = T0;
        for (int i = 0; i < 20 && q.Current(now) is { } cur; i++)
        {
            if (seen.Count == 0 || seen[^1] != cur.SessionId) seen.Add(cur.SessionId);
            now = now.AddSeconds(1);
        }
        Assert.Equal(new[] { "a", "b", "c" }, seen);   // ninguno perdido, sin duplicados
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void Merging_the_current_alert_restarts_its_duration()
    {
        var q = new NotchEventQueue();
        q.Enqueue(A(NotchAlertKind.Completed, "a", T0));
        Assert.NotNull(q.Current(T0));
        q.Enqueue(A(NotchAlertKind.Completed, "a", T0.AddSeconds(3)));
        Assert.NotNull(q.Current(T0.AddSeconds(5)));                       // el original habría caducado
        Assert.Null(q.Current(T0.AddSeconds(3) + NotchTiming.Completed));   // cuenta desde la fusión
    }
}
