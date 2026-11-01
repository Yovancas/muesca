using ClaudeBarWin.Services;

public class HelperDeployerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cbd-" + Guid.NewGuid().ToString("N"));
    public HelperDeployerTests() => Directory.CreateDirectory(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private string Src(string content) { var p = Path.Combine(_root, "src.exe"); File.WriteAllText(p, content); return p; }
    private string Target => Path.Combine(_root, "bin", HelperDeployer.ExeName);

    [Fact]
    public void Copies_when_missing()
    {
        Assert.Equal(Target, HelperDeployer.Ensure(Src("v1"), Target));
        Assert.Equal("v1", File.ReadAllText(Target));
    }

    [Fact]
    public void Replaces_when_different_even_if_target_is_open()
    {
        HelperDeployer.Ensure(Src("v1"), Target);
        // Un exe en ejecución se puede renombrar pero no sobrescribir: FileShare.Delete simula eso.
        using (new FileStream(Target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            HelperDeployer.Ensure(Src("v2"), Target);
        Assert.Equal("v2", File.ReadAllText(Target));
    }

    [Fact]
    public void Leaves_identical_target_alone()
    {
        HelperDeployer.Ensure(Src("v1"), Target);
        var t = File.GetLastWriteTimeUtc(Target);
        HelperDeployer.Ensure(Src("v1"), Target);
        Assert.Equal(t, File.GetLastWriteTimeUtc(Target));
    }

    [Fact]
    public void Without_source_uses_existing_target_or_null()
    {
        Assert.Null(HelperDeployer.Ensure(null, Target));
        HelperDeployer.Ensure(Src("v1"), Target);
        Assert.Equal(Target, HelperDeployer.Ensure(null, Target));
    }

    [Fact]
    public void Cleans_old_copies()
    {
        HelperDeployer.Ensure(Src("v1"), Target);
        HelperDeployer.Ensure(Src("v2"), Target);
        HelperDeployer.Ensure(Src("v2"), Target);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Target)!, "claudebar-helper.old-*"));
    }
}
