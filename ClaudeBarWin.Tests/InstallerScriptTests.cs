using System.Text.RegularExpressions;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// El asistente de installer/ClaudeBarWin.iss repite en Pascal los perfiles, los valores por defecto y las reglas de las
/// recomendaciones (ISCC no corre aquí: estos tests leen el script como texto). Si cambia el C#, el .iss tiene que
/// cambiar igual; y cada texto del asistente tiene que existir en inglés y en español (Inno falla al pedir uno que no
/// está).
/// </summary>
public class InstallerScriptTests
{
    private static readonly string Script =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ClaudeBarWin.iss"));

    /// <summary>Cuerpo de una función o procedimiento del [Code] (desde su cabecera hasta el primer «end;» a columna 0).</summary>
    private static string Body(string name)
    {
        var m = Regex.Match(Script, $@"^(?:function|procedure) {name}\b.*?^end;", RegexOptions.Multiline | RegexOptions.Singleline);
        Assert.True(m.Success, $"no está {name} en el .iss");
        return m.Value;
    }

    /// <summary>«K=1;K=0» → diccionario.</summary>
    private static Dictionary<string, bool> Table(string table) =>
        table.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(kv => kv.Split('='))
            .ToDictionary(kv => kv[0], kv => kv[1] == "1");

    private static readonly (string Pascal, SetupProfile Profile)[] Profiles =
    {
        ("PQuota", SetupProfile.QuotaOnly), ("PCoder", SetupProfile.Coder), ("PFull", SetupProfile.FullNotch),
        ("PMinimal", SetupProfile.Minimal),
    };

    /// <summary>Lo que devuelve cada rama de un «case P of» de cadenas.</summary>
    private static Dictionary<string, string> CaseStrings(string body) =>
        Regex.Matches(body, @"(\w+): Result := '([^']*)';").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

    private static string[] FeatureKeys() =>
        Regex.Matches(Body("FeatureKey"), @"Result := '(\w+)';").Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void Profile_ids_match_the_app()
    {
        var ids = CaseStrings(Body("ProfileId"));
        foreach (var (pascal, profile) in Profiles)
            Assert.Equal(SetupProfiles.Id(profile), ids[pascal]);
        Assert.Contains($"Result := '{SetupProfiles.Id(SetupProfile.Custom)}';", Body("ProfileId"));
    }

    [Fact]
    public void Profile_tables_are_a_literal_copy_of_SetupProfiles_changes()
    {
        var checklist = FeatureKeys().ToHashSet();
        var tables = CaseStrings(Body("ProfileTable"));
        foreach (var (pascal, profile) in Profiles)
        {
            var expected = SetupProfiles.Changes(profile)
                .Where(kv => checklist.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => (bool)kv.Value);
            Assert.Equal(expected.OrderBy(kv => kv.Key), Table(tables[pascal]).OrderBy(kv => kv.Key));
        }
        // Personalizado no fija nada.
        Assert.DoesNotContain("PCustom", tables.Keys);
    }

    [Fact]
    public void Checklist_keys_are_boolean_settings_of_the_whitelist()
    {
        var keys = FeatureKeys();
        Assert.Equal(11, keys.Length);
        Assert.Equal(keys.Length, keys.Distinct().Count());
        foreach (var key in keys)
            Assert.Equal(typeof(bool), SetupSettings.TypeOf(key));
    }

    [Fact]
    public void Checklist_defaults_match_AppConfig()
    {
        var table = Table(Regex.Match(Body("FeatureDefaultsTable"), @"Result := '([^']*)';").Groups[1].Value);
        var cfg = new AppConfig();
        Assert.Equal(FeatureKeys().Order(), table.Keys.Order());
        foreach (var (key, value) in table)
            Assert.Equal(SetupSettings.Get(cfg, key), value);
    }

    [Fact]
    public void Answer_keys_written_to_the_json_are_known_settings()
    {
        var keys = Regex.Matches(Body("BuildSetupJson"), @"AddOverride\('(\w+)'").Select(m => m.Groups[1].Value).ToArray();
        Assert.NotEmpty(keys);
        foreach (var key in keys) Assert.True(SetupSettings.IsKnown(key), key);
        Assert.Contains($"\"version\": {SetupRequest.CurrentVersion},", Script);
    }

    [Fact]
    public void Languages_and_sizes_match_the_app()
    {
        var langs = Regex.Matches(Body("LangCode"), @"Result := '([\w-]+)';").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(new[] { "system" }.Concat(Localization.Languages.Select(l => l.Code)).Order(), langs.Order());
        Assert.Contains($"LangCount = {langs.Length};", Script);

        var sizes = Regex.Matches(Body("SizeValue"), @"Result := (\d+);").Select(m => int.Parse(m.Groups[1].Value)).ToArray();
        Assert.Equal(AppConfig.NotchSizeOptions.Order(), sizes.Order());
        Assert.Contains($"SizeCount = {sizes.Length};", Script);
    }

    [Fact]
    public void Recommendation_constants_match_SetupRecommendations()
    {
        Assert.Contains($"SmallScreenLogicalWidth = {SetupRecommendations.SmallScreenLogicalWidth};", Script);
        // Umbrales de «Auto»: los del .iss tienen que ser los saltos de NotchLayout.AutoScale.
        var steps = Regex.Matches(Body("AutoSizePercent"), @"W >= (\d+) then Result := (\d+)")
            .Select(m => (Width: int.Parse(m.Groups[1].Value), Percent: int.Parse(m.Groups[2].Value))).ToArray();
        Assert.Equal(2, steps.Length);
        foreach (var (width, percent) in steps)
        {
            Assert.Equal(percent, (int)Math.Round(NotchLayout.AutoScale(width) * 100));
            Assert.NotEqual(percent, (int)Math.Round(NotchLayout.AutoScale(width - 1) * 100));
        }
    }

    [Fact]
    public void The_temp_json_name_is_one_the_app_deletes()
    {
        var name = Regex.Match(Script, @"SetupJsonName = '([^']+)';").Groups[1].Value;
        Assert.True(SetupApply.IsSetupFileName(name), name);
        Assert.Contains("--apply-setup", Script);
    }

    // ---- textos: inglés y español, sin huecos ----

    private static Dictionary<string, HashSet<string>> CustomMessages()
    {
        var section = Regex.Match(Script, @"^\[CustomMessages\](.*?)^\[", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
        var byLang = new Dictionary<string, HashSet<string>> { ["en"] = new(), ["es"] = new() };
        foreach (Match m in Regex.Matches(section, @"^(en|es)\.(\w+)=(.*)$", RegexOptions.Multiline))
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Groups[3].Value), $"{m.Groups[1].Value}.{m.Groups[2].Value} vacío");
            Assert.True(byLang[m.Groups[1].Value].Add(m.Groups[2].Value), $"{m.Groups[1].Value}.{m.Groups[2].Value} repetido");
        }
        return byLang;
    }

    [Fact]
    public void Every_custom_message_exists_in_english_and_spanish()
    {
        var msgs = CustomMessages();
        Assert.NotEmpty(msgs["en"]);
        Assert.Equal(msgs["en"].Order(), msgs["es"].Order());
    }

    [Fact]
    public void Every_message_the_code_asks_for_is_defined()
    {
        var defined = CustomMessages()["en"];
        var code = Script[Script.IndexOf("[Code]", StringComparison.Ordinal)..];
        var used = Regex.Matches(code, @"CustomMessage\('(\w+)'\)").Select(m => m.Groups[1].Value).ToHashSet();
        // Nombres compuestos en tiempo de ejecución: 'Profile' + ProfileKey(P) [+ 'Desc'] y los de las casillas.
        foreach (var key in CaseStrings(Body("ProfileKey")).Values.Append("Custom"))
        {
            used.Add("Profile" + key);
            used.Add("Profile" + key + "Desc");
        }
        foreach (Match m in Regex.Matches(Body("FeatureMessage"), @"Result := '(\w+)';")) used.Add(m.Groups[1].Value);
        Assert.True(used.Count > 40);
        foreach (var name in used) Assert.Contains(name, defined);
        // Y los que traen las traducciones de Inno, que se usan con {cm:…}.
        Assert.Contains("{cm:CreateDesktopIcon}", Script);
        Assert.Contains("{cm:LaunchProgram,Muesca}", Script);
    }

    [Fact]
    public void Silent_installs_never_apply_the_setup()
    {
        // NetSparkle y winget instalan con /VERYSILENT: ni preguntas ni --apply-setup (comportamiento de siempre).
        Assert.Matches(@"\(CurStep = ssPostInstall\) and ShouldApplySetup\(\) then\s+ApplySetup\(\);", Body("CurStepChanged"));
        Assert.Matches(@"Result := not WizardSilent and not KeepConfig\(\);", Body("ShouldApplySetup"));
        // Y el [UninstallRun] de los hooks sigue ahí.
        Assert.Contains("Parameters: \"--uninstall-hooks\"; Flags: runhidden waituntilterminated; RunOnceId: \"RemoveClaudeBarHooks\"", Script);
    }

    [Fact]
    public void The_apply_setup_exit_code_is_checked_and_reported_only_when_interactive()
    {
        // Antes iba en [Run] sin mirar el código: un perfil que no se aplicaba acababa en «instalado» sin avisar.
        var run = Regex.Match(Script, @"^\[Run\](.*?)^\[", RegexOptions.Multiline | RegexOptions.Singleline).Groups[1].Value;
        Assert.DoesNotMatch(@"(?m)^Filename:.*--apply-setup", run);
        var apply = Body("ApplySetup");
        Assert.Contains("Exec(ExpandConstant('{app}\\ClaudeBarWin.exe'), '--apply-setup \"' + SetupJsonPath('') + '\"'", apply);
        Assert.Contains("ewWaitUntilTerminated, ResultCode)", apply);
        Assert.Contains("if WizardSilent or (Launched and (ResultCode = 0)) then Exit;", apply);
        Assert.Contains("MsgBox(FmtMessage(CustomMessage('SetupNotLaunched')", apply);   // un error de Windows, aparte
        Assert.Contains("MsgBox(CustomMessage('SetupHooksFailed')", apply);
        Assert.Contains("MsgBox(FmtMessage(CustomMessage('SetupFailed')", apply);
        Assert.Contains("MsgBox(FmtMessage(CustomMessage('SetupCrashed')", apply);       // la app se cayó a medias
        // Los códigos que distingue son los de SetupOutcome.ExitCode: 1 = sin hooks; 2 a 4 = no se tocó nada.
        Assert.Contains("else if ResultCode = 1 then", apply);
        Assert.Contains("else if (ResultCode >= 2) and (ResultCode <= 4) then", apply);
        Assert.Equal(1, new SetupOutcome(SetupStatus.Applied, "", [], [], HooksError: "x").ExitCode);
        Assert.Equal(0, new SetupOutcome(SetupStatus.Applied, "", [], []).ExitCode);
        Assert.All(new[] { SetupStatus.NotFound, SetupStatus.Invalid, SetupStatus.Failed },
            st => Assert.InRange(SetupOutcome.Fail(st, "").ExitCode, 2, 4));
    }

    [Fact]
    public void Live_sessions_come_ticked_only_with_the_coder_profile()
    {
        // Instalan hooks en el ~/.claude/settings.json global: marcadas solo con «Programo con Claude Code».
        var prefill = Body("PrefillLiveSessions");
        Assert.Contains("if HooksDetected then Exit;", prefill);
        Assert.Contains("LiveCheck.Checked := (P = PCoder);", prefill);
        Assert.Contains("WithRecommended(CustomMessage('LiveSessions'), P = PCoder)", prefill);
        Assert.Contains("LiveCheck.Checked := HooksDetected or (SelectedProfile() = PCoder);", Body("CreateClaudePage"));
        Assert.Matches(@"PrefillLiveSessions\(SelectedProfile\(\)\);", Body("NextButtonClick"));
        // Y la línea de al lado dice dónde van y cómo se quitan.
        var hint = Regex.Match(Script, @"^es\.LiveSessionsHint=(.*)$", RegexOptions.Multiline).Groups[1].Value;
        Assert.Contains("~/.claude/settings.json", hint);
        Assert.Contains("bandeja", hint);
    }

    [Fact]
    public void Notch_permissions_are_worked_out_from_the_choices_not_from_the_page_state()
    {
        // PermCheck.Enabled solo se pone al día al entrar en la página de Claude Code.
        Assert.Contains("Result := LiveCheck.Checked and FeatureOn(FNotch);", Body("PermissionsAvailable"));
        var value = Body("PermissionsValue");
        Assert.Contains("PermissionsAvailable() and PermCheck.Checked", value);
        Assert.DoesNotContain("Enabled", value);
    }
}
