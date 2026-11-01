using System.Drawing;

namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Control de la página Uso bajo el ratón o pulsado.</summary>
public enum UsageControl { None, Tile, Segment, Range, Legend, Plot, ProChip }

/// <summary>Control de la página Uso (<see cref="Index"/>: tarjeta 0 = 5 h / 1 = 7 d, segmento, rango o serie de la leyenda).</summary>
public readonly record struct UsageHit(UsageControl Control, int Index = 0);

/// <summary>
/// Rectángulos de la página Uso en px físicos (de <see cref="UsageLayout.Compute"/>): cabecera, columna de números a la
/// izquierda (tarjetas de 5 h y 7 d con su anillo, filas por modelo) y, a la derecha, la gráfica con su barra (modo y
/// rangos), el área, el eje de tiempo y la leyenda.
/// </summary>
public readonly record struct UsageRects(RectangleF Header, RectangleF Left, RectangleF Right, RectangleF Toolbar,
    RectangleF Plot, RectangleF Axis, RectangleF Legend, float Scale)
{
    /// <summary>Tarjeta de la ventana <paramref name="i"/> (0 = 5 h, 1 = 7 d).</summary>
    public RectangleF Tile(int i) => new(Left.Left, Left.Top + i * (UsageLayout.TileHeight + UsageLayout.TileGap) * Scale,
        Left.Width, UsageLayout.TileHeight * Scale);

    /// <summary>Anillo de la tarjeta <paramref name="i"/>.</summary>
    public RectangleF Ring(int i)
    {
        var t = Tile(i);
        float d = UsageLayout.Ring * Scale;
        return new RectangleF(t.Left + UsageLayout.TileInset * Scale, t.Top + (t.Height - d) / 2f, d, d);
    }

    /// <summary>Columna de texto de la tarjeta <paramref name="i"/> (a la derecha del anillo).</summary>
    public RectangleF TileText(int i)
    {
        var t = Tile(i);
        float x = Ring(i).Right + UsageLayout.RingGap * Scale;
        return RectangleF.FromLTRB(x, t.Top + UsageLayout.TileTextInset * Scale, Math.Max(x, t.Right - UsageLayout.TileInset * Scale),
            t.Bottom - UsageLayout.TileTextInset * Scale);
    }

    /// <summary>Fila <paramref name="i"/> bajo las tarjetas (Opus, Sonnet, uso extra).</summary>
    public RectangleF Row(int i)
    {
        float top = Tile(1).Bottom + UsageLayout.TileGap * Scale + i * UsageLayout.ModelRow * Scale;
        return new RectangleF(Left.Left + UsageLayout.TileInset * Scale, top, Left.Width - 2 * UsageLayout.TileInset * Scale,
            UsageLayout.ModelRow * Scale);
    }

    /// <summary>Segmento <paramref name="i"/> del selector Gasto | Cuota.</summary>
    public RectangleF Segment(int i) => new(Toolbar.Left + i * UsageLayout.SegmentWidth * Scale, Toolbar.Top,
        UsageLayout.SegmentWidth * Scale, Toolbar.Height);

    /// <summary>Botón del rango <paramref name="i"/> (orden de <see cref="UsageRanges.All"/>), alineados a la derecha.</summary>
    public RectangleF Chip(int i)
    {
        int n = UsageRanges.All.Count;
        float w = UsageLayout.ChipWidth * Scale, gap = UsageLayout.ChipGap * Scale;
        return new RectangleF(Toolbar.Right - (n - i) * w - (n - 1 - i) * gap, Toolbar.Top, w, Toolbar.Height);
    }

    /// <summary>Chip «Pro» de la gráfica sin el Pro, a la derecha de la barra.</summary>
    public RectangleF ProChip
    {
        get
        {
            float w = UsageLayout.ProChipWidth * Scale, h = UsageLayout.ProChipHeight * Scale;
            return new RectangleF(Toolbar.Right - w, Toolbar.Top + (Toolbar.Height - h) / 2f, w, h);
        }
    }

    /// <summary>Sin el Pro: el rango fijo, en texto, a la izquierda del chip.</summary>
    public RectangleF RangeLabel => RectangleF.FromLTRB(Segment(1).Right + UsageLayout.ToolGap * Scale, Toolbar.Top,
        Math.Max(Segment(1).Right, ProChip.Left - UsageLayout.ToolGap * Scale), Toolbar.Bottom);

    /// <summary>Hueco <paramref name="i"/> de la leyenda (<paramref name="slots"/> iguales, por defecto
    /// <see cref="UsageLayout.LegendSlots"/>); con <paramref name="span"/> &gt; 1 ocupa los siguientes también.</summary>
    public RectangleF LegendSlot(int i, int span = 1, int slots = UsageLayout.LegendSlots)
    {
        float w = Legend.Width / Math.Max(1, slots);
        return new RectangleF(Legend.Left + i * w, Legend.Top, w * Math.Max(1, span), Legend.Height);
    }
}

/// <summary>
/// Geometría de la página Uso. PURA: la MISMA para pintar y para acertar el ratón. Va a su tamaño final centrada en la
/// pastilla y anclada arriba: mientras el expandido crece al cambiar de página, la página se destapa (recortada a la pastilla)
/// en vez de encogerse y estirarse. Todo en px lógicos × escala.
/// </summary>
public static class UsageLayout
{
    /// <summary>Ancho de la pastilla en la página Uso (más que las demás: la gráfica necesita aire). Cabe en la superficie de
    /// 700 con orejas, squash y sombra.</summary>
    public const float PageWidth = 560f;

    /// <summary>Margen lateral de las páginas del expandido (el de <c>NotchContentPainter</c>).</summary>
    public const float PadX = 18f;

    public const float Header = 22f, HeaderGap = 6f;
    public const float LeftColumn = 192f, ColumnGap = 18f;
    public const float TileHeight = 46f, TileGap = 6f, TileInset = 6f, TileTextInset = 1f, Ring = 34f, RingGap = 10f;
    public const float ModelRow = 16f;
    public const int Rows = 3;
    public const float Toolbar = 20f, ToolbarGap = 6f, Axis = 14f, LegendGap = 4f, Legend = 16f;
    public const float SegmentWidth = 74f, ChipWidth = 28f, ChipGap = 2f, ToolGap = 8f, ProChipWidth = 34f, ProChipHeight = 16f;
    public const int LegendSlots = 4;

    /// <summary>Huecos de la leyenda: en el gasto, uno por familia y al menos tres (con pocas, más aire para su gasto); en la
    /// cuota, cuatro (5 h, 7 d y la proyección a lo ancho de los otros dos).</summary>
    public static int LegendSlotsFor(UsageChartMode mode, int series) =>
        mode == UsageChartMode.Spend ? Math.Clamp(series, 3, LegendSlots) : LegendSlots;

    /// <summary>Alto del cuerpo: dos tarjetas y tres filas a la izquierda (152) con aire debajo; barra, gráfica, eje y
    /// leyenda a la derecha.</summary>
    public const float Body = 160f;

    /// <summary>Alto del área de la gráfica (lo que deja el cuerpo tras la barra, el eje y la leyenda).</summary>
    public const float Plot = Body - Toolbar - ToolbarGap - Axis - LegendGap - Legend;

    /// <summary>Alto del contenido de la página (sin márgenes, aviso ni puntos).</summary>
    public const float ContentHeight = Header + HeaderGap + Body;

    /// <summary>Ancho del contenido (la pastilla menos los márgenes).</summary>
    public const float ContentWidth = PageWidth - 2 * PadX;

    /// <summary>Rectángulos de la página dentro de <paramref name="inner"/> (la zona de contenido del expandido).</summary>
    public static UsageRects Compute(RectangleF inner, float s)
    {
        float w = ContentWidth * s, x = inner.Left + (inner.Width - w) / 2f, top = inner.Top;
        var header = new RectangleF(x, top, w, Header * s);
        float bodyTop = header.Bottom + HeaderGap * s;
        var left = new RectangleF(x, bodyTop, LeftColumn * s, Body * s);
        float rx = left.Right + ColumnGap * s;
        var right = RectangleF.FromLTRB(rx, bodyTop, x + w, bodyTop + Body * s);
        var toolbar = new RectangleF(rx, bodyTop, right.Width, Toolbar * s);
        var plot = new RectangleF(rx, toolbar.Bottom + ToolbarGap * s, right.Width, Plot * s);
        var axis = new RectangleF(rx, plot.Bottom, right.Width, Axis * s);
        var legend = new RectangleF(rx, axis.Bottom + LegendGap * s, right.Width, Legend * s);
        return new UsageRects(header, left, right, toolbar, plot, axis, legend, s);
    }

    /// <summary>
    /// Control bajo <paramref name="p"/>. Las tarjetas y el selector de modo valen siempre (navegar no es Pro); los rangos, la
    /// leyenda y el hover de la gráfica solo con <paramref name="interactive"/> (el Pro, en la ventana interactiva); sin el
    /// Pro (<paramref name="locked"/>) el chip «Pro» y el rango en texto abren la tarjeta. <paramref name="series"/>: series
    /// de la leyenda (las de más, nada), repartida en <paramref name="legendSlots"/> huecos (<see cref="LegendSlotsFor"/>).
    /// Sin <paramref name="modes"/> («Mostrar gasto estimado» apagado) no hay selector de modo: ni se pinta ni responde.
    /// </summary>
    public static UsageHit HitTest(PointF p, UsageRects r, int series, bool interactive, bool locked, int legendSlots = LegendSlots,
        bool modes = true)
    {
        for (int i = 0; i < 2; i++)
            if (r.Tile(i).Contains(p)) return new(UsageControl.Tile, i);
        for (int i = 0; modes && i < 2; i++)
            if (r.Segment(i).Contains(p)) return new(UsageControl.Segment, i);
        if (locked)
        {
            if (r.ProChip.Contains(p) || r.RangeLabel.Contains(p)) return new(UsageControl.ProChip);
            return default;
        }
        if (!interactive) return default;
        for (int i = 0; i < UsageRanges.All.Count; i++)
            if (r.Chip(i).Contains(p)) return new(UsageControl.Range, i);
        for (int i = 0; i < Math.Min(series, legendSlots); i++)
            if (r.LegendSlot(i, 1, legendSlots).Contains(p)) return new(UsageControl.Legend, i);
        var plot = RectangleF.Inflate(r.Plot, 0f, UsageLayout.Axis * r.Scale / 2f);
        if (plot.Contains(p)) return new(UsageControl.Plot);
        return default;
    }
}
