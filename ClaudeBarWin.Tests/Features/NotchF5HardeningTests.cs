using System.ComponentModel;
using System.Diagnostics;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Notch.Platform;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Robustez y seguridad de la bandeja y las tareas (F5, revisión): extensión «efectiva» de lo que se abre (puntos y
/// espacios finales, flujos ADS), carpeta que deja de serlo, unidades que no se tocan desde el hilo de UI, guardado fallido
/// de la bandeja, log sin rutas y recordatorios que no se repiten al reiniciar. Nada se abre de verdad.
/// </summary>
public sealed class NotchF5HardeningTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 28, 10, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbw-f5h-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private static ShelfItem File(string path) => new(Guid.NewGuid(), ShelfKind.File, path, "x", T0);

    private sealed class FakeShell : IShellLauncher
    {
        public readonly List<string> Calls = new();
        public bool OpenFile(string path) { Calls.Add("file:" + path); return true; }
        public bool OpenFolder(string path) { Calls.Add("folder:" + path); return true; }
        public bool Reveal(string path) { Calls.Add("reveal:" + path); return true; }
        public bool OpenUrl(string url) { Calls.Add("url:" + url); return true; }
        public bool CopyText(string text) { Calls.Add("copy:" + text); return true; }
    }

    // ---------------------------------------------------------------- a) ShelfOpen: extensión efectiva

    [Theory]
    [InlineData(@"C:\x\evil.exe.")]            // Windows quita el punto final: abre evil.exe
    [InlineData(@"C:\x\evil.exe . .")]
    [InlineData(@"C:\x\evil.exe::$DATA")]      // flujo principal = el propio evil.exe
    [InlineData("evil.exe::$DATA")]
    [InlineData(@"C:\x\informe.pdf:oculto.exe")]   // cualquier flujo alternativo: nunca se abre con el shell
    [InlineData(@"C:\x\script.py")]
    [InlineData(@"C:\x\script.PYW")]
    [InlineData(@"C:\x\a.wsc")]
    [InlineData(@"C:\x\a.sct")]
    [InlineData(@"C:\x\a.xll")]
    [InlineData(@"C:\x\a.website")]
    [InlineData(@"C:\x\a.iqy")]
    [InlineData(@"C:\x\a.slk")]
    [InlineData(@"C:\x\a.sh")]
    [InlineData("")]
    [InlineData(@"D:\sin-extension")]          // sin tipo conocido: se enseña, no se abre
    public void Trailing_dots_streams_and_new_script_types_are_executable(string path)
    {
        Assert.True(ShelfOpen.IsExecutable(path));
    }

    // Tipos que la vieja lista negra dejaba pasar y Windows ejecuta, instala o carga al abrirlos (revisión de B8). Con la
    // lista blanca, cualquier tipo fuera de documentos/imágenes/audio/vídeo va al Explorador.
    [Theory]
    [InlineData(".rdp")] [InlineData(".appinstaller")] [InlineData(".vsto")] [InlineData(".jnlp")] [InlineData(".xbap")]
    [InlineData(".msu")] [InlineData(".hlp")] [InlineData(".msh")] [InlineData(".msh1")] [InlineData(".msh2")]
    [InlineData(".mshxml")] [InlineData(".msh1xml")] [InlineData(".msh2xml")]
    [InlineData(".mdb")] [InlineData(".mde")] [InlineData(".accde")] [InlineData(".ade")] [InlineData(".adp")]
    [InlineData(".mda")] [InlineData(".maf")] [InlineData(".mam")] [InlineData(".maq")] [InlineData(".mar")]
    [InlineData(".mas")] [InlineData(".mat")] [InlineData(".mav")] [InlineData(".maw")]
    [InlineData(".theme")] [InlineData(".themepack")] [InlineData(".deskthemepack")] [InlineData(".ins")]
    [InlineData(".isp")] [InlineData(".shs")] [InlineData(".shb")] [InlineData(".xnk")] [InlineData(".wsb")]
    [InlineData(".cer")] [InlineData(".crt")] [InlineData(".docm")] [InlineData(".xlsm")] [InlineData(".doc")]
    [InlineData(".rtf")] [InlineData(".html")] [InlineData(".svg")] [InlineData(".APPINSTALLER")]
    public void Dangerous_types_missing_from_the_old_blacklist_are_revealed(string ext)
    {
        var path = @"C:\x\a" + ext;
        Assert.True(ShelfOpen.IsExecutable(path));
        Assert.True(ShelfOpen.IsExecutable(path + ". "));   // disfrazado con punto y espacio finales
        var shell = new FakeShell();
        Assert.True(ShelfOpen.Run(File(path), true, shell));
        Assert.Equal(new[] { "reveal:" + path }, shell.Calls);
    }

    [Theory]
    [InlineData(@"C:\x\informe.pdf")]
    [InlineData(@"C:\x\informe.pdf. ")]
    [InlineData(@"C:\carpeta.exe\foto.jpg")]   // el «.exe» de una carpeta del camino no cuenta
    [InlineData(@"C:\x\notas.TXT")]
    [InlineData(@"C:\x\tabla.xlsx")]
    [InlineData(@"C:\x\foto.HEIC")]
    [InlineData(@"C:\x\cancion.mp3")]
    [InlineData(@"C:\x\video.mp4")]
    [InlineData(@"C:\x\fotos.zip")]
    public void Documents_are_not_executable(string path)
    {
        Assert.False(ShelfOpen.IsExecutable(path));
    }

    [Theory]
    [InlineData(@"C:\x\evil.exe.", ".exe")]
    [InlineData(@"C:\x\evil.exe::$DATA", ".exe")]
    [InlineData("C:evil.bat", ".bat")]
    [InlineData(@"C:\x\nada", "")]
    public void Effective_extension_is_what_the_shell_sees(string path, string expected)
    {
        Assert.Equal(expected, ShelfOpen.EffectiveExtension(path));
    }

    [Theory]
    [InlineData(@"C:\x\evil.exe.")]
    [InlineData(@"C:\x\evil.exe::$DATA")]
    public void Disguised_programs_are_revealed_never_shell_executed(string path)
    {
        var shell = new FakeShell();
        Assert.Equal(ShelfOpenAction.Reveal, ShelfOpen.Decide(File(path), available: true));
        Assert.True(ShelfOpen.Run(File(path), true, shell));
        Assert.Equal(new[] { "reveal:" + path }, shell.Calls);
    }

    // ---------------------------------------------------------------- b) OpenFolder

    [Fact]
    public void OpenFolder_opens_only_a_real_folder_and_reveals_a_file_in_its_place()
    {
        var started = new List<ProcessStartInfo>();
        bool isDir = true, isFile = false;
        var shell = new ShellLauncher(psi => { started.Add(psi); return true; }, _ => isDir, _ => isFile);

        Assert.True(shell.OpenFolder(@"C:\docs\x.exe"));
        Assert.Equal("\"" + @"C:\docs\x.exe" + "\"", started[^1].Arguments);
        Assert.False(started[^1].UseShellExecute);

        // Borrada y sustituida por un archivo tras la foto: se enseña, no se abre.
        isDir = false;
        isFile = true;
        Assert.True(shell.OpenFolder(@"C:\docs\x.exe"));
        Assert.Equal(ShellLauncher.RevealArguments(@"C:\docs\x.exe"), started[^1].Arguments);
        Assert.False(started[^1].UseShellExecute);

        // Ya no hay nada: nada.
        isFile = false;
        int before = started.Count;
        Assert.False(shell.OpenFolder(@"C:\docs\x.exe"));
        Assert.Equal(before, started.Count);
    }

    [Fact]
    public void OpenFolder_treats_a_failing_check_as_missing()
    {
        int starts = 0;
        var shell = new ShellLauncher(_ => { starts++; return true; },
            _ => throw new UnauthorizedAccessException(), _ => throw new IOException());
        Assert.False(shell.OpenFolder(@"C:\docs"));
        Assert.Equal(0, starts);
    }

    // ---------------------------------------------------------------- c) unidades que no se tocan

    [Theory]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.Removable)]
    [InlineData(DriveType.CDRom)]
    [InlineData(DriveType.NoRootDirectory)]
    public void Network_and_removable_drives_are_available_without_touching_the_disk(DriveType type)
    {
        string? asked = null;
        bool ok = ShelfDefaults.Exists(@"Z:\proyecto\a.txt", root => { asked = root; return type; },
            _ => throw new InvalidOperationException("no debe tocar el disco"));
        Assert.True(ok);
        Assert.Equal(@"Z:\", asked);
    }

    [Fact]
    public void Fixed_drives_are_checked_and_unc_paths_are_not()
    {
        int checks = 0;
        Assert.False(ShelfDefaults.Exists(@"C:\a.txt", _ => DriveType.Fixed, _ => { checks++; return false; }));
        Assert.True(ShelfDefaults.Exists(@"C:\a.txt", _ => DriveType.Fixed, _ => { checks++; return true; }));
        Assert.Equal(2, checks);
        // Revisión API (punto 6): una ruta de red ya no se da por disponible, pero sigue sin tocarse.
        Assert.False(ShelfDefaults.Exists(@"\\srv\c$\a.txt", _ => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException()));
        // Si preguntar el tipo falla, se comprueba como siempre.
        Assert.True(ShelfDefaults.Exists(@"C:\a.txt", _ => throw new IOException(), _ => true));
    }

    [Fact]
    public void Snapshot_with_default_existence_does_not_touch_network_letters()
    {
        Directory.CreateDirectory(_dir);
        var store = new ShelfStore(Path.Combine(_dir, "shelf.json"), p => ShelfDefaults.Exists(p, _ => DriveType.Network,
            _ => throw new InvalidOperationException("no debe tocar el disco")), _ => false, () => T0);
        store.AddPaths(new[] { @"Z:\remoto\a.txt" });
        var snap = ShelfSnapshot.Take(store);
        Assert.True(snap.Available[0]);
    }

    // ---------------------------------------------------------------- d) guardado fallido

    [Fact]
    public void A_failed_save_rolls_the_shelf_back_so_shown_and_saved_match()
    {
        var path = Path.Combine(_dir, "shelf.json");
        var store = new ShelfStore(path, _ => true, _ => false, () => T0);
        var kept = store.AddText("se queda");
        int changed = 0;
        store.Changed += () => changed++;

        // Una carpeta con el nombre del archivo: el reemplazo atómico falla siempre.
        System.IO.File.Delete(path);
        Directory.CreateDirectory(path);

        Assert.ThrowsAny<Exception>(() => store.AddText("no cabe"));
        Assert.Equal(new[] { kept }, store.Items);
        Assert.ThrowsAny<Exception>(() => store.Clear());
        Assert.Equal(new[] { kept }, store.Items);
        Assert.ThrowsAny<Exception>(() => store.Remove(new[] { kept.Id }));
        Assert.Equal(new[] { kept }, store.Items);
        Assert.ThrowsAny<Exception>(() => store.AddPaths(new[] { @"C:\a.txt" }));
        Assert.Equal(new[] { kept }, store.Items);
        Assert.Equal(0, changed);
    }

    // ---------------------------------------------------------------- e) log sin rutas

    [Fact]
    public void Launch_failures_are_logged_without_the_path_or_url()
    {
        const string secret = @"C:\Users\ana\privado\nominas.pdf";
        var file = new ProcessStartInfo(secret) { UseShellExecute = true, Verb = "open" };
        var line = ShellLauncher.FailureLog(file, new Win32Exception(2, "No se encuentra " + secret));
        Assert.Contains("archivo", line);
        Assert.Contains("2", line);
        Assert.DoesNotContain("privado", line);
        Assert.DoesNotContain("nominas", line);

        var url = new ProcessStartInfo("https://ex.com/token=abc") { UseShellExecute = true };
        var urlLine = ShellLauncher.FailureLog(url, new InvalidOperationException("https://ex.com/token=abc"));
        Assert.Contains("enlace", urlLine);
        Assert.Contains(nameof(InvalidOperationException), urlLine);
        Assert.DoesNotContain("token", urlLine);

        var reveal = new ProcessStartInfo(ShellLauncher.ExplorerPath, ShellLauncher.RevealArguments(secret)) { UseShellExecute = false };
        Assert.Contains("revelar", ShellLauncher.FailureLog(reveal, new Win32Exception(5)));
        var folder = new ProcessStartInfo(ShellLauncher.ExplorerPath, "\"" + secret + "\"") { UseShellExecute = false };
        var folderLine = ShellLauncher.FailureLog(folder, new FileNotFoundException("x", secret));
        Assert.Contains("carpeta", folderLine);
        Assert.DoesNotContain("privado", folderLine);
    }

    // ---------------------------------------------------------------- e2) rutas de red y de dispositivo (revisión API, punto 6)

    public static TheoryData<string> RemotePaths => new()
    {
        @"\\host\share\a.pdf", "//host/share/a.pdf", @"\/host\share\a.pdf", @"\\?\C:\x\a.pdf", @"\\.\pipe\x",
        @"\\?\UNC\host\share\a.pdf", @"\??\C:\x\a.pdf", @"  \\host\share\a.pdf", "\"\\\\host\\share\\a.pdf\"",
    };

    [Theory]
    [MemberData(nameof(RemotePaths))]
    public void Unc_and_device_paths_are_detected(string path) => Assert.True(ShelfStore.IsUncOrDevicePath(path));

    [Theory]
    [InlineData(@"C:\x\a.pdf")]
    [InlineData(@"D:\")]
    [InlineData(@"\x\a.pdf")]
    [InlineData("")]
    [InlineData(null)]
    public void Local_paths_are_not_unc(string? path) => Assert.False(ShelfStore.IsUncOrDevicePath(path));

    [Theory]
    [MemberData(nameof(RemotePaths))]
    public void AddPaths_rejects_unc_and_device_paths_without_touching_the_disk(string path)
    {
        var store = new ShelfStore(Path.Combine(_dir, "shelf.json"),
            _ => throw new InvalidOperationException("no debe tocar el disco"),
            _ => throw new InvalidOperationException("no debe tocar el disco"), () => T0);
        var added = store.AddPaths(new[] { path, @"C:\docs\informe.pdf" });
        var only = Assert.Single(added);
        Assert.Equal(@"C:\docs\informe.pdf", only.Value);
        Assert.DoesNotContain(store.Items, i => i.Value.Contains("host", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ShelfKind.File, @"\\host\share\a.pdf")]
    [InlineData(ShelfKind.File, @"\\host\share\setup.exe")]
    [InlineData(ShelfKind.File, @"\\?\C:\x\a.pdf")]
    [InlineData(ShelfKind.File, @"\\.\C:\x\a.pdf")]
    [InlineData(ShelfKind.Folder, @"\\host\share")]
    [InlineData(ShelfKind.Folder, "//host/share")]
    public void Opening_a_unc_or_device_entry_from_an_old_shelf_does_nothing(ShelfKind kind, string path)
    {
        // Un shelf.json antiguo o manipulado puede traerlas: ni abrir, ni enseñar, ni comprobar.
        var item = new ShelfItem(Guid.NewGuid(), kind, path, "x", T0);
        Assert.Equal(ShelfOpenAction.None, ShelfOpen.Decide(item, available: true));
        var shell = new FakeShell();
        Assert.False(ShelfOpen.Run(item, true, shell));
        Assert.Empty(shell.Calls);
        Assert.False(ShelfDefaults.Exists(path, _ => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException()));
    }

    [Fact]
    public void ShellLauncher_refuses_unc_and_device_paths_too()
    {
        int starts = 0;
        var shell = new ShellLauncher(_ => { starts++; return true; },
            _ => throw new InvalidOperationException("no debe tocar el disco"),
            _ => throw new InvalidOperationException("no debe tocar el disco"));
        Assert.False(shell.OpenFile(@"\\host\share\a.pdf"));
        Assert.False(shell.OpenFolder(@"\\host\share"));
        Assert.False(shell.Reveal(@"\\?\C:\x\a.pdf"));
        Assert.False(shell.OpenFolder(@"\\.\pipe\x"));
        Assert.Equal(0, starts);
    }

    [Theory]
    [InlineData("https://ex.com/a b?q=ñ", "https://ex.com/a%20b?q=%C3%B1")]
    [InlineData("HTTPS://EX.com", "https://ex.com/")]
    [InlineData("  https://ex.com/x  ", "https://ex.com/x")]
    public void OpenUrl_passes_the_canonical_absolute_uri(string url, string expected)
    {
        var started = new List<ProcessStartInfo>();
        var shell = new ShellLauncher(psi => { started.Add(psi); return true; }, _ => false, _ => false);
        Assert.True(shell.OpenUrl(url));
        Assert.Equal(expected, Assert.Single(started).FileName);
        Assert.True(started[0].UseShellExecute);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData(@"\\host\share\a.pdf")]
    [InlineData("no es una url")]
    public void OpenUrl_refuses_anything_but_http(string url)
    {
        int starts = 0;
        var shell = new ShellLauncher(_ => { starts++; return true; }, _ => false, _ => false);
        Assert.False(shell.OpenUrl(url));
        Assert.Equal(0, starts);
    }

    // ---------------------------------------------------------------- f) recordatorios persistidos

    private string TasksJson => Path.Combine(_dir, "tasks.json");

    [Fact]
    public void A_fired_reminder_does_not_fire_again_after_a_restart()
    {
        var s = new TaskStore(TasksJson);
        var t = s.Add("Llamar", T0, 0);
        var clock = new TaskReminderClock();
        var due = clock.Poll(s.Items, T0.AddMinutes(1));
        Assert.Single(due);
        s.MarkNotified(due.Select(x => x.Id));

        // «Reinicio»: almacén y reloj nuevos.
        var again = new TaskStore(TasksJson);
        Assert.True(again.Items[0].Notified);
        Assert.Empty(new TaskReminderClock().Poll(again.Items, T0.AddMinutes(2)));

        // Aplazar da otra hora de aviso: vuelve a sonar.
        again.Snooze(t.Id, TimeSpan.FromMinutes(10), T0.AddMinutes(2));
        Assert.False(again.Items[0].Notified);
        Assert.Single(new TaskReminderClock().Poll(again.Items, T0.AddMinutes(13)));

        // Cambiar la hora también.
        var moved = again.Items[0] with { RemindAtLocal = T0.AddHours(1) };
        again.Update(moved);
        again.MarkNotified(new[] { t.Id });
        again.Update(again.Items[0] with { RemindAtLocal = T0.AddHours(2) });
        Assert.Single(new TaskReminderClock().Poll(again.Items, T0.AddHours(2).AddMinutes(1)));
    }

    [Fact]
    public void MarkNotified_saves_once_and_ignores_unknown_or_timeless_tasks()
    {
        var s = new TaskStore(TasksJson);
        var a = s.Add("a", T0, 0);
        var b = s.Add("b", null, 0);
        int changed = 0;
        s.Changed += () => changed++;
        s.MarkNotified(new[] { a.Id, b.Id, Guid.NewGuid() });
        Assert.Equal(1, changed);
        s.MarkNotified(new[] { a.Id, b.Id });   // ya apuntado: sin escribir
        Assert.Equal(1, changed);
        Assert.Equal(T0, s.Items[0].NotifiedForLocal);
        Assert.Null(s.Items[1].NotifiedForLocal);
    }

    [Fact]
    public void Old_tasks_files_without_the_field_still_load_and_ring()
    {
        Directory.CreateDirectory(_dir);
        var id = Guid.NewGuid();
        System.IO.File.WriteAllText(TasksJson, $$"""
            { "Version": 1, "Tasks": [ { "Id": "{{id}}", "Title": "vieja", "RemindAtLocal": "2026-10-28T10:00:00",
              "SnoozedUntilLocal": null, "Done": false, "DoneAt": null, "Color": 2 } ] }
            """);
        var s = new TaskStore(TasksJson);
        Assert.Single(s.Items);
        Assert.Null(s.Items[0].NotifiedForLocal);
        Assert.False(s.Items[0].Notified);
        Assert.Single(new TaskReminderClock().Poll(s.Items, T0.AddMinutes(1)));
    }

    [Fact]
    public void Computed_reminder_state_is_not_written_to_disk()
    {
        var s = new TaskStore(TasksJson);
        s.Add("a", T0, 0);
        var json = System.IO.File.ReadAllText(TasksJson);
        Assert.Contains("NotifiedForLocal", json);
        Assert.DoesNotContain("DueAtLocal", json);
        Assert.DoesNotContain("\"Notified\"", json);
    }

    [Theory]
    [InlineData("2026-10-28T23:59:30", 30)]
    [InlineData("2026-10-28T00:00:00", 86_400)]
    [InlineData("2026-10-28T12:00:00", 43_200)]
    public void Until_midnight_points_to_the_next_local_midnight(string now, int seconds)
    {
        var d = TaskTimes.UntilMidnight(DateTime.Parse(now, System.Globalization.CultureInfo.InvariantCulture),
            TimeZoneInfo.Utc);
        Assert.Equal(TimeSpan.FromSeconds(seconds), d);
    }

    [Theory]
    [InlineData("2026-03-29T00:30:00", 22.5)]   // se adelanta la hora (02:00 → 03:00): 1 h menos de espera real
    [InlineData("2026-10-25T00:30:00", 24.5)]   // se atrasa (03:00 → 02:00): 1 h más
    [InlineData("2026-10-28T12:00:00", 12.0)]   // día normal
    public void Until_midnight_counts_real_time_across_daylight_saving_changes(string now, double hours)
    {
        var madrid = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        var d = TaskTimes.UntilMidnight(DateTime.Parse(now, System.Globalization.CultureInfo.InvariantCulture), madrid);
        Assert.Equal(TimeSpan.FromHours(hours), d);
    }
}
