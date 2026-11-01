using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;
using ClaudeBarWin.Services.State;

namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Una foto de lo que hay que pintar en este frame. La construye <c>NotchForm</c> con los valores
/// ANIMADOS (ancho/alto/alfas salen de los muelles, no del modo objetivo) y el renderer la pinta tal cual.
///
/// <list type="bullet">
/// <item><see cref="Width"/>/<see cref="Height"/>: caja del contenido en píxeles físicos (ya × <see cref="Scale"/>).</item>
/// <item><see cref="SpinnerPhase"/>: vueltas, se usa la parte fraccionaria.</item>
/// <item><see cref="ContentAlpha"/> [0, 1]: fundido del contenido mientras la forma cambia.</item>
/// <item><see cref="PillAlpha"/> [0, 1]: alfa de TODA la ventana (Snap Layouts, aparecer/ocultar); va como
/// alfa constante de <c>UpdateLayeredWindow</c>, sin repintar.</item>
/// <item><see cref="ShadowAlpha"/> / <see cref="HaloAlpha"/> [0, 1]: sombra bajo la pastilla y halo rojo del
/// aviso 95 % (objetivos en <see cref="NotchMotion"/>).</item>
/// <item><see cref="Squash"/> [−1, 1]: squash-stretch de la FORMA (<see cref="NotchMotion.Squashed"/>); el
/// contenido se pinta con la caja sin deformar.</item>
/// <item><see cref="ContentOffsetY"/>: desplazamiento vertical del contenido en px a escala 1 (entrada 4 → 0).</item>
/// <item><see cref="ReduceMotion"/>: sin spinner (glifo fijo) y Permiso con borde fijo en vez de rebote.</item>
/// <item><see cref="PermissionArm"/> [0, 1]: armado de los botones del Permiso (1 = aceptan clics); con
/// <see cref="ReduceMotion"/> no se pinta el relleno, solo atenuado → activo.</item>
/// <item><see cref="PermissionAnswer"/>: botón ya pulsado («Permitido» / «Denegado»), o <c>None</c>.</item>
/// <item><see cref="PermissionHeld"/>: botón con el ratón pulsado encima y aún sin soltar (se encoge 1 px por lado
/// y se oscurece un 6 %), o <c>None</c>.</item>
/// <item><see cref="Pages"/>: páginas del expandido (F5); <c>null</c> o una sola = el expandido de siempre, sin puntos.</item>
/// <item><see cref="Music"/>: lo que pinta la página Música y, con <see cref="MusicView.IdleFace"/>, el reposo con la
/// canción (F5 · B3); <c>null</c> sin música.</item>
/// <item><see cref="Tasks"/>: la página Tareas (F5 · B5); <c>null</c> = sin tareas cargadas (texto de vacío). El aviso de
/// recordatorio usa <see cref="PermissionArm"/> / <see cref="PermissionHeld"/> para sus botones.</item>
/// <item><see cref="Idle"/>: reposo, vista previa y raya (F5 · B7): reloj en vez de los anillos y el gato; <c>null</c> = como
/// siempre.</item>
/// <item><see cref="ReadOnly"/>: el Permiso o Recordatorio con botones sale SIN ellos (un notch por monitor y este no es el
/// principal efectivo): título, detalle y «Haz clic para responder aquí» (o la respuesta dada en el otro).</item>
/// <item><see cref="Update"/>: hay una versión nueva (<see cref="NotchUpdateView"/>): un punto de acento en el reposo y la
/// vista previa y, en el expandido, la línea de arriba con el anillo de la descarga o el botón «Actualizar»; <c>null</c> sin
/// oferta.</item>
/// <item><see cref="Locked"/>: páginas con el candado del Pro (edición gratis): en vez de su contenido, su nombre con el chip
/// «Pro», qué traen y la pista para abrir la tarjeta.</item>
/// <item><see cref="ProCard"/>: la tarjeta del Pro abierta en el expandido (lo que incluye, precio y los botones); <c>null</c>
/// cerrada.</item>
/// <item><see cref="Hold"/>: zona de hover retenida de un cambio de página (forma vieja y nueva) que la zona de clic
/// invisible también cubre mientras la pastilla cambia de tamaño; <c>default</c> = solo la pastilla.</item>
/// <item><see cref="Usage"/>: la página Uso (números, gráfica, su transición y el hover), todo ya calculado; <c>null</c> sin
/// datos todavía (cabecera y «Cargando…»).</item>
/// <item><see cref="Focus"/>: el pomodoro (la página Foco, el mini anillo del notch cerrado y el bote del aviso del fin de fase),
/// con los textos ya formateados; <c>null</c> = nada del pomodoro a la vista.</item>
/// </list>
/// </summary>
public sealed record NotchFrame(float Width, float Height, float Scale, NotchMode Mode, NotchAlert? Alert,
    IReadOnlyList<SessionState> Sessions, RealUsage? Quota, float SpinnerPhase, float ContentAlpha,
    float PillAlpha = 1f, float ShadowAlpha = 0f, float HaloAlpha = 0f, float Squash = 0f,
    float ContentOffsetY = 0f, bool ReduceMotion = false, float PermissionArm = 1f,
    PermissionVerdict PermissionAnswer = PermissionVerdict.None,
    PermissionVerdict PermissionHeld = PermissionVerdict.None,
    NotchPagesView? Pages = null, MusicView? Music = null, ShelfView? Shelf = null, TasksView? Tasks = null,
    IdleView? Idle = null, bool ReadOnly = false, NotchUpdateView? Update = null, NotchProLocks Locked = NotchProLocks.None,
    NotchProCardView? ProCard = null, UsageView? Usage = null, FocusView? Focus = null, HoverHold Hold = default,
    double QuotaAlertPct = CompactLyrics.DefaultQuotaAlertPct, DateTimeOffset? Clock = null);

/// <summary>
/// El pomodoro en este frame. <see cref="Texts"/> llegan YA formateados (la ventana los rehace cuando cambia el segundo o el
/// minuto que se ve, el estado o el idioma; nunca por frame). <see cref="Progress"/>: lo transcurrido de la fase con el segundo
/// que enseña la página; <see cref="CompactProgress"/>: con lo que enseña el notch cerrado (minutos), así su arco cambia a la
/// vez que su texto. <see cref="Finished"/>: la fase que acaba de terminar (la oferta de empezar la que toca está a la vista);
/// null sin oferta. <see cref="Cycle"/> de
/// <see cref="LongEvery"/>: los puntos del ciclo. <see cref="TaskColor"/>: el color de la tarea enlazada (−1 = ninguna).
/// <see cref="Week"/>: focos de los últimos 7 días (hoy el último). <see cref="Locked"/>: sin el Pro, la tarea y la semana con el
/// chip. <see cref="Interactive"/>: la ventana de los botones. <see cref="Face"/>: el notch cerrado enseña el mini anillo (la
/// maquetación que SE PINTA lo lleva). Hover (con su intensidad) y pulsación de <see cref="FocusLayout.HitTest"/>.
/// <see cref="Pop"/>: escala del anillo en el bote del fin de fase (1 = quieto).
/// </summary>
public sealed record FocusView(FocusTexts Texts, FocusPhase Phase, FocusRun Run, FocusPhase? Finished, float Progress,
    float CompactProgress, int Cycle, int LongEvery, int TaskColor, IReadOnlyList<int> Week, bool Locked, bool Interactive,
    bool Face, FocusHit Hover = default, float HoverIntensity = 0f, FocusHit Pressed = default, float Pop = 1f);

/// <summary>
/// La página Uso en este frame. <see cref="Numbers"/> y <see cref="Chart"/> llegan YA formateados (la ventana los rehace al
/// cambiar los datos, el rango, el idioma o las series ocultas; nunca por frame). La transición de la gráfica:
/// <see cref="Morph"/> con su <see cref="Progress"/>; en un morphing, <see cref="Shape"/> es la forma intermedia (si no, la de
/// <see cref="Chart"/>) y <see cref="Previous"/> la gráfica que sale (sus marcas se funden; en un fundido, entera).
/// <see cref="Mode"/>/<see cref="Range"/>: el selector y el rango elegidos. <see cref="Interactive"/>: hover, rangos y leyenda
/// (con el Pro y en la ventana interactiva); <see cref="Locked"/>: sin el Pro (gráfica quieta con el chip «Pro»).
/// <see cref="Hover"/>/<see cref="HoverIntensity"/>/<see cref="Pressed"/>: el control bajo el ratón (realce animado) y el
/// pulsado. <see cref="Bucket"/>: casilla bajo el ratón en la gráfica, fraccionaria mientras la línea se desliza (−1 = sin
/// hover), con su alfa <see cref="TipAlpha"/>; el tooltip es el de <see cref="TipIndex"/>. <see cref="Status"/>: si la
/// gráfica no es aún la del rango elegido, se atenúa con «Cargando…» (o el aviso de que falló) encima y sin hover.
/// <see cref="Modes"/>: el selector Gasto | Cuota (sin «Mostrar gasto estimado», no se pinta: la página es solo Cuota).
/// </summary>
public sealed record UsageView(UsageNumbers Numbers, UsageChartModel Chart, UsageChartMode Mode, ChartRange Range,
    UsageMorphKind Morph = UsageMorphKind.None, float Progress = 1f, UsageShape? Shape = null, UsageChartModel? Previous = null,
    bool Interactive = true, bool Locked = false, UsageHit Hover = default, float HoverIntensity = 0f, UsageHit Pressed = default,
    float Bucket = -1f, int TipIndex = -1, float TipAlpha = 0f, UsageChartStatus Status = UsageChartStatus.Ready,
    bool Modes = true);

/// <summary>
/// La página Bandeja en este frame (F5 · B4). <see cref="Items"/>: de la más nueva a la más antigua;
/// <see cref="Available"/> paralela (cacheada en <see cref="ShelfSnapshot"/>: el painter no toca el disco).
/// <see cref="Selected"/>: selección múltiple. <see cref="Scroll"/>: desplazamiento ANIMADO de la fila de tarjetas en px
/// lógicos (<see cref="ShelfLayout.ScrollFor"/>; el mismo con que se acierta el clic). Hover (con su intensidad animada) y
/// pulsación de <see cref="ShelfLayout.HitTest"/>. <see cref="DropTarget"/>: algo aceptable se está arrastrando encima
/// (contorno). <see cref="Copied"/>: entrada de texto recién copiada al portapapeles («Copiado»).
/// <see cref="Thumbs"/>: miniatura del sistema de cada entrada (paralela; <c>null</c> = icono dibujado), de la caché de
/// <see cref="ShelfThumbnails"/>: el painter no crea bitmaps. <see cref="Details"/>: la línea gris de cada tarjeta, ya
/// formateada (<see cref="ShelfCardText.Detail"/>); sin ella, el tipo.
/// </summary>
public sealed record ShelfView(IReadOnlyList<ShelfItem> Items, IReadOnlyList<bool> Available,
    IReadOnlyCollection<Guid> Selected, float Scroll = 0f, ShelfHit Hover = default, float HoverIntensity = 0f,
    ShelfHit Pressed = default, bool DropTarget = false, Guid? Copied = null, IReadOnlyList<Bitmap?>? Thumbs = null,
    IReadOnlyList<string>? Details = null);

/// <summary>
/// El reposo en este frame (F5 · B7), según la maquetación que SE PINTA. <see cref="Clock"/>: hora y fecha en vez de los
/// anillos (solo en el reposo). <see cref="Mascot"/>: el gato (ojos) a la izquierda del contenido o, en la raya, en su
/// lugar; <c>null</c> sin gato. <see cref="MascotAlpha"/>: su alfa propio, para que no parpadee cuando solo cambia la cara
/// de al lado (anillos ↔ reloj); al aparecer o irse sigue el fundido del contenido.
/// </summary>
public sealed record IdleView(bool Clock, string Time, string Date, MascotLook? Mascot = null, float MascotAlpha = 1f);

/// <summary>
/// Páginas del expandido en este frame (sale de <see cref="PageTransition"/>): lista, página que entra y la que sale
/// (<c>null</c> si no hay cruce), alfa de la que entra (<see cref="Blend"/>; la que sale lleva <c>1 − Blend</c>),
/// desplazamientos horizontales en px lógicos enteros y posición de la píldora indicadora en unidades de página.
/// <see cref="Arrows"/>: las flechas ‹ › a los lados de los puntos (<see cref="PageArrows"/>); <c>null</c> = sin flechas.
/// </summary>
public sealed record NotchPagesView(IReadOnlyList<NotchPage> Pages, NotchPage Current, NotchPage? Previous, float Blend,
    float CurrentShift, float PreviousShift, float IndicatorPos, PageArrowsView? Arrows = null);

/// <summary>
/// La música en este frame (F5 · B3). <see cref="Track"/> null = no suena nada. <see cref="Position"/>: ya interpolada
/// al instante del frame. <see cref="IdleFace"/>: el reposo enseña la canción en vez de los anillos. La letra: estado,
/// líneas, línea actual y la que se va con su cruce (<see cref="LineBlend"/>). <see cref="SplitBlend"/>: 0 = vista normal,
/// 1 = dividida con la letra. Hover (con su intensidad animada) y pulsación del control bajo el ratón
/// (<see cref="HoverLine"/>: fila de la letra −1/0/+1). <see cref="LyricsEnabled"/>: sin el ajuste no hay botón «Letra».
/// Iconos animados (de <see cref="MusicIconMotion"/>): <see cref="PlayMorph"/> = forma del botón central a media
/// transición (0 = barras de pausa, 1 = triángulo; <c>null</c> = quieto, manda <c>Track.Playing</c>);
/// <see cref="NextSkip"/>/<see cref="PreviousSkip"/> = desplazamiento normalizado del triángulo (0 = en su sitio,
/// +1 = metido en la barra, −1 = fuera del icono por el lado de entrada).
/// Letras v2: con <see cref="LyricsState.Plain"/>, <see cref="Lines"/> son las de la letra plana con tiempos ESTIMADOS
/// (<see cref="PlainLyricsTiming"/>) y se pintan con un realce más suave. <see cref="Tools"/>: botones de la vista de letra
/// que se ven; <see cref="SourceLabel"/>: texto de «Otra letra» («LRCLIB 1/3»), atenuado con <see cref="SourceBusy"/>
/// (pidiendo candidatos o sin otra que enseñar); <see cref="Notice"/>: aviso pequeño («letra sin sincronizar» o el
/// desfase). Todo lo trae la ventana ya calculado: el painter no formatea texto en cada frame.
/// Buscando la letra (<see cref="LyricsState.Loading"/>): <see cref="SearchSeconds"/> = instante de la animación de letras
/// que suben (<see cref="LyricsSearchParticles"/>); con <see cref="ReduceMotion"/>, fotograma fijo.
/// <see cref="Compact"/>: con <see cref="IdleFace"/>, el reposo enseña la letra compacta (la línea actual) en vez del título.
/// </summary>
public sealed record MusicView(NowPlaying? Track, TimeSpan Position, bool IdleFace = false,
    LyricsState LyricsState = LyricsState.Idle, IReadOnlyList<LyricLine>? Lines = null, int Line = -1,
    int PreviousLine = -1, float LineBlend = 1f, float SplitBlend = 0f, MusicControl Hover = MusicControl.None,
    int HoverLine = 0, float HoverIntensity = 0f, MusicControl Pressed = MusicControl.None, bool LyricsEnabled = true,
    float[]? Equalizer = null, float? PlayMorph = null, float NextSkip = 0f, float PreviousSkip = 0f,
    LyricsTools Tools = LyricsTools.None, string? SourceLabel = null, bool SourceBusy = false, string? Notice = null,
    double SearchSeconds = 0, bool ReduceMotion = false, CompactLyricsView? Compact = null);

/// <summary>
/// Letra compacta del reposo en este frame (geometría de <see cref="CompactLyrics.Compute"/>). <see cref="Line"/>: la línea
/// actual («♪» en las pausas); <see cref="PreviousLine"/>: la que se va en el cruce (de <see cref="CompactLyricsMotion"/>),
/// con su alfa y su desplazamiento vertical en px lógicos enteros. <see cref="Art"/>: carátula en la ranura izquierda (sin
/// ella, o sin carátula en la pista, una nota «♪» atenuada); <see cref="Quota"/>: anillo de 5 h a la derecha, con su
/// énfasis (<see cref="QuotaEmphasis"/>: 0 = atenuado, 1 = entero) y su % (<see cref="QuotaReveal"/>: 0 = sin %, 1 = a la
/// vista), de <see cref="CompactQuotaRing"/>.
/// </summary>
public sealed record CompactLyricsView(string Line, string? PreviousLine = null, float Alpha = 1f, float PreviousAlpha = 0f,
    int Offset = 0, int PreviousOffset = 0, bool Art = true, bool Quota = true, float QuotaEmphasis = 0f,
    float QuotaReveal = 0f)
{
    public CompactLyricsSlots Slots => new(Art, Quota);
}

/// <summary>
/// Lo que <see cref="NotchContentPainter"/> pintó del Permiso con botones en el último frame: la petición, el título y el
/// detalle tal como venían en el aviso, y si el detalle se vio ENTERO (<paramref name="Complete"/>). El clic se compara
/// con esto, no con lo que la máquina cree que se enseña.
/// </summary>
public sealed record PermissionPaint(string RequestId, string Title, string? Detail, bool Complete);
