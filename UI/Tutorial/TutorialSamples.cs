using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.UI.Tutorial;

/// <summary>
/// Los datos de ejemplo del notch de demostración: sesiones, cuota, avisos, canciones con su letra, tareas, el archivo de la
/// Bandeja, la página Uso y el pomodoro. Inventados y fijos (nunca los del usuario, y nada sale del equipo); las vistas se
/// montan con los MISMOS constructores que usa el notch de verdad (<see cref="UsageText"/>, <see cref="UsageChartBuilder"/>,
/// <see cref="FocusText"/>…). Se crean una vez por idioma: aquí no se formatea nada por frame.
/// </summary>
public sealed class TutorialSamples
{
    /// <summary>El proyecto del Permiso de ejemplo (el de «tu turno» en la lista).</summary>
    public const string PermissionProject = "api";
    public const string PermissionDetail = "Bash: npm test -- --watch=false";
    public const string WorkingProject = "muesca";

    private readonly Strings _s;
    private readonly DateTime _utc0, _local0;
    private readonly AppSnapshot _snapshot;
    private readonly PlanInfo _plan = new("max", "default_claude_max_20x");
    private readonly Dictionary<(UsageChartMode, QuotaFocus), (UsageNumbers, UsageChartModel)> _usage = new();
    private readonly NotchTask[] _tasks;
    private IReadOnlyList<NotchTask>? _ordered;
    private int _orderedMask = -1;

    public TutorialSamples(Strings strings, DateTime utcNow, DateTime localNow)
    {
        _s = strings ?? throw new ArgumentNullException(nameof(strings));
        _utc0 = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        _local0 = localNow;
        var five = new UsageWindow(42, new DateTimeOffset(_utc0).AddHours(1).AddMinutes(40));
        var seven = new UsageWindow(18, new DateTimeOffset(_utc0).AddDays(3));
        Quota = new RealUsage { FiveHour = five, SevenDay = seven };
        HighQuota = new RealUsage { FiveHour = five with { UtilizationPct = 96 }, SevenDay = seven };
        _snapshot = new AppSnapshot
        {
            Usage = new RealUsage
            {
                FiveHour = new UsageWindow(64, new DateTimeOffset(_utc0).AddHours(1).AddMinutes(40)),
                SevenDay = new UsageWindow(38, new DateTimeOffset(_utc0).AddDays(2).AddHours(6)),
                SevenDayOpus = new UsageWindow(31, null),
                SevenDaySonnet = new UsageWindow(7, null),
                ExtraUsageEnabled = true,
            },
            LatestState = UsageFetchState.Ok,
            UsageAtUtc = _utc0,
            PaceFive = new PaceResult("5h", 64, 1.16, 55, new DateTimeOffset(_utc0).AddHours(1).AddMinutes(10),
                new DateTimeOffset(_utc0).AddHours(1).AddMinutes(40), true, PaceStatus.Over),
            PaceSeven = new PaceResult("7d", 38, 0.86, 44, null, new DateTimeOffset(_utc0).AddDays(2).AddHours(6), false, PaceStatus.Ok),
            Health = new HealthStatus(HealthLevel.Operational, "All Systems Operational"),
        };

        var t = strings.Tut.SampleTasks;
        var today = _local0.Date;
        _tasks = new[]
        {
            new NotchTask(TaskId(1), Pick(t, 0), today.AddHours(17), null, false, null, 0, Focuses: 2),
            new NotchTask(TaskId(2), Pick(t, 1), null, null, false, null, 2, Focuses: 1),
            new NotchTask(TaskId(3), Pick(t, 2), today.AddDays(1).AddHours(9), null, false, null, 4),
        };

        Sessions = new[]
        {
            Session("a", WorkingProject, SessionPhase.Processing, "Edit", 0),
            Session("b", PermissionProject, SessionPhase.WaitingForInput, null, 1),
            Session("c", "infra", SessionPhase.Processing, "Bash", 3),
            Session("d", "web", SessionPhase.Idle, null, 6),
            Session("e", "docs", SessionPhase.Idle, null, 9),
            Session("f", "mobile", SessionPhase.Idle, null, 14),
            Session("g", "data", SessionPhase.Idle, null, 20),
            Session("h", "blog", SessionPhase.Idle, null, 31),
        };
        Listed = Sessions.OrderBy(x => x.Phase.Priority()).ThenByDescending(x => x.LastActivityUtc).ToArray();
        // Con el notch en calma (reposo, reloj, raya, avisos), nadie trabaja: las mismas sesiones, sin las que giran.
        Quiet = Sessions.Select(x => x.Phase.IsActive() ? Session(x.SessionId, x.ProjectName, SessionPhase.Idle, null, 2) : x)
            .ToArray();
        File = new ShelfItem(new Guid(0x7a1c, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8), ShelfKind.File,
            @"C:\Users\you\Documents\" + strings.Tut.SampleFile, strings.Tut.SampleFile, _utc0);
        FileDetail = "PDF · " + ShelfCardText.Size(1_258_291, strings.Culture);
    }

    public Strings Strings => _s;

    /// <summary>Hora local del ejemplo (el reloj del reposo y de la bandeja de mentira).</summary>
    public DateTime LocalNow => _local0;

    /// <summary>El reloj de la demo: el instante de los datos de ejemplo. Contra él se cuentan los reinicios de la cuota (la de
    /// 5 h reinicia 1 h 40 min después), no contra el de la máquina: con un reloj fijo (las capturas) o el tutorial abierto
    /// mucho rato, el notch decía «resetea en reseteando…».</summary>
    public DateTimeOffset Clock => new(_utc0);

    public RealUsage Quota { get; }

    /// <summary>La de 5 h al 96 % (el aviso en rojo con halo).</summary>
    public RealUsage HighQuota { get; }

    /// <summary>Ocho sesiones: dos trabajando, una esperando que escribas y el resto en reposo (la lista no cabe entera).</summary>
    public IReadOnlyList<SessionState> Sessions { get; }

    /// <summary>Las mismas sesiones sin ninguna trabajando (el notch en calma).</summary>
    public IReadOnlyList<SessionState> Quiet { get; }

    /// <summary>Las sesiones en el orden en que las lista el expandido (prioridad de fase y luego la más reciente).</summary>
    public IReadOnlyList<SessionState> Listed { get; }

    /// <summary>Las de la lista desde <paramref name="offset"/> (la rueda la desplaza, como el notch de verdad).</summary>
    public IReadOnlyList<SessionState> SessionsFrom(int offset) => offset <= 0 ? Sessions : Listed.Skip(offset).ToArray();

    public ShelfItem File { get; }
    public string FileDetail { get; }

    // ---------------------------------------------------------------- avisos

    public NotchAlert Done => new(NotchAlertKind.Completed, "a", WorkingProject, null, _utc0, null);

    public NotchAlert QuotaAlert => new(NotchAlertKind.QuotaAlert, "q", "5h", "96%", _utc0, null);

    /// <summary>El Permiso con botones (simulado: su petición no existe; nada se contesta a Claude).</summary>
    public NotchAlert Permission => new(NotchAlertKind.Permission, "b", PermissionProject, PermissionDetail, _utc0, null,
        "tutorial");

    public NotchAlert Reminder => new(NotchAlertKind.Reminder, TaskAlerts.SessionId(_tasks[0].Id), _tasks[0].Title, null, _utc0, null);

    // ---------------------------------------------------------------- música

    private static readonly (string Title, string Artist, string Album, Color Accent)[] Tracks =
    {
        ("Paper Lanterns", "The Quiet Hours", "Night Market", Color.FromArgb(0xE8, 0xB0, 0x4C)),
        ("Low Tide", "Marlow & June", "Coastlines", Color.FromArgb(0x6C, 0xB4, 0xD8)),
        ("Northbound", "Echo Avenue", "Northbound", Color.FromArgb(0xC8, 0x7A, 0xD8)),
    };

    /// <summary>Letra inventada (no es de ninguna canción): ocho líneas cada 4 s.</summary>
    public static readonly IReadOnlyList<LyricLine> Lyrics = new[]
    {
        "Paper lanterns over the bay", "Every window keeps the light", "We walk slow so it can stay",
        "Counting boats into the night", "Hold the string and let it rise", "Somewhere north the morning waits",
        "Every lantern finds the sky", "And the harbour hums our names",
    }.Select((text, i) => new LyricLine(TimeSpan.FromSeconds(30 + i * 4), text)).ToArray();

    /// <summary>La línea que suena en el ejemplo (la cuarta).</summary>
    public const int LyricNow = 3;

    public NowPlaying Track(int i, bool playing)
    {
        var (title, artist, album, accent) = Tracks[((i % Tracks.Length) + Tracks.Length) % Tracks.Length];
        return new NowPlaying(title, artist, album, playing, TimeSpan.FromSeconds(42 + 4 * LyricNow), TimeSpan.FromSeconds(185),
            _utc0, null, accent, "tutorial");
    }

    // ---------------------------------------------------------------- tareas

    /// <summary>Las tareas de ejemplo con las hechas de <paramref name="doneMask"/>, ya ordenadas (como la página).</summary>
    public IReadOnlyList<NotchTask> Tasks(int doneMask)
    {
        if (_ordered is not null && doneMask == _orderedMask) return _ordered;
        var items = _tasks.Select((t, i) => (doneMask & (1 << i)) != 0 ? t with { Done = true, DoneAt = _local0 } : t).ToArray();
        _orderedMask = doneMask;
        return _ordered = TaskList.Order(items);
    }

    /// <summary>Fila (en el orden de la página) de la tarea de ejemplo <paramref name="sample"/>, y al revés.</summary>
    public int SampleAt(int row, int doneMask)
    {
        var list = Tasks(doneMask);
        if (row < 0 || row >= list.Count) return -1;
        return Array.FindIndex(_tasks, t => t.Id == list[row].Id);
    }

    public TaskDraftView Draft => new(_s.Tut.SampleDraft, "", TaskQuick.In1Hour, TaskField.Title, 3, true, true);

    /// <summary>La tarea enlazada al pomodoro (la <paramref name="i"/>-ésima).</summary>
    public FocusTaskInfo FocusTask(int i)
    {
        var t = _tasks[((i % _tasks.Length) + _tasks.Length) % _tasks.Length];
        return new FocusTaskInfo(t.Title, t.Color, t.Focuses);
    }

    // ---------------------------------------------------------------- uso

    /// <summary>Números y gráfica de la página Uso para ese modo y esa ventana destacada (se calculan una vez).</summary>
    public (UsageNumbers Numbers, UsageChartModel Chart) Usage(UsageChartMode mode, QuotaFocus focus)
    {
        if (mode == UsageChartMode.Spend) focus = QuotaFocus.None;
        else if (focus == QuotaFocus.None) focus = QuotaFocus.Five;
        if (_usage.TryGetValue((mode, focus), out var cached)) return cached;
        var numbers = UsageText.Build(_snapshot, _plan, _utc0, mode == UsageChartMode.Spend ? QuotaFocus.None : focus, _s);
        UsageChartModel chart;
        if (mode == UsageChartMode.Spend)
        {
            var data = Spend();
            var slots = new ChartSeriesAssigner().Assign(UsageChartBuilder.Families(data));
            chart = UsageChartBuilder.Spend(data, ChartRange.Day1, slots, new HashSet<string>(), _s);
        }
        else
        {
            var u = _snapshot.Usage!;
            var q = new UsageQuotaInputs(u.FiveHour, u.SevenDay, _snapshot.PaceFive, _snapshot.PaceSeven, new[] { 80, 95 });
            chart = UsageChartBuilder.Quota(Percent(), focus == QuotaFocus.Seven ? ChartRange.Week1 : ChartRange.Hours5, _utc0, focus,
                q, new HashSet<string>(), true, _s);
        }
        return _usage[(mode, focus)] = (numbers, chart);
    }

    public RealUsage? UsageQuota => _snapshot.Usage;

    /// <summary>24 h de gasto por horas: la mañana con Opus, la tarde con los dos.</summary>
    private List<HistoryBucket> Spend()
    {
        var list = new List<HistoryBucket>(24);
        for (int i = 0; i < 24; i++)
        {
            var start = (_utc0 - TimeSpan.FromHours(24 - i)).ToLocalTime();
            double work = start.Hour is >= 9 and <= 20 ? 1 : 0.05;
            var by = new Dictionary<string, double>
            {
                ["Opus"] = Math.Round(work * (1.2 + Math.Sin(i * 0.7) * 0.9 + 0.9), 2),
                ["Sonnet"] = Math.Round(work * (0.4 + Math.Cos(i * 0.5) * 0.35 + 0.35), 2),
            };
            if (i % 7 == 3) by["Haiku"] = 0.12;
            list.Add(new HistoryBucket(start, start.ToString("HH'h'", _s.Culture),
                by.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value)));
        }
        return list;
    }

    /// <summary>7 días de muestras cada 10 min (y el último tramo cada minuto): la de 5 h sube tras un reinicio.</summary>
    private List<PctPoint> Percent()
    {
        var list = new List<PctPoint>(1300);
        for (int m = 7 * 24 * 60; m >= 0; m -= m > 300 ? 10 : 1)
        {
            var at = _utc0.AddMinutes(-m);
            double phase = (m % 300) / 300.0;
            double five = m <= 300 ? Math.Min(100, Math.Max(0, 200 - m) * 0.32) : 20 + 50 * (1 - phase);
            double seven = Math.Min(100, 38 - m / (7.0 * 24 * 60) * 30);
            list.Add(new PctPoint(at, Math.Round(five, 1), Math.Round(Math.Max(0, seven), 1)));
        }
        return list;
    }

    // ---------------------------------------------------------------- pomodoro

    /// <summary>Duración de la fase de ejemplo (25 / 5 min).</summary>
    public static TimeSpan Length(FocusPhase phase) =>
        TimeSpan.FromMinutes(phase == FocusPhase.Focus ? FocusDurations.Default.FocusMinutes : FocusDurations.Default.ShortMinutes);

    /// <summary>
    /// El pomodoro como lo monta <c>NotchForm.FocusFrame</c>: el tercer foco de hoy (dos hechos), la semana con focos, la tarea
    /// enlazada <paramref name="task"/> y lo que queda (<paramref name="remaining"/>). <paramref name="face"/>: el mini anillo
    /// del notch cerrado.
    /// </summary>
    public FocusView Focus(FocusRun run, FocusPhase phase, int task, TimeSpan remaining, bool face, FocusHit hover = default,
        float hoverIntensity = 0f, FocusHit pressed = default)
    {
        var length = Length(phase);
        var todayDate = DateOnly.FromDateTime(_local0);
        int[] counts = { 3, 5, 0, 4, 6, 2, 2 };
        var week = counts.Select((c, i) => new FocusDay(todayDate.AddDays(i - 6), c, c * 25)).ToArray();
        var info = FocusTask(task);
        var texts = FocusText.Build(phase, run, null, remaining, 2, FocusDurations.Default.LongEvery, week[^1], week, info,
            anyPendingTask: true, _s);
        return new FocusView(texts, phase, run, null,
            FocusClock.Progress(TimeSpan.FromSeconds(FocusClock.ShownSeconds(remaining)), length),
            FocusClock.Progress(FocusClock.CompactShown(remaining), length), 2, FocusDurations.Default.LongEvery, info.Color,
            counts, Locked: false, Interactive: true, face, hover, hoverIntensity, pressed);
    }

    // ---------------------------------------------------------------- piezas

    private SessionState Session(string id, string project, SessionPhase phase, string? tool, int minutesAgo) => new()
    {
        SessionId = id, Cwd = @"C:\code\" + project, ProjectName = project, Phase = phase, LastTool = tool,
        LastActivityUtc = _utc0.AddMinutes(-minutesAgo),
    };

    private static Guid TaskId(int n) => new(0x7a1c, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)n);

    private static string Pick(string[] values, int i) => values is { Length: > 0 } && i >= 0 && i < values.Length ? values[i] : "";
}
