using ClaudeBarWin.Services.Updates;

namespace ClaudeBarWin.Tests;

/// <summary>
/// Las etapas de <see cref="VelopackUpdates"/> (Idle → Checking → Downloading → ReadyToApply, o Failed) con un motor falso:
/// sin red ni Velopack, y sin esperas con reloj (todo se espera con tareas).
/// </summary>
public class VelopackUpdatesTests
{
    private sealed class FakeEngine : IUpdateEngine
    {
        private bool _installed = true;
        /// <summary>Se llama cada vez que se lee <see cref="IsInstalled"/>, con el número de lectura (desde 1).</summary>
        public Action<int>? OnIsInstalled;
        public int IsInstalledReads;

        public bool IsInstalled
        {
            get { OnIsInstalled?.Invoke(Interlocked.Increment(ref IsInstalledReads)); return _installed; }
            set => _installed = value;
        }
        public string? PendingVersion { get; set; }
        public Func<Task<string?>> Check = () => Task.FromResult<string?>(null);
        public Func<Action<int>, Task> Download = p => { p(40); p(100); return Task.CompletedTask; };
        public readonly List<bool> Applied = new();
        public Action? OnApply;
        public int Checks, Downloads;

        public Task<string?> CheckAsync() { Interlocked.Increment(ref Checks); return Check(); }
        public Task DownloadAsync(Action<int> progress) { Interlocked.Increment(ref Downloads); return Download(progress); }
        public void ApplyAfterExit(bool restart) { OnApply?.Invoke(); lock (Applied) Applied.Add(restart); }
    }

    /// <summary>El actualizador con un «hilo de UI» síncrono que cuenta lo que se le manda, y una salida que se anota.</summary>
    private sealed class Rig : IDisposable
    {
        public readonly FakeEngine Engine = new();
        public readonly List<UpdateStage> Stages = new();
        public int Posts, Exits;
        public VelopackUpdates Updates = null!;

        public Rig Build()
        {
            Updates = new VelopackUpdates(Engine, a => { Interlocked.Increment(ref Posts); a(); }, () => Interlocked.Increment(ref Exits));
            Updates.Changed += () => { lock (Stages) if (Stages.Count == 0 || Stages[^1] != Updates.Stage) Stages.Add(Updates.Stage); };
            return this;
        }

        public async Task CheckAndSettle()
        {
            await Updates.CheckNowAsync();
            await Updates.Settled;
        }

        public void Dispose() => Updates.Dispose();
    }

    private static Rig New(Action<FakeEngine>? setup = null)
    {
        var rig = new Rig();
        setup?.Invoke(rig.Engine);
        return rig.Build();
    }

    [Fact]
    public async Task Up_to_date_goes_back_to_idle_without_a_version()
    {
        using var rig = New();

        await rig.CheckAndSettle();

        Assert.Equal(UpdateStage.Idle, rig.Updates.Stage);
        Assert.Null(rig.Updates.AvailableVersion);
        Assert.Equal(new[] { UpdateStage.Checking, UpdateStage.Idle }, rig.Stages);
        Assert.Equal(0, rig.Engine.Downloads);
    }

    [Fact]
    public async Task A_new_version_is_downloaded_in_the_background_and_left_ready()
    {
        var progress = new List<double>();
        using var rig = New(e => e.Check = () => Task.FromResult<string?>("0.5.1"));
        rig.Updates.Changed += () => { if (rig.Updates.Stage == UpdateStage.Downloading) progress.Add(rig.Updates.DownloadProgress); };

        await rig.CheckAndSettle();

        Assert.Equal(UpdateStage.ReadyToApply, rig.Updates.Stage);
        Assert.Equal("0.5.1", rig.Updates.AvailableVersion);
        Assert.Equal(1.0, rig.Updates.DownloadProgress);
        Assert.Equal(new[] { UpdateStage.Checking, UpdateStage.Downloading, UpdateStage.ReadyToApply }, rig.Stages);
        Assert.Equal(new[] { 0.0, 0.4, 1.0 }, progress);
        // Nada se aplica hasta pulsar o cerrar.
        Assert.Empty(rig.Engine.Applied);
        Assert.Equal(0, rig.Exits);
    }

    [Fact]
    public async Task CheckNow_returns_once_the_check_is_done_while_the_download_goes_on()
    {
        var download = new TaskCompletionSource();
        using var rig = New(e =>
        {
            e.Check = () => Task.FromResult<string?>("0.5.1");
            e.Download = _ => download.Task;
        });

        await rig.Updates.CheckNowAsync();

        Assert.Equal(UpdateStage.Downloading, rig.Updates.Stage);
        Assert.Equal("0.5.1", rig.Updates.AvailableVersion);
        // Con la descarga en camino no se vuelve a preguntar.
        await rig.Updates.CheckNowAsync();
        Assert.Equal(1, rig.Engine.Checks);

        download.SetResult();
        await rig.Updates.Settled;
        Assert.Equal(UpdateStage.ReadyToApply, rig.Updates.Stage);
    }

    [Fact]
    public async Task A_failed_check_is_Failed_and_keeps_no_version()
    {
        using var rig = New(e => e.Check = () => Task.FromException<string?>(new HttpRequestException("sin red")));

        await rig.CheckAndSettle();

        Assert.Equal(UpdateStage.Failed, rig.Updates.Stage);
        Assert.Null(rig.Updates.AvailableVersion);
        Assert.Equal(0, rig.Engine.Downloads);
    }

    [Fact]
    public async Task A_rejected_signature_is_Failed_and_nothing_is_downloaded()
    {
        // VelopackEngine lanza si solo hay releases con la firma mala (no es «al día»).
        using var rig = New(e => e.Check = () => Task.FromException<string?>(new InvalidDataException("descartadas: v0.5.2")));

        await rig.CheckAndSettle();

        Assert.Equal(UpdateStage.Failed, rig.Updates.Stage);
        Assert.Equal(0, rig.Engine.Downloads);
        await rig.Updates.ApplyAndRestartAsync();
        Assert.Empty(rig.Engine.Applied);
        Assert.Equal(0, rig.Exits);
    }

    [Fact]
    public async Task A_failed_download_keeps_the_version_and_ApplyAndRestart_retries_it()
    {
        int attempt = 0;
        using var rig = New(e =>
        {
            e.Check = () => Task.FromResult<string?>("0.5.1");
            e.Download = p => ++attempt == 1 ? Task.FromException(new IOException("checksum")) : Task.CompletedTask;
        });

        await rig.CheckAndSettle();
        Assert.Equal(UpdateStage.Failed, rig.Updates.Stage);
        Assert.Equal("0.5.1", rig.Updates.AvailableVersion);

        await rig.Updates.ApplyAndRestartAsync();

        Assert.Equal(2, rig.Engine.Downloads);
        Assert.Equal(new[] { true }, rig.Engine.Applied);
        Assert.Equal(1, rig.Exits);
    }

    [Fact]
    public async Task ApplyAndRestart_with_a_ready_version_hands_over_to_UpdateExe_and_closes_the_app_once()
    {
        using var rig = New(e => e.Check = () => Task.FromResult<string?>("0.5.1"));
        await rig.CheckAndSettle();

        await rig.Updates.ApplyAndRestartAsync();
        await rig.Updates.ApplyAndRestartAsync();   // un segundo clic no lanza otro Update.exe
        rig.Updates.Dispose();                      // ni el cierre que provoca

        Assert.Equal(new[] { true }, rig.Engine.Applied);
        Assert.Equal(1, rig.Exits);
        Assert.Equal(1, rig.Engine.Checks);         // ya estaba descargada: no se vuelve a preguntar
    }

    [Fact]
    public async Task ApplyAndRestart_from_scratch_checks_downloads_and_applies()
    {
        using var rig = New(e => e.Check = () => Task.FromResult<string?>("0.5.1"));

        await rig.Updates.ApplyAndRestartAsync();

        Assert.Equal(1, rig.Engine.Checks);
        Assert.Equal(1, rig.Engine.Downloads);
        Assert.Equal(new[] { true }, rig.Engine.Applied);
        Assert.Equal(1, rig.Exits);
    }

    [Fact]
    public async Task ApplyAndRestart_with_nothing_new_does_nothing()
    {
        using var rig = New();

        await rig.Updates.ApplyAndRestartAsync();

        Assert.Empty(rig.Engine.Applied);
        Assert.Equal(0, rig.Exits);
        Assert.Equal(UpdateStage.Idle, rig.Updates.Stage);
    }

    [Fact]
    public async Task If_UpdateExe_cannot_be_launched_the_app_stays_open()
    {
        using var rig = New(e =>
        {
            e.Check = () => Task.FromResult<string?>("0.5.1");
            e.OnApply = () => throw new FileNotFoundException("Update.exe");
        });
        await rig.CheckAndSettle();

        await rig.Updates.ApplyAndRestartAsync();

        Assert.Equal(UpdateStage.Failed, rig.Updates.Stage);
        Assert.Equal(0, rig.Exits);
    }

    [Fact]
    public async Task Closing_with_a_ready_version_applies_it_silently_on_exit()
    {
        var rig = New(e => e.Check = () => Task.FromResult<string?>("0.5.1"));
        await rig.CheckAndSettle();

        rig.Updates.Dispose();
        rig.Updates.Dispose();

        Assert.Equal(new[] { false }, rig.Engine.Applied);   // restart: false → en silencio, sin volver a abrir
        Assert.Equal(0, rig.Exits);
    }

    [Fact]
    public async Task Closing_during_a_check_still_applies_the_version_already_downloaded()
    {
        // La vuelta de cada 6 h (o una desde el menú) pone la etapa en Checking aunque la 0.5.1 ya esté lista.
        var asked = new TaskCompletionSource();
        var answer = new TaskCompletionSource<string?>();
        var rig = New(e =>
        {
            e.PendingVersion = "0.5.1";
            e.Check = () => { asked.TrySetResult(); return answer.Task; };
        });
        var check = rig.Updates.CheckNowAsync();
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(UpdateStage.Checking, rig.Updates.Stage);

        rig.Updates.Dispose();
        answer.SetResult("0.5.2");
        await check;

        Assert.Equal(new[] { false }, rig.Engine.Applied);
        Assert.Equal(0, rig.Engine.Downloads);   // y la comprobación que acaba después no descarga nada
    }

    [Fact]
    public async Task Closing_while_a_newer_version_downloads_applies_nothing()
    {
        // Al empezar a bajar la 0.5.2, Velopack ya ha borrado el paquete de la 0.5.1: no queda nada que aplicar.
        var download = new TaskCompletionSource();
        var rig = New(e =>
        {
            e.PendingVersion = "0.5.1";
            e.Check = () => Task.FromResult<string?>("0.5.2");
            e.Download = _ => download.Task;
        });
        await rig.Updates.CheckNowAsync();
        Assert.Equal(UpdateStage.Downloading, rig.Updates.Stage);

        rig.Updates.Dispose();
        download.SetResult();

        Assert.Empty(rig.Engine.Applied);
    }

    [Fact]
    public async Task Closing_without_a_ready_version_applies_nothing()
    {
        var rig = New(e => e.Check = () => Task.FromResult<string?>(null));
        await rig.CheckAndSettle();

        rig.Updates.Dispose();

        Assert.Empty(rig.Engine.Applied);
    }

    [Fact]
    public async Task A_copy_not_installed_by_Velopack_never_checks()
    {
        using var rig = New(e => e.IsInstalled = false);

        await rig.CheckAndSettle();                 // desde el menú: se dice que no se pudo
        Assert.Equal(UpdateStage.Failed, rig.Updates.Stage);
        await rig.Updates.ApplyAndRestartAsync();

        Assert.Equal(0, rig.Engine.Checks);
        Assert.Empty(rig.Engine.Applied);
        Assert.Equal(0, rig.Exits);
    }

    [Fact]
    public async Task The_silent_loop_on_a_copy_not_installed_by_Velopack_shows_nothing()
    {
        using var rig = New(e => e.IsInstalled = false);

        await rig.Updates.SilentCheckAsync();
        await rig.Updates.Settled;

        Assert.Equal(UpdateStage.Idle, rig.Updates.Stage);
        Assert.Empty(rig.Stages);
        Assert.Equal(0, rig.Engine.Checks);
    }

    [Fact]
    public async Task The_silent_loop_does_not_ask_again_with_a_version_ready()
    {
        using var rig = New(e => e.Check = () => Task.FromResult<string?>("0.5.1"));
        await rig.CheckAndSettle();

        await rig.Updates.SilentCheckAsync();
        await rig.Updates.Settled;

        Assert.Equal(1, rig.Engine.Checks);
        Assert.Equal(UpdateStage.ReadyToApply, rig.Updates.Stage);
    }

    [Fact]
    public async Task A_silent_check_that_fails_is_Failed_too()
    {
        using var rig = New(e => e.Check = () => Task.FromException<string?>(new HttpRequestException("sin red")));

        await rig.Updates.SilentCheckAsync();
        await rig.Updates.Settled;

        Assert.Equal(UpdateStage.Failed, rig.Updates.Stage);
    }

    [Fact]
    public async Task StartLoop_checks_right_away_once()
    {
        var asked = new TaskCompletionSource();
        using var rig = New(e => e.Check = () => { asked.TrySetResult(); return Task.FromResult<string?>(null); });

        rig.Updates.StartLoop();
        rig.Updates.StartLoop();
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await rig.Updates.Settled;

        Assert.Equal(1, rig.Engine.Checks);
        Assert.Equal(UpdateStage.Idle, rig.Updates.Stage);
    }

    [Fact]
    public void A_version_downloaded_in_a_previous_run_is_ready_from_the_start()
    {
        using var rig = New(e => e.PendingVersion = "0.5.1");

        Assert.Equal(UpdateStage.ReadyToApply, rig.Updates.Stage);
        Assert.Equal("0.5.1", rig.Updates.AvailableVersion);
        Assert.Equal(1.0, rig.Updates.DownloadProgress);
    }

    [Fact]
    public async Task Rechecking_the_version_already_downloaded_does_not_download_it_again()
    {
        using var rig = New(e =>
        {
            e.PendingVersion = "0.5.1";
            e.Check = () => Task.FromResult<string?>("0.5.1");
        });

        await rig.CheckAndSettle();

        Assert.Equal(UpdateStage.ReadyToApply, rig.Updates.Stage);
        Assert.Equal(0, rig.Engine.Downloads);
    }

    [Fact]
    public async Task A_failed_check_does_not_lose_a_version_already_downloaded()
    {
        int call = 0;
        using var rig = New(e => e.Check = () => ++call == 1
            ? Task.FromResult<string?>("0.5.1")
            : Task.FromException<string?>(new HttpRequestException("sin red")));
        await rig.CheckAndSettle();

        await rig.CheckAndSettle();

        Assert.Equal(UpdateStage.ReadyToApply, rig.Updates.Stage);
        Assert.Equal("0.5.1", rig.Updates.AvailableVersion);
        rig.Updates.Dispose();
        Assert.Equal(new[] { false }, rig.Engine.Applied);   // y se aplica al cerrar
    }

    [Fact]
    public async Task A_version_already_downloaded_stays_ready_if_the_release_disappears()
    {
        int call = 0;
        using var rig = New(e => e.Check = () => Task.FromResult<string?>(++call == 1 ? "0.5.1" : null));
        await rig.CheckAndSettle();

        await rig.CheckAndSettle();

        Assert.Equal(UpdateStage.ReadyToApply, rig.Updates.Stage);
        Assert.Equal("0.5.1", rig.Updates.AvailableVersion);
    }

    [Fact]
    public async Task A_failed_download_of_a_newer_version_leaves_nothing_ready()
    {
        // Velopack borra los demás paquetes al descargar, también si falla: la 0.5.1 ya no está.
        int call = 0;
        using var rig = New(e =>
        {
            e.Check = () => Task.FromResult<string?>(++call == 1 ? "0.5.1" : "0.5.2");
            e.Download = _ => call == 1 ? Task.CompletedTask : Task.FromException(new IOException("checksum"));
        });
        await rig.CheckAndSettle();

        await rig.CheckAndSettle();

        Assert.Equal(UpdateStage.Failed, rig.Updates.Stage);
        Assert.Equal("0.5.2", rig.Updates.AvailableVersion);
        rig.Updates.Dispose();
        Assert.Empty(rig.Engine.Applied);
    }

    [Fact]
    public async Task Once_the_apply_is_scheduled_no_check_or_download_starts()
    {
        // Una descarga nueva borraría el paquete que Update.exe está esperando para aplicar.
        using var rig = New(e => e.Check = () => Task.FromResult<string?>("0.5.1"));
        await rig.CheckAndSettle();
        await rig.Updates.ApplyAndRestartAsync();

        rig.Engine.Check = () => Task.FromResult<string?>("0.5.2");
        await rig.CheckAndSettle();
        await rig.Updates.SilentCheckAsync();

        Assert.Equal(1, rig.Engine.Checks);
        Assert.Equal(1, rig.Engine.Downloads);
    }

    [Fact]
    public async Task A_check_already_under_way_when_the_apply_is_scheduled_does_not_start_a_download()
    {
        // Lista la 0.5.1, empieza una comprobación (p. ej. desde el notch) y, antes de que pase a «Comprobando», se pulsa
        // «Aplicar». La comprobación encuentra la 0.5.2: descargarla borraría el paquete que espera Update.exe.
        using var rig = New(e =>
        {
            e.PendingVersion = "0.5.1";
            e.Check = () => Task.FromResult<string?>("0.5.2");
        });
        Task? apply = null;
        rig.Engine.IsInstalledReads = 0;
        // Lectura 1: la de CheckNowAsync; lectura 2: la de la comprobación ya en marcha, antes de preguntar.
        rig.Engine.OnIsInstalled = n => { if (n == 2) apply = rig.Updates.ApplyAndRestartAsync(); };

        await rig.CheckAndSettle();
        await apply!;

        Assert.Equal(1, rig.Engine.Checks);
        Assert.Equal(0, rig.Engine.Downloads);
        Assert.Equal(new[] { true }, rig.Engine.Applied);
        Assert.Equal(1, rig.Exits);
        Assert.Equal(UpdateStage.ReadyToApply, rig.Updates.Stage);
        Assert.Equal("0.5.1", rig.Updates.AvailableVersion);
    }

    [Fact]
    public async Task An_older_version_than_the_one_ready_does_not_replace_it()
    {
        // Ya lista la 0.5.2 y su release se borra: lo último publicado vuelve a ser la 0.5.1.
        using var rig = New(e =>
        {
            e.PendingVersion = "0.5.2";
            e.Check = () => Task.FromResult<string?>("0.5.1");
        });

        await rig.CheckAndSettle();

        Assert.Equal(UpdateStage.ReadyToApply, rig.Updates.Stage);
        Assert.Equal("0.5.2", rig.Updates.AvailableVersion);
        Assert.Equal(0, rig.Engine.Downloads);
    }

    [Theory]
    [InlineData("0.5.2", "0.5.1", true)]
    [InlineData("0.10.0", "0.9.9", true)]
    [InlineData("0.5.1", "0.5.1", false)]
    [InlineData("0.5.0", "0.5.1", false)]
    [InlineData("0.5.1", "0.5.1-beta", true)]
    [InlineData("basura", "0.5.1", false)]
    [InlineData("0.5.2", "basura", false)]
    public void Versions_compare_as_semver(string candidate, string current, bool newer)
    {
        Assert.Equal(newer, VelopackUpdates.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData(true, "0.5.1", "Muesca se está cerrando y aplicará al salir la v0.5.1")]
    [InlineData(true, null, "Muesca se está cerrando")]
    [InlineData(false, "0.5.1", "ya se pidió aplicar la v0.5.1")]
    [InlineData(false, null, "ya se pidió aplicar la versión descargada")]
    public void The_skipped_download_log_says_whether_it_is_closing_or_applying(bool closing, string? ready, string why)
    {
        var message = VelopackUpdates.SkippedDownloadMessage("0.5.2", closing, ready);

        Assert.Equal($"actualizaciones: hay una versión nueva (v0.5.2), pero {why}: no se descarga", message);
        if (closing) Assert.DoesNotContain("pidió", message);
        else Assert.DoesNotContain("cerrando", message);
    }

    [Fact]
    public async Task Checking_from_the_menu_on_a_copy_not_installed_fails_right_away()
    {
        using var rig = New(e => e.IsInstalled = false);

        await rig.Updates.CheckNowAsync();

        Assert.Equal(UpdateStage.Failed, rig.Updates.Stage);
        Assert.Equal(0, rig.Engine.Checks);
    }

    [Fact]
    public async Task Concurrent_checks_share_one_request()
    {
        var gate = new TaskCompletionSource<string?>();
        using var rig = New(e => e.Check = () => gate.Task);

        var a = rig.Updates.CheckNowAsync();
        var b = rig.Updates.CheckNowAsync();
        gate.SetResult(null);
        await Task.WhenAll(a, b);

        Assert.Equal(1, rig.Engine.Checks);
    }

    /// <summary>Un «hilo de UI» de verdad: un hilo propio que ejecuta, en orden, lo que se le manda.</summary>
    private sealed class UiThread : IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;

        public UiThread()
        {
            _thread = new Thread(() => { foreach (var action in _queue.GetConsumingEnumerable()) action(); })
            {
                IsBackground = true,
                Name = "UI de prueba",
            };
            _thread.Start();
        }

        public int ThreadId => _thread.ManagedThreadId;

        public void Post(Action action) => _queue.Add(action);

        /// <summary>Termina cuando ya se ha ejecutado todo lo mandado hasta ahora.</summary>
        public Task Drain()
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() => done.SetResult());
            return done.Task;
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join(TimeSpan.FromSeconds(10));
            _queue.Dispose();
        }
    }

    [Fact]
    public async Task Changed_always_goes_through_the_UI_thread()
    {
        using var ui = new UiThread();
        var engine = new FakeEngine { Check = () => Task.FromResult<string?>("0.5.1") };
        using var updates = new VelopackUpdates(engine, ui.Post, () => { });
        var threads = new System.Collections.Concurrent.ConcurrentQueue<int>();
        updates.Changed += () => threads.Enqueue(Environment.CurrentManagedThreadId);

        await updates.CheckNowAsync();
        await updates.Settled;
        await ui.Drain().WaitAsync(TimeSpan.FromSeconds(10));

        // Comprobación, descarga con su progreso y «lista»: todo desde hilos del pool, y cada Changed en el de UI.
        Assert.NotEmpty(threads);
        Assert.All(threads, id => Assert.Equal(ui.ThreadId, id));
        Assert.NotEqual(ui.ThreadId, Environment.CurrentManagedThreadId);
    }

    [Fact]
    public async Task After_Dispose_nothing_else_starts()
    {
        var rig = New(e => e.Check = () => Task.FromResult<string?>("0.5.1"));
        rig.Updates.Dispose();

        await rig.Updates.CheckNowAsync();
        rig.Updates.StartLoop();
        await rig.Updates.Settled;

        Assert.Equal(0, rig.Engine.Checks);
        Assert.Equal(0, rig.Posts);
    }
}
