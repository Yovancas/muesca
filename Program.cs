using System.Text;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.Services.Motion;
using ClaudeBarWin.Services.Packaging;
using ClaudeBarWin.Services.Updates;
using ClaudeBarWin.UI;
using Velopack;
using MonitorCatalog = ClaudeBarWin.Services.Notch.Platform.MonitorCatalog;
using NotchNative = ClaudeBarWin.UI.Notch.NotchNative;

namespace ClaudeBarWin;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack, lo primero de todo (también antes de los modos por línea de comandos): sus hooks de instalar,
        // actualizar y desinstalar entran por aquí y cierran el proceso sin llegar a nada más (VelopackStartup). Lo suyo nunca
        // impide arrancar la app (una build de desarrollo, un localizador que falla…), y sus avisos van a claudebar.log
        // también aquí, antes de que el arranque normal lo encienda. En la edición de la Store (MSIX) no: la instala y la
        // actualiza la Store.
        bool store = StoreEdition.IsActive;
        if (!store)
        {
            bool logWas = AppLog.Enabled;
            AppLog.Enabled = true;
            try { VelopackStartup.Configure(VelopackApp.Build(), args).Run(); }
            catch (Exception ex) { AppLog.Error("velopack: fallo al arrancar", ex); }
            finally { AppLog.Enabled = logWas; }
        }
        // «Iniciar con Windows» arranca con --autostart (el lanzador estable lo reenvía): es un arranque normal. En la Store
        // lo lanza la tarea de inicio del paquete, sin argumentos.
        StartupManager.StartedWithWindows = store
            ? StoreStartupTask.LaunchedAtLogon()
            : args.Any(a => string.Equals(a, AppLocation.AutostartArg, StringComparison.OrdinalIgnoreCase));

        if (args.Contains("--report"))
        {
            RunReport().GetAwaiter().GetResult();
            return;
        }

        if (args.Contains("--render-test"))
        {
            ApplicationConfiguration.Initialize();
            RunRenderTest().GetAwaiter().GetResult();
            return;
        }

        if (args.Contains("--dump-menu"))
        {
            Console.WriteLine(TrayAppContext.DescribeMenu());
            return;
        }

        if (args.Contains("--db-test"))
        {
            RunDbTest();
            return;
        }

        if (args.Contains("--render-demo"))
        {
            ApplicationConfiguration.Initialize();
            RunRenderDemo();
            return;
        }

        if (args.Contains("--render-gif"))
        {
            ApplicationConfiguration.Initialize();
            RunRenderGif();
            return;
        }

        if (args.Contains("--notify-demo"))
        {
            ApplicationConfiguration.Initialize();
            RunNotifyDemo();
            return;
        }

        if (args.Contains("--uninstall-hooks"))
        {
            // Lo llama el desinstalador de Inno ([UninstallRun]; con Velopack es su hook de desinstalación). Sin UI; nunca
            // debe fallar la desinstalación. El acceso directo de inicio lo borra el propio Inno.
            UninstallCleanup.Run(removeStartupShortcut: false);
            return;
        }

        int setupAt = Array.IndexOf(args, "--apply-setup");
        if (setupAt >= 0)
        {
            // Lo llama el asistente de instalación (Inno [Run]) tras copiar los archivos y antes de lanzar la app. Sin UI
            // y sin el mutex de instancia única: aplica el perfil elegido a config.json y sale.
            Environment.ExitCode = RunApplySetup(setupAt + 1 < args.Length ? args[setupAt + 1] : null);
            return;
        }

        if (args.Contains("--hook-test"))
        {
            RunHookTest();
            return;
        }

        if (args.Contains("--tap-test"))
        {
            RunTapTest();
            return;
        }

        // v0.4 (para scripts/tap-e2e.ps1): imprime el comando de statusline que la app escribiría para
        // <helper> y <GitBash|PowerShell> delante de <original>, con el MISMO código que usa el interruptor.
        int wrapAt = Array.IndexOf(args, "--tap-wrap");
        if (wrapAt >= 0 && args.Length >= wrapAt + 4)
        {
            var shell = Enum.Parse<ClaudeShell>(args[wrapAt + 2], ignoreCase: true);
            var src = new System.Text.Json.Nodes.JsonObject
            {
                ["statusLine"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "command", ["command"] = args[wrapAt + 3] },
            }.ToJsonString();
            var wrapped = StatuslineInstaller.Enable(src, args[wrapAt + 1], shell);
            Console.Out.Write(System.Text.Json.Nodes.JsonNode.Parse(wrapped)!["statusLine"]!["command"]!.GetValue<string>());
            return;
        }

        ApplicationConfiguration.Initialize();

        using var mutex = new Mutex(initiallyOwned: true, VelopackStartup.SingleInstanceMutex, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(TrayAppContext.ShowSignalName);
                ev.Set();
            }
            catch { }
            return;
        }

        // v0.3.10: log de diagnóstico + manejadores globales. Antes no había ninguno: una excepción en
        // un handler async void o en un hilo de fondo mataba el proceso sin dejar rastro, y el icono
        // simplemente desaparecía de la bandeja.
        AppLog.Enabled = true;
        AppLog.Info($"arranque v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version} pid={Environment.ProcessId}" +
                    (StartupManager.StartedWithWindows ? " (inicio con Windows)" : "") +
                    (VelopackStartup.UpdatedTo is { } updated ? $" (recién actualizada a la v{updated})" : ""));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => AppLog.Error("excepción no controlada en el hilo de UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Error($"excepción no controlada (terminating={e.IsTerminating})", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("excepción de Task no observada", e.Exception);
            e.SetObserved();
        };

        using var ctx = new TrayAppContext();
        Application.Run(ctx);
        AppLog.Info("salida normal");
    }

    /// <summary>
    /// <c>--apply-setup &lt;ruta.json&gt;</c>: lo elegido en el asistente de instalación sobre config.json
    /// (<see cref="SetupApply"/>). Los hooks, con el mismo <see cref="HookInstaller"/> que el menú de la bandeja; el inicio con
    /// Windows, con <see cref="StartupManager"/>. Devuelve el código de salida de <see cref="SetupOutcome.ExitCode"/>.
    /// </summary>
    private static int RunApplySetup(string? path)
    {
        AppLog.Enabled = true;
        if (string.IsNullOrWhiteSpace(path))
        {
            AppLog.Warn("asistente: --apply-setup sin ruta del JSON");
            return 2;
        }
        // PerMonitorV2 (como la app): los monitores en px físicos, igual que los ve el notch.
        ApplicationConfiguration.Initialize();

        var effects = new SetupEffects(
            HooksInstalled: HookInstaller.IsInstalled,
            InstallHooks: permissions => HookInstaller.Install(HookScript.Contents(),
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), HelperDeployer.Ensure(), permissions),
            SetStartWithWindows: on => { if (on) StartupManager.Enable(); else StartupManager.Disable(); });
        var outcome = SetupApply.Run(path, AppConfig.ConfigPath, DetectSetupMachine(), effects, DateTime.UtcNow);
        StartupManager.WaitForPendingChange(TimeSpan.FromSeconds(12));   // Store: la tarea de inicio cambia en segundo plano

        var detail = outcome.Changed.Count == 0 ? "sin cambios" : "cambia " + string.Join(", ", outcome.Changed);
        if (outcome.Status == SetupStatus.Applied)
            AppLog.Info($"asistente: {outcome.Message}; {detail}" +
                        (outcome.ConfigBackup is { } b ? $"; copia en {b}" : "") +
                        (outcome.HooksInstalled ? "; sesiones en vivo instaladas" : ""));
        else
            AppLog.Warn($"asistente: no se aplicó nada ({outcome.Status}): {outcome.Message}");
        foreach (var ignored in outcome.Ignored) AppLog.Warn($"asistente: ignorado {ignored}");
        if (outcome.HooksError is { } err) AppLog.Warn($"asistente: no se pudieron instalar las sesiones en vivo: {err}");
        return outcome.ExitCode;
    }

    /// <summary>Monitores (con su DPI efectivo) y si Claude Code está instalado, para las recomendaciones del asistente.</summary>
    private static SetupMachine DetectSetupMachine()
    {
        var monitors = new List<SetupMonitor>();
        try
        {
            foreach (var m in MonitorCatalog.Current())
            {
                int dpi = 96;
                try
                {
                    var center = new NotchNative.POINT { X = m.Bounds.Left + m.Bounds.Width / 2, Y = m.Bounds.Top + m.Bounds.Height / 2 };
                    var hmon = NotchNative.MonitorFromPoint(center, NotchNative.MONITOR_DEFAULTTONEAREST);
                    if (hmon != IntPtr.Zero && NotchNative.GetDpiForMonitor(hmon, NotchNative.MDT_EFFECTIVE_DPI,
                            out uint dx, out _) == 0 && dx > 0)
                        dpi = (int)dx;
                }
                catch { /* sin shcore: 96 */ }
                monitors.Add(new SetupMonitor(m.DeviceName, m.Bounds, m.IsPrimary, dpi));
            }
        }
        catch (Exception ex) { AppLog.Warn($"asistente: no se pudieron leer los monitores: {ex.Message}"); }
        return new SetupMachine(monitors, Directory.Exists(ClaudePaths.ConfigDir));
    }

    private static void RunDbTest()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "claudebar-dbtest.db");
        try { File.Delete(tmp); } catch { }
        var store = new UsageHistoryStore(tmp);
        var u = new RealUsage
        {
            FiveHour = new UsageWindow(42, DateTimeOffset.UtcNow.AddHours(2)),
            SevenDay = new UsageWindow(25, DateTimeOffset.UtcNow.AddDays(3))
        };
        store.Append(u, DateTime.UtcNow);
        int rows = store.Count();
        var pts = store.QueryPercent(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5), 100);
        string line = $"DB_TEST ok rows={rows} points={pts.Count} " +
            $"5h={pts.FirstOrDefault()?.FivePct} 7d={pts.FirstOrDefault()?.SevenPct}";
        Console.WriteLine(line);
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "claudebar-dbtest.txt"), line); } catch { }
        try { File.Delete(tmp); } catch { }
    }

    /// <summary>
    /// v0.4 — diagnóstico del enganche de la statusline, SIN llamar a la API: helper desplegado, shell que usará
    /// Claude Code, comando que se escribiría, estado en settings.json, snapshots y la cuota fusionada solo con ellos.
    /// </summary>
    private static void RunTapTest()
    {
        var now = DateTime.UtcNow;
        var helper = HelperDeployer.Ensure();
        var shell = ClaudeShellDetector.Detect();
        Console.WriteLine($"helper:      {helper ?? "(no disponible)"}");
        Console.WriteLine($"shell:       {shell}");
        if (helper is not null)
            Console.WriteLine($"comando:     {StatuslineInstaller.TeeCommand(helper, shell, "<tu statusline>")}");
        Console.WriteLine($"settings:    {ClaudeSettingsFile.SettingsPath}");
        try
        {
            var json = File.Exists(ClaudeSettingsFile.SettingsPath) ? File.ReadAllText(ClaudeSettingsFile.SettingsPath) : "{}";
            Console.WriteLine($"enganche:    {StatuslineInstaller.State(json)}");
        }
        catch (Exception ex) { Console.WriteLine($"enganche:    error leyendo settings.json ({ex.Message})"); }

        var store = new RateLimitSnapshotStore(RateLimitSnapshotStore.DefaultDir);
        var snaps = store.ReadAll(now);
        Console.WriteLine($"snapshots:   {snaps.Count} en {store.Dir}");
        foreach (var s in snaps.OrderByDescending(s => s.SeenUtc))
            Console.WriteLine($"  {s.SessionId,-40} visto hace {(int)(now - s.SeenUtc).TotalSeconds} s · " +
                              string.Join(" · ", s.Windows.Select(w => $"{w.Key} {w.Value.Pct:0.#}%")));
        var merged = QuotaMerger.Merge(snaps, null, DateTime.MinValue, now);
        Console.WriteLine(merged is null
            ? "fusión:      sin datos"
            : $"fusión:      5h {merged.Usage.FiveHour?.UtilizationPct:0.#}% · 7d {merged.Usage.SevenDay?.UtilizationPct:0.#}% " +
              $"(visto hace {(int)(now - merged.ObservedUtc).TotalSeconds} s, fresca: {QuotaPolicy.IsFresh(merged.ObservedUtc, now)})");
    }

    private static void RunHookTest()
    {
        // Requiere una instancia de ClaudeBar corriendo con sesiones en vivo activadas.
        var seq = new (string ev, string status, string tool)[]
        {
            ("SessionStart", "starting", ""),
            ("PreToolUse", "running_tool", "Bash"),
            ("PermissionRequest", "waiting_for_approval", "Write"),
            ("PostToolUse", "processing", "Write"),
            ("Stop", "waiting_for_input", ""),
        };
        foreach (var (ev, status, tool) in seq)
        {
            var json = $"{{\"session_id\":\"hook-test\",\"cwd\":\"C:\\\\Users\\\\zorro\\\\Proyectos\\\\demo\",\"pid\":{Environment.ProcessId},\"event\":\"{ev}\",\"status\":\"{status}\",\"tool\":\"{tool}\",\"ts\":0}}";
            try
            {
                using var c = new System.IO.Pipes.NamedPipeClientStream(".", "claudebar", System.IO.Pipes.PipeDirection.Out);
                c.Connect(500);
                using var w = new StreamWriter(c) { AutoFlush = true };
                w.WriteLine(json);
                Console.WriteLine($"sent {ev}/{status}");
            }
            catch (Exception ex) { Console.WriteLine($"FAIL {ev}: {ex.Message} (¿está Muesca abierta con las sesiones en vivo activadas?)"); }
            System.Threading.Thread.Sleep(1500);
        }
    }

    private static async Task<AppSnapshot> BuildSnapshotAsync(AppConfig cfg)
    {
        var api = new UsageApiClient();
        var parser = new TranscriptParser();
        var now = DateTime.UtcNow;

        UsageResult result;
        var usageAt = now;
        if (cfg.QuotaOAuth)
            result = await api.FetchAsync(cfg.QuotaCliRefresh);
        else
        {
            // Lectura con la sesión de Claude Code apagada: solo la statusline, como en la app.
            var snaps = QuotaPolicy.UsableSnapshots(StatuslineInstaller.IsInstalled(),
                new RateLimitSnapshotStore(RateLimitSnapshotStore.DefaultDir).ReadAll(now));
            DateTime? seen = snaps.Count > 0 ? snaps.Max(sn => sn.SeenUtc) : null;
            var merged = QuotaMerger.Merge(snaps, null, DateTime.MinValue, now);
            bool live = merged is { FromStatusline: true } && QuotaPolicy.IsFresh(seen, now);
            result = new UsageResult
            {
                State = QuotaPolicy.EffectiveState(oauthEnabled: false, live, UsageFetchState.StatuslineOnly),
                Usage = merged?.Usage,
            };
            if (merged is not null) usageAt = merged.ObservedUtc;
        }
        WindowStats? spend = null;
        if (cfg.ShowSpendEstimate)
        {
            var window = TimeSpan.FromDays(cfg.SpendWindowDays);
            spend = UsageAggregator.Build(parser.Read(now - window), now, window, window).Week;
        }
        HealthStatus? health = cfg.ShowHealth ? await StatusClient.FetchAsync() : null;

        PaceResult? paceFive = null, paceSeven = null;
        if (result.Usage is not null)
        {
            var recent = new UsageHistoryStore().RecentForRate(now.AddHours(-6));
            (paceFive, paceSeven) = PaceCalculator.Compute(result.Usage, recent, now);
        }

        return new AppSnapshot
        {
            Usage = result.Usage,
            LatestState = result.State,
            UsageAtUtc = usageAt,
            Spend = spend,
            SpendDays = cfg.SpendWindowDays,
            Health = health,
            PaceFive = paceFive,
            PaceSeven = paceSeven
        };
    }

    private static async Task RunReport()
    {
        var cfg = AppConfig.Load();
        // Plan real (API de perfil, con la caché/credenciales de respaldo), igual que la app. Con la sesión de Claude Code
        // apagada, ni se abre .credentials.json: plan.json o «Plan desconocido».
        var planResolver = new PlanResolver(new UsageApiClient(), cfg.QuotaOAuth);
        await planResolver.RefreshAsync(cfg.QuotaOAuth);
        var plan = planResolver.Current;
        var snap = await BuildSnapshotAsync(cfg);

        var sb = new StringBuilder();
        sb.AppendLine($"=== {Brand.Name} report ===");
        sb.AppendLine($"{plan.Display}  (plan: {planResolver.Source})");
        sb.AppendLine($"State  : {snap.LatestState}");
        sb.AppendLine($"Quota  : {(cfg.QuotaOAuth ? "Claude Code session (unofficial OAuth API) + status line" : "status line only")}"
            + (cfg.QuotaOAuth && cfg.QuotaCliRefresh ? " · CLI refresh on" : ""));
        if (snap.Usage is { } u)
        {
            sb.AppendLine();
            sb.AppendLine($"5h  : {Bar(u.FiveHour)}");
            sb.AppendLine($"7d  : {Bar(u.SevenDay)}");
            if (u.SevenDayOpus is not null) sb.AppendLine($"7d Opus  : {Bar(u.SevenDayOpus)}");
            if (u.SevenDaySonnet is not null) sb.AppendLine($"7d Sonnet: {Bar(u.SevenDaySonnet)}");
            sb.AppendLine($"extra usage enabled: {u.ExtraUsageEnabled}");
        }
        sb.AppendLine();
        sb.AppendLine($"-- pace --");
        AppendPace(sb, "5h", snap.PaceFive);
        AppendPace(sb, "7d", snap.PaceSeven);
        if (snap.Spend is { } s)
        {
            sb.AppendLine();
            sb.AppendLine($"-- estimated spend ({snap.SpendDays}d, API-equiv) --");
            sb.AppendLine($"  total: ${s.CostUsd:0.00}  ({s.Messages} turns)");
            foreach (var kv in s.CostByModel.OrderByDescending(k => k.Value))
                // T13b: familia sin tarifa en el catálogo → "—" (no mentir con $0.00), como en la UI.
                sb.AppendLine($"     {kv.Key,-7}: " + (s.IsUnpriced(kv.Key) ? "— (no rate)" : $"${kv.Value:0.00}"));
        }

        var text = sb.ToString();
        Console.WriteLine(text);
        try
        {
            var outPath = Path.Combine(Path.GetTempPath(), "claudebar-report.txt");
            File.WriteAllText(outPath, text);
            Console.WriteLine($"(written to {outPath})");
        }
        catch { }
    }

    private static void AppendPace(StringBuilder sb, string win, PaceResult? p)
    {
        if (p is null) { sb.AppendLine($"  {win}: —"); return; }
        string eta = p.EtaUtc is { } e ? e.ToLocalTime().ToString("ddd HH:mm") : "—";
        sb.AppendLine($"  {win}: ritmo {p.PaceRatio * 100:0}% · eta {eta} · " +
            $"exhausts_before_reset={p.ExhaustsBeforeReset} · {p.Status}");
    }

    private static string Bar(UsageWindow? w)
    {
        if (w is null) return "—";
        string cd = UsageFormat.Countdown(w.ResetsAt, "resetting…");
        return cd.Length > 0 ? $"{w.UtilizationPct:0.#}%  (resets in {cd})" : $"{w.UtilizationPct:0.#}%";
    }

    /// <summary>Fires the four milestone notifications in sequence (🟢→🔴) so the
    /// user can see how they escalate, then exits. Diagnostic / demo only.</summary>
    private static void RunNotifyDemo()
    {
        var trayBadges = new (int pct, string dot, ToolTipIcon icon, UsageStatus st)[]
        {
            (25, "🟢", ToolTipIcon.Info, UsageStatus.Ok),
            (50, "🟡", ToolTipIcon.Warning, UsageStatus.Ok),
            (75, "🟠", ToolTipIcon.Warning, UsageStatus.Warn),
            (95, "🔴", ToolTipIcon.Error, UsageStatus.Critical)
        };

        var tray = new NotifyIcon { Visible = true, Text = Brand.Name + " demo" };
        Icon? cur = null;
        var ctx = new ApplicationContext();

        void Show(int idx)
        {
            var s = trayBadges[idx];
            var ic = TrayIconRenderer.Render(s.pct, Theme.StatusColor(Theme.Dark, s.st));
            tray.Icon = ic;
            cur?.Dispose();
            cur = ic;
            tray.BalloonTipIcon = s.icon;
            tray.BalloonTipTitle = $"{s.dot} Claude {s.pct}%+ de cuota usada";
            tray.BalloonTipText = "Demo · 5h 24% · 7d 24%  (Max · Max 5x)";
            tray.ShowBalloonTip(4000);
        }

        int i = 0;
        Show(i++);
        var t = new System.Windows.Forms.Timer { Interval = 4500 };
        t.Tick += (_, _) =>
        {
            if (i < trayBadges.Length) Show(i++);
            else { t.Stop(); tray.Visible = false; tray.Dispose(); cur?.Dispose(); ctx.ExitThread(); }
        };
        t.Start();
        Application.Run(ctx);
    }

    // ---- README screenshots from synthetic demo data (no personal usage) ----

    private static AppSnapshot DemoSnapshot(DateTime now)
    {
        var usage = new RealUsage
        {
            FiveHour = new UsageWindow(62, new DateTimeOffset(now.AddHours(1).AddMinutes(40), TimeSpan.Zero)),
            SevenDay = new UsageWindow(84, new DateTimeOffset(now.AddDays(2).AddHours(6), TimeSpan.Zero)),
            SevenDaySonnet = new UsageWindow(12, new DateTimeOffset(now.AddDays(2), TimeSpan.Zero)),
            ExtraUsageEnabled = false
        };
        // T13a: familias DERIVADAS de ids reales vía ModelFamily.FromId — el demo enseña que una
        // familia nueva (claude-fable-5 → "Fable") aparece sola, sin listas hardcodeadas.
        var spend = new WindowStats { CostUsd = 480.9, Messages = 1234 };
        spend.CostByModel[ModelFamily.FromId("claude-opus-4-8")] = 420.50;
        spend.CostByModel[ModelFamily.FromId("claude-sonnet-4-5")] = 35.20;
        spend.CostByModel[ModelFamily.FromId("claude-fable-5")] = 24.10;
        spend.CostByModel[ModelFamily.FromId("claude-haiku-4-5-20251001")] = 1.10;
        // T13b: familia SIN tarifa en ninguna fuente del catálogo — la fila pinta "— <sin tarifa>"
        // y NO suma al total (no se miente con $0).
        spend.CostByModel[ModelFamily.FromId("claude-mythos-1")] = 0;
        spend.UnpricedModels.Add(ModelFamily.FromId("claude-mythos-1"));

        var paceFive = new PaceResult("5h", 62, 1.30, 47.7,
            new DateTimeOffset(now.AddHours(1).AddMinutes(10), TimeSpan.Zero),
            new DateTimeOffset(now.AddHours(1).AddMinutes(40), TimeSpan.Zero), true, PaceStatus.Critical);
        var paceSeven = new PaceResult("7d", 84, 0.95, 88.4, null,
            new DateTimeOffset(now.AddDays(2).AddHours(6), TimeSpan.Zero), false, PaceStatus.Ok);

        return new AppSnapshot
        {
            Usage = usage, LatestState = UsageFetchState.Ok, UsageAtUtc = now,
            Spend = spend, SpendDays = 7,
            Health = new HealthStatus(HealthLevel.Operational, "All Systems Operational"),
            PaceFive = paceFive, PaceSeven = paceSeven
        };
    }

    private static List<HistoryBucket> DemoBuckets(DateTime now)
    {
        var list = new List<HistoryBucket>();
        var rnd = new Random(7);
        // Los renders de demo/README usan Language="en": etiquetas en en-US, no en la cultura del SO
        // (T2: los screenshots ingleses salían con "lun 18h" en máquinas españolas).
        var ci = Localization.Get("en").Culture;
        for (int i = 0; i < 12; i++)
        {
            double op = 8 + i * 2.6 + 6 * Math.Abs(Math.Sin(i * 0.9)) + rnd.NextDouble() * 4; // rising, peak on the right
            double so = rnd.NextDouble() * 7;
            double ha = rnd.NextDouble() * 1.5;
            double fa = 2 + 3 * Math.Abs(Math.Sin(i * 1.7)); // T13a: familia Fable visible en la pila
            var t = now.AddHours(-5 * (12 - i)).ToLocalTime();
            list.Add(new HistoryBucket(t, t.ToString("ddd HH'h'", ci), new Dictionary<string, double>
            {
                [ModelFamily.FromId("claude-opus-4-8")] = op,
                [ModelFamily.FromId("claude-sonnet-4-5")] = so,
                [ModelFamily.FromId("claude-haiku-4-5-20251001")] = ha,
                [ModelFamily.FromId("claude-fable-5")] = fa
            }));
        }
        return list;
    }

    private static List<PctPoint> DemoPct(DateTime now)
    {
        var list = new List<PctPoint>();
        const int n = 80;
        double stepMin = 7.0 * 24 * 60 / n;
        for (int i = 0; i < n; i++)
        {
            var t = now.AddMinutes(-(n - i) * stepMin);
            double seven = 84.0 * i / (n - 1);          // weekly ramp to 84%
            double fiveSaw = (i % 16) / 16.0 * 72;       // 5h sawtooth
            list.Add(new PctPoint(t, fiveSaw, seven));
        }
        return list;
    }

    private static void RunRenderDemo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "claudebar-demo");
        Directory.CreateDirectory(dir);
        var now = DateTime.UtcNow;
        var snap = DemoSnapshot(now);
        var buckets = DemoBuckets(now);
        var pct = DemoPct(now);
        var plan = new PlanInfo("max", "default_claude_max_5x");

        var shots = new (string theme, string mode, ChartRange range, string file)[]
        {
            ("dark", "spend", ChartRange.Hours5, "dashboard-dark.png"),
            ("light", "spend", ChartRange.Hours5, "dashboard-light.png"),
            ("cli", "percent", ChartRange.Week1, "dashboard-cli.png")
        };
        foreach (var (theme, mode, range, file) in shots)
        {
            var cfg = new AppConfig
            {
                Theme = theme, ChartMode = mode, ChartPctWindow = "7d", Language = "en",
                ShowSpendEstimate = true, ShowHealth = true, ShowChart = true
            };
            using var form = new DashboardForm();
            form.PrepareForRender(snap, cfg, plan, buckets, pct, range);
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(Path.Combine(dir, file));
        }

        // Tray icon badges by status.
        var trayBadges = new (int pct, UsageStatus st)[]
        { (42, UsageStatus.Ok), (78, UsageStatus.Warn), (95, UsageStatus.Critical), (130, UsageStatus.Critical) };
        const int scale = 3, pad = 10, icon = 32;
        using (var strip = new Bitmap((icon * scale + pad) * trayBadges.Length + pad, icon * scale + pad * 2))
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(45, 45, 48));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            int x = pad;
            foreach (var s in trayBadges)
            {
                using var ic = TrayIconRenderer.Render(s.pct, Theme.StatusColor(Theme.Dark, s.st));
                using var b = ic.ToBitmap();
                g.DrawImage(b, new Rectangle(x, pad, icon * scale, icon * scale));
                x += icon * scale + pad;
            }
            strip.Save(Path.Combine(dir, "tray-icons.png"));
        }

        Console.WriteLine(dir);
    }

    // ---- GIF frame sequences for the README microinteraction animations (F3) ----
    // Like --render-demo (synthetic data, no personal usage) but instead of single PNGs it dumps
    // NUMBERED frame sequences by sweeping the deterministic RenderMotionOverride across the animation
    // timeline. ffmpeg then assembles each folder into a looping .gif (see build-gifs in the README).
    //   apertura/  — panel open: fade + staggered section entry + bar/number tween, tSinceOpen 0→440 ms.
    //   mascota/   — mascot life (Processing): braille spinner + blink jitter + playful verb, elapsed 0→2700 ms.
    //   hover/     — hover highlight fading in over the Quota section, intensity 0→1.
    //   celebracion/ — reset celebration flash ("✓ quota renewed") + Happy mood pulsing in.
    // Frames go to %TEMP%\claudebar-gif\<seq>\frame_###.png; only the FINAL .gif belongs in assets/.
    private static void RunRenderGif()
    {
        var root = Path.Combine(Path.GetTempPath(), "claudebar-gif");
        var now = DateTime.UtcNow;
        var snap = DemoSnapshot(now);
        var buckets = DemoBuckets(now);
        var pct = DemoPct(now);
        var plan = new PlanInfo("max", "default_claude_max_5x");

        // Config de marketing: tema oscuro, mascota viva, Spend+Chart desplegados (panel lleno).
        AppConfig Cfg() => new()
        {
            Theme = "dark", ChartMode = "spend", ChartPctWindow = "7d", Language = "en",
            ShowSpendEstimate = true, ShowHealth = true, ShowChart = true,
            LiveSessionsEnabled = true, ShowMascot = true,
            CollapsedSpend = false, CollapsedChart = false,
        };

        var processing = new LiveSessionsView { GlobalPhase = SessionPhase.Processing, ActiveCount = 1 };

        // Renderiza UN fotograma a PNG. Si fadeAlpha < 1, mezcla el contenido contra el fondo del tema
        // para que el fade de apertura SE VEA (DrawToBitmap captura el contenido pero no el Opacity de la
        // ventana en capas). devuelve el tamaño del panel para reusarlo entre fotogramas de la misma seq.
        Size Shot(string seqDir, int idx, AppConfig cfg, DashboardForm.RenderMotionOverride mo,
                  LiveSessionsView live, double fadeAlpha)
        {
            using var form = new DashboardForm();
            form.PrepareForRender(snap, cfg, plan, buckets, pct, ChartRange.Hours5, mo, live);
            int fw = form.Width, fh = form.Height;
            using var content = new Bitmap(fw, fh);
            form.DrawToBitmap(content, new Rectangle(0, 0, fw, fh));

            var file = Path.Combine(seqDir, $"frame_{idx:000}.png");
            if (fadeAlpha >= 0.999)
            {
                content.Save(file);
            }
            else
            {
                // Fade real: lienzo con el fondo del tema + el contenido a alfa = fadeAlpha encima.
                using var canvas = new Bitmap(fw, fh);
                using var g = Graphics.FromImage(canvas);
                g.Clear(form.BackColor);
                var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = (float)Math.Clamp(fadeAlpha, 0, 1) };
                using var ia = new System.Drawing.Imaging.ImageAttributes();
                ia.SetColorMatrix(cm);
                g.DrawImage(content, new Rectangle(0, 0, fw, fh), 0, 0, fw, fh, GraphicsUnit.Pixel, ia);
                canvas.Save(file);
            }
            return new Size(fw, fh);
        }

        string Seq(string name)
        {
            var d = Path.Combine(root, name);
            if (Directory.Exists(d)) Directory.Delete(d, true);
            Directory.CreateDirectory(d);
            return d;
        }

        // 1) APERTURA: fade (muelle crítico) + stagger de secciones + muelles de barras/números.
        //    Barrido tSinceOpen 0→700 ms (cubre el asentamiento de los muelles y el stagger de todas las
        //    secciones) + unos fotogramas de "hold" en el estado asentado para que el loop respire antes de reabrir.
        {
            var dir = Seq("apertura");
            int i = 0;
            const double endMs = 700, stepMs = 20;
            for (double t = 0; t <= endMs + 0.001; t += stepMs)
            {
                double fade = MotionState.SpringProgressAt(t);
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(TSinceOpenMs: t, MascotElapsedMs: t), processing, fade);
            }
            for (int h = 0; h < 12; h++) // hold asentado
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(TSinceOpenMs: endMs, MascotElapsedMs: endMs), processing, 1.0);
            Console.WriteLine($"apertura: {i} frames -> {dir}");
        }

        // 2) MASCOTA: vida en Processing. tSinceOpen alto (panel asentado) y barrido de MascotElapsedMs
        //    0→2700 ms = 3 ciclos completos del spinner (90 ms × 10 glifos) ⇒ LOOP perfecto del braille.
        {
            var dir = Seq("mascota");
            int i = 0;
            const double endMs = 2700, stepMs = 30; // 90 fotogramas, 30 ms/frame
            for (double t = 0; t < endMs - 0.001; t += stepMs)
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(
                        TSinceOpenMs: 4000, MascotElapsedMs: t, MascotMood: Mood.Focused), processing, 1.0);
            Console.WriteLine($"mascota: {i} frames -> {dir}");
        }

        // 3) HOVER: realce de la sección de Cuota apareciendo. Barrido manual de la intensidad del
        //    fade-in (OutQuad sobre FadeMs) reusando el override de hover: pasamos por una rampa de
        //    fotogramas con la clave activa y, para el inicio "sin realce", la clave a null.
        {
            var dir = Seq("hover");
            int i = 0;
            // Antes del hover: panel asentado, sin realce.
            for (int h = 0; h < 6; h++)
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(TSinceOpenMs: 4000, MascotElapsedMs: 4000), processing, 1.0);
            // Hover ON (el override fuerza intensidad plena); mantenemos unos fotogramas para que se lea.
            for (int h = 0; h < 18; h++)
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(
                        TSinceOpenMs: 4000, HoveredKey: "sec:quota", MascotElapsedMs: 4000 + h * 30), processing, 1.0);
            Console.WriteLine($"hover: {i} frames -> {dir}");
        }

        // 4) CELEBRACIÓN: destello "✓ cuota renovada" + humor Happy. Barrido corto de MascotElapsed para
        //    que la mascota siga viva durante el destello; loop tras unos fotogramas de hold.
        {
            var dir = Seq("celebracion");
            int i = 0;
            for (int h = 0; h < 30; h++)
                Shot(dir, i++, Cfg(),
                    new DashboardForm.RenderMotionOverride(
                        TSinceOpenMs: 4000, MascotElapsedMs: h * 30, MascotMood: Mood.Happy, Celebrating: true),
                    processing, 1.0);
            Console.WriteLine($"celebracion: {i} frames -> {dir}");
        }

        // 5) AJUSTES: el panel de ajustes haciendo SCROLL (feature v0.3.7: el alto se limita al 65% de la
        //    pantalla y el contenido rueda). Loop elegante: hold arriba → barrido eased al scroll MÁXIMO →
        //    hold abajo → barrido eased de vuelta arriba. ~30 ms/frame. A diferencia de las 4 secuencias de
        //    motion (que inyectan RenderMotionOverride), aquí conducimos el scroll real del form vía los
        //    ganchos internal SetSettingsScrollForRender/SettingsMaxScrollForRender.
        {
            var dir = Seq("ajustes");
            int i = 0;

            // Un único form reutilizado por toda la secuencia: ShowSettings() resetea el scroll a 0 UNA vez,
            // y un primer DrawToBitmap puebla _settingsContentH/_settingsViewportTop para conocer el overflow.
            using var form = new DashboardForm();
            form.PrepareForRender(snap, Cfg(), plan, buckets, pct, ChartRange.Hours5);
            form.ShowSettings(); // entra en modo ajustes y deja el scroll en 0 (NO volver a llamarlo en el bucle)
            int fw = form.Width, fh = form.Height;

            // Render de UN fotograma del panel de ajustes al scroll px indicado. Acota a [0, overflow] y
            // captura. Devuelve el overflow vigente para el barrido (válido ya tras el 1er DrawToBitmap).
            int ShotSettings(int scrollPx)
            {
                form.SetSettingsScrollForRender(scrollPx);
                using var bmp = new Bitmap(fw, fh);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, fw, fh));
                bmp.Save(Path.Combine(dir, $"frame_{i++:000}.png"));
                return form.SettingsMaxScrollForRender;
            }

            // Fotograma 0 en el tope arriba: además de ser el primer hold, puebla los campos del viewport
            // para que SettingsMaxScrollForRender devuelva el overflow real del contenido.
            int maxScroll = ShotSettings(0);

            const int holdFrames = 10;     // ~10 frames de hold arriba/abajo (a 30 ms ≈ 300 ms de pausa)
            const int sweepFrames = 30;    // tramo de barrido (eased): 30 frames ≈ 900 ms por sentido

            // Hold arriba (ya pintamos el frame 0, faltan holdFrames-1 para completar el respiro inicial).
            for (int h = 1; h < holdFrames; h++) ShotSettings(0);

            if (maxScroll > 0)
            {
                // Barrido suave ARRIBA→ABAJO con ease-in-out (arranca y frena lento, rápido en el medio):
                // InOutQuad no existe en Easing, así que usamos InOutCubic (misma familia simétrica, gate F3).
                for (int k = 1; k <= sweepFrames; k++)
                {
                    double e = Easing.InOutCubic((double)k / sweepFrames);
                    ShotSettings((int)Math.Round(e * maxScroll));
                }
                // Hold abajo (en el scroll máximo) para que se lea el final del panel.
                for (int h = 0; h < holdFrames; h++) ShotSettings(maxScroll);
                // Barrido de vuelta ABAJO→ARRIBA (mismo easing) para cerrar el loop limpiamente.
                for (int k = 1; k <= sweepFrames; k++)
                {
                    double e = Easing.InOutCubic((double)k / sweepFrames);
                    ShotSettings((int)Math.Round((1.0 - e) * maxScroll));
                }
            }
            Console.WriteLine($"ajustes: {i} frames (overflow {maxScroll}px) -> {dir}");
        }

        // 6) BANDEJA: ciclo del icono de bandeja. El badge del % sube 5→99 con el degradado verde→ámbar→rojo
        //    y las FORMAS de accesibilidad apareciendo (triángulo al cruzar warn 70, rombo al cruzar critical
        //    90); al final el punto ámbar de atención parpadea 3 veces (independiente del color de cuota). UN
        //    solo icono grande centrado en un lienzo cuadrado con fondo de barra de tareas oscura.
        {
            var dir = Seq("bandeja");
            int i = 0;

            const double warn = 70, crit = 90;          // mismos umbrales por defecto que usa el dashboard
            const int canvas = 180;                     // lienzo cuadrado
            const int iconNative = 48;                  // tamaño nativo del badge
            const int iconDraw = iconNative * 3;        // 48px ×3 = 144px (el icono ocupa el centro)
            const int iconOff = (canvas - iconDraw) / 2; // offset para centrar el icono en el lienzo
            var taskbarBg = Color.FromArgb(42, 42, 42); // gris de barra de tareas oscura (Color.FromArgb(42,42,42))

            // Estado por forma según los umbrales (mismo mapeo que TrayAppContext.StatusFor): el % decide
            // tanto el color (RiskColor) como la forma (Ok→círculo, Warn→triángulo, Critical→rombo).
            UsageStatus StatusForPct(int pct) =>
                pct >= crit ? UsageStatus.Critical : pct >= warn ? UsageStatus.Warn : UsageStatus.Ok;

            // Pinta un icono ya renderizado, centrado y escalado ×3 con HighQualityBicubic (bordes suaves
            // tipo "icono de app ampliado", sin moteado del anti-aliasing magnificado en bloques).
            void ShotIcon(Icon ico)
            {
                using var frame = new Bitmap(canvas, canvas);
                using (var g = Graphics.FromImage(frame))
                {
                    g.Clear(taskbarBg);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    using var b = ico.ToBitmap();
                    g.DrawImage(b, new Rectangle(iconOff, iconOff, iconDraw, iconDraw));
                }
                frame.Save(Path.Combine(dir, $"frame_{i++:000}.png"));
            }

            // a) Rampa 5→99: ~60 frames con easing suave (OutQuad: arranca rápido y frena cerca del 99) para
            //    que el cruce de umbrales (aparición de triángulo/rombo) se aprecie sin prisa al final.
            const int rampFrames = 60;
            for (int k = 0; k < rampFrames; k++)
            {
                double e = Easing.OutQuad((double)k / (rampFrames - 1));
                int pctVal = (int)Math.Round(5 + e * (99 - 5)); // 5 → 99 (renombrado: 'pct' ya es el List<PctPoint> externo)
                using var ico = TrayIconRenderer.Render(pctVal, Theme.Dark, warn, crit, StatusForPct(pctVal));
                ShotIcon(ico);
            }

            // b) Parpadeo del punto ámbar de atención (pending): % FIJO en 18 (verde franco con el degradado)
            //    para que se entienda que el punto es INDEPENDIENTE del color de cuota. 3 ciclos de 4 frames
            //    ON + 4 OFF = 24 frames.
            const int blinkPct = 18;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int on = 0; on < 4; on++) // punto encendido
                {
                    using var ico = TrayIconRenderer.Render(blinkPct, Theme.Dark, warn, crit,
                        StatusForPct(blinkPct), stale: false, pending: true);
                    ShotIcon(ico);
                }
                for (int off = 0; off < 4; off++) // punto apagado
                {
                    using var ico = TrayIconRenderer.Render(blinkPct, Theme.Dark, warn, crit,
                        StatusForPct(blinkPct), stale: false, pending: false);
                    ShotIcon(ico);
                }
            }
            Console.WriteLine($"bandeja: {i} frames -> {dir}");
        }

        Console.WriteLine(root);
    }

    private static async Task RunRenderTest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "claudebar-render");
        Directory.CreateDirectory(dir);

        var trayBadges = new (int pct, UsageStatus st)[]
        {
            (42, UsageStatus.Ok), (78, UsageStatus.Warn), (95, UsageStatus.Critical), (130, UsageStatus.Critical)
        };
        const int scale = 3, pad = 10, icon = 32;
        using (var strip = new Bitmap((icon * scale + pad) * trayBadges.Length + pad, icon * scale + pad * 2))
        using (var g = Graphics.FromImage(strip))
        {
            g.Clear(Color.FromArgb(45, 45, 48));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            int x = pad;
            foreach (var s in trayBadges)
            {
                using var ic = TrayIconRenderer.Render(s.pct, Theme.StatusColor(Theme.Dark, s.st));
                using var bmp = ic.ToBitmap();
                g.DrawImage(bmp, new Rectangle(x, pad, icon * scale, icon * scale));
                x += icon * scale + pad;
            }
            strip.Save(Path.Combine(dir, "icons.png"));
        }

        var cfg = AppConfig.Load();
        var plan = new PlanResolver(new UsageApiClient(), cfg.QuotaOAuth).Current; // sin red: caché del perfil o credenciales
        var snap = await BuildSnapshotAsync(cfg);

        var now = DateTime.UtcNow;
        var records = new TranscriptParser().Read(now - UsageHistory.Lookback(ChartRange.Hours5));
        // Etiquetas del eje X con la cultura del idioma configurado (T2), igual que hará la app real.
        var buckets = UsageHistory.Build(records, ChartRange.Hours5, now, Localization.ForConfig(cfg).Culture);
        var pct = new UsageHistoryStore().QueryPercent(now - UsageHistory.Lookback(ChartRange.Hours5), now, 120);
        using (var form = new DashboardForm())
        {
            form.PrepareForRender(snap, cfg, plan, buckets, pct, ChartRange.Hours5);
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(Path.Combine(dir, "data.png"));

            // Vista de ajustes (mismo form): cambia el modo y reajusta el alto.
            form.ShowSettings();
            using var bmpS = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmpS, new Rectangle(0, 0, form.Width, form.Height));
            bmpS.Save(Path.Combine(dir, "settings.png"));
        }

        // ---- QA T7: tabs de rango en claro, hover de ajustes con clip y chips de hitos con mezcla ----
        // (a) §3 #5: tema claro con la gráfica expandida — las tabs 1H/5H/24H/7D/30D deben leerse como
        //     chips (borde Separator en el inactivo; antes BgElevated puro invisible sobre #FAFAFA).
        {
            var cfgLight = AppConfig.Load();
            cfgLight.Theme = "light";
            cfgLight.CollapsedChart = false;
            using var formC = new DashboardForm();
            formC.PrepareForRender(snap, cfgLight, plan, buckets, pct, ChartRange.Hours5);
            using var bmpC = new Bitmap(formC.Width, formC.Height);
            formC.DrawToBitmap(bmpC, new Rectangle(0, 0, formC.Width, formC.Height));
            bmpC.Save(Path.Combine(dir, "chart-tabs-light.png"));
        }
        // (b)+(c) §3 #6/#8: ajustes con scroll y hover en una fila cortada por el viewport (el realce
        //     HoverBg debe recortarse, no sangrar sobre el chrome) + hitos 25/75 ON y 50/95 OFF para
        //     ver el estado on/off de los chips (el default trae los 4 activos = 4 chips idénticos).
        {
            var cfgT7 = AppConfig.Load();
            cfgT7.NotifyMilestones = new[] { 25, 75 };
            using var formS = new DashboardForm();
            // El hover cae en la fila CORTADA por el borde superior del viewport (toggle:ShowSpend con
            // scroll 64): su realce inflado es el que sangraba sobre el chrome sin el clip.
            formS.PrepareForRender(snap, cfgT7, plan, buckets, pct, ChartRange.Hours5,
                new DashboardForm.RenderMotionOverride(TSinceOpenMs: 4000, HoveredKey: "set:toggle:ShowSpend"));
            formS.ShowSettings();
            formS.SetSettingsScrollForRender(64);
            using var bmpH = new Bitmap(formS.Width, formS.Height);
            // Dos pasadas: la 1ª puebla los rects de ajustes (el realce usa los de la pasada anterior).
            formS.DrawToBitmap(bmpH, new Rectangle(0, 0, formS.Width, formS.Height));
            formS.DrawToBitmap(bmpH, new Rectangle(0, 0, formS.Width, formS.Height));
            bmpH.Save(Path.Combine(dir, "settings-hover-clip.png"));
        }

        // ---- QA T13a: familias dinámicas (claude-fable-5 → "Fable") en filas de gasto + gráfica ----
        // Datos sintéticos del demo (incluyen la familia Fable derivada del id real vía
        // ModelFamily.FromId) con Gasto y Gráfica desplegados: filas y leyenda deben salir de los
        // DATOS, no de una lista fija Opus/Sonnet/Haiku.
        {
            var nowQa = DateTime.UtcNow;
            var cfgF = new AppConfig
            {
                Theme = "dark", ChartMode = "spend", ChartPctWindow = "7d", Language = "en",
                ShowSpendEstimate = true, ShowHealth = true, ShowChart = true,
                CollapsedSpend = false, CollapsedChart = false
            };
            using var formF = new DashboardForm();
            formF.PrepareForRender(DemoSnapshot(nowQa), cfgF, plan, DemoBuckets(nowQa), DemoPct(nowQa), ChartRange.Hours5);
            using var bmpF = new Bitmap(formF.Width, formF.Height);
            formF.DrawToBitmap(bmpF, new Rectangle(0, 0, formF.Width, formF.Height));
            bmpF.Save(Path.Combine(dir, "families-dynamic.png"));
        }

        // ---- QA T13b: toggle "Actualizar tarifas online" (sección SISTEMA, al fondo de ajustes) ----
        // Scroll al máximo para ver el toggle de privacidad del catálogo de tarifas con su subtítulo.
        {
            using var formP = new DashboardForm();
            formP.PrepareForRender(snap, AppConfig.Load(), plan, buckets, pct, ChartRange.Hours5);
            formP.ShowSettings();
            using var bmpP = new Bitmap(formP.Width, formP.Height);
            formP.DrawToBitmap(bmpP, new Rectangle(0, 0, formP.Width, formP.Height)); // 1ª pasada: puebla el overflow
            formP.SetSettingsScrollForRender(formP.SettingsMaxScrollForRender);
            formP.DrawToBitmap(bmpP, new Rectangle(0, 0, formP.Width, formP.Height));
            bmpP.Save(Path.Combine(dir, "settings-system-pricing.png"));
        }

        // Mascota: sesiones en vivo ON para verla en la cabecera.
        cfg.LiveSessionsEnabled = true;
        cfg.ShowMascot = true;
        using (var formL = new DashboardForm())
        {
            formL.PrepareForRender(snap, cfg, plan, buckets, pct, ChartRange.Hours5);
            using var bmpL = new Bitmap(formL.Width, formL.Height);
            formL.DrawToBitmap(bmpL, new Rectangle(0, 0, formL.Width, formL.Height));
            bmpL.Save(Path.Combine(dir, "mascot.png"));
        }

        // ---- Microinteracciones congeladas (Tarea 8): fade+stagger+tween a 0/90/200 ms, hover sobre
        //      una fila, mascota en varias fases/humores, celebración y una pasada reduce-motion. ----
        RenderMotionFrames(dir, snap, plan, buckets, pct);

        // Tira de badges del icono de bandeja para QA visual: matriz barra clara/oscura × 3 formas
        // (Ok/Warn/Critical) × estado fresco/stale, a 48px (nativo) y 16px (tamaño real a 100% DPI).
        // Valida legibilidad y contraste del texto adaptativo + el estado por forma a 16px.
        {
            const double warn = 70, crit = 90;
            // (etiqueta, %, estado, stale). El % elige el color por riesgo; la forma sigue al estado.
            var statuses = new (string label, int pct, UsageStatus st)[]
            {
                ("12%", 12, UsageStatus.Ok),
                ("68%", 68, UsageStatus.Warn),
                ("95%", 95, UsageStatus.Critical),
            };
            // Una columna por (estado × fresco/stale); dos filas por barra (oscura y clara).
            var cols = new List<(string label, int pct, UsageStatus st, bool stale)>();
            foreach (var s in statuses) cols.Add((s.label, s.pct, s.st, false));
            foreach (var s in statuses) cols.Add((s.label + " ·old", s.pct, s.st, true));
            cols.Add(("pend", 45, UsageStatus.Warn, false)); // badge "pending" sigue cubierto
            cols.Add(("99+", 120, UsageStatus.Critical, false));
            cols.Add(("err", 0, UsageStatus.Ok, false));     // marcador de fila de error

            // Swatches desde los tokens (T6b): los MISMOS colores asumidos contra los que el renderer
            // pre-compone el velo stale, así cada fila valida la decisión de contraste de su barra.
            var bars = new (string name, Color bg, bool light)[]
            {
                ("dark taskbar",  TaskbarColors.Dark, false),
                ("light taskbar", TaskbarColors.Light, true),
            };

            const int cell = 64;
            const int rowH = 8 + 48 + 4 + 16 + 22; // 48px nativo + 16px real + etiqueta
            using var strip = new Bitmap(cols.Count * cell, bars.Length * rowH);
            using (var g2 = Graphics.FromImage(strip))
            {
                g2.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                using var lf = new Font("Segoe UI", 8f);
                for (int r = 0; r < bars.Length; r++)
                {
                    int rowY = r * rowH;
                    g2.SetClip(new Rectangle(0, rowY, strip.Width, rowH));
                    g2.Clear(bars[r].bg);
                    Color txt = ColorMath.Contrast(bars[r].bg);
                    using var labelBrush = new SolidBrush(txt);
                    for (int i = 0; i < cols.Count; i++)
                    {
                        var col = cols[i];
                        Icon ico = col.label == "err"
                            ? TrayIconRenderer.RenderError(Theme.Dark.Neutral)
                            : TrayIconRenderer.Render(col.pct, Theme.Dark, warn, crit, col.st, col.stale,
                                pending: col.label == "pend", taskbarLight: bars[r].light);
                        using var bm = ico.ToBitmap();
                        int cx = i * cell;
                        g2.DrawImage(bm, cx + (cell - 48) / 2, rowY + 8, 48, 48);          // 48px nativo
                        g2.DrawImage(bm, cx + (cell - 16) / 2, rowY + 8 + 48 + 4, 16, 16);  // 16px = barra real
                        g2.DrawString(col.label, lf, labelBrush, cx + 4, rowY + 8 + 48 + 4 + 16 + 2);
                        ico.Dispose();
                    }
                }
                g2.ResetClip();
            }
            strip.Save(Path.Combine(dir, "tray-badges.png"));
        }

        Console.WriteLine("rendered data.png + settings.png + mascot.png + tray-badges.png");
        Console.WriteLine("       + chart-tabs-light.png + settings-hover-clip.png + families-dynamic.png");
        Console.WriteLine("       + settings-system-pricing.png");
        Console.WriteLine("       + motion-0/90/200.png + hover.png + mascot-*.png + celebration.png + reduce-motion.png");
        Console.WriteLine(dir);
    }

    /// <summary>
    /// Genera los fotogramas FIJOS de las microinteracciones de F3 (Tarea 8) inyectando un
    /// <see cref="DashboardForm.RenderMotionOverride"/> determinista (sin reloj de UI): fade+stagger+tween
    /// a tSinceOpen = 0/90/200 ms, hover sobre una sección, la mascota en varias fases/humores, un
    /// destello de celebración y una pasada con reduce-motion (== estado final).
    /// </summary>
    private static void RenderMotionFrames(string dir, AppSnapshot snap, PlanInfo plan,
        List<HistoryBucket> buckets, List<PctPoint> pct)
    {
        // Config base con la mascota visible para ver también su vida en los fotogramas.
        AppConfig Base() => new()
        {
            Theme = "dark", ChartMode = "spend", ChartPctWindow = "7d", Language = "en",
            ShowSpendEstimate = true, ShowHealth = true, ShowChart = true,
            LiveSessionsEnabled = true, ShowMascot = true
        };

        void Shot(string file, AppConfig cfg, DashboardForm.RenderMotionOverride? mo, LiveSessionsView? live)
        {
            using var form = new DashboardForm();
            form.PrepareForRender(snap, cfg, plan, buckets, pct, ChartRange.Hours5, mo, live);
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(Path.Combine(dir, file));
        }

        // Mascota procesando (fase animada): spinner + verbo vivos en los tres instantes.
        var processing = new LiveSessionsView { GlobalPhase = SessionPhase.Processing, ActiveCount = 1 };

        // 1) Apertura a 0 / 90 / 200 ms: fade de opacidad + entrada escalonada + tween de barras/números.
        foreach (var (t, file) in new[] { (0.0, "motion-0.png"), (90.0, "motion-90.png"), (200.0, "motion-200.png") })
            Shot(file, Base(), new DashboardForm.RenderMotionOverride(TSinceOpenMs: t, MascotElapsedMs: t), processing);

        // 2) Hover sobre la sección de cuota (realce eased a intensidad plena), ya asentado (t alto).
        Shot("hover.png", Base(),
            new DashboardForm.RenderMotionOverride(TSinceOpenMs: 2000, HoveredKey: "sec:quota", MascotElapsedMs: 2000),
            processing);

        // 3) Mascota en varias fases/humores: animador a un elapsed que muestra spinner/verbo + humor forzado.
        var moods = new (SessionPhase phase, Mood mood, string file)[]
        {
            (SessionPhase.Processing,         Mood.Focused, "mascot-processing.png"),
            (SessionPhase.WaitingForApproval, Mood.Alert,   "mascot-waiting.png"),
            (SessionPhase.Idle,               Mood.Neutral, "mascot-idle.png"),
        };
        foreach (var (phase, mood, file) in moods)
        {
            var live = new LiveSessionsView { GlobalPhase = phase, ActiveCount = phase == SessionPhase.Idle ? 0 : 1 };
            // Bote de atención visible en la fase que pide atención (offset > 0).
            int bounce = phase.NeedsAttention() ? Motion.BounceAmplitudePx : 0;
            Shot(file, Base(),
                new DashboardForm.RenderMotionOverride(
                    TSinceOpenMs: 3000, MascotElapsedMs: 320, MascotMood: mood, MascotBounceOffsetY: bounce),
                live);
        }

        // 4) Celebración de reset (destello "✓ cuota renovada" + humor Happy).
        Shot("celebration.png", Base(),
            new DashboardForm.RenderMotionOverride(
                TSinceOpenMs: 3000, MascotElapsedMs: 200, MascotMood: Mood.Happy, Celebrating: true),
            processing);

        // 5) Reduce-motion ON: el gate único colapsa TODO a su estado final. Con override + ReduceMotion,
        //    fade=1, stagger=0, tween=target, sin spinner/jitter ni hover ⇒ idéntico al estado de hoy.
        var reduced = Base();
        reduced.ReduceMotion = true;
        Shot("reduce-motion.png", reduced,
            new DashboardForm.RenderMotionOverride(TSinceOpenMs: 90, HoveredKey: "sec:quota", MascotElapsedMs: 320, Celebrating: true),
            processing);
    }
}
