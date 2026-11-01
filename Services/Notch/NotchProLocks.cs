using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Services.Notch;

/// <summary>
/// Páginas del expandido que se ven con el candado del Pro (edición gratis): siguen en el carrusel, pero en vez de su
/// contenido enseñan el chip «Pro» y qué traen, y un clic abre la tarjeta. Sus servicios están apagados (música, letras,
/// tareas y sus avisos, bandeja) y sus datos se quedan en disco como estaban. Lo decide <c>NotchProGating</c> con
/// <c>ProGate</c>; solo se marca lo que el usuario tiene encendido en Ajustes. <see cref="UsageCharts"/> no es una página: la
/// página Uso es gratis y solo sus gráficas se quedan quietas (sin hover, rangos ni proyección) con el chip «Pro». Tampoco
/// <see cref="FocusPro"/>: la página Foco es gratis y solo lo avanzado del pomodoro lleva el chip.
/// </summary>
[Flags]
public enum NotchProLocks
{
    None = 0,
    Music = 1,
    Tasks = 2,
    Shelf = 4,
    /// <summary>Gráficas de la página Uso sin interacción (<c>ProFeature.NotchUsage</c>).</summary>
    UsageCharts = 8,
    /// <summary>Pomodoro básico (<c>ProFeature.NotchFocus</c>): la página Foco es gratis; enlazar con una tarea, las estadísticas
    /// de la semana y las duraciones personalizadas llevan el chip «Pro».</summary>
    FocusPro = 16,
}

/// <summary>Página ↔ candado.</summary>
public static class NotchProLock
{
    /// <summary>El candado de esa página (Claude y Uso no tienen: son gratis siempre).</summary>
    public static NotchProLocks For(NotchPage page) => page switch
    {
        NotchPage.Music => NotchProLocks.Music,
        NotchPage.Tasks => NotchProLocks.Tasks,
        NotchPage.Shelf => NotchProLocks.Shelf,
        _ => NotchProLocks.None,
    };

    /// <summary>¿Esa página va con el candado?</summary>
    public static bool Has(NotchProLocks locks, NotchPage page) => For(page) is var l && l != NotchProLocks.None && (locks & l) != 0;
}
