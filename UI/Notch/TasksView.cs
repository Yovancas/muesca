using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// La página Tareas en este frame (F5 · B5). <see cref="Items"/>: ya ordenadas (<see cref="TaskList.Order"/>).
/// <see cref="NowLocal"/>/<see cref="Use24h"/>: para las etiquetas de hora (hoy, mañana, fecha) y marcar las vencidas.
/// <see cref="Draft"/>: la edición en línea abierta (<c>null</c> si no se edita). <see cref="Offset"/>: primera tarea mostrada
/// (rueda). Hover (con su intensidad animada) y pulsación de <see cref="TasksLayout.HitTest"/>.
/// Los botones del aviso de recordatorio reutilizan <see cref="NotchFrame.PermissionArm"/> y
/// <see cref="NotchFrame.PermissionHeld"/> (Allow = «Hecha», Deny = «+10 min»).
/// </summary>
public sealed record TasksView(IReadOnlyList<NotchTask> Items, DateTime NowLocal, bool Use24h = true,
    TaskDraftView? Draft = null, int Offset = 0, TaskHit Hover = default, float HoverIntensity = 0f,
    TaskHit Pressed = default);
