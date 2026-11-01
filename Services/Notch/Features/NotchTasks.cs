using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>
/// Una tarea del notch. Horas en hora LOCAL (lo que el usuario eligió en el reloj de pared).
/// <paramref name="SnoozedUntilLocal"/>, si existe, sustituye a <paramref name="RemindAtLocal"/> como hora de aviso.
/// <paramref name="Color"/> es un índice de paleta 0..5. <paramref name="NotifiedForLocal"/>: la hora de aviso que YA
/// sonó (persistida para que un reinicio no la repita); aplazar o cambiar la hora da otra hora de aviso, que vuelve a
/// sonar. Los <c>tasks.json</c> viejos no lo traen (<c>null</c> = no ha sonado). <paramref name="Focuses"/>: focos completos del
/// pomodoro enlazado a esta tarea (página Foco, Pro); los archivos viejos no lo traen (0).
/// </summary>
public sealed record NotchTask(
    Guid Id, string Title, DateTime? RemindAtLocal, DateTime? SnoozedUntilLocal, bool Done, DateTime? DoneAt, int Color,
    DateTime? NotifiedForLocal = null, int Focuses = 0)
{
    /// <summary>Hora de aviso vigente: el aplazamiento si lo hay, si no la hora elegida.</summary>
    [JsonIgnore]
    public DateTime? DueAtLocal => SnoozedUntilLocal ?? RemindAtLocal;

    /// <summary>¿Su hora de aviso vigente ya sonó?</summary>
    [JsonIgnore]
    public bool Notified => NotifiedForLocal is { } n && DueAtLocal == n;
}

/// <summary>
/// Almacén de tareas en <c>tasks.json</c> (escritura atómica con <see cref="AtomicFile"/> tras cada cambio).
/// <list type="bullet">
/// <item>Título: una sola línea, sin espacios en los bordes, máx. <see cref="MaxTitle"/> caracteres (sin partir
/// pares sustitutos). Vacío → <see cref="ArgumentException"/>.</item>
/// <item>Color recortado a 0..<see cref="MaxColor"/>.</item>
/// <item>Las hechas se purgan al día siguiente de completarse (<see cref="Purge"/>, también en
/// <see cref="Complete"/> y <see cref="Snooze"/>).</item>
/// <item>Archivo ausente o corrupto → lista vacía (no peta; se sobrescribe en el siguiente cambio).</item>
/// </list>
/// No es seguro entre hilos: se usa desde el hilo de UI.
/// </summary>
public sealed class TaskStore
{
    public const int MaxTitle = 80;
    public const int MaxColor = 5;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly Regex LineBreaks = new(@"\s*[\r\n]+\s*", RegexOptions.CultureInvariant);

    private readonly string _path;
    private readonly List<NotchTask> _items;

    public TaskStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
        _items = Load(path);
    }

    public event Action? Changed;

    public IReadOnlyList<NotchTask> Items => _items.ToArray();

    public NotchTask Add(string title, DateTime? remindAt, int color)
    {
        var t = new NotchTask(Guid.NewGuid(), NormalizeTitle(title), remindAt, null, false, null, ClampColor(color));
        _items.Add(t);
        Commit();
        return t;
    }

    /// <summary>Sustituye la tarea con el mismo <see cref="NotchTask.Id"/> (si ya no existe, no hace nada).
    /// Cambiar la hora del recordatorio anula el aplazamiento.</summary>
    public void Update(NotchTask t)
    {
        ArgumentNullException.ThrowIfNull(t);
        int i = IndexOf(t.Id);
        if (i < 0) return;
        var fixedUp = t with { Title = NormalizeTitle(t.Title), Color = ClampColor(t.Color) };
        if (fixedUp.RemindAtLocal != _items[i].RemindAtLocal) fixedUp = fixedUp with { SnoozedUntilLocal = null };
        _items[i] = fixedUp;
        Commit();
    }

    public void Remove(Guid id)
    {
        if (_items.RemoveAll(x => x.Id == id) > 0) Commit();
    }

    public void Complete(Guid id, DateTime now)
    {
        int i = IndexOf(id);
        if (i >= 0) _items[i] = _items[i] with { Done = true, DoneAt = now };
        PurgeCore(now);
        Commit();
    }

    /// <summary>Pospone el aviso a <c>now + by</c>. El llamador debe sacar la tarea del conjunto de avisos ya
    /// dados de <see cref="TaskReminders.Due"/> para que vuelva a sonar.</summary>
    public void Snooze(Guid id, TimeSpan by, DateTime now)
    {
        if (by <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(by));
        int i = IndexOf(id);
        if (i < 0) return;
        _items[i] = _items[i] with { SnoozedUntilLocal = now + by };
        PurgeCore(now);
        Commit();
    }

    /// <summary>Apunta que estas tareas ya avisaron de su hora vigente (UN guardado). Las que no existen o no tienen hora
    /// se ignoran; sin cambios no escribe.</summary>
    public void MarkNotified(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        bool changed = false;
        foreach (var id in ids)
        {
            int i = IndexOf(id);
            if (i < 0 || _items[i].DueAtLocal is not { } at || _items[i].NotifiedForLocal == at) continue;
            _items[i] = _items[i] with { NotifiedForLocal = at };
            changed = true;
        }
        if (changed) Commit();
    }

    /// <summary>Apunta un foco completo del pomodoro en esa tarea (si ya no existe, nada).</summary>
    public void AddFocus(Guid id)
    {
        int i = IndexOf(id);
        if (i < 0) return;
        _items[i] = _items[i] with { Focuses = Math.Max(0, _items[i].Focuses) + 1 };
        Commit();
    }

    /// <summary>Quita las tareas hechas un día anterior a <paramref name="now"/> (al arrancar y al cambiar de día).</summary>
    public void Purge(DateTime now)
    {
        if (PurgeCore(now)) Commit();
    }

    private bool PurgeCore(DateTime now) =>
        _items.RemoveAll(x => x.Done && x.DoneAt is { } d && d.Date < now.Date) > 0;

    private int IndexOf(Guid id) => _items.FindIndex(x => x.Id == id);

    private void Commit()
    {
        AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(new TaskFile(1, _items), Json));
        Changed?.Invoke();
    }

    private static List<NotchTask> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var f = JsonSerializer.Deserialize<TaskFile>(File.ReadAllText(path), Json);
            return f?.Tasks?
                .Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Title))
                .Select(t => t with { Title = NormalizeTitle(t.Title), Color = ClampColor(t.Color), Focuses = Math.Max(0, t.Focuses) })
                .ToList() ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new();
        }
    }

    private static string NormalizeTitle(string title)
    {
        var s = LineBreaks.Replace(title ?? "", " ").Trim();
        if (s.Length == 0) throw new ArgumentException("El título no puede estar vacío.", nameof(title));
        if (s.Length <= MaxTitle) return s;
        int cut = MaxTitle;
        if (char.IsHighSurrogate(s[cut - 1])) cut--;
        return s[..cut].TrimEnd();
    }

    private static int ClampColor(int c) => Math.Clamp(c, 0, MaxColor);

    private sealed record TaskFile(int Version, List<NotchTask> Tasks);
}

/// <summary>
/// Cuándo suena un recordatorio. PURO: el instante llega por parámetro.
/// <list type="bullet">
/// <item>Vence si no está hecha, <c>now ≥ hora de aviso</c> (el aplazamiento si lo hay) y no se avisó ya (ni en esta
/// ejecución ni, según <see cref="NotchTask.Notified"/>, en una anterior).</item>
/// <item>Si venció hace más de <see cref="StaleAfter"/> (12 h) ya no avisa (PC apagado un día entero).</item>
/// <item>Tras suspender u ocultar el notch, la siguiente llamada devuelve la vencida una sola vez.</item>
/// </list>
/// </summary>
public static class TaskReminders
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(12);

    /// <summary>
    /// La tarea vencida más antigua que no se ha avisado, o <c>null</c>. La añade a <paramref name="alreadyFired"/>
    /// (cada llamada devuelve como mucho una; llamar en bucle hasta <c>null</c> para vaciar).
    /// </summary>
    public static NotchTask? Due(IReadOnlyList<NotchTask> items, DateTime nowLocal, ISet<Guid> alreadyFired)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(alreadyFired);
        NotchTask? best = null;
        DateTime bestAt = DateTime.MaxValue;
        foreach (var t in items)
        {
            if (t.Done || t.Notified || When(t) is not { } at || alreadyFired.Contains(t.Id)) continue;
            if (nowLocal < at || nowLocal - at > StaleAfter) continue;
            if (at < bestAt) { best = t; bestAt = at; }
        }
        if (best is not null) alreadyFired.Add(best.Id);
        return best;
    }

    /// <summary>La próxima hora de aviso estrictamente posterior a <paramref name="nowLocal"/> (para la cola de
    /// despertares), o <c>null</c> si no hay ninguna.</summary>
    public static DateTime? NextDue(IReadOnlyList<NotchTask> items, DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(items);
        DateTime? next = null;
        foreach (var t in items)
        {
            if (t.Done || When(t) is not { } at || at <= nowLocal) continue;
            if (next is null || at < next) next = at;
        }
        return next;
    }

    private static DateTime? When(NotchTask t) => t.DueAtLocal;
}
