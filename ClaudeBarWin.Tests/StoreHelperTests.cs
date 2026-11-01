using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Licensing;
using ClaudeBarWin.Services.Packaging;

/// <summary>
/// Edición de la Store (MSIX, docs/msix.md): el helper no se copia a AppData; los hooks lo lanzan por el alias del paquete,
/// <c>muesca-helper.exe</c>, que se elige con cuidado (otro paquete puede declarar el mismo alias) y se reconoce en
/// settings.json igual que el exe de siempre.
/// </summary>
public class StoreHelperTests
{
    private const string Family = "Bekay.Muesca_8wekyb3d8bbwe";
    private const string Local = @"C:\Users\ana\AppData\Local";
    private static readonly string WindowsApps = Path.Combine(Local, "Microsoft", "WindowsApps");
    private static readonly string Own = Path.Combine(WindowsApps, Family, StoreEdition.HelperAlias);
    private static readonly string Shared = Path.Combine(WindowsApps, StoreEdition.HelperAlias);

    private static AppExecLinkInfo Link(string family, string exe = "claudebar-helper.exe") =>
        new(family, family + "!Helper", $@"C:\Program Files\WindowsApps\{family.Replace("_", "_1.0.0.0_x64__")}\{exe}");

    private static string? Resolve(IEnumerable<string> existing, Func<string, AppExecLinkInfo?> read)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return StoreEdition.ResolveHelperAlias(Local, Family, set.Contains, read);
    }

    // ── Qué alias se escribe en settings.json ───────────────────────────────────────────────────────────

    [Fact]
    public void The_package_family_alias_is_used()
    {
        Assert.Equal(Own, Resolve(new[] { Own, Shared }, _ => Link(Family)));
    }

    [Fact]
    public void The_family_alias_is_accepted_when_its_target_cannot_be_read()
    {
        // El formato del destino no está documentado: sin poder leerlo, la carpeta de la familia sigue siendo solo nuestra.
        Assert.Equal(Own, Resolve(new[] { Own }, _ => null));
    }

    [Fact]
    public void The_shared_alias_is_never_used()
    {
        // Otro paquete puede declarar el mismo alias después de comprobarlo: ni aunque hoy lance nuestro helper.
        Assert.Null(Resolve(new[] { Shared }, _ => Link(Family)));
        Assert.Null(Resolve(new[] { Shared }, _ => Link("Evil.Helper_abcdefghjkmnp")));
        Assert.Null(Resolve(new[] { Shared }, _ => null));
    }

    [Fact]
    public void A_family_alias_that_points_elsewhere_is_rejected()
    {
        Assert.Null(Resolve(new[] { Own, Shared }, p => p == Own ? Link("Evil.Helper_abcdefghjkmnp") : Link(Family)));
        Assert.Null(Resolve(new[] { Own }, _ => Link(Family, "evil.exe")));
        Assert.Null(Resolve(new[] { Own }, _ => Link(Family, "ClaudeBarWin.exe")));
    }

    [Fact]
    public void Without_alias_there_is_no_helper()
    {
        Assert.Null(Resolve(Array.Empty<string>(), _ => Link(Family)));
    }

    [Fact]
    public void Shared_data_lives_in_the_package_folder()
    {
        // La misma ruta que calcula el helper (HelperPaths.PackageDataDir, en sus tests).
        Assert.Equal(Path.Combine(Local, "Packages", Family, "LocalState", "ClaudeBarWin"), StoreEdition.DataDir(Local, Family));
    }

    [Fact]
    public void Outside_a_package_everything_is_the_GitHub_edition()
    {
        // Los tests no corren empaquetados.
        Assert.False(StoreEdition.IsActive);
        Assert.Null(StoreEdition.FamilyName);
        Assert.Null(StoreEdition.InstallPath);
        Assert.Null(StoreEdition.HelperAliasPath());
        Assert.Null(PackageIdentity.FamilyName());
        Assert.Null(PackageIdentity.InstallPath());
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLAUDEBAR_DATA_DIR")))
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeBarWin"),
                HelperDeployer.DataDir);
    }

    // ── El destino del alias (IO_REPARSE_TAG_APPEXECLINK) ───────────────────────────────────────────────

    private static byte[] Reparse(uint tag, params string[] strings)
    {
        var payload = Encoding.Unicode.GetBytes(string.Concat(strings.Select(s => s + "\0")));
        var data = new byte[12 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), (ushort)(4 + payload.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 3);   // versión
        payload.CopyTo(data, 12);
        return data;
    }

    [Fact]
    public void An_app_exec_link_gives_package_app_and_target()
    {
        var target = @"C:\Program Files\WindowsApps\Bekay.Muesca_1.0.0.0_x64__8wekyb3d8bbwe\claudebar-helper.exe";
        var info = AppExecLink.Parse(Reparse(AppExecLink.ReparseTag, Family, Family + "!Helper", target, "0"));
        Assert.NotNull(info);
        Assert.Equal(Family, info!.PackageFamilyName);
        Assert.Equal(Family + "!Helper", info.AppUserModelId);
        Assert.Equal(target, info.TargetPath);
        Assert.True(info.Launches(Family, "claudebar-helper.exe"));
        Assert.True(info.Launches(Family.ToUpperInvariant(), "CLAUDEBAR-HELPER.EXE"));
        Assert.False(info.Launches("Evil.Helper_abcdefghjkmnp", "claudebar-helper.exe"));
        Assert.False(info.Launches(Family, "ClaudeBarWin.exe"));

        // Con solo las tres primeras cadenas también vale (el tipo de aplicación no se usa).
        Assert.NotNull(AppExecLink.Parse(Reparse(AppExecLink.ReparseTag, Family, Family + "!Helper", target)));
        // Con bytes de sobra detrás (el búfer de DeviceIoControl es mayor): se lee lo que dice la longitud.
        Assert.NotNull(AppExecLink.Parse(Reparse(AppExecLink.ReparseTag, Family, Family + "!Helper", target, "0").Concat(new byte[64]).ToArray()));
    }

    [Fact]
    public void Anything_else_is_not_an_app_exec_link()
    {
        var target = @"C:\x\claudebar-helper.exe";
        Assert.Null(AppExecLink.Parse(Array.Empty<byte>()));
        Assert.Null(AppExecLink.Parse(new byte[11]));
        Assert.Null(AppExecLink.Parse(Reparse(0xA000000C, Family, Family + "!Helper", target)));            // un enlace simbólico
        Assert.Null(AppExecLink.Parse(Reparse(AppExecLink.ReparseTag, Family, "Otro_abcdefghjkmnp!App", target)));  // app de otro paquete
        Assert.Null(AppExecLink.Parse(Reparse(AppExecLink.ReparseTag, "", "!Helper", target)));
        Assert.Null(AppExecLink.Parse(Reparse(AppExecLink.ReparseTag, Family, Family + "!Helper", "")));
        Assert.Null(AppExecLink.Parse(Reparse(AppExecLink.ReparseTag, Family, Family + "!Helper")));          // sin destino

        var truncated = Reparse(AppExecLink.ReparseTag, Family, Family + "!Helper", target);
        Assert.Null(AppExecLink.Parse(truncated.AsSpan(0, truncated.Length - 10)));   // la longitud dice más de lo que hay
    }

    [Fact]
    public void Reading_a_normal_file_or_a_missing_one_gives_null()
    {
        Assert.Null(AppExecLink.TryRead(Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid().ToString("N") + ".exe")));
        var file = Path.GetTempFileName();
        try { Assert.Null(AppExecLink.TryRead(file)); }
        finally { File.Delete(file); }
    }

    // ── El alias se reconoce en settings.json como el helper de siempre ─────────────────────────────────

    private static readonly string Alias = Path.Combine(WindowsApps, Family, StoreEdition.HelperAlias);

    [Fact]
    public void The_alias_is_a_helper_path_with_its_exact_name()
    {
        Assert.True(HelperCommand.IsHelperExePath(Alias));
        Assert.True(HelperCommand.IsHelperExePath("muesca-helper"));
        Assert.True(HelperCommand.IsHelperExePath("C:/x/MUESCA-HELPER.EXE"));
        Assert.False(HelperCommand.IsHelperExePath(@"C:\x\not-muesca-helper.exe"));
        Assert.False(HelperCommand.IsHelperExePath(@"C:\x\muesca-helper2.exe"));
        Assert.False(HelperCommand.IsHelperExePath(@"C:\x\muesca.exe"));
    }

    [Fact]
    public void Hooks_with_the_alias_are_ours_in_exec_and_shell_form()
    {
        var exec = HookInstaller.HelperHookSpec(true, Alias, ClaudeShell.GitBash);
        var perm = HookInstaller.PermissionSpec(true, Alias, ClaudeShell.GitBash);
        var merged = HookInstaller.MergeSettings("{}", exec, perm);
        Assert.True(HookInstaller.HasOurHooks(merged));
        Assert.Contains("\"permission\"", merged);
        Assert.False(HookInstaller.HasOurHooks(HookInstaller.RemoveHooks(merged)));
        Assert.DoesNotContain("muesca-helper", HookInstaller.RemoveHooks(merged));

        foreach (var shell in new[] { ClaudeShell.GitBash, ClaudeShell.PowerShell })
        {
            var spec = HookInstaller.HelperHookSpec(false, @"C:\Users\John O'Brien\AppData\Local\Microsoft\WindowsApps\muesca-helper.exe", shell);
            var json = HookInstaller.MergeSettings("{}", spec);
            Assert.True(HookInstaller.HasOurHooks(json));
            Assert.False(HookInstaller.HasOurHooks(HookInstaller.RemoveHooks(json)));
        }
    }

    [Theory]
    [InlineData("""{ "type": "command", "command": "C:\\x\\not-muesca-helper.exe", "args": ["hook"] }""")]
    [InlineData("""{ "type": "command", "command": "C:\\x\\muesca-helper.exe", "args": ["hook", "--x"] }""")]
    [InlineData("""{ "type": "command", "command": "C:/x/muesca-helper.exe hook && rm -rf x" }""")]
    [InlineData("""{ "type": "command", "command": "echo muesca-helper.exe hook" }""")]
    public void Lookalikes_of_the_alias_are_not_ours(string hook)
    {
        var json = $$"""{ "hooks": { "Stop": [ { "matcher": "*", "hooks": [ {{hook}} ] } ] } }""";
        Assert.False(HookInstaller.HasOurHooks(json));
        Assert.Contains("muesca-helper", HookInstaller.RemoveHooks(json));   // no se toca lo ajeno
    }

    [Fact]
    public void Moving_from_the_GitHub_helper_to_the_alias_rewrites_our_entries_only()
    {
        var old = HookInstaller.MergeSettings("{}", HookInstaller.HelperHookSpec(true, @"C:\L\ClaudeBarWin\bin\claudebar-helper.exe", ClaudeShell.GitBash));
        var upgraded = HookInstaller.UpgradedSettings(old, HookInstaller.HelperHookSpec(true, Alias, ClaudeShell.GitBash));
        Assert.NotNull(upgraded);
        Assert.Contains("muesca-helper.exe", upgraded!);
        Assert.DoesNotContain("claudebar-helper.exe", upgraded);
        // Y al revés (vuelve a la edición de GitHub).
        var back = HookInstaller.UpgradedSettings(upgraded, HookInstaller.HelperHookSpec(true, @"C:\L\ClaudeBarWin\bin\claudebar-helper.exe", ClaudeShell.GitBash));
        Assert.NotNull(back);
        Assert.DoesNotContain("muesca-helper", back!);
    }

    [Fact]
    public void The_statusline_tap_with_the_alias_is_ours_and_restores_the_original()
    {
        var src = """{ "statusLine": { "type": "command", "command": "ccstatusline" } }""";
        foreach (var shell in new[] { ClaudeShell.GitBash, ClaudeShell.PowerShell })
        {
            var on = StatuslineInstaller.Enable(src, Alias, shell);
            Assert.Equal(TapState.Tee, StatuslineInstaller.State(on));
            Assert.Equal("ccstatusline", JsonNode.Parse(StatuslineInstaller.Disable(on))!["statusLine"]!["command"]!.GetValue<string>());

            // De la ruta del helper de GitHub a la del alias: Upgrade cambia el prefijo y conserva la del usuario.
            var github = StatuslineInstaller.Enable(src, "C:/L/ClaudeBarWin/bin/claudebar-helper.exe", shell);
            var moved = StatuslineInstaller.Upgrade(github, Alias, shell);
            Assert.NotNull(moved);
            Assert.Equal(StatuslineInstaller.TeeCommand(Alias, shell, "ccstatusline"),
                JsonNode.Parse(moved!)!["statusLine"]!["command"]!.GetValue<string>());
        }

        // Uno que solo se parece no es nuestro.
        Assert.Equal(TapState.NotInstalled, StatuslineInstaller.State(
            """{ "statusLine": { "type": "command", "command": "C:/x/not-muesca-helper.exe statusline" } }"""));
    }
}
