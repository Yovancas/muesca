using System.Drawing;
using System.Globalization;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Partes de la página Bandeja que responden al ratón: «Vaciar», una tarjeta y las flechas ‹ ›.</summary>
public enum ShelfControl { None, Clear, Card, Previous, Next }

/// <summary>Qué hay bajo el cursor: el control y, en una tarjeta, el índice de la entrada en la lista mostrada (de la más
/// nueva a la más antigua).</summary>
public readonly record struct ShelfHit(ShelfControl Control, int Index = -1);

/// <summary>
/// Rectángulos de la página Bandeja (px físicos). La MISMA cuenta para pintar y para acertar el clic. Las tarjetas van en una
/// fila (<see cref="Strip"/>) y se ven las que asoman a <see cref="Viewport"/>; si no caben todas (<see cref="Overflows"/>),
/// las flechas <see cref="Previous"/> / <see cref="Next"/> van a los lados (si caben, están vacías).
/// </summary>
public readonly record struct ShelfRects(RectangleF Header, RectangleF Title, RectangleF ClearButton, RectangleF List,
    RectangleF Strip, RectangleF Viewport, RectangleF Previous, RectangleF Next, int Count, int Slots, float Scale)
{
    /// <summary>¿Hay más tarjetas que sitios a la vista?</summary>
    public bool Overflows => Count > Slots;

    /// <summary>Tarjeta <paramref name="i"/> con la fila desplazada <paramref name="scroll"/> px lógicos, en px enteros (el texto
    /// no se emborrona al deslizar). Puede caer fuera del visor: al pintar se recorta a él. Vacía fuera de rango.</summary>
    public RectangleF Card(int i, float scroll)
    {
        if (i < 0 || i >= Count) return RectangleF.Empty;
        float x = MathF.Round(Viewport.Left + (i * ShelfLayout.CardStride - scroll) * Scale);
        return new RectangleF(x, MathF.Round(Strip.Top), ShelfLayout.CardWidth * Scale, ShelfLayout.CardHeight * Scale);
    }

    /// <summary>Tarjetas que asoman al visor con ese desplazamiento (primera y última, inclusive); (0, −1) sin ninguna.</summary>
    public (int First, int Last) Range(float scroll)
    {
        if (Count == 0 || Scale <= 0) return (0, -1);
        float view = Viewport.Width / Scale;
        int first = (int)MathF.Floor((scroll - ShelfLayout.CardWidth) / ShelfLayout.CardStride) + 1;
        int last = (int)MathF.Ceiling((scroll + view) / ShelfLayout.CardStride) - 1;
        return (Math.Clamp(first, 0, Count - 1), Math.Clamp(last, 0, Count - 1));
    }

    /// <summary>Hueco de la miniatura en <paramref name="card"/>: cuadrado, centrado arriba.</summary>
    public RectangleF Thumb(RectangleF card)
    {
        float t = ShelfLayout.Thumb * Scale;
        return new RectangleF(MathF.Round(card.Left + (card.Width - t) / 2f), card.Top + ShelfLayout.CardPadTop * Scale, t, t);
    }

    /// <summary>Línea del nombre en <paramref name="card"/>, bajo la miniatura.</summary>
    public RectangleF Name(RectangleF card)
    {
        float inset = ShelfLayout.NameInset * Scale;
        float top = card.Top + (ShelfLayout.CardPadTop + ShelfLayout.Thumb + ShelfLayout.NameGap) * Scale;
        return new RectangleF(card.Left + inset, top, card.Width - 2f * inset, ShelfLayout.NameLine * Scale);
    }

    /// <summary>Línea gris del detalle (tamaño, «Carpeta»…) en <paramref name="card"/>, bajo el nombre.</summary>
    public RectangleF Detail(RectangleF card)
    {
        var name = Name(card);
        return new RectangleF(name.Left, name.Bottom, name.Width, ShelfLayout.DetailLine * Scale);
    }
}

/// <summary>Ventana de una lista con fila «+N más»: primera entrada mostrada, cuántas se pintan y cuántas resume esa fila.</summary>
public readonly record struct ShelfWindow(int First, int Shown, int More);

/// <summary>
/// Maquetación de la página Bandeja del expandido (F5 · B4). PURA, en px lógicos × escala dentro de la zona de contenido
/// de la página (<c>NotchContentPainter.PageInner</c>):
/// <code>
/// Bandeja                                         [Vaciar]   cabecera (con selección: «Quitar (N)»)
/// ┌────┐  ┌────┐  ┌────┐  ┌────┐  ┌────┐                     una tarjeta por entrada, la más nueva a la izquierda:
/// │ ▣  │  │ ▤  │  │ ↗  │  │ ≡  │  │ ▢  │                     miniatura del sistema (o icono dibujado) de 48,
/// informe…  fotos  ejemplo  Una nota  borrado                nombre recortado con «…» (conserva la extensión)
/// 2,4 MB  Carpeta  Enlace   Texto  No disponible             y, en gris, tamaño o tipo
/// </code>
/// Si no caben todas, flechas ‹ › a los lados que pasan una página (la rueda, una tarjeta). La fila mide siempre
/// <see cref="StripWidth"/> y va centrada: mientras la pastilla crece, las tarjetas no se recolocan. El alto de la página
/// (<see cref="NotchLayout.PageSize"/>) es la cabecera más una fila de tarjetas (vacía, la línea del texto de vacío).
/// </summary>
public static class ShelfLayout
{
    public const float ClearWidth = 84f, ClearHeight = 18f;

    /// <summary>Tarjeta: miniatura de <see cref="Thumb"/> arriba y, debajo, el nombre y el detalle.</summary>
    public const float CardWidth = 72f, CardHeight = 88f, CardGap = 6f, Thumb = 48f, CardPadTop = 4f, NameGap = 4f,
        NameLine = 16f, DetailLine = 14f, NameInset = 4f, CardRadius = 8f, ThumbRadius = 6f;

    /// <summary>Flechas ‹ ›: cápsulas a los lados de la fila, a la altura de las miniaturas, y hueco mínimo hasta las tarjetas.</summary>
    public const float ArrowWidth = 18f, ArrowHeight = 32f, ArrowGap = 6f;

    /// <summary>Ancho de la fila: el de la zona de contenido del expandido a su tamaño final (480 − 2 × 18).</summary>
    public const float StripWidth = NotchLayout.ExpandedWidth - 2f * 18f;

    /// <summary>De una tarjeta a la siguiente.</summary>
    public const float CardStride = CardWidth + CardGap;

    /// <summary>Hueco entre el icono y el texto de una fila (la lista de Tareas).</summary>
    public const float IconGap = 8f;

    /// <summary>Alto (px lógicos) del contenido de la página: cabecera y fila de tarjetas; vacía, cabecera y la línea del
    /// texto de vacío.</summary>
    public static float ContentHeight(int count) =>
        NotchLayout.ExpandedLine + (count > 0 ? CardHeight : NotchLayout.ExpandedLine);

    /// <summary>Lado (px físicos) de la miniatura que se pide al shell a la escala <paramref name="s"/>.</summary>
    public static int ThumbPx(float s) => Math.Max(1, (int)MathF.Round(Thumb * s));

    /// <summary>Tarjetas que caben enteras en <paramref name="width"/> px lógicos (al menos una).</summary>
    public static int SlotsIn(float width) => Math.Max(1, (int)MathF.Floor((width + CardGap + 0.5f) / CardStride));

    /// <summary>Sitios a la vista con <paramref name="count"/> entradas: los de la fila entera o, si no caben todas, los que
    /// quedan entre las flechas.</summary>
    public static int Slots(int count)
    {
        int free = SlotsIn(StripWidth);
        return count > free ? SlotsIn(StripWidth - 2f * (ArrowWidth + ArrowGap)) : free;
    }

    /// <summary>Primera tarjeta más a la derecha que se puede llevar a la izquierda (con ella, la última se ve).</summary>
    public static int MaxFirst(int count) => Math.Max(0, count - Slots(count));

    /// <summary>Desplazamiento (px lógicos) con <paramref name="first"/> como primera tarjeta a la vista.</summary>
    public static float ScrollFor(int first) => first * CardStride;

    /// <summary>Flecha: una página (los sitios a la vista) hacia <paramref name="direction"/> (−1 ‹, +1 ›), acotada a los
    /// extremos. Las ruedas avanzan de una en una con <see cref="Step"/>.</summary>
    public static int Page(int first, int count, int direction) => Step(first, count, Math.Sign(direction) * Slots(count));

    /// <summary><paramref name="first"/> movida <paramref name="cards"/> tarjetas, acotada a 0..<see cref="MaxFirst"/>.</summary>
    public static int Step(int first, int count, int cards) => Math.Clamp(first + cards, 0, MaxFirst(count));

    /// <summary>¿La flecha hacia <paramref name="direction"/> movería algo desde <paramref name="first"/>?</summary>
    public static bool CanPage(int first, int count, int direction) =>
        Page(first, count, direction) != Math.Clamp(first, 0, MaxFirst(count));

    public static ShelfRects Compute(RectangleF inner, float s, int count)
    {
        float line = NotchLayout.ExpandedLine * s;
        var header = new RectangleF(inner.Left, inner.Top, inner.Width, line);
        float cw = ClearWidth * s, ch = ClearHeight * s;
        var clear = new RectangleF(inner.Right - cw, header.Top + (line - ch) / 2f, cw, ch);
        var title = RectangleF.FromLTRB(inner.Left, header.Top, Math.Max(inner.Left, clear.Left - 8f * s), header.Bottom);
        var list = RectangleF.FromLTRB(inner.Left, header.Bottom, inner.Right, Math.Max(header.Bottom, inner.Bottom));

        count = Math.Max(0, count);
        float cx = inner.Left + inner.Width / 2f;
        var strip = new RectangleF(cx - StripWidth * s / 2f, header.Bottom, StripWidth * s, CardHeight * s);
        int slots = Slots(count);
        RectangleF viewport, previous = RectangleF.Empty, next = RectangleF.Empty;
        if (count > slots)
        {
            // Flechas en los bordes de la fila y el visor centrado entre ellas, justo con los sitios enteros: quieta, ninguna
            // tarjeta asoma a medias.
            float aw = ArrowWidth * s, ah = ArrowHeight * s;
            float ay = strip.Top + (CardPadTop + Thumb / 2f) * s - ah / 2f;
            previous = new RectangleF(strip.Left, ay, aw, ah);
            next = new RectangleF(strip.Right - aw, ay, aw, ah);
            float vw = (slots * CardStride - CardGap) * s;
            viewport = new RectangleF(MathF.Round(cx - vw / 2f), strip.Top, vw, strip.Height);
        }
        else viewport = new RectangleF(strip.Left, strip.Top, Math.Max(0f, count * CardStride - CardGap) * s, strip.Height);
        return new ShelfRects(header, title, clear, list, strip, viewport, previous, next, count, slots, s);
    }

    /// <summary>
    /// Control bajo <paramref name="p"/> con la fila desplazada <paramref name="scroll"/> (el MISMO que se pinta): «Vaciar»,
    /// una flecha (solo si hay más tarjetas que sitios) o una tarjeta, solo dentro del visor (la que asoma a medias no se
    /// acierta por fuera). El título y los huecos no son nada; sin entradas, nada.
    /// </summary>
    public static ShelfHit HitTest(PointF p, RectangleF inner, float s, int count, float scroll)
    {
        if (count <= 0 || !inner.Contains(p)) return default;
        var r = Compute(inner, s, count);
        if (r.ClearButton.Contains(p)) return new(ShelfControl.Clear);
        if (r.Overflows)
        {
            if (r.Previous.Contains(p)) return new(ShelfControl.Previous);
            if (r.Next.Contains(p)) return new(ShelfControl.Next);
        }
        if (!r.Viewport.Contains(p)) return default;
        var (first, last) = r.Range(scroll);
        for (int i = first; i <= last; i++)
            if (r.Card(i, scroll).Contains(p)) return new(ShelfControl.Card, i);
        return default;
    }

    /// <summary>Lista con fila «+N más» (la de Tareas): si lo que queda desde <paramref name="offset"/> no cabe, la última fila
    /// dice «+N más»; al final de la lista (desplazamiento máximo) la última entrada se ve.</summary>
    public static ShelfWindow Window(int count, int rows, int offset)
    {
        if (count <= 0 || rows <= 0) return new ShelfWindow(0, 0, 0);
        int first = Math.Clamp(offset, 0, MaxOffset(count, rows));
        int left = count - first;
        int shown = left > rows ? rows - 1 : left;
        return new ShelfWindow(first, shown, left - shown);
    }

    /// <summary>Desplazamiento máximo de una lista de <paramref name="rows"/> filas (rueda).</summary>
    public static int MaxOffset(int count, int rows) => Math.Max(0, count - Math.Max(1, rows));
}

/// <summary>Qué vale como pulsación y como clic en la página Bandeja (F5 · B4). PURO.</summary>
public static class ShelfClickRules
{
    /// <summary>
    /// Control que se recuerda al pulsar. El segundo clic de un doble clic (<c>WM_LBUTTONDBLCLK</c>; la ventana de WinForms
    /// lleva <c>CS_DBLCLKS</c>) solo vale en las flechas, para pasar páginas deprisa: en una tarjeta la abriría dos veces y en
    /// «Quitar (N)», con la selección ya quitada por el primero, vaciaría TODA la bandeja sin preguntar.
    /// </summary>
    public static ShelfHit Press(ShelfHit hit, bool doubleClick) =>
        doubleClick && hit.Control is not (ShelfControl.Previous or ShelfControl.Next) ? default : hit;

    /// <summary>¿«Vaciar» / «Quitar (N)» actúa al soltar? Solo si dice lo mismo que al pulsarlo: si entre pulsar y soltar
    /// apareció o desapareció la selección, el botón cambió de significado y no hace nada.</summary>
    public static bool ClearStillMeans(bool selectionAtPress, int selectedNow) => selectionAtPress == (selectedNow > 0);
}

/// <summary>
/// Textos de las tarjetas de la bandeja (F5 · B4). PUROS: la ventana los calcula al cambiar la bandeja, el idioma o lo que
/// se mira en segundo plano, y el nombre recortado se mide una vez por nombre y escala; el pintor no formatea por frame.
/// </summary>
public static class ShelfCardText
{
    public const string Ellipsis = "…";

    private static readonly string[] Units = { "KB", "MB", "GB", "TB", "PB" };

    /// <summary>Tamaño como el Explorador (base 1024): «512 B», «2,4 MB» / «2.4 MB» según <paramref name="culture"/>; un decimal
    /// por debajo de 10 y ninguno desde 10 («24 MB»); lo que redondea a 1024 pasa a la unidad siguiente («1 MB»).</summary>
    public static string Size(long bytes, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (bytes < 1024) return Math.Max(0, bytes).ToString(culture) + " B";
        double v = bytes / 1024.0;
        int u = 0;
        while (v >= 1024 && u < Units.Length - 1) { v /= 1024; u++; }
        double r = Round(v);
        if (r >= 1024 && u < Units.Length - 1)
        {
            v = r / 1024;
            u++;
            r = Round(v);
        }
        return r.ToString(r < 10 ? "0.#" : "0", culture) + " " + Units[u];

        static double Round(double x) =>
            x < 10 ? Math.Round(x, 1, MidpointRounding.AwayFromZero) : Math.Round(x, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Línea gris bajo el nombre: el tamaño de un archivo ya mirado, «Carpeta», «Enlace» o «Texto»; un archivo sin tamaño
    /// (aún sin mirar, o en una unidad que no se toca), «Archivo». «No disponible» y «Copiado» los pone el pintor: dependen
    /// del instante. Manda lo mirado en disco sobre el tipo con que entró (una carpeta sustituida por un archivo).
    /// </summary>
    public static string Detail(ShelfItem item, ShelfFileProbe? probe, Strings strings)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(strings);
        if (item.Kind == ShelfKind.Link) return strings.NotchShelfLink;
        if (item.Kind == ShelfKind.Text) return strings.NotchShelfText;
        if (probe is { State: ShelfProbeState.File } file) return Size(file.Length, strings.Culture);
        if (probe is { State: ShelfProbeState.Folder }) return strings.NotchShelfFolder;
        return item.Kind == ShelfKind.Folder ? strings.NotchShelfFolder : strings.NotchShelfFile;
    }

    /// <summary>
    /// <paramref name="name"/> en <paramref name="maxWidth"/> px según <paramref name="measure"/>: entero si cabe; si no, con
    /// <paramref name="keepExtension"/> (archivos), recortado por el medio con «…» conservando la extensión
    /// («informe-an….pdf», como el Explorador); sin extensión, o si ni así cabe, recortado por el final («informe-anu…»).
    /// Nunca parte un par sustituto ni deja un espacio antes de «…».
    /// </summary>
    public static string FitName(string name, float maxWidth, Func<string, float> measure, bool keepExtension = true)
    {
        ArgumentNullException.ThrowIfNull(measure);
        if (string.IsNullOrEmpty(name) || measure(name) <= maxWidth) return name ?? "";
        string tail = keepExtension ? ExtensionTail(name) : "";
        if (tail.Length > 0)
        {
            string head = Longest(name[..^tail.Length], p => measure(p + Ellipsis + tail) <= maxWidth);
            if (head.Length > 0) return head + Ellipsis + tail;
        }
        return Longest(name, p => measure(p + Ellipsis) <= maxWidth) + Ellipsis;
    }

    // Extensión de 1 a 7 caracteres tras el último punto (no la de «.gitignore»: ese punto empieza el nombre).
    private static string ExtensionTail(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && name.Length - dot is >= 2 and <= 8 ? name[dot..] : "";
    }

    // Prefijo más largo de s que cumple fits (búsqueda binaria: medir es caro), sin cortar un par sustituto ni acabar en
    // espacio. "" si ni un carácter cabe.
    private static string Longest(string s, Func<string, bool> fits)
    {
        int lo = 0, hi = s.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (fits(Cut(s, mid))) lo = mid;
            else hi = mid - 1;
        }
        return Cut(s, lo).TrimEnd();
    }

    private static string Cut(string s, int n)
    {
        if (n > 0 && n < s.Length && char.IsHighSurrogate(s[n - 1])) n--;
        return s[..n];
    }
}

/// <summary>
/// Foto de la bandeja para pintar (F5 · B4): entradas de la más nueva a la más antigua y si siguen disponibles. La
/// disponibilidad se mira UNA vez al tomarla (al cambiar la bandeja o al mostrarse la página), nunca por frame: es E/S de
/// disco. Al abrir una entrada se vuelve a mirar esa sola (<see cref="WithAvailability"/>). Inmutable.
/// </summary>
public sealed class ShelfSnapshot
{
    public static readonly ShelfSnapshot Empty = new(Array.Empty<ShelfItem>(), Array.Empty<bool>());

    private ShelfSnapshot(IReadOnlyList<ShelfItem> items, IReadOnlyList<bool> available)
    {
        Items = items;
        Available = available;
    }

    /// <summary>De la más nueva a la más antigua (el orden de la página).</summary>
    public IReadOnlyList<ShelfItem> Items { get; }

    /// <summary>Paralela a <see cref="Items"/>.</summary>
    public IReadOnlyList<bool> Available { get; }

    /// <summary>Toma la foto. <paramref name="isAvailable"/> por defecto es <see cref="ShelfStore.IsAvailable"/>.</summary>
    public static ShelfSnapshot Take(ShelfStore store, Func<ShelfItem, bool>? isAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var check = isAvailable ?? store.IsAvailable;
        var items = store.Items.Reverse().ToArray();
        var available = new bool[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            try { available[i] = check(items[i]); }
            catch { available[i] = false; }
        }
        return new ShelfSnapshot(items, available);
    }

    /// <summary>¿Disponible según la foto? Una entrada que no está en la foto, no.</summary>
    public bool IsAvailable(ShelfItem item)
    {
        for (int i = 0; i < Items.Count; i++)
            if (Items[i].Id == item.Id) return Available[i];
        return false;
    }

    /// <summary>Otra foto con la disponibilidad de <paramref name="id"/> cambiada (o esta misma si no cambia nada).</summary>
    public ShelfSnapshot WithAvailability(Guid id, bool available)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Id != id) continue;
            if (Available[i] == available) return this;
            var copy = Available.ToArray();
            copy[i] = available;
            return new ShelfSnapshot(Items, copy);
        }
        return this;
    }
}

/// <summary>
/// Selección múltiple de la bandeja (F5 · B4) con los modificadores del clic (vienen en el <c>wParam</c> del
/// <c>WM_LBUTTONUP</c>: sin ganchos de teclado). Como el Explorador: Ctrl alterna una entrada y mueve el ancla; Mayús
/// selecciona del ancla a la entrada (sustituye la selección; con Ctrl la suma) sin mover el ancla. PURA.
/// </summary>
public sealed class ShelfSelection
{
    private readonly HashSet<Guid> _ids = new();
    private Guid? _anchor;

    public int Count => _ids.Count;

    public IReadOnlyCollection<Guid> Ids => _ids.ToArray();

    public bool Contains(Guid id) => _ids.Contains(id);

    /// <summary>Clic con Ctrl y/o Mayús sobre <paramref name="id"/>; <paramref name="order"/> = orden de la lista.</summary>
    public void Click(Guid id, IReadOnlyList<Guid> order, bool ctrl, bool shift)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (shift)
        {
            int a = _anchor is { } anchor ? IndexOf(order, anchor) : -1, b = IndexOf(order, id);
            if (!ctrl) _ids.Clear();
            if (a < 0 || b < 0)
            {
                _ids.Add(id);
                _anchor = id;
                return;
            }
            for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) _ids.Add(order[i]);
            return;
        }
        if (ctrl && !_ids.Remove(id)) _ids.Add(id);
        else if (!ctrl)
        {
            _ids.Clear();
            _ids.Add(id);
        }
        _anchor = id;
    }

    public void Clear()
    {
        _ids.Clear();
        _anchor = null;
    }

    /// <summary>Quita las entradas que ya no están en la bandeja.</summary>
    public void Prune(IEnumerable<Guid> alive)
    {
        var set = alive.ToHashSet();
        _ids.RemoveWhere(i => !set.Contains(i));
        if (_anchor is { } a && !set.Contains(a)) _anchor = null;
    }

    private static int IndexOf(IReadOnlyList<Guid> order, Guid id)
    {
        for (int i = 0; i < order.Count; i++)
            if (order[i] == id) return i;
        return -1;
    }
}

/// <summary>Efectos de arrastrar y soltar (mismos valores que <c>DragDropEffects</c>: se convierten con un cast).</summary>
[Flags]
public enum ShelfDropEffect { None = 0, Copy = 1, Move = 2, Link = 4 }

/// <summary>
/// Lo que se suelta en el notch (F5 · B4), ya decidido: archivos/carpetas (ganan a todo), un enlace http(s) (la URL de
/// un navegador gana a su título) o texto suelto. Un «enlace» que no es http(s) (<c>javascript:</c>, <c>file:</c>) entra
/// como TEXTO, que nunca se abre ni se ejecuta. PURO.
/// </summary>
public sealed record ShelfDrop(IReadOnlyList<string> Paths, string? Link, string? Text)
{
    public static readonly ShelfDrop None = new(Array.Empty<string>(), null, null);

    public bool IsEmpty => Paths.Count == 0 && Link is null && Text is null;

    /// <summary>Decide a partir de los formatos del arrastre: <c>FileDrop</c>, <c>UniformResourceLocatorW</c> y
    /// <c>UnicodeText</c> (cualquiera puede faltar). De una avalancha de archivos solo entran los que caben.</summary>
    public static ShelfDrop Parse(IReadOnlyList<string>? files, string? url, string? text)
    {
        var paths = files?.Where(f => !string.IsNullOrWhiteSpace(f)).Take(ShelfStore.MaxItems).ToArray();
        if (paths is { Length: > 0 }) return new ShelfDrop(paths, null, null);

        if (FirstLine(url) is { } u && IsHttp(u)) return new ShelfDrop(Array.Empty<string>(), u, null);
        if (string.IsNullOrWhiteSpace(text)) return None;
        var t = text.Trim();
        if (!t.Contains('\n') && IsHttp(t)) return new ShelfDrop(Array.Empty<string>(), t, null);
        return new ShelfDrop(Array.Empty<string>(), null, text);
    }

    /// <summary>
    /// Respuesta al arrastre. NUNCA <see cref="ShelfDropEffect.Move"/>: con «mover», el Explorador (o el editor) borraría
    /// el original tras soltar, y la bandeja solo guarda la ruta. Archivos: «vínculo» si el origen lo admite (es lo que se
    /// guarda), si no «copiar»; enlaces y texto: «copiar». Sin nada útil, o si es nuestro propio arrastre, nada.
    /// </summary>
    public static ShelfDropEffect Effect(ShelfDropEffect allowed, ShelfDrop drop, bool selfDrag)
    {
        ArgumentNullException.ThrowIfNull(drop);
        if (selfDrag || drop.IsEmpty) return ShelfDropEffect.None;
        var (first, second) = drop.Paths.Count > 0
            ? (ShelfDropEffect.Link, ShelfDropEffect.Copy)
            : (ShelfDropEffect.Copy, ShelfDropEffect.Link);
        return allowed.HasFlag(first) ? first : allowed.HasFlag(second) ? second : ShelfDropEffect.None;
    }

    /// <summary>Lo añade a la bandeja; devuelve las entradas nuevas (o la ya existente si era un duplicado de texto/enlace).</summary>
    public IReadOnlyList<ShelfItem> ApplyTo(ShelfStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (Paths.Count > 0) return store.AddPaths(Paths);
        if (Link is not null) return new[] { store.AddLink(Link) };
        if (Text is not null) return new[] { store.AddText(Text) };
        return Array.Empty<ShelfItem>();
    }

    internal static bool IsHttp(string s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? FirstLine(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var line = s.Split('\n')[0].Trim().TrimEnd('\0').Trim();
        return line.Length == 0 ? null : line;
    }
}

/// <summary>Lo que se arrastra fuera: rutas (<c>FileDrop</c>) y/o texto (enlaces y notas, una por línea).</summary>
public sealed record ShelfDragPayload(IReadOnlyList<string> Files, string? Text);

/// <summary>Arrastrar entradas fuera del notch (F5 · B4). PURO.</summary>
public static class ShelfDragOut
{
    /// <summary>Efectos que se ofrecen al destino: copiar o vincular, nunca mover (la entrada no debe quedarse colgando).</summary>
    public const ShelfDropEffect Allowed = ShelfDropEffect.Copy | ShelfDropEffect.Link;

    /// <summary>Si la entrada pulsada está en la selección, se arrastra toda la selección (en el orden de la lista); si no,
    /// solo ella.</summary>
    public static IReadOnlyList<ShelfItem> ItemsFor(ShelfItem pressed, IReadOnlyList<ShelfItem> ordered, ShelfSelection selection)
    {
        ArgumentNullException.ThrowIfNull(pressed);
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(selection);
        if (!selection.Contains(pressed.Id)) return new[] { pressed };
        return ordered.Where(i => selection.Contains(i.Id)).ToArray();
    }

    /// <summary>Rutas disponibles (las borradas/movidas no viajan) y el texto de enlaces y notas; null si no queda nada.</summary>
    public static ShelfDragPayload? Build(IEnumerable<ShelfItem> items, Func<ShelfItem, bool> available)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(available);
        var files = new List<string>();
        var texts = new List<string>();
        foreach (var i in items)
        {
            if (i.Kind is ShelfKind.Link or ShelfKind.Text) texts.Add(i.Value);
            else if (available(i)) files.Add(i.Value);
        }
        if (files.Count == 0 && texts.Count == 0) return null;
        return new ShelfDragPayload(files, texts.Count == 0 ? null : string.Join("\r\n", texts));
    }

    /// <summary>¿El ratón pulsado se movió lo bastante para empezar a arrastrar? Como Windows: fuera del rectángulo de
    /// <c>SM_CXDRAG × SM_CYDRAG</c> centrado en el punto de la pulsación.</summary>
    public static bool Exceeded(Point start, Point now, Size dragSize) =>
        Math.Abs(now.X - start.X) > dragSize.Width / 2 || Math.Abs(now.Y - start.Y) > dragSize.Height / 2;
}

/// <summary>Qué hace el clic en una entrada.</summary>
public enum ShelfOpenAction { None, OpenFile, OpenFolder, Reveal, OpenUrl, CopyText }

/// <summary>Lo que el notch pide al shell de Windows (doble en los tests). Cada método devuelve si pudo; nunca lanza.</summary>
public interface IShellLauncher
{
    bool OpenFile(string path);
    bool OpenFolder(string path);
    bool Reveal(string path);
    bool OpenUrl(string url);
    bool CopyText(string text);
}

/// <summary>
/// Clic en una entrada de la bandeja (F5 · B4). PURO. El texto NUNCA se ejecuta: se copia al portapapeles. Un archivo
/// solo se abre con el shell si su tipo está en una LISTA BLANCA de documentos, imágenes, audio y vídeo; todo lo demás
/// (programas, scripts, instaladores, accesos directos, .rdp, .appinstaller, temas, bases de Access, certificados, sin
/// extensión…) se enseña seleccionado en el Explorador. Una lista negra nunca está completa: Windows tiene decenas de
/// tipos que ejecutan código o instalan algo al abrirlos. Una entrada borrada o movida no hace nada (la página la marca).
/// Defensa ante un <c>shelf.json</c> manipulado: un «archivo» que no es una ruta absoluta local (las de red y de
/// dispositivo, <c>\\host\…</c>, <c>\\?\…</c>, <c>\\.\…</c>, tampoco) o un enlace que no es http(s) no hacen nada.
/// </summary>
public static class ShelfOpen
{
    // Tipos que se abren con su visor sin ejecutar código. Fuera a propósito: Office con macros (.docm/.xlsm/.pptm) y los
    // binarios antiguos (.doc/.xls/.ppt, que también las llevan), .rtf (objetos OLE), .html/.svg (script en el navegador)
    // y los comprimidos salvo .zip (los abre el Explorador).
    private static readonly HashSet<string> Openable = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documentos y texto
        ".pdf", ".txt", ".md", ".csv", ".log", ".json", ".docx", ".xlsx", ".pptx", ".odt", ".ods", ".odp", ".epub",
        // Imágenes
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".heic", ".heif", ".avif", ".tif", ".tiff", ".ico",
        // Audio
        ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma",
        // Vídeo
        ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv",
        // Comprimidos que abre el propio Explorador
        ".zip",
    };

    /// <summary>
    /// ¿NO se puede abrir con el shell (se enseña en el Explorador)? Cierto salvo que el tipo esté en la lista blanca de
    /// documentos, imágenes, audio y vídeo. La extensión se mira como la ve Windows al abrir: sin puntos ni espacios
    /// finales (<c>evil.exe.</c> abre <c>evil.exe</c>) y sin flujo alternativo de NTFS (<c>evil.exe::$DATA</c> es el
    /// propio <c>evil.exe</c>). Cualquier flujo alternativo cuenta como programa (<c>informe.pdf:x.exe</c> no es un
    /// documento que abrir), igual que una ruta vacía o un archivo sin extensión.
    /// </summary>
    public static bool IsExecutable(string path)
    {
        if (LastSegment(path) is not { } name) return true;
        int colon = name.IndexOf(':');
        return colon >= 0 || !Openable.Contains(ExtensionOf(name));
    }

    /// <summary>
    /// Extensión que usará el shell: del último segmento de la ruta se corta en el primer «:» (flujo ADS; la letra de
    /// unidad va antes del último separador) y se quitan puntos y espacios finales. "" sin extensión; <c>null</c> si la
    /// ruta está vacía. PURO.
    /// </summary>
    internal static string? EffectiveExtension(string? path)
    {
        if (LastSegment(path) is not { } name) return null;
        int colon = name.IndexOf(':');
        return ExtensionOf(colon >= 0 ? name[..colon] : name);
    }

    // Último segmento de la ruta, sin la letra de unidad de «C:evil.exe»; null si la ruta está vacía.
    private static string? LastSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        int sep = path.LastIndexOfAny(new[] { '\\', '/' });
        if (sep >= 0) return path[(sep + 1)..];
        return path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]) ? path[2..] : path;
    }

    private static string ExtensionOf(string name)
    {
        name = name.TrimEnd('.', ' ');
        int dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..];
    }

    public static ShelfOpenAction Decide(ShelfItem item, bool available)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!available) return ShelfOpenAction.None;
        return item.Kind switch
        {
            ShelfKind.Text => ShelfOpenAction.CopyText,
            ShelfKind.Link => ShelfDrop.IsHttp(item.Value) ? ShelfOpenAction.OpenUrl : ShelfOpenAction.None,
            _ when !IsLocalPath(item.Value) => ShelfOpenAction.None,
            ShelfKind.Folder => ShelfOpenAction.OpenFolder,
            _ => IsExecutable(item.Value) ? ShelfOpenAction.Reveal : ShelfOpenAction.OpenFile,
        };
    }

    /// <summary>Decide y lo pide al shell. <c>true</c> si se hizo algo.</summary>
    public static bool Run(ShelfItem item, bool available, IShellLauncher shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        return Decide(item, available) switch
        {
            ShelfOpenAction.OpenFile => shell.OpenFile(item.Value),
            ShelfOpenAction.OpenFolder => shell.OpenFolder(item.Value),
            ShelfOpenAction.Reveal => shell.Reveal(item.Value),
            ShelfOpenAction.OpenUrl => shell.OpenUrl(item.Value),
            ShelfOpenAction.CopyText => shell.CopyText(item.Value),
            _ => false,
        };
    }

    private static bool IsLocalPath(string p)
    {
        if (ShelfStore.IsUncOrDevicePath(p)) return false;   // ni NTLM contra un host ajeno ni rutas de dispositivo
        try { return Path.IsPathFullyQualified(p) && !p.Contains("://", StringComparison.Ordinal); }
        catch (ArgumentException) { return false; }
    }
}

/// <summary>Valores por defecto de la bandeja en la app (F5 · B4).</summary>
public static class ShelfDefaults
{
    /// <summary><c>%APPDATA%\ClaudeBarWin\notch\shelf.json</c>.</summary>
    public static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeBarWin", "notch", "shelf.json");

    /// <summary>
    /// Existencia para <see cref="ShelfStore"/>: archivo o carpeta local. Una ruta de red o de dispositivo
    /// (<c>\\servidor\…</c>, <c>\\?\…</c>, <c>\\.\…</c>) se da por NO disponible sin tocarla: la bandeja ya no las acepta (un
    /// <c>shelf.json</c> antiguo o manipulado podría traerlas) y comprobarla colgaría el hilo de UI si el recurso está
    /// caído. Una letra de unidad de red, extraíble, de CD o sin montar (<see cref="UntouchedDrive"/>) se da por
    /// disponible sin tocarla: un USB dormido o una unidad mapeada caída también cuelgan.
    /// </summary>
    public static bool Exists(string path) => Exists(path, DriveTypeOf, p => File.Exists(p) || Directory.Exists(p));

    /// <summary>Como <see cref="Exists(string)"/> con el tipo de unidad y la comprobación de disco inyectados (tests).</summary>
    internal static bool Exists(string path, Func<string, DriveType> driveType, Func<string, bool> onDisk)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (ShelfStore.IsUncOrDevicePath(path)) return false;
        if (path.Length >= 2 && path[1] == ':' && char.IsAsciiLetter(path[0]) && UntouchedDrive(SafeDriveType(driveType, path[..1] + @":\")))
            return true;
        return onDisk(path);
    }

    /// <summary>Unidades que no se tocan desde el hilo de UI (se dan por disponibles).</summary>
    internal static bool UntouchedDrive(DriveType t) =>
        t is DriveType.Network or DriveType.Removable or DriveType.CDRom or DriveType.NoRootDirectory;

    // GetDriveType no lee la unidad: solo pregunta al gestor de montajes qué es esa letra.
    private static DriveType DriveTypeOf(string root) => new DriveInfo(root).DriveType;

    private static DriveType SafeDriveType(Func<string, DriveType> driveType, string root)
    {
        try { return driveType(root); }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { return DriveType.Unknown; }
    }
}
