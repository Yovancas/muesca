using System.Buffers.Binary;
using System.Xml.Linq;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Packaging;
using ClaudeBarWin.Services.Updates;

/// <summary>
/// El paquete MSIX de la Store (installer/msix): el manifiesto, sus recursos y build-msix.ps1 repiten nombres que la app usa
/// (exes, alias del helper, tarea de inicio). Si uno cambia y el otro no, el helper o «Iniciar con Windows» dejan de funcionar
/// en silencio. Ver docs/msix.md.
/// </summary>
public class MsixPackageTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "TestData", "msix");
    private static readonly XDocument Manifest = XDocument.Load(Path.Combine(Dir, "Package.appxmanifest"));
    private static readonly string Script = File.ReadAllText(Path.Combine(Dir, "build-msix.ps1"));

    private static readonly XNamespace F = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap3 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/3";
    private static readonly XNamespace Desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

    private static XElement Root => Manifest.Root!;
    private static IEnumerable<XElement> Applications => Root.Element(F + "Applications")!.Elements(F + "Application");
    private static XElement App(string id) => Applications.Single(a => (string?)a.Attribute("Id") == id);

    [Fact]
    public void Identity_comes_from_Partner_Center_as_placeholders()
    {
        var identity = Root.Element(F + "Identity")!;
        Assert.Equal("__IDENTITY_NAME__", (string?)identity.Attribute("Name"));
        Assert.Equal("__IDENTITY_PUBLISHER__", (string?)identity.Attribute("Publisher"));
        Assert.Equal("__VERSION__", (string?)identity.Attribute("Version"));
        Assert.Equal("__ARCH__", (string?)identity.Attribute("ProcessorArchitecture"));
    }

    [Fact]
    public void It_is_Muesca_by_Bekay()
    {
        var props = Root.Element(F + "Properties")!;
        Assert.Equal("Muesca", (string?)props.Element(F + "DisplayName"));
        Assert.Equal("Bekay", (string?)props.Element(F + "PublisherDisplayName"));
        Assert.Equal("Muesca", (string?)App("Muesca").Element(Uap + "VisualElements")!.Attribute("DisplayName"));
        Assert.DoesNotContain("Claude", (string?)props.Element(F + "DisplayName"));
    }

    [Fact]
    public void Only_runFullTrust_is_declared()
    {
        var caps = Root.Element(F + "Capabilities")!.Elements().ToList();
        var cap = Assert.Single(caps);
        Assert.Equal(Rescap + "Capability", cap.Name);
        Assert.Equal("runFullTrust", (string?)cap.Attribute("Name"));
    }

    [Fact]
    public void Runs_on_the_same_Windows_as_the_app()
    {
        var family = Root.Element(F + "Dependencies")!.Element(F + "TargetDeviceFamily")!;
        Assert.Equal("Windows.Desktop", (string?)family.Attribute("Name"));
        // SupportedOSPlatformVersion del csproj (Windows 10 1809).
        Assert.Equal("10.0.17763.0", (string?)family.Attribute("MinVersion"));
    }

    [Fact]
    public void The_app_starts_with_Windows_only_if_the_user_enables_it()
    {
        var app = App("Muesca");
        Assert.Equal(AppLocation.ExeName, (string?)app.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)app.Attribute("EntryPoint"));

        var ext = app.Element(F + "Extensions")!.Elements(Desktop + "Extension").Single(e => (string?)e.Attribute("Category") == "windows.startupTask");
        Assert.Equal(AppLocation.ExeName, (string?)ext.Attribute("Executable"));
        var task = ext.Element(Desktop + "StartupTask")!;
        Assert.Equal(StoreEdition.StartupTaskId, (string?)task.Attribute("TaskId"));
        Assert.Equal("false", (string?)task.Attribute("Enabled"));   // sin consentimiento, no (política 10.2.8)
    }

    [Fact]
    public void The_helper_is_launched_by_its_alias_and_hidden_from_Start()
    {
        var helper = App("Helper");
        Assert.Equal(HelperDeployer.ExeName, (string?)helper.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)helper.Attribute("EntryPoint"));
        Assert.Equal("none", (string?)helper.Element(Uap + "VisualElements")!.Attribute("AppListEntry"));

        var ext = helper.Element(F + "Extensions")!.Element(Uap3 + "Extension")!;
        Assert.Equal("windows.appExecutionAlias", (string?)ext.Attribute("Category"));
        Assert.Equal(HelperDeployer.ExeName, (string?)ext.Attribute("Executable"));
        var alias = ext.Element(Uap3 + "AppExecutionAlias")!.Element(Desktop + "ExecutionAlias")!;
        Assert.Equal(StoreEdition.HelperAlias, (string?)alias.Attribute("Alias"));
        Assert.True(HelperCommand.IsHelperExePath(StoreEdition.HelperAlias));   // settings.json lo reconoce como nuestro
    }

    [Fact]
    public void Declares_the_eight_languages_of_the_app()
    {
        var languages = Root.Element(F + "Resources")!.Elements(F + "Resource").Select(r => (string)r.Attribute("Language")!).ToList();
        Assert.Equal(Localization.Languages.Length, languages.Count);
        // Del código de la app al de Windows: zh-Hant es zh-TW; los demás, idioma-región.
        var expected = Localization.Languages.Select(l => l.Code switch
        {
            "en" => "en-US", "es" => "es-ES", "nl" => "nl-NL", "fr" => "fr-FR", "de" => "de-DE",
            "ja" => "ja-JP", "ko" => "ko-KR", "zh-Hant" => "zh-TW", var other => other,
        });
        Assert.Equal(expected, languages);
    }

    // ── Recursos visuales (installer/msix/make-assets.py) ──────────────────────────────────────────────

    private static (int W, int H) PngSize(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Dir, "Assets", file));
        Assert.True(bytes.Length > 24 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G', file);
        return (BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)));
    }

    public static TheoryData<string, int, int> Logos => new()
    {
        { "StoreLogo", 50, 50 },
        { "Square44x44Logo", 44, 44 },
        { "Square150x150Logo", 150, 150 },
        { "Wide310x150Logo", 310, 150 },
        { "SplashScreen", 620, 300 },
    };

    [Theory]
    [MemberData(nameof(Logos))]
    public void Every_logo_has_its_scales(string name, int width, int height)
    {
        foreach (var scale in new[] { 100, 200, 400 })
            Assert.Equal((width * scale / 100, height * scale / 100), PngSize($"{name}.scale-{scale}.png"));
    }

    [Fact]
    public void The_small_icon_has_the_taskbar_and_explorer_sizes()
    {
        foreach (var px in new[] { 16, 24, 32, 48, 256 })
        {
            Assert.Equal((px, px), PngSize($"Square44x44Logo.targetsize-{px}.png"));
            Assert.Equal((px, px), PngSize($"Square44x44Logo.targetsize-{px}_altform-unplated.png"));
        }
    }

    [Fact]
    public void The_manifest_only_names_assets_that_exist()
    {
        var names = Manifest.Descendants().SelectMany(e => e.Attributes()).Select(a => a.Value)
            .Concat(Manifest.Descendants(F + "Logo").Select(e => e.Value))
            .Where(v => v.StartsWith(@"Assets\", StringComparison.Ordinal))
            .Distinct().ToList();
        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            var file = Path.GetFileNameWithoutExtension(name[@"Assets\".Length..]);
            Assert.True(File.Exists(Path.Combine(Dir, "Assets", file + ".scale-100.png")), name);
        }
    }

    // ── build-msix.ps1 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_script_builds_both_exes_like_the_GitHub_edition()
    {
        Assert.Contains($"$MainExe   = '{AppLocation.ExeName}'", Script);
        Assert.Contains($"$HelperExe = '{HelperDeployer.ExeName}'", Script);
        Assert.Contains("'--self-contained', 'true'", Script);
        Assert.Contains("'-p:PublishSingleFile=false'", Script);
        Assert.Contains("'-p:PublishAot=true'", Script);
        Assert.Contains("[string[]]$Arch = @('x64', 'arm64')", Script);
    }

    [Fact]
    public void The_script_fills_every_placeholder_and_packs_with_the_sdk_tools()
    {
        foreach (var marker in new[] { "__IDENTITY_NAME__", "__IDENTITY_PUBLISHER__", "__VERSION__", "__ARCH__" })
            Assert.Contains($"'{marker}'", Script);
        Assert.Contains("makepri.exe", Script);
        Assert.Contains("makeappx.exe", Script);
        Assert.Contains("'bundle'", Script);
        Assert.Contains(".msixupload", Script);
        // Nada de Velopack: las actualizaciones las da la Store.
        Assert.DoesNotContain("vpk", Script);
        // Ni sube nada.
        Assert.DoesNotContain("gh release", Script);
    }

    [Fact]
    public void The_script_rejects_0x_versions_and_keeps_the_fourth_part_at_zero()
    {
        Assert.Contains("La Store no admite versiones 0.x", Script);
        Assert.Contains("'{0}.{1}.{2}.0'", Script);
    }
}
