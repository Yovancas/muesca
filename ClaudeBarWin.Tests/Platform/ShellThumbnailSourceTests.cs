using System.Drawing;
using System.Drawing.Imaging;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.Notch.Platform;

namespace ClaudeBarWin.Tests.Platform;

/// <summary>
/// Lado Windows de las miniaturas de la bandeja (F5 · B4): <see cref="ShellThumbnailSource"/> mira archivos y carpetas sin
/// tocar unidades de red ni rutas UNC y nunca lanza; <see cref="StaWorker"/> ejecuta en orden, en un hilo STA de fondo que
/// termina solo sin trabajo. La extracción real del shell no se prueba aquí (depende de los extractores instalados): se
/// verifica a mano en Windows. El vigilante del hilo: un trabajo colgado se abandona y la cola sigue en otro hilo STA, con
/// tope de hilos abandonados.
/// </summary>
public sealed class ShellThumbnailSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbw-thumbs-" + Guid.NewGuid().ToString("N"));

    public ShellThumbnailSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    // ---------------------------------------------------------------- Probe

    [Fact]
    public void Probe_reports_files_folders_and_missing_paths()
    {
        var src = new ShellThumbnailSource();
        var file = Path.Combine(_dir, "nota.txt");
        File.WriteAllText(file, "hola");
        var when = new DateTime(2026, 10, 29, 8, 30, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, when);

        var p = src.Probe(file);
        Assert.Equal(ShelfProbeState.File, p.State);
        Assert.Equal(4, p.Length);
        Assert.Equal(when, p.ModifiedUtc);

        Assert.Equal(ShelfProbeState.Folder, src.Probe(_dir).State);
        Assert.Equal(ShelfProbeState.Missing, src.Probe(Path.Combine(_dir, "no-existe.pdf")).State);
        Assert.Equal(ShelfProbeState.Missing, src.Probe("").State);
    }

    [Theory]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.NoRootDirectory)]
    public void Network_and_unmounted_drives_are_never_touched(DriveType type)
    {
        string? asked = null;
        var p = ShellThumbnailSource.Probe(@"Z:\proyecto\foto.jpg", root => { asked = root; return type; },
            _ => throw new InvalidOperationException("no debía tocar el disco"));
        Assert.Equal(@"Z:\", asked);
        Assert.Equal(ShelfProbeState.Opaque, p.State);
    }

    [Fact]
    public void Local_drives_are_checked_and_unc_paths_never()
    {
        int checks = 0;
        var found = new ShelfFileProbe(ShelfProbeState.File, 10, DateTime.UtcNow);
        Assert.Equal(found, ShellThumbnailSource.Probe(@"C:\a.jpg", _ => DriveType.Fixed, _ => { checks++; return found; }));
        Assert.Equal(found, ShellThumbnailSource.Probe(@"E:\a.jpg", _ => DriveType.Removable, _ => { checks++; return found; }));
        Assert.Equal(2, checks);

        // Un fallo al preguntar el tipo de unidad no impide mirar el archivo.
        Assert.Equal(found, ShellThumbnailSource.Probe(@"C:\a.jpg", _ => throw new IOException(), _ => found));

        // Red o dispositivo: ni el tipo de unidad ni el disco.
        Assert.Equal(ShelfProbeState.Missing, ShellThumbnailSource.Probe(@"\\srv\c$\a.jpg",
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException()).State);
        Assert.Equal(ShelfProbeState.Missing, ShellThumbnailSource.Probe(@"\\?\C:\a.jpg",
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException()).State);
    }

    // ---------------------------------------------------------------- enlaces (simbólicos y de unión)

    [Fact]
    public void A_path_through_a_link_to_another_machine_is_never_touched()
    {
        var asked = new List<string>();
        string? Links(string p)
        {
            asked.Add(p);
            return p == @"C:\enlace" ? @"\\host\share" : null;
        }
        var probe = ShellThumbnailSource.Probe(@"C:\enlace\fotos\playa.jpg", _ => DriveType.Fixed,
            _ => throw new InvalidOperationException("no debía tocar el disco"), Links);
        Assert.Equal(ShelfProbeState.Opaque, probe.State);
        // De la raíz hacia abajo y parando en el enlace remoto: lo de dentro ni se consulta (seguiría el enlace).
        Assert.Equal(new[] { @"C:\", @"C:\enlace" }, asked);
    }

    [Fact]
    public void A_local_link_is_followed_as_usual()
    {
        var found = new ShelfFileProbe(ShelfProbeState.File, 10, DateTime.UtcNow);
        var p = ShellThumbnailSource.Probe(@"C:\enlace\playa.jpg", _ => DriveType.Fixed, _ => found,
            q => q == @"C:\enlace" ? @"D:\datos" : null);
        Assert.Equal(found, p);
        // Un enlace que no se puede leer no cuenta.
        Assert.Equal(found, ShellThumbnailSource.Probe(@"C:\a.jpg", _ => DriveType.Fixed, _ => found,
            _ => throw new UnauthorizedAccessException()));
    }

    [Theory]
    [InlineData(@"\\host\share\x", true)]
    [InlineData(@"//host/share/x", true)]
    [InlineData(@"\\?\UNC\host\share\x", true)]
    [InlineData(@"\??\UNC\host\share\x", true)]
    [InlineData(@"\\?\Volume{0c2e2a1f-0000-0000-0000-100000000000}\x", true)]   // no se sabe: se trata como ajeno
    [InlineData(@"file://host/share/x", true)]
    [InlineData(@"Z:\proyecto", true)]                         // letra de red
    [InlineData(@"D:\datos", false)]
    [InlineData(@"\\?\D:\datos", false)]                      // forma de dispositivo de una letra local
    [InlineData(@"\??\D:\datos", false)]
    [InlineData(@"..\hermana", false)]                          // relativo: misma máquina
    public void Link_targets_on_another_machine_are_detected(string target, bool remote) =>
        Assert.Equal(remote, ShellThumbnailSource.RemoteTarget(target, root => root == @"Z:\" ? DriveType.Network : DriveType.Fixed));

    // ---------------------------------------------------------------- Extract

    [Fact]
    public void Extract_never_throws_and_refuses_what_it_should_not_touch()
    {
        var src = new ShellThumbnailSource();
        Assert.Null(src.Extract(Path.Combine(_dir, "no-existe.jpg"), 48));
        Assert.Null(src.Extract(@"\\servidor-que-no-existe-cbw\c$\foto.jpg", 48));
        Assert.Null(src.Extract(@"\\?\C:\foto.jpg", 48));
        Assert.Null(src.Extract("", 48));
        Assert.Null(src.Extract(_dir, 0));
        Assert.Null(src.Extract(_dir, 100_000));
    }

    [Fact]
    public void A_folder_whose_desktop_ini_points_to_another_machine_is_not_asked()
    {
        // Si se le pidiera al shell, iría a buscar el icono a ese host (NTLM); con la comprobación, ni lo intenta.
        var folder = Path.Combine(_dir, "con-icono-remoto");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "desktop.ini"),
            "[.ShellClassInfo]\r\nIconResource=\\\\cbw-no-existe.invalid\\share\\a.ico,0\r\n");
        Assert.Null(new ShellThumbnailSource().Extract(folder, 48));
    }

    [Fact]
    public void A_shortcut_file_is_never_asked_even_where_a_folder_used_to_be()
    {
        // La comprobación se repite en Extract con lo que hay en el disco: un .url con su icono en otra máquina, nunca.
        var url = Path.Combine(_dir, "era-carpeta.url");
        File.WriteAllText(url, "[InternetShortcut]\r\nURL=https://example.com/\r\n"
            + "IconFile=\\\\cbw-no-existe.invalid\\share\\a.ico\r\nIconIndex=0\r\n");
        Assert.Null(new ShellThumbnailSource().Extract(url, 48));
    }

    [Fact]
    public void Copying_the_pixels_keeps_them_and_frees_the_bitmap_if_it_fails()
    {
        // 2 × 1, BGRA: un píxel rojo opaco y uno verde opaco.
        var px = new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 };
        using (var ok = ShellThumbnailSource.FromBgra(px, 2, 1, PixelFormat.Format32bppArgb))
        {
            Assert.Equal(Color.FromArgb(255, 255, 0, 0), ok.GetPixel(0, 0));
            Assert.Equal(Color.FromArgb(255, 0, 255, 0), ok.GetPixel(1, 0));
        }

        // Búfer corto: Marshal.Copy lanza; la excepción sigue y el bitmap ya está liberado.
        Bitmap? made = null;
        Assert.ThrowsAny<ArgumentException>(() =>
            ShellThumbnailSource.FromBgra(new byte[4], 2, 2, PixelFormat.Format32bppPArgb, b => made = b));
        Assert.NotNull(made);
        Assert.Throws<ArgumentException>(() => made!.Width);
    }

    [Fact]
    public void Premultiplied_alpha_is_detected_from_the_pixels()
    {
        // BGRA. Premultiplicado: ningún canal pasa de su alfa. Sin premultiplicar: un borde blanco al 50 % (255 > 128).
        Assert.True(ShellThumbnailSource.Premultiplied(new byte[] { 0, 0, 0, 0, 64, 64, 64, 128, 10, 20, 255, 255 }));
        Assert.False(ShellThumbnailSource.Premultiplied(new byte[] { 0, 0, 0, 0, 255, 255, 255, 128 }));
        Assert.True(ShellThumbnailSource.Premultiplied(Array.Empty<byte>()));
    }

    // ---------------------------------------------------------------- StaWorker

    [Fact]
    public void Worker_runs_jobs_in_order_on_one_background_sta_thread()
    {
        using var worker = new StaWorker("test: miniaturas");
        var order = new List<int>();
        var apartments = new List<ApartmentState>();
        var threads = new HashSet<int>();
        using var done = new ManualResetEventSlim();
        for (int i = 0; i < 3; i++)
        {
            int n = i;
            worker.Post(() =>
            {
                order.Add(n);
                apartments.Add(Thread.CurrentThread.GetApartmentState());
                threads.Add(Environment.CurrentManagedThreadId);
                if (n == 2) done.Set();
            });
        }
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.Equal(new[] { 0, 1, 2 }, order);
        Assert.All(apartments, a => Assert.Equal(ApartmentState.STA, a));
        Assert.Single(threads);
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, threads);
    }

    [Fact]
    public void Worker_survives_a_failing_job_and_exits_when_idle()
    {
        using var worker = new StaWorker("test: miniaturas", TimeSpan.FromMilliseconds(50), StaWorker.HangAfter);
        using var done = new ManualResetEventSlim();
        worker.Post(() => throw new InvalidOperationException("falla"));
        worker.Post(done.Set);
        Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "la cola sigue tras un trabajo que lanza");

        Assert.True(SpinWait.SpinUntil(() => !worker.Running, TimeSpan.FromSeconds(10)), "sin trabajo, el hilo termina");
        using var again = new ManualResetEventSlim();
        worker.Post(again.Set);                                  // y vuelve a nacer con el siguiente
        Assert.True(again.Wait(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Worker_drops_pending_jobs_on_dispose()
    {
        var worker = new StaWorker("test: miniaturas");
        using var gate = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        int ran = 0;
        worker.Post(() => { started.Set(); gate.Wait(TimeSpan.FromSeconds(10)); });
        worker.Post(() => Interlocked.Increment(ref ran));
        Assert.True(started.Wait(TimeSpan.FromSeconds(10)));
        worker.Dispose();                                        // el segundo aún no empezó: no se ejecuta
        gate.Set();
        Assert.True(SpinWait.SpinUntil(() => !worker.Running, TimeSpan.FromSeconds(10)));
        worker.Post(() => Interlocked.Increment(ref ran));       // cerrado: no hace nada
        Assert.Equal(0, Volatile.Read(ref ran));
        Assert.False(worker.Running);
    }

    [Fact]
    public void A_hung_job_is_abandoned_and_the_queue_goes_on_in_another_sta_thread()
    {
        using var worker = new StaWorker("test: miniaturas", TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(150));
        using var release = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        int hung = 0, next = 0;
        var apartment = ApartmentState.Unknown;
        worker.Post(() =>
        {
            hung = Environment.CurrentManagedThreadId;
            release.Wait(TimeSpan.FromSeconds(20));                 // un extractor de terceros que no vuelve
        });
        worker.Post(() =>
        {
            next = Environment.CurrentManagedThreadId;
            apartment = Thread.CurrentThread.GetApartmentState();
            done.Set();
        });
        try
        {
            Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "la cola sigue tras el trabajo colgado");
            Assert.NotEqual(hung, next);
            Assert.Equal(ApartmentState.STA, apartment);
            Assert.Equal(1, worker.Abandoned);
        }
        finally { release.Set(); }
        Assert.True(SpinWait.SpinUntil(() => worker.Abandoned == 0, TimeSpan.FromSeconds(10)), "el abandonado sale al volver");
    }

    [Fact]
    public void Abandoned_threads_are_capped_and_the_queue_waits_for_one_to_come_back()
    {
        using var worker = new StaWorker("test: miniaturas", TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100), maxAbandoned: 1);
        using var release1 = new ManualResetEventSlim();
        using var release2 = new ManualResetEventSlim();
        using var started2 = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        worker.Post(() => release1.Wait(TimeSpan.FromSeconds(20)));
        worker.Post(() =>
        {
            started2.Set();
            release2.Wait(TimeSpan.FromSeconds(20));
        });
        worker.Post(done.Set);
        try
        {
            Assert.True(started2.Wait(TimeSpan.FromSeconds(10)), "el primero se abandona y sigue el segundo");
            // El segundo también se cuelga y el tope (1) está lleno: el tercero no puede empezar.
            Assert.False(done.Wait(TimeSpan.FromMilliseconds(600)));
            release1.Set();                                          // vuelve el primero: hay sitio para abandonar el segundo
            Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            release1.Set();
            release2.Set();
        }
    }
}
