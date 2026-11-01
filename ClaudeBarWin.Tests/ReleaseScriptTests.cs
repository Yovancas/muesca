using System.Text.RegularExpressions;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Updates;

namespace ClaudeBarWin.Tests;

/// <summary>
/// scripts/release-velopack.ps1 y el puente desde Inno (installer/bridge/ClaudeBarWin-Bridge.iss) repiten nombres y la clave
/// pública de la app (ni PowerShell ni ISCC corren aquí: se leen como texto). Si cambia el C#, tienen que cambiar igual.
/// </summary>
public class ReleaseScriptTests
{
    private static string TestData(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    private static readonly string Script = TestData("release-velopack.ps1");
    private static readonly string Bridge = TestData("ClaudeBarWin-Bridge.iss");
    private static readonly string Legacy = TestData("ClaudeBarWin.iss");

    /// <summary>Valor de <c>$Nombre = '…'</c> a principio de línea en el script.</summary>
    private static string PsVar(string name)
    {
        var m = Regex.Match(Script, $@"^\${name}\s*=\s*'([^']*)'", RegexOptions.Multiline);
        Assert.True(m.Success, $"no está ${name} en release-velopack.ps1");
        return m.Groups[1].Value;
    }

    /// <summary>Valor de una constante de cadena del [Code] del puente.</summary>
    private static string PascalConst(string name)
    {
        var m = Regex.Match(Bridge, $@"^\s*{name} = '([^']*)';", RegexOptions.Multiline);
        Assert.True(m.Success, $"no está {name} en el puente");
        return m.Groups[1].Value;
    }

    /// <summary>El puente sin comentarios (líneas «;» de Inno y «//» del [Code]), para mirar solo lo que se ejecuta.</summary>
    private static readonly string BridgeCode = Regex.Replace(Regex.Replace(Bridge, @"(?m)^\s*;.*$", ""), @"//[^\n]*", "");

    /// <summary>AppId de un .iss sin la llave doble con que Inno escapa la primera.</summary>
    private static string AppId(string iss) =>
        Regex.Match(iss, @"^AppId=\{(\{[0-9A-Fa-f-]+\})", RegexOptions.Multiline).Groups[1].Value;

    [Fact]
    public void The_script_signs_only_with_the_key_the_app_verifies()
    {
        Assert.Equal(ManifestSignature.PublicKeyBase64, PsVar("PublicKey"));
        Assert.Equal(ManifestSignature.SignatureSuffix, PsVar("SignatureSuffix"));
        // releases.<canal>.json, el mismo que lee SignedGithubSource.
        Assert.Contains("$ManifestName = \"releases.$Channel.json\"", Script);
        Assert.Equal(SignedGithubSource.ManifestName(PsVar("Channel")), $"releases.{PsVar("Channel")}.json");
    }

    [Fact]
    public void Pack_names_match_what_the_app_looks_for()
    {
        Assert.Equal(AppLocation.VelopackPackId, PsVar("PackId"));
        Assert.Equal(AppLocation.VelopackStubName, PsVar("PackTitle") + ".exe");
        Assert.Equal(AppLocation.ExeName, PsVar("MainExe"));
        // El repositorio por defecto es el de la app (con -GhRepo se puede subir a uno de pruebas).
        var repo = Regex.Match(Script, @"\[string\]\$GhRepo = '([^']+)'").Groups[1].Value;
        Assert.Equal(SignedGithubSource.RepoUrl, "https://github.com/" + repo);
        Assert.Contains("$RepoUrl  = \"https://github.com/$GhRepo\"", Script);
    }

    [Fact]
    public void Nothing_leaves_the_machine_without_Upload()
    {
        int gate = Script.IndexOf("if (-not $Upload) {", StringComparison.Ordinal);
        Assert.True(gate > 0);
        Assert.Contains("return", Script.Substring(gate, 200));
        foreach (var call in new[] { "& $vpk upload github", "gh release upload", "gh release download", "gh api -X PATCH" })
        {
            int at = Script.IndexOf(call, StringComparison.Ordinal);
            Assert.True(at > gate, $"«{call}» va antes de la comprobación de -Upload");
        }
        // vpk deja la release en borrador (sin --publish): se publica solo después de subir y comprobar la firma.
        var vpkUpload = Regex.Match(Script, @"& \$vpk upload github[^\n]*\n[^\n]*").Value;
        Assert.DoesNotContain("--publish", vpkUpload);
        // Y antes de publicar: el manifiesto subido es exactamente esta versión, solo hay un borrador con el tag y en él solo lo
        // nuestro, y la firma verifica desde GitHub.
        int publish = Script.IndexOf("gh api -X PATCH", StringComparison.Ordinal);
        foreach (var check in new[] { "Assert-ManifestMatchesBuild $uploaded", "Assert-ReleaseAssets $releaseId",
                     "Test-ManifestSignature (Join-Path $check", "(Get-DraftReleaseId) -ne $releaseId" })
        {
            int at = Script.IndexOf(check, StringComparison.Ordinal);
            Assert.True(at > gate && at < publish, $"«{check}» tiene que ir entre la subida y la publicación");
        }
        Assert.True(Script.IndexOf("Assert-ManifestMatchesBuild $uploaded", StringComparison.Ordinal)
                    < Script.IndexOf("Protect-Manifest $uploaded", StringComparison.Ordinal));
    }

    [Fact]
    public void No_variable_collides_with_another_one_in_another_case()
    {
        // PowerShell no distingue mayúsculas: $Repo y $repo son la misma variable (y un parámetro se pisaría sin avisar).
        var code = Regex.Replace(Script, @"(?m)#.*$", "");
        var names = Regex.Matches(code, @"\$(?!env:)([A-Za-z_]\w*)").Select(m => m.Groups[1].Value).Distinct().ToList();
        var clashes = names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => string.Join("/", g));
        Assert.Empty(clashes);
    }

    [Fact]
    public void The_script_signs_the_same_message_the_app_verifies()
    {
        Assert.Equal(ManifestSignature.SigningContext, PsVar("SigningContext"));
        Assert.Contains("\"$SigningContext`n$($GhRepo.ToLowerInvariant())`n\"", Script);
    }

    [Fact]
    public void The_delta_base_is_checked_against_its_signed_manifest_before_packing()
    {
        int download = Script.IndexOf("& $vpk download github", StringComparison.Ordinal);
        int check = Script.IndexOf("Test-SignedBase $base.FullName", StringComparison.Ordinal);
        int pack = Script.IndexOf("& $vpk @packArgs", StringComparison.Ordinal);
        Assert.True(download > 0 && download < check && check < pack);
    }

    [Fact]
    public void The_target_repo_must_exist_with_that_exact_name_before_building_or_signing()
    {
        // La firma lleva el repositorio (y la app solo acepta la de su RepoUrl): sin el renombrado hecho no se firma nada.
        var fn = Regex.Match(Script, @"function Assert-TargetRepo\(\[bool\]\$Mandatory\) \{.*?\n\}", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(fn), "falta Assert-TargetRepo");
        Assert.Contains("gh repo view $GhRepo --json nameWithOwner,isArchived", fn);
        // gh sigue los renombrados: el nombre que devuelve tiene que ser el pedido, si no es un nombre viejo.
        Assert.Contains("$found.nameWithOwner -ne $GhRepo", fn);
        Assert.Contains("Could not resolve to a Repository", fn);
        Assert.Matches(@"if \(\$Mandatory\) \{ throw", fn);

        int call = Script.IndexOf("Assert-TargetRepo $Upload.IsPresent", StringComparison.Ordinal);
        Assert.True(call > 0, "no se llama a Assert-TargetRepo");
        Assert.Contains("if ($Upload -or -not [string]::IsNullOrWhiteSpace($env:SPARKLE_PRIVATE_KEY)) { Assert-TargetRepo", Script);
        foreach (var later in new[] { "& $dotnet publish $csproj", "$key = Get-SigningKey", "Protect-Manifest $manifest", "Protect-Manifest $uploaded" })
        {
            int at = Script.IndexOf(later, StringComparison.Ordinal);
            Assert.True(at > call, $"«{later}» va antes de comprobar el repositorio de destino");
        }
    }

    [Fact]
    public void The_private_key_only_comes_from_the_environment()
    {
        Assert.Contains("$env:SPARKLE_PRIVATE_KEY", Script);
        Assert.DoesNotContain(".sparkle-keys", Script);
        Assert.DoesNotContain("--key-path", Script);
        Assert.DoesNotContain("--private-key-override", Script);
    }

    [Fact]
    public void The_package_shows_Muesca_by_Bekay_with_its_icon()
    {
        // Lo que se ve en el menú Inicio, «Aplicaciones» y Setup.exe (docs/branding.md); el packId sigue siendo interno.
        Assert.Equal(Brand.Name, PsVar("PackTitle"));
        Assert.Equal(Brand.Publisher, PsVar("PackAuthors"));
        Assert.Contains("'--packAuthors', $PackAuthors", Script);
        Assert.Contains(@"Join-Path $repo 'branding\muesca.ico'", Script);
        Assert.Equal("ClaudeBar", PsVar("PackId"));
        // El puente: nombre y editor de Muesca; el lanzador que abre, el de Muesca.
        Assert.Contains("AppName=Muesca", Bridge);
        Assert.Contains("AppPublisher=Bekay", Bridge);
        Assert.Contains("AppPublisherURL=" + Brand.RepoUrl, Bridge);
        Assert.DoesNotContain("AppName=ClaudeBar", Bridge);
    }

    [Fact]
    public void The_splash_and_icon_are_optional()
    {
        Assert.Contains(@"installer\assets\splash.gif", Script);
        Assert.Matches(@"if \(Test-Path \$splash\) \{ \$packArgs \+= @\('--splashImage', \$splash\) \}", Script);
        Assert.Matches(@"if \(Test-Path \$icon\) \{ \$packArgs \+= @\('--icon', \$icon\) \}", Script);
    }

    [Fact]
    public void Uploading_requires_the_bridge_or_an_explicit_NoBridge_that_is_not_marked_latest()
    {
        // Sin puente, una release marcada como la última deja sin actualizaciones a las instalaciones de Inno
        // (releases/latest/download/appcast.xml da 404).
        Assert.Contains("[switch]$NoBridge", Script);
        Assert.Contains("if ($Upload -and -not $Bridge -and -not $NoBridge) {", Script);
        Assert.Contains("if ($Bridge -and $NoBridge) { throw", Script);
        // Con el puente en su propio repositorio (-BridgeRepo, el viejo), la release de Muesca no la lee NetSparkle y se
        // marca siempre como la última; el puente se publica como la última de su repositorio.
        Assert.Contains("$makeLatest = if ($Bridge -or $SeparateBridge) { 'true' } else { 'false' }", Script);
        Assert.Contains("--repo $BridgeRepo --title \"Muesca $Version (puente)\" --notes $bridgeNotes --latest", Script);
        Assert.Contains("--base-url \"https://github.com/$BridgeRepo/releases/download/$tag/\"", Script);
        Assert.Contains("-f make_latest=$makeLatest", Script);
        Assert.DoesNotContain("make_latest=true", Script);
        // La comprobación va antes de compilar o subir nada.
        Assert.True(Script.IndexOf("-not $NoBridge", StringComparison.Ordinal) < Script.IndexOf("# ---------------- 0) Comprobaciones", StringComparison.Ordinal));
    }

    [Fact]
    public void The_bridge_retires_exactly_the_Inno_install_without_running_its_uninstaller()
    {
        Assert.Equal("{A7C3E1F2-5B8D-4E9A-9C1F-3D2E4F6A8B0C}", AppId(Legacy));
        Assert.Equal(AppId(Legacy), PascalConst("LegacyAppId"));
        Assert.NotEqual(AppId(Legacy), AppId(Bridge));
        Assert.Contains("Uninstallable=no", Bridge);
        Assert.Contains("CreateAppDir=no", Bridge);
        // Su [UninstallRun] quitaría los hooks de Claude Code: ni se ejecuta unins000.exe ni se llama a --uninstall-hooks.
        Assert.DoesNotContain("[UninstallRun]", BridgeCode);
        Assert.DoesNotContain("--uninstall-hooks", BridgeCode);
        Assert.DoesNotMatch(@"Exec\([^;]*unins", BridgeCode);
        // Y no borra carpetas enteras: solo archivos conocidos y la carpeta si queda vacía.
        Assert.DoesNotContain("DelTree", BridgeCode);
        Assert.Contains("RemoveDir(Dir)", BridgeCode);
    }

    [Fact]
    public void The_bridge_installs_Velopack_silently_and_opens_the_stable_launcher()
    {
        Assert.Contains("'--silent --log \"'", Bridge);
        Assert.Equal(AppLocation.VelopackStubName, PascalConst("VelopackStub"));
        Assert.Equal(AppLocation.VelopackPackId, PascalConst("VelopackPackId"));
        Assert.Equal(AppLocation.ExeName, PascalConst("MainExe"));
        Assert.Equal(AppLocation.AutostartArg, PascalConst("AutostartArg"));
        Assert.Equal(VelopackStartup.SingleInstanceMutex, PascalConst("AppMutexName"));
        // El nombre que espera el script.
        Assert.Contains("OutputBaseFilename=ClaudeBarWin-Bridge-{#MyVersion}", Bridge);
        Assert.Contains("ClaudeBarWin-Bridge-$Version.exe", Script);
        Assert.Contains("/DSetupExe=$setupExe", Script);
    }

    [Fact]
    public void Every_bridge_message_exists_in_English_and_Spanish()
    {
        var used = Regex.Matches(Bridge, @"CustomMessage\('(\w+)'\)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(used);
        foreach (var name in used)
        {
            Assert.Matches($@"(?m)^en\.{name}=", Bridge);
            Assert.Matches($@"(?m)^es\.{name}=", Bridge);
        }
    }
}
