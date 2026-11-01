using ClaudeBarWin.Config;
using ClaudeBarWin.Services.Licensing;
using Windows.Services.Store;

namespace ClaudeBarWin.Tests.Licensing;

/// <summary>
/// La edición de la Store sin paquete MSIX (con una Store falsa): la prueba empieza sola, el último estado conocido manda
/// sin red, solo una respuesta válida quita el Pro, la compra desbloquea al momento y todo aviso va por <c>post</c>.
/// </summary>
public class StoreProLicenseTests
{
    private static readonly DateTime T0 = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);

    private sealed class FakeBackend : IStoreBackend
    {
        public Func<StoreLicenseInfo>? License { get; set; } = () => new StoreLicenseInfo(true, false);
        /// <summary>Si se pone, la consulta devuelve esta tarea (para que acabe cuando diga el test).</summary>
        public Func<Task<StoreLicenseInfo>>? LicenseTask { get; set; }
        public Func<PurchaseResult>? Purchase { get; set; } = () => PurchaseResult.Purchased;
        public string? PriceText { get; set; } = "9,99 €";
        public int LicenseCalls { get; private set; }
        public bool Disposed { get; private set; }
        public event Action? LicensesChanged;

        public Task<StoreLicenseInfo> GetLicenseAsync()
        {
            LicenseCalls++;
            return LicenseTask is { } t ? t() : Task.FromResult(License!());
        }

        public Task<PurchaseResult> PurchaseAsync() => Task.FromResult(Purchase!());
        public Task<string?> GetPriceAsync() => Task.FromResult(PriceText);
        public void RaiseChanged() => LicensesChanged?.Invoke();
        public bool HasListeners => LicensesChanged is not null;
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeStore : IProStateStore
    {
        public ProSnapshot Saved;
        public int Saves;
        /// <summary>La config no se puede leer (bloqueada).</summary>
        public bool Locked;
        public FakeStore(ProSnapshot initial) => Saved = initial;
        public ProSnapshot Load() => Locked ? throw new IOException("bloqueado") : Saved;
        public void SaveTrialStart(DateTime startUtc) { Saved = Saved with { TrialStartUtc = startUtc }; Saves++; }
        public void SaveLastKnownPro(bool owns)
        {
            Saved = Saved with { LastKnownPro = owns, EverOwnedPro = Saved.EverOwnedPro || owns };
            Saves++;
        }
    }

    /// <summary>Reloj y temporizadores a mano: el primero que crea la licencia es el de la prueba, el segundo el de la
    /// confirmación; los de <c>WaitAsync</c> vienen después.</summary>
    private sealed class FakeTime : TimeProvider
    {
        private readonly Func<DateTime> _now;
        public readonly List<FakeTimer> Timers = new();
        public FakeTime(Func<DateTime> now) => _now = now;
        public override DateTimeOffset GetUtcNow() => new(_now(), TimeSpan.Zero);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var t = new FakeTimer(() => callback(state)) { Due = dueTime };
            lock (Timers) Timers.Add(t);
            return t;
        }
        public FakeTimer Trial => Timers[0];
        public FakeTimer Recheck => Timers[1];
    }

    private sealed class FakeTimer : ITimer
    {
        private readonly Action _fire;
        public FakeTimer(Action fire) => _fire = fire;
        public TimeSpan Due { get; set; }
        public bool Armed => Due != Timeout.InfiniteTimeSpan;
        public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return true; }
        public void Fire() => _fire();
        public void Dispose() => Due = Timeout.InfiniteTimeSpan;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    /// <summary>El «hilo de UI»: lo encolado con post se ejecuta al llamar a <see cref="Pump"/>.</summary>
    private sealed class Ui
    {
        private readonly Queue<Action> _queue = new();
        public void Post(Action a) { lock (_queue) _queue.Enqueue(a); }
        public void Pump()
        {
            while (true)
            {
                Action a;
                lock (_queue) { if (_queue.Count == 0) return; a = _queue.Dequeue(); }
                a();
            }
        }
    }

    private sealed record Rig(StoreProLicense License, FakeBackend Backend, FakeStore Store, Ui Ui, Func<int> Changes, FakeTime Time);

    private static Rig NewRig(ProSnapshot saved, DateTime? now = null, Func<DateTime>? clock = null, bool locked = false)
    {
        var backend = new FakeBackend();
        var store = new FakeStore(saved) { Locked = locked };
        var ui = new Ui();
        var time = new FakeTime(clock ?? (() => now ?? T0));
        var license = new StoreProLicense(backend, store, ui.Post, time);
        int changes = 0;
        license.Changed += () => changes++;
        return new Rig(license, backend, store, ui, () => changes, time);
    }

    private static (StoreProLicense License, FakeBackend Backend, FakeStore Store, Ui Ui, Func<int> Changes) New(
        ProSnapshot saved, DateTime? now = null, Func<DateTime>? clock = null)
    {
        var r = NewRig(saved, now, clock);
        return (r.License, r.Backend, r.Store, r.Ui, r.Changes);
    }

    [Fact]
    public void The_first_run_starts_the_seven_day_trial_and_saves_it_on_the_ui_thread()
    {
        var (license, _, store, ui, _) = New(default);
        using (license)
        {
            Assert.Equal(ProState.Trial, license.State);
            Assert.Equal(7, license.TrialDaysLeft);
            Assert.True(license.CanPurchaseHere);
            Assert.False(license.TrialEnded);
            Assert.Equal(0, store.Saves);   // nada fuera del hilo de UI
            ui.Pump();
            Assert.Equal(new ProSnapshot(T0, false), store.Saved);
            Assert.Equal(1, store.Saves);   // solo la fecha: el último estado conocido no se toca
        }
    }

    [Fact]
    public void A_trial_already_started_is_kept_and_ends_on_its_day()
    {
        var (during, _, store, _, _) = New(new ProSnapshot(T0, false), T0.AddDays(5.5));
        using (during)
        {
            Assert.Equal(ProState.Trial, during.State);
            Assert.Equal(2, during.TrialDaysLeft);
            Assert.Equal(T0, during.TrialStartUtc);
        }
        var (after, _, _, _, _) = New(new ProSnapshot(T0, false), T0.AddDays(7.2));
        using (after)
        {
            Assert.Equal(ProState.Free, after.State);
            Assert.Null(after.TrialDaysLeft);
            Assert.True(after.TrialEnded);
        }
        Assert.Equal(0, store.Saves);
    }

    /// <summary>El temporizador de la prueba se arma al próximo cambio (más un segundo) y, al disparar, la acaba.</summary>
    [Fact]
    public void The_trial_timer_is_armed_for_the_next_change_and_ends_the_trial_when_it_fires()
    {
        var now = T0.AddDays(6.98);
        var rig = NewRig(new ProSnapshot(T0, false), clock: () => now);
        using (rig.License)
        {
            Assert.Equal(1, rig.License.TrialDaysLeft);
            var left = T0.AddDays(7) - now;
            Assert.Equal(left + TimeSpan.FromSeconds(1), rig.Time.Trial.Due);

            now = T0.AddDays(7).AddSeconds(1);
            rig.Time.Trial.Fire();
            Assert.Equal(ProState.Trial, rig.License.State);   // el disparo va al hilo de UI
            rig.Ui.Pump();
            Assert.Equal(ProState.Free, rig.License.State);
            Assert.True(rig.License.TrialEnded);
            Assert.Equal(1, rig.Changes());
            Assert.False(rig.Time.Trial.Armed);   // acabada: ningún disparo más
        }
    }

    /// <summary>El temporizador no cuenta la suspensión: nunca espera más de una hora y recalcula con el reloj de pared, así
    /// un portátil que duerme no alarga la prueba.</summary>
    [Fact]
    public void The_trial_timer_never_waits_more_than_an_hour_so_sleep_does_not_stretch_the_trial()
    {
        var now = T0.AddDays(2.5);
        var rig = NewRig(new ProSnapshot(T0, false), clock: () => now);
        using (rig.License)
        {
            Assert.Equal(5, rig.License.TrialDaysLeft);
            Assert.Equal(StoreProLicense.MaxTimerWait, rig.Time.Trial.Due);

            now = T0.AddDays(4.2);   // durmió casi dos días: el disparo de la hora llega al despertar
            rig.Time.Trial.Fire();
            rig.Ui.Pump();
            Assert.Equal(3, rig.License.TrialDaysLeft);

            now = T0.AddDays(7.5);
            rig.Time.Trial.Fire();
            rig.Ui.Pump();
            Assert.Equal(ProState.Free, rig.License.State);
        }
    }

    /// <summary>Con config.json bloqueado no se empieza otra prueba ni se escribe nada; al poder leerlo, sigue lo guardado.</summary>
    [Fact]
    public void An_unreadable_config_neither_restarts_the_trial_nor_overwrites_the_last_known_state()
    {
        var rig = NewRig(new ProSnapshot(T0.AddDays(-30), true, true), locked: true);
        using (rig.License)
        {
            rig.Ui.Pump();
            Assert.Equal(0, rig.Store.Saves);
            Assert.Null(rig.License.TrialStartUtc);
            Assert.False(rig.License.TrialEnded);
            Assert.Equal(StoreProLicense.LoadRetry, rig.Time.Trial.Due);   // se reintenta

            rig.Time.Trial.Fire();   // sigue bloqueado
            rig.Ui.Pump();
            Assert.Equal(0, rig.Store.Saves);

            rig.Store.Locked = false;
            rig.Time.Trial.Fire();
            rig.Ui.Pump();
            Assert.Equal(ProState.Pro, rig.License.State);
            Assert.Equal(T0.AddDays(-30), rig.License.TrialStartUtc);
            Assert.Equal(0, rig.Store.Saves);
            Assert.Equal(new ProSnapshot(T0.AddDays(-30), true, true), rig.Store.Saved);
        }
    }

    [Fact]
    public async Task A_purchase_while_the_config_is_unreadable_is_saved_once_it_can_be_read()
    {
        var rig = NewRig(new ProSnapshot(T0.AddDays(-10), false), locked: true);
        using (rig.License)
        {
            Assert.Equal(PurchaseResult.Purchased, await rig.License.PurchaseAsync());
            rig.Ui.Pump();
            Assert.Equal(ProState.Pro, rig.License.State);
            Assert.Equal(0, rig.Store.Saves);

            rig.Store.Locked = false;
            await rig.License.RefreshAsync();
            rig.Ui.Pump();
            Assert.Equal(new ProSnapshot(T0.AddDays(-10), true, true), rig.Store.Saved);
        }
    }
    [Fact]
    public async Task Last_known_pro_is_kept_when_the_store_does_not_answer_or_answers_without_a_valid_license()
    {
        var (license, backend, store, ui, changes) = New(new ProSnapshot(T0.AddDays(-30), true));
        using (license)
        {
            Assert.Equal(ProState.Pro, license.State);   // ya al arrancar, sin esperar a la red

            backend.License = () => throw new IOException("sin red");
            await license.RefreshAsync();
            ui.Pump();
            Assert.Equal(ProState.Pro, license.State);

            backend.License = () => new StoreLicenseInfo(false, false);   // la Store no da una licencia válida
            await license.RefreshAsync();
            ui.Pump();
            Assert.Equal(ProState.Pro, license.State);
            Assert.True(store.Saved.LastKnownPro);
            Assert.Equal(1, changes());   // solo el precio (leído al refrescar), nunca el estado
        }
    }

    /// <summary>Una sola respuesta «válida» sin el complemento no quita el Pro (en una app gratuita casi todas lo son: caché
    /// sin sincronizar, otra cuenta). La segunda, pedida por el temporizador media hora después, sí.</summary>
    [Fact]
    public async Task Pro_is_removed_only_by_two_answers_without_the_add_on_half_an_hour_apart()
    {
        var now = T0;
        var rig = NewRig(new ProSnapshot(T0.AddDays(-30), true, true), clock: () => now);
        using (rig.License)
        {
            rig.Backend.License = () => new StoreLicenseInfo(true, false);   // reembolso, otra cuenta… o caché sin sincronizar
            await rig.License.RefreshAsync();
            rig.Ui.Pump();
            Assert.Equal(ProState.Pro, rig.License.State);
            Assert.True(rig.Store.Saved.LastKnownPro);
            Assert.Equal(StoreProLicense.DenialConfirmation + TimeSpan.FromSeconds(1), rig.Time.Recheck.Due);

            now = T0.AddMinutes(5);   // otra igual enseguida (aviso de cambio de licencias): aún no basta
            rig.Backend.RaiseChanged();
            rig.Ui.Pump();
            Assert.Equal(ProState.Pro, rig.License.State);

            now = T0.AddMinutes(31);
            rig.Time.Recheck.Fire();   // la confirmación
            rig.Ui.Pump();
            Assert.Equal(ProState.Free, rig.License.State);
            Assert.False(rig.Store.Saved.LastKnownPro);
            Assert.False(rig.License.TrialEnded);   // hubo compra: nada de «tu prueba ha terminado»
        }
    }

    [Fact]
    public async Task An_answer_with_the_add_on_cancels_a_pending_denial()
    {
        var now = T0;
        var rig = NewRig(new ProSnapshot(T0.AddDays(-30), true, true), clock: () => now);
        using (rig.License)
        {
            rig.Backend.License = () => new StoreLicenseInfo(true, false);
            await rig.License.RefreshAsync();
            rig.Backend.License = () => new StoreLicenseInfo(true, true);   // la caché ya se sincronizó
            await rig.License.RefreshAsync();
            Assert.False(rig.Time.Recheck.Armed);

            now = T0.AddHours(2);
            rig.Backend.License = () => new StoreLicenseInfo(true, false);   // la próxima negativa vuelve a empezar la cuenta
            await rig.License.RefreshAsync();
            rig.Ui.Pump();
            Assert.Equal(ProState.Pro, rig.License.State);
        }
    }

    [Fact]
    public async Task Restore_purchase_removes_pro_at_once_when_the_store_says_no()
    {
        var rig = NewRig(new ProSnapshot(T0.AddDays(-30), true, true));
        using (rig.License)
        {
            rig.Backend.License = () => new StoreLicenseInfo(true, false);
            Assert.Equal(PurchaseResult.NotOwned, await rig.License.RestoreAsync());
            rig.Ui.Pump();
            Assert.Equal(ProState.Free, rig.License.State);
            Assert.False(rig.Store.Saved.LastKnownPro);
        }
    }

    /// <summary>Una consulta lanzada antes de comprar que vuelve después (sin el complemento: la caché aún no lo sabe) no pisa
    /// la compra.</summary>
    [Fact]
    public async Task A_refresh_started_before_buying_that_ends_after_it_does_not_undo_the_purchase()
    {
        var rig = NewRig(new ProSnapshot(T0.AddDays(-10), false));
        using (rig.License)
        {
            var slow = new TaskCompletionSource<StoreLicenseInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Backend.LicenseTask = () => slow.Task;
            var refresh = rig.License.RefreshAsync();
            Assert.False(refresh.IsCompleted);

            Assert.Equal(PurchaseResult.Purchased, await rig.License.PurchaseAsync());
            Assert.Equal(ProState.Pro, rig.License.State);

            slow.SetResult(new StoreLicenseInfo(true, false));
            await refresh;
            rig.Ui.Pump();
            Assert.Equal(ProState.Pro, rig.License.State);
            Assert.True(rig.Store.Saved.LastKnownPro);
            Assert.False(rig.Time.Recheck.Armed);   // descartada: ni siquiera cuenta como primera negativa
        }
    }

    /// <summary>Una Store que no contesta no deja la tarjeta en «Abriendo Microsoft Store…» ni bloquea los refrescos.</summary>
    [Fact]
    public async Task A_store_that_hangs_times_out_and_does_not_block_later_refreshes_or_restore()
    {
        var rig = NewRig(new ProSnapshot(T0.AddDays(-30), true, true));
        using (rig.License)
        {
            var never = new TaskCompletionSource<StoreLicenseInfo>();
            rig.Backend.LicenseTask = () => never.Task;
            var refresh = rig.License.RefreshAsync();
            Assert.False(refresh.IsCompleted);
            var timeout = rig.Time.Timers.Skip(2).Single();   // el de WaitAsync
            Assert.Equal(StoreProLicense.QueryTimeout, timeout.Due);
            timeout.Fire();
            await refresh;
            Assert.Equal(ProState.Pro, rig.License.State);

            var restore = rig.License.RestoreAsync();
            rig.Time.Timers.Skip(3).Single().Fire();
            Assert.Equal(PurchaseResult.NetworkError, await restore);

            rig.Backend.LicenseTask = null;
            await rig.License.RefreshAsync();
            Assert.Equal(3, rig.Backend.LicenseCalls);
        }
    }
    [Fact]
    public async Task Buying_unlocks_at_once_and_is_remembered()
    {
        var (license, backend, store, ui, changes) = New(new ProSnapshot(T0.AddDays(-10), false));
        using (license)
        {
            Assert.Equal(ProState.Free, license.State);
            var result = await license.PurchaseAsync();
            Assert.Equal(PurchaseResult.Purchased, result);
            Assert.Equal(ProState.Pro, license.State);   // en caliente, sin volver a preguntar
            ui.Pump();
            Assert.True(store.Saved.LastKnownPro);
            Assert.Equal(1, changes());
            Assert.Equal(0, backend.LicenseCalls);
        }
    }

    [Theory]
    [InlineData(PurchaseResult.Cancelled)]
    [InlineData(PurchaseResult.NetworkError)]
    [InlineData(PurchaseResult.NotAvailable)]
    [InlineData(PurchaseResult.Failed)]
    public async Task A_purchase_that_does_not_complete_changes_nothing(PurchaseResult outcome)
    {
        var (license, backend, store, ui, changes) = New(new ProSnapshot(T0, false), T0.AddDays(2));
        using (license)
        {
            backend.Purchase = () => outcome;
            Assert.Equal(outcome, await license.PurchaseAsync());
            ui.Pump();
            Assert.Equal(ProState.Trial, license.State);
            Assert.Equal(0, changes());
            Assert.Equal(0, store.Saves);
        }
    }

    [Fact]
    public async Task A_purchase_that_throws_is_a_failure_not_a_crash()
    {
        var (license, backend, _, _, _) = New(new ProSnapshot(T0.AddDays(-10), false));
        using (license)
        {
            backend.Purchase = () => throw new InvalidOperationException("sin ventana");
            Assert.Equal(PurchaseResult.Failed, await license.PurchaseAsync());
            Assert.Equal(ProState.Free, license.State);
        }
    }

    [Fact]
    public async Task Restore_says_what_the_store_knows()
    {
        var (license, backend, _, ui, _) = New(new ProSnapshot(T0.AddDays(-10), false));
        using (license)
        {
            backend.License = () => throw new IOException("sin red");
            Assert.Equal(PurchaseResult.NetworkError, await license.RestoreAsync());
            backend.License = () => new StoreLicenseInfo(false, false);
            Assert.Equal(PurchaseResult.NetworkError, await license.RestoreAsync());
            backend.License = () => new StoreLicenseInfo(true, false);
            Assert.Equal(PurchaseResult.NotOwned, await license.RestoreAsync());
            backend.License = () => new StoreLicenseInfo(true, true);
            Assert.Equal(PurchaseResult.AlreadyOwned, await license.RestoreAsync());
            ui.Pump();
            Assert.Equal(ProState.Pro, license.State);
        }
    }

    [Fact]
    public void A_license_change_from_the_store_refreshes_on_the_ui_thread()
    {
        var (license, backend, _, ui, _) = New(new ProSnapshot(T0.AddDays(-10), false));
        using (license)
        {
            backend.License = () => new StoreLicenseInfo(true, true);   // comprado en otro equipo
            backend.RaiseChanged();                                     // llega en cualquier hilo
            Assert.Equal(0, backend.LicenseCalls);
            ui.Pump();
            Assert.Equal(1, backend.LicenseCalls);
            ui.Pump();
            Assert.Equal(ProState.Pro, license.State);
        }
    }

    [Fact]
    public async Task The_price_comes_from_the_store_and_nothing_fires_after_dispose()
    {
        var (license, backend, _, ui, changes) = New(new ProSnapshot(T0, false));
        await license.RefreshAsync();
        ui.Pump();
        Assert.Equal("9,99 €", license.Price);
        int before = changes();
        license.Dispose();
        Assert.True(backend.Disposed);
        Assert.False(backend.HasListeners);
        backend.RaiseChanged();
        license.Recompute();
        ui.Pump();
        Assert.Equal(before, changes());
        Assert.Equal(PurchaseResult.Failed, await license.PurchaseAsync());
    }

    [Fact]
    public void Store_answers_map_to_purchase_results()
    {
        Assert.Equal(PurchaseResult.Purchased, WindowsStoreBackend.Map(StorePurchaseStatus.Succeeded));
        Assert.Equal(PurchaseResult.AlreadyOwned, WindowsStoreBackend.Map(StorePurchaseStatus.AlreadyPurchased));
        Assert.Equal(PurchaseResult.Cancelled, WindowsStoreBackend.Map(StorePurchaseStatus.NotPurchased));
        Assert.Equal(PurchaseResult.NetworkError, WindowsStoreBackend.Map(StorePurchaseStatus.NetworkError));
        Assert.Equal(PurchaseResult.Failed, WindowsStoreBackend.Map(StorePurchaseStatus.ServerError));
        Assert.Equal(PurchaseResult.Failed, WindowsStoreBackend.Map((StorePurchaseStatus)77));
    }

    [Fact]
    public void The_pro_add_on_is_found_by_its_token_and_the_store_link_by_its_id()
    {
        Assert.True(ProCatalog.IsProAddOn("Pro"));
        Assert.True(ProCatalog.IsProAddOn(" pro "));
        Assert.False(ProCatalog.IsProAddOn("ProPlus"));
        Assert.False(ProCatalog.IsProAddOn(null));
        Assert.Equal("ms-windows-store://pdp/?productid=" + ProCatalog.StoreProductId, ProCatalog.StoreUrl(null));
        Assert.Equal("ms-windows-store://pdp/?productid=9NABC", ProCatalog.StoreUrl(" 9NABC "));
        Assert.Equal("ms-windows-store://pdp/?productid=a%26b", ProCatalog.StoreUrl("a&b"));
    }

    [Fact]
    public void The_config_store_keeps_trial_start_and_last_known_state()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cb-pro-{Guid.NewGuid():N}.json");
        try
        {
            new AppConfig { Language = "es" }.Save(path);
            var store = new ConfigProStateStore(() => AppConfig.Load(path), change =>
            {
                var c = AppConfig.Load(path);
                change(c);
                c.Save(path);
            });
            Assert.Equal(default, store.Load());
            store.SaveTrialStart(T0);
            Assert.False(store.Load().LastKnownPro);
            store.SaveLastKnownPro(true);
            var back = store.Load();
            Assert.Equal(T0, back.TrialStartUtc);
            Assert.Equal(DateTimeKind.Utc, back.TrialStartUtc!.Value.Kind);
            Assert.True(back.LastKnownPro);
            Assert.True(back.EverOwnedPro);
            store.SaveLastKnownPro(false);   // reembolso: que la hubo se queda
            Assert.Equal(new ProSnapshot(T0, false, true), store.Load());
            Assert.Equal("es", AppConfig.Load(path).Language);   // el resto de la config no se toca
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Package_identity_is_read_from_the_win32_answer()
    {
        Assert.True(PackageIdentity.Interpret(PackageIdentity.ErrorInsufficientBuffer));
        Assert.False(PackageIdentity.Interpret(PackageIdentity.AppModelErrorNoPackage));
        Assert.False(PackageIdentity.Interpret(0));
        Assert.False(PackageIdentity.HasIdentity());   // los tests corren sin paquete
    }
}
