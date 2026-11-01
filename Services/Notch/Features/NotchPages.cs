namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Qué mueve la rueda en el expandido (<see cref="UsageSeries"/>: alterna las series de la gráfica de Uso).</summary>
public enum WheelTarget { Pages, SessionList, ShelfList, TaskList, UsageSeries }

/// <summary>Cómo se enseña una página pedida desde fuera del carrusel (<see cref="NotchPages.HowToShow"/>).</summary>
public enum PageShow
{
    /// <summary>Ya es la actual con el expandido abierto: nada que mover.</summary>
    Stay,
    /// <summary>El expandido ya está abierto: cruce animado hasta ella.</summary>
    Slide,
    /// <summary>El expandido se está abriendo (aún no se ve): de golpe, y la apertura cuenta como suya para que Sync no la
    /// devuelva a Claude, aunque el carrusel ya estuviera en ella al cerrar.</summary>
    Jump,
}

/// <summary>Filas de cada página del expandido (deciden su alto en <see cref="NotchLayout.PageSize"/>). En la Bandeja, las
/// entradas: con alguna, una fila de tarjetas.</summary>
public readonly record struct PageRows(int Sessions, int Tasks, int Shelf);

/// <summary>Páginas del expandido: lista por defecto y a dónde va la rueda. PURO.</summary>
public static class NotchPages
{
    /// <summary>Claude (el expandido de siempre), Uso (cuota, ritmo y gasto con sus gráficas), Música, Tareas, Foco (el pomodoro,
    /// junto a las tareas con las que se enlaza) y Bandeja.</summary>
    public static readonly IReadOnlyList<NotchPage> Default =
        new[] { NotchPage.Claude, NotchPage.Usage, NotchPage.Music, NotchPage.Tasks, NotchPage.Focus, NotchPage.Shelf };

    /// <summary>
    /// Lista de páginas válida para <see cref="PageCarousel"/>: <c>null</c> → <see cref="Default"/>; Claude siempre la
    /// primera (es el expandido, donde se integran los avisos); el resto en su orden y sin repetidas.
    /// </summary>
    public static IReadOnlyList<NotchPage> Normalize(IReadOnlyList<NotchPage>? pages)
    {
        if (pages is null) return Default;
        var list = new List<NotchPage>(pages.Count + 1) { NotchPage.Claude };
        foreach (var p in pages)
            if (!list.Contains(p)) list.Add(p);
        return list;
    }

    /// <summary>Las páginas que de verdad se ofrecen: <see cref="Normalize"/> sin Música si la música está apagada ni Tareas
    /// si lo están las tareas (una página que no puede mostrar nada no ocupa un punto del carrusel).</summary>
    public static IReadOnlyList<NotchPage> Effective(IReadOnlyList<NotchPage>? pages, bool music, bool tasks) =>
        Normalize(pages).Where(p => (music || p != NotchPage.Music) && (tasks || p != NotchPage.Tasks)).ToArray();

    /// <summary>Como <see cref="Effective(IReadOnlyList{NotchPage}?, bool, bool)"/>, pero las páginas con el candado del Pro
    /// (<paramref name="locked"/>) se ofrecen aunque su servicio esté apagado: se ven con el candado, no desaparecen.</summary>
    public static IReadOnlyList<NotchPage> Effective(IReadOnlyList<NotchPage>? pages, bool music, bool tasks, NotchProLocks locked) =>
        Effective(pages, music || (locked & NotchProLocks.Music) != 0, tasks || (locked & NotchProLocks.Tasks) != 0);

    /// <summary>
    /// Deslizamiento horizontal (trackpad con dos dedos o rueda inclinable, <c>WM_MOUSEHWHEEL</c>) traducido al delta que
    /// entiende <see cref="PageCarousel.OnWheel"/>: positivo (hacia la derecha) = página siguiente, como la rueda hacia
    /// abajo, que llega en negativo. Lo horizontal siempre cambia de página: nunca desplaza listas.
    /// </summary>
    public static int HorizontalToCarouselDelta(int hwheelDelta) => -hwheelDelta;

    /// <summary>Cómo enseñar <paramref name="page"/> (clic en un anillo, arrastre a la Bandeja…) estando el carrusel en
    /// <paramref name="current"/>. Con el expandido abriéndose siempre salta, también si ya estaba en ella: «cada apertura
    /// empieza en Claude» no aplica a una apertura que pide su página.</summary>
    public static PageShow HowToShow(NotchPage page, NotchPage current, bool wasExpanded) =>
        !wasExpanded ? PageShow.Jump : page == current ? PageShow.Stay : PageShow.Slide;

    /// <summary>
    /// La rueda cambia de página, salvo con una sola página o sobre la lista de sesiones de Claude cuando no cabe entera
    /// (ahí la desplaza, como antes de las páginas). Igual con las listas de la Bandeja (B4) y de Tareas (B5): <paramref name="overSessionList"/>
    /// y <paramref name="listOverflows"/> hablan de la lista de la página actual.
    /// </summary>
    public static WheelTarget RouteWheel(NotchPage current, int pageCount, bool overSessionList, bool listOverflows)
    {
        if (pageCount < 2 || (current == NotchPage.Claude && overSessionList && listOverflows)) return WheelTarget.SessionList;
        if (!overSessionList || !listOverflows) return WheelTarget.Pages;
        return current switch
        {
            NotchPage.Shelf => WheelTarget.ShelfList,
            NotchPage.Tasks => WheelTarget.TaskList,   // B5: la lista de tareas, igual que la de la Bandeja
            NotchPage.Usage => WheelTarget.UsageSeries, // sobre la gráfica con varias series (Pro): las alterna
            _ => WheelTarget.Pages,
        };
    }
}
