using ClaudeBarWin.Services;

public class ShellQuoteTests
{
    [Theory]
    [InlineData(@"C:\Users\zorro\AppData\Local\ClaudeBarWin\bin\claudebar-helper.exe", ClaudeShell.GitBash,
                "C:/Users/zorro/AppData/Local/ClaudeBarWin/bin/claudebar-helper.exe")]
    [InlineData(@"C:\Users\zorro\AppData\Local\ClaudeBarWin\bin\claudebar-helper.exe", ClaudeShell.PowerShell,
                "C:/Users/zorro/AppData/Local/ClaudeBarWin/bin/claudebar-helper.exe")]
    [InlineData(@"C:\Users\John Smith\x\claudebar-helper.exe", ClaudeShell.GitBash, "'C:/Users/John Smith/x/claudebar-helper.exe'")]
    [InlineData(@"C:\Users\John Smith\x\claudebar-helper.exe", ClaudeShell.PowerShell, "& 'C:/Users/John Smith/x/claudebar-helper.exe'")]
    [InlineData(@"C:\Users\O'Brien\claudebar-helper.exe", ClaudeShell.GitBash, @"'C:/Users/O'\''Brien/claudebar-helper.exe'")]
    [InlineData(@"C:\Users\O'Brien\claudebar-helper.exe", ClaudeShell.PowerShell, "& 'C:/Users/O''Brien/claudebar-helper.exe'")]
    [InlineData(@"C:\Users\José\claudebar-helper.exe", ClaudeShell.GitBash, "'C:/Users/José/claudebar-helper.exe'")]
    public void Command_quotes_only_when_needed(string path, ClaudeShell shell, string expected) =>
        Assert.Equal(expected, ShellQuote.Command(path, shell));

    [Fact]
    public void Detect_uses_explicit_git_bash_path()
    {
        string? Env(string k) => k == "CLAUDE_CODE_GIT_BASH_PATH" ? @"D:\git\bin\bash.exe" : null;
        Assert.Equal(ClaudeShell.GitBash, ClaudeShellDetector.Detect(Env, p => p == @"D:\git\bin\bash.exe"));
    }

    [Fact]
    public void Detect_finds_bash_next_to_git_in_path()
    {
        string? Env(string k) => k == "PATH" ? @"C:\Tools;C:\Program Files\Git\cmd" : null;
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { @"C:\Program Files\Git\cmd\git.exe", @"C:\Program Files\Git\bin\bash.exe" };
        Assert.Equal(ClaudeShell.GitBash, ClaudeShellDetector.Detect(Env, files.Contains));
    }

    [Fact]
    public void Detect_falls_back_to_powershell()
    {
        string? Env(string k) => k == "PATH" ? @"C:\Tools" : null;
        Assert.Equal(ClaudeShell.PowerShell, ClaudeShellDetector.Detect(Env, _ => false));
    }
}
