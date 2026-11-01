using System.Diagnostics;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class ForegroundDetectorTests
{
    [Fact]
    public void Null_pid_is_not_foreground()
        => Assert.False(new ForegroundDetector().IsSessionForeground((int?)null));

    [Fact]
    public void Does_not_throw_for_arbitrary_pid()
    {
        var d = new ForegroundDetector();
        var _ = d.IsSessionForeground(999999); // pid inexistente: false sin excepción
    }

    private static SessionState Session(params int[] ancestorPids) => new()
    {
        SessionId = "s", Cwd = "c", Pid = 111,
        Ancestors = ancestorPids.Select(p => new ProcessInfo(p, "x.exe", DateTime.UtcNow)).ToList(),
    };

    // v0.3.10: antes se buscaba al revés (desde la ventana hacia arriba) y ParentPid devolvía siempre null.
    [Fact]
    public void Foreground_window_owned_by_an_ancestor_counts_as_focused()
        => Assert.True(ForegroundDetector.IsSessionForeground(Session(200, 300, 400), 300));

    [Fact]
    public void Foreground_window_of_an_unrelated_process_is_not_focused()
        => Assert.False(ForegroundDetector.IsSessionForeground(Session(200, 300), 999));

    [Fact]
    public void No_foreground_window_is_never_focused()
        => Assert.False(ForegroundDetector.IsSessionForeground(Session(200), 0));

    [Fact]
    public void Ancestors_of_the_test_process_are_resolved_with_start_times()
    {
        // El runner de tests siempre tiene padre (dotnet/testhost/vstest): la instantánea Toolhelp32 funciona.
        var anc = ProcessTree.Ancestors(Environment.ProcessId);
        Assert.NotEmpty(anc);
        Assert.All(anc, a => Assert.True(a.Pid > 0 && a.ExeName.Length > 0));
        Assert.True(ProcessTree.IsAlive(anc[0]));
        Assert.False(ProcessTree.IsAlive(anc[0] with { StartUtc = anc[0].StartUtc.AddHours(-1) }));
    }

    [Fact]
    public void FindClaude_prefers_claude_exe_over_node()
    {
        var anc = new List<ProcessInfo>
        {
            new(1, "bash.exe", DateTime.UtcNow), new(2, "node.exe", DateTime.UtcNow), new(3, "claude.exe", DateTime.UtcNow),
        };
        Assert.Equal(3, ProcessTree.FindClaude(anc)!.Value.Pid);
        Assert.Null(ProcessTree.FindClaude(new List<ProcessInfo> { new(1, "pwsh.exe", DateTime.UtcNow) }));
    }
}
