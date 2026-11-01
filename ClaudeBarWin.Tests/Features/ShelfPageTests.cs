using System.Drawing;
using ClaudeBarWin.Services.Notch;
using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// Página Bandeja (F5 · B4), lógica pura: geometría de las tarjetas y sus flechas compartida entre pintar y acertar el clic
/// (<see cref="ShelfLayout"/>), paginación con 1, 3, 8 y 30 entradas, orden y disponibilidad cacheada
/// (<see cref="ShelfSnapshot"/>), selección con Ctrl/Mayús (<see cref="ShelfSelection"/>),
/// lo que se suelta (<see cref="ShelfDrop"/>), lo que se arrastra fuera (<see cref="ShelfDragOut"/>) y qué hace el clic
/// (<see cref="ShelfOpen"/>, con un lanzador falso: nada se abre de verdad).
/// </summary>
public sealed class ShelfPageTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 29, 9, 0, 0);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cbw-shelfpage-" + Guid.NewGuid().ToString("N"));
    private readonly HashSet<string> _existing = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _now = T0;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private ShelfStore Store() =>
        new(Path.Combine(_dir, "shelf.json"), p => _existing.Contains(p), _ => false, () => (_now = _now.AddSeconds(1)));

    private static ShelfItem Item(ShelfKind kind, string value, string? display = null) =>
        new(Guid.NewGuid(), kind, value, display ?? value, T0);

    /// <summary>Zona de contenido de la página Bandeja con <paramref name="n"/> entradas (la misma cuenta que el painter).</summary>
    private static RectangleF Inner(int n, float s, bool withAlert = false)
    {
        var size = NotchLayout.PageSize(NotchPage.Shelf, new PageRows(0, 0, n), withAlert, withDots: true, s);
        float pad = 18f * s, top = NotchLayout.ExpandedPadTop * s;
        if (withAlert) top += (NotchLayout.ExpandedLine + NotchLayout.ExpandedAlertGap) * s;
        float bottom = size.Height - (NotchLayout.ExpandedPadBottom + NotchLayout.PageDotsRow) * s;
        return RectangleF.FromLTRB(100f + pad, top, 100f + size.Width - pad, bottom);
    }

    private static PointF Center(RectangleF r) => new(r.X + r.Width / 2f, r.Y + r.Height / 2f);

    // ---------------------------------------------------------------- ShelfLayout (tarjetas)

    [Theory]
    [InlineData(1, 1f, false)]
    [InlineData(3, 1.25f, false)]
    [InlineData(8, 1.5f, false)]
    [InlineData(30, 1f, true)]
    [InlineData(30, 1.5f, true)]
    public void Page_height_fits_the_header_and_one_row_of_cards(int n, float s, bool alert)
    {
        var inner = Inner(n, s, alert);
        var r = ShelfLayout.Compute(inner, s, n);
        Assert.True(r.Header.Height > 0 && r.ClearButton.Width > 0);
        Assert.True(r.Header.Contains(Center(r.ClearButton)), "«Vaciar» va en la cabecera");
        Assert.True(r.Strip.Top >= r.Header.Bottom - 0.01f, "las tarjetas van bajo la cabecera");
        Assert.True(r.Strip.Bottom <= inner.Bottom + 0.01f, $"la fila cabe: {r.Strip.Bottom} > {inner.Bottom}");
        Assert.True(inner.Bottom - r.Strip.Bottom < 1f, "la página no crece de más");
        for (int i = 0; i < Math.Min(n, r.Slots); i++)
        {
            var card = r.Card(i, 0f);
            Assert.True(card.Left >= inner.Left - 0.5f && card.Right <= inner.Right + 0.5f, $"tarjeta {i} dentro de la página");
            var thumb = r.Thumb(card);
            Assert.Equal(ShelfLayout.Thumb * s, thumb.Width, 3);
            Assert.True(card.Contains(thumb), "la miniatura va dentro de su tarjeta");
            Assert.True(r.Name(card).Top >= thumb.Bottom - 0.01f, "el nombre, bajo la miniatura");
            Assert.True(r.Detail(card).Top >= r.Name(card).Bottom - 0.01f, "el detalle, bajo el nombre");
            Assert.True(r.Detail(card).Bottom <= card.Bottom + 0.01f);
        }
    }

    [Theory]
    [InlineData(1, false, 0)]
    [InlineData(3, false, 0)]
    [InlineData(8, true, 3)]
    [InlineData(30, true, 25)]
    public void Cards_paginate_with_arrows_only_when_they_overflow(int n, bool overflows, int maxFirst)
    {
        var r = ShelfLayout.Compute(Inner(n, 1f), 1f, n);
        Assert.Equal(overflows, r.Overflows);
        Assert.Equal(5, r.Slots);                                   // 444 px de fila: cinco tarjetas, con flechas o sin ellas
        Assert.Equal(maxFirst, ShelfLayout.MaxFirst(n));
        Assert.Equal(overflows, !r.Previous.IsEmpty);
        Assert.Equal(overflows, !r.Next.IsEmpty);
        if (overflows)
        {
            Assert.True(r.Previous.Right <= r.Viewport.Left, "‹ no tapa las tarjetas");
            Assert.True(r.Next.Left >= r.Viewport.Right, "› no tapa las tarjetas");
        }

        // Quieta al principio: las que caben, enteras en el visor, y ninguna más asoma.
        var (first, last) = r.Range(0f);
        Assert.Equal(0, first);
        Assert.Equal(Math.Min(n, r.Slots) - 1, last);
        for (int i = first; i <= last; i++)
        {
            var card = r.Card(i, 0f);
            Assert.True(card.Left >= r.Viewport.Left - 0.5f && card.Right <= r.Viewport.Right + 0.5f, $"tarjeta {i} entera");
        }

        // Al final: la última se ve entera.
        float end = ShelfLayout.ScrollFor(maxFirst);
        Assert.Equal(n - 1, r.Range(end).Last);
        Assert.True(r.Card(n - 1, end).Right <= r.Viewport.Right + 0.5f);
    }

    [Fact]
    public void Arrows_turn_a_page_and_stop_at_the_ends()
    {
        // 8 tarjetas, 5 a la vista: › lleva a la última página (3) y ‹ vuelve al principio.
        Assert.Equal(3, ShelfLayout.Page(0, 8, +1));
        Assert.Equal(0, ShelfLayout.Page(3, 8, -1));
        Assert.True(ShelfLayout.CanPage(0, 8, +1));
        Assert.False(ShelfLayout.CanPage(0, 8, -1));
        Assert.False(ShelfLayout.CanPage(3, 8, +1));

        // 30: de cinco en cinco hasta la 25.
        int first = 0;
        var pages = new List<int>();
        while (ShelfLayout.CanPage(first, 30, +1)) pages.Add(first = ShelfLayout.Page(first, 30, +1));
        Assert.Equal(new[] { 5, 10, 15, 20, 25 }, pages);

        // Sin desbordar (1 o 3), las flechas no mueven nada.
        Assert.Equal(0, ShelfLayout.Page(0, 3, +1));
        Assert.False(ShelfLayout.CanPage(0, 1, +1));
        Assert.False(ShelfLayout.CanPage(0, 3, -1));

        // La rueda va de una en una y se acota; el desplazamiento es de tarjetas enteras.
        Assert.Equal(1, ShelfLayout.Step(0, 8, 1));
        Assert.Equal(3, ShelfLayout.Step(2, 8, 5));
        Assert.Equal(0, ShelfLayout.Step(1, 8, -4));
        Assert.Equal(3 * ShelfLayout.CardStride, ShelfLayout.ScrollFor(3), 3);
    }

    [Theory]
    [InlineData(ShelfControl.Previous, true, true)]                 // pasar páginas deprisa
    [InlineData(ShelfControl.Next, true, true)]
    [InlineData(ShelfControl.Card, true, false)]                    // abriría la tarjeta dos veces
    [InlineData(ShelfControl.Clear, true, false)]                   // «Quitar (N)» ya sin selección = vaciar TODO
    [InlineData(ShelfControl.Card, false, true)]
    [InlineData(ShelfControl.Clear, false, true)]
    [InlineData(ShelfControl.Next, false, true)]
    public void A_double_click_only_counts_as_a_press_on_the_arrows(ShelfControl control, bool doubleClick, bool kept)
    {
        var hit = new ShelfHit(control, control == ShelfControl.Card ? 2 : -1);
        Assert.Equal(kept ? hit : default, ShelfClickRules.Press(hit, doubleClick));
    }

    [Fact]
    public void Double_click_on_Remove_N_never_empties_the_whole_shelf()
    {
        // Dos seleccionadas, doble clic en «Quitar (2)». 1.er clic: pulsar (con selección) y soltar → quita las dos.
        var first = ShelfClickRules.Press(new ShelfHit(ShelfControl.Clear), doubleClick: false);
        Assert.Equal(ShelfControl.Clear, first.Control);
        Assert.True(ShelfClickRules.ClearStillMeans(selectionAtPress: true, selectedNow: 2));
        // 2.º clic (WM_LBUTTONDBLCLK): no cuenta como pulsación, y al soltar no coincide con nada pulsado.
        Assert.Equal(default, ShelfClickRules.Press(new ShelfHit(ShelfControl.Clear), doubleClick: true));
        // Y aunque llegara: pulsado con selección y soltado sin ella, el botón cambió de significado → nada.
        Assert.False(ShelfClickRules.ClearStillMeans(selectionAtPress: true, selectedNow: 0));
        Assert.False(ShelfClickRules.ClearStillMeans(selectionAtPress: false, selectedNow: 1));
        Assert.True(ShelfClickRules.ClearStillMeans(selectionAtPress: false, selectedNow: 0));   // «Vaciar» de siempre
    }

    [Fact]
    public void HitTest_finds_cards_arrows_and_clear_with_the_same_geometry()
    {
        const float s = 1.25f;
        var inner = Inner(8, s);
        var r = ShelfLayout.Compute(inner, s, 8);
        ShelfHit Hit(PointF p, float scroll = 0f) => ShelfLayout.HitTest(p, inner, s, 8, scroll);

        Assert.Equal(new ShelfHit(ShelfControl.Clear), Hit(Center(r.ClearButton)));
        Assert.Equal(new ShelfHit(ShelfControl.Previous), Hit(Center(r.Previous)));
        Assert.Equal(new ShelfHit(ShelfControl.Next), Hit(Center(r.Next)));
        Assert.Equal(new ShelfHit(ShelfControl.Card, 0), Hit(Center(r.Card(0, 0f))));
        Assert.Equal(new ShelfHit(ShelfControl.Card, 4), Hit(Center(r.Card(4, 0f))));

        // En la última página, el mismo sitio es otra tarjeta (la cuenta usa el desplazamiento que se pinta).
        float end = ShelfLayout.ScrollFor(3);
        Assert.Equal(new ShelfHit(ShelfControl.Card, 3), Hit(Center(r.Card(0, 0f)), end));
        Assert.Equal(new ShelfHit(ShelfControl.Card, 7), Hit(Center(r.Card(4, 0f)), end));

        // A medio deslizar, la tarjeta que asoma por la derecha no se acierta fuera del visor.
        float half = ShelfLayout.CardStride / 2f;
        var peeking = r.Card(5, half);
        Assert.True(peeking.Right > r.Viewport.Right);
        Assert.Equal(new ShelfHit(ShelfControl.Card, 5), Hit(new PointF(r.Viewport.Right - 2f, Center(peeking).Y), half));
        Assert.Equal(default, Hit(new PointF(r.Viewport.Right + 2f, Center(peeking).Y), half));

        // El título, el hueco entre dos tarjetas y fuera de la página: nada.
        Assert.Equal(default, Hit(new PointF(r.Header.Left + 2, Center(r.Header).Y)));
        var card0 = r.Card(0, 0f);
        Assert.Equal(default, Hit(new PointF(card0.Right + ShelfLayout.CardGap * s / 2f, Center(card0).Y)));
        Assert.Equal(default, Hit(new PointF(inner.Left - 5, inner.Top + 30)));
    }

    [Fact]
    public void Without_overflow_there_are_no_arrows_to_hit()
    {
        var inner = Inner(3, 1f);
        var r = ShelfLayout.Compute(inner, 1f, 3);
        var full = ShelfLayout.Compute(inner, 1f, 8);   // donde irían las flechas
        Assert.Equal(new ShelfHit(ShelfControl.Card, 2), ShelfLayout.HitTest(Center(r.Card(2, 0f)), inner, 1f, 3, 0f));
        Assert.Equal(default, ShelfLayout.HitTest(Center(full.Next), inner, 1f, 3, 0f));
        // Donde iría una quinta tarjeta: sitio vacío.
        var empty = new PointF(r.Viewport.Left + 4 * ShelfLayout.CardStride + ShelfLayout.CardWidth / 2f, Center(r.Card(0, 0f)).Y);
        Assert.Equal(default, ShelfLayout.HitTest(empty, inner, 1f, 3, 0f));
        Assert.True(r.Card(0, 0f).Left - inner.Left < 1f, "sin flechas, las tarjetas empiezan a la izquierda, bajo el título");
    }

    [Fact]
    public void Cards_do_not_move_while_the_pill_grows()
    {
        // Mientras se abre, la zona de contenido es más estrecha (centrada igual): la fila no se recoloca.
        var full = Inner(8, 1f);
        var narrow = RectangleF.Inflate(full, -120f, 0f);
        var a = ShelfLayout.Compute(full, 1f, 8);
        var b = ShelfLayout.Compute(narrow, 1f, 8);
        Assert.Equal(a.Card(2, 0f), b.Card(2, 0f));
        Assert.Equal(a.Next, b.Next);
        Assert.Equal(a.Viewport, b.Viewport);
    }

    [Fact]
    public void Empty_shelf_has_no_clear_button_cards_nor_arrows()
    {
        var inner = Inner(0, 1f);
        var r = ShelfLayout.Compute(inner, 1f, 0);
        Assert.False(r.Overflows);
        Assert.True(r.Previous.IsEmpty && r.Next.IsEmpty);
        Assert.Equal((0, -1), r.Range(0f));
        Assert.Equal(RectangleF.Empty, r.Card(0, 0f));
        Assert.Equal(default, ShelfLayout.HitTest(Center(r.ClearButton), inner, 1f, 0, 0f));
        Assert.Equal(default, ShelfLayout.HitTest(new PointF(inner.Left + 30, r.List.Top + 10), inner, 1f, 0, 0f));
    }

    [Theory]
    [InlineData(1f, 48)]
    [InlineData(1.25f, 60)]
    [InlineData(1.5f, 72)]
    public void Thumbnails_are_requested_in_physical_pixels(float s, int px) => Assert.Equal(px, ShelfLayout.ThumbPx(s));

    [Theory]
    [InlineData(3, 5, 0, 0, 3, 0)]      // caben todas
    [InlineData(8, 5, 0, 0, 4, 4)]      // la última fila dice «+4 más»
    [InlineData(8, 5, 2, 2, 4, 2)]
    [InlineData(8, 5, 3, 3, 5, 0)]      // al final: sin «+N más», la última se alcanza
    [InlineData(8, 5, 99, 3, 5, 0)]     // desplazamiento fuera de rango: se acota
    [InlineData(0, 5, 0, 0, 0, 0)]
    public void Window_shows_rows_and_the_more_line(int count, int rows, int offset, int first, int shown, int more)
    {
        // La ventana de filas con «+N más» la usa la lista de Tareas.
        Assert.Equal(new ShelfWindow(first, shown, more), ShelfLayout.Window(count, rows, offset));
    }

    // ---------------------------------------------------------------- ShelfSnapshot

    [Fact]
    public void Snapshot_lists_newest_first_and_caches_availability()
    {
        _existing.Add(@"C:\a.txt");
        var store = Store();
        store.AddPaths(new[] { @"C:\a.txt" });
        store.AddLink("https://example.com/x");
        store.AddPaths(new[] { @"C:\gone.txt" });

        int checks = 0;
        var snap = ShelfSnapshot.Take(store, i => { checks++; return store.IsAvailable(i); });
        Assert.Equal(new[] { "gone.txt", "example.com/x", "a.txt" }, snap.Items.Select(i => i.Display));
        Assert.Equal(new[] { false, true, true }, snap.Available);
        Assert.Equal(3, checks);

        // Consultar la caché no vuelve a tocar el disco (se pinta en cada frame).
        Assert.False(snap.IsAvailable(snap.Items[0]));
        Assert.True(snap.IsAvailable(snap.Items[2]));
        Assert.Equal(3, checks);

        // El archivo se borró después de soltarlo: al intentar abrirlo se re-comprueba y se marca.
        _existing.Remove(@"C:\a.txt");
        var marked = snap.WithAvailability(snap.Items[2].Id, false);
        Assert.False(marked.IsAvailable(snap.Items[2]));
        Assert.True(snap.IsAvailable(snap.Items[2]), "las instantáneas son inmutables");
        Assert.False(ShelfSnapshot.Empty.IsAvailable(snap.Items[2]));
    }

    // ---------------------------------------------------------------- ShelfSelection

    private static readonly Guid[] Ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();

    [Fact]
    public void Ctrl_toggles_and_Shift_selects_a_range_from_the_anchor()
    {
        var sel = new ShelfSelection();
        sel.Click(Ids[1], Ids, ctrl: true, shift: false);
        sel.Click(Ids[4], Ids, ctrl: true, shift: false);
        Assert.Equal(2, sel.Count);
        sel.Click(Ids[1], Ids, ctrl: true, shift: false);           // Ctrl otra vez: la quita
        Assert.False(sel.Contains(Ids[1]));
        Assert.True(sel.Contains(Ids[4]));

        sel.Click(Ids[2], Ids, ctrl: false, shift: true);           // ancla = 1 (último Ctrl-clic): 1..2, sustituye
        Assert.Equal(new[] { Ids[1], Ids[2] }, Ids.Where(sel.Contains));
        sel.Click(Ids[5], Ids, ctrl: false, shift: true);           // el ancla no se mueve con Mayús
        Assert.Equal(Ids[1..6], Ids.Where(sel.Contains));
        sel.Click(Ids[0], Ids, ctrl: true, shift: true);            // Ctrl+Mayús: añade el rango 0..1
        Assert.Equal(6, sel.Count);
    }

    [Fact]
    public void Shift_without_anchor_selects_just_that_entry()
    {
        var sel = new ShelfSelection();
        sel.Click(Ids[3], Ids, ctrl: false, shift: true);
        Assert.Equal(new[] { Ids[3] }, Ids.Where(sel.Contains));
    }

    [Fact]
    public void Prune_drops_entries_that_left_the_shelf()
    {
        var sel = new ShelfSelection();
        sel.Click(Ids[0], Ids, true, false);
        sel.Click(Ids[2], Ids, true, false);
        sel.Prune(new[] { Ids[2], Ids[3] });
        Assert.Equal(new[] { Ids[2] }, Ids.Where(sel.Contains));
        sel.Clear();
        Assert.Equal(0, sel.Count);
        sel.Click(Ids[4], Ids, false, true);                         // tras Clear no queda ancla
        Assert.Equal(new[] { Ids[4] }, Ids.Where(sel.Contains));
    }

    // ---------------------------------------------------------------- ShelfDrop

    [Fact]
    public void Files_win_over_text_and_blank_paths_are_skipped()
    {
        var d = ShelfDrop.Parse(new[] { @"C:\a.txt", " ", @"C:\b" }, "https://x.com", "hola");
        Assert.Equal(new[] { @"C:\a.txt", @"C:\b" }, d.Paths);
        Assert.Null(d.Link);
        Assert.Null(d.Text);
    }

    [Fact]
    public void A_flood_of_files_keeps_only_what_fits_in_the_shelf()
    {
        var many = Enumerable.Range(0, 500).Select(i => $@"C:\f{i}.txt").ToArray();
        Assert.Equal(ShelfStore.MaxItems, ShelfDrop.Parse(many, null, null).Paths.Count);
    }

    [Theory]
    [InlineData(null, "https://example.com/a", "https://example.com/a", null)]          // URL de un navegador
    [InlineData("https://ex.com/a\r\nTítulo", "Título", "https://ex.com/a", null)]       // la URL gana al título
    [InlineData(null, "  https://ex.com/b  ", "https://ex.com/b", null)]                 // texto que es un enlace
    [InlineData(null, "javascript:alert(1)", null, "javascript:alert(1)")]              // nunca un enlace: texto
    [InlineData("file:///C:/x.exe", "file:///C:/x.exe", null, "file:///C:/x.exe")]
    [InlineData(null, "dos\nlíneas https://ex.com", null, "dos\nlíneas https://ex.com")]
    [InlineData(null, "   ", null, null)]
    public void Urls_become_links_only_if_http(string? url, string? text, string? link, string? asText)
    {
        var d = ShelfDrop.Parse(null, url, text);
        Assert.Empty(d.Paths);
        Assert.Equal(link, d.Link);
        Assert.Equal(asText, d.Text);
        Assert.Equal(link is null && asText is null, d.IsEmpty);
    }

    [Theory]
    [InlineData(ShelfDropEffect.Copy | ShelfDropEffect.Move | ShelfDropEffect.Link, true, ShelfDropEffect.Link)]
    [InlineData(ShelfDropEffect.Copy | ShelfDropEffect.Move, true, ShelfDropEffect.Copy)]
    [InlineData(ShelfDropEffect.Move, true, ShelfDropEffect.None)]        // solo mover: NUNCA (el origen borraría)
    [InlineData(ShelfDropEffect.Copy | ShelfDropEffect.Move | ShelfDropEffect.Link, false, ShelfDropEffect.Copy)]
    [InlineData(ShelfDropEffect.Link, false, ShelfDropEffect.Link)]
    [InlineData(ShelfDropEffect.Move, false, ShelfDropEffect.None)]
    public void Effect_never_answers_Move(ShelfDropEffect allowed, bool files, ShelfDropEffect expected)
    {
        var d = files ? ShelfDrop.Parse(new[] { @"C:\a.txt" }, null, null) : ShelfDrop.Parse(null, null, "nota");
        Assert.Equal(expected, ShelfDrop.Effect(allowed, d, selfDrag: false));
    }

    [Fact]
    public void Nothing_usable_or_our_own_drag_is_refused()
    {
        var all = ShelfDropEffect.Copy | ShelfDropEffect.Move | ShelfDropEffect.Link;
        Assert.Equal(ShelfDropEffect.None, ShelfDrop.Effect(all, ShelfDrop.None, selfDrag: false));
        Assert.Equal(ShelfDropEffect.None, ShelfDrop.Effect(all, ShelfDrop.Parse(new[] { @"C:\a" }, null, null), selfDrag: true));
    }

    [Fact]
    public void ApplyTo_adds_each_kind_to_the_store()
    {
        _existing.Add(@"C:\a.txt");
        var store = Store();
        Assert.Single(ShelfDrop.Parse(new[] { @"C:\a.txt" }, null, null).ApplyTo(store));
        Assert.Equal(ShelfKind.Link, Assert.Single(ShelfDrop.Parse(null, "https://ex.com", null).ApplyTo(store)).Kind);
        Assert.Equal(ShelfKind.Text, Assert.Single(ShelfDrop.Parse(null, null, "apunte").ApplyTo(store)).Kind);
        Assert.Empty(ShelfDrop.None.ApplyTo(store));
        Assert.Equal(3, store.Items.Count);
    }

    // ---------------------------------------------------------------- ShelfDragOut

    [Fact]
    public void Dragging_a_selected_entry_takes_the_whole_selection_in_list_order()
    {
        var items = Enumerable.Range(0, 4).Select(i => Item(ShelfKind.File, $@"C:\f{i}")).ToArray();
        var sel = new ShelfSelection();
        var order = items.Select(i => i.Id).ToArray();
        sel.Click(items[3].Id, order, true, false);
        sel.Click(items[1].Id, order, true, false);

        Assert.Equal(new[] { items[1], items[3] }, ShelfDragOut.ItemsFor(items[3], items, sel));
        Assert.Equal(new[] { items[0] }, ShelfDragOut.ItemsFor(items[0], items, sel));   // fuera de la selección: solo esa
    }

    [Fact]
    public void Payload_has_available_paths_and_text_but_never_missing_files()
    {
        var file = Item(ShelfKind.File, @"C:\a.txt");
        var gone = Item(ShelfKind.File, @"C:\gone.txt");
        var folder = Item(ShelfKind.Folder, @"C:\docs");
        var link = Item(ShelfKind.Link, "https://ex.com");
        var text = Item(ShelfKind.Text, "hola");

        var p = ShelfDragOut.Build(new[] { file, gone, folder, link, text }, i => i != gone);
        Assert.NotNull(p);
        Assert.Equal(new[] { @"C:\a.txt", @"C:\docs" }, p!.Files);
        Assert.Equal("https://ex.com\r\nhola", p.Text);

        Assert.Null(ShelfDragOut.Build(new[] { gone }, i => i != gone));
        var onlyFiles = ShelfDragOut.Build(new[] { file }, _ => true);
        Assert.Null(onlyFiles!.Text);
        Assert.Empty(ShelfDragOut.Build(new[] { text }, _ => true)!.Files);
        Assert.False(ShelfDragOut.Allowed.HasFlag(ShelfDropEffect.Move));
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(2, 0, false)]
    [InlineData(3, 0, true)]        // como Windows: más de la mitad de SM_CXDRAG (4 → 2) a cualquier lado
    [InlineData(0, -3, true)]
    [InlineData(-2, 2, false)]
    public void Drag_starts_past_half_the_system_drag_size(int dx, int dy, bool expected)
    {
        Assert.Equal(expected, ShelfDragOut.Exceeded(new Point(50, 50), new Point(50 + dx, 50 + dy), new Size(4, 4)));
    }

    // ---------------------------------------------------------------- ShelfOpen

    private sealed class FakeShell : IShellLauncher
    {
        public readonly List<string> Calls = new();
        public bool Result = true;
        public bool OpenFile(string path) { Calls.Add("file:" + path); return Result; }
        public bool OpenFolder(string path) { Calls.Add("folder:" + path); return Result; }
        public bool Reveal(string path) { Calls.Add("reveal:" + path); return Result; }
        public bool OpenUrl(string url) { Calls.Add("url:" + url); return Result; }
        public bool CopyText(string text) { Calls.Add("copy:" + text); return Result; }
    }

    [Theory]
    [InlineData(ShelfKind.File, @"C:\docs\informe.pdf", true, ShelfOpenAction.OpenFile)]
    [InlineData(ShelfKind.File, @"C:\docs\setup.EXE", true, ShelfOpenAction.Reveal)]          // nunca ejecuta: lo enseña
    [InlineData(ShelfKind.File, @"C:\docs\run.ps1", true, ShelfOpenAction.Reveal)]
    [InlineData(ShelfKind.File, @"C:\docs\acceso.lnk", true, ShelfOpenAction.Reveal)]
    [InlineData(ShelfKind.File, @"C:\docs\informe.pdf", false, ShelfOpenAction.None)]          // borrado/movido: nada
    [InlineData(ShelfKind.File, "https://ex.com/a.pdf", true, ShelfOpenAction.None)]          // «archivo» que no es ruta
    [InlineData(ShelfKind.File, @"relativo\a.pdf", true, ShelfOpenAction.None)]
    [InlineData(ShelfKind.Folder, @"C:\docs", true, ShelfOpenAction.OpenFolder)]
    [InlineData(ShelfKind.Folder, @"C:\docs", false, ShelfOpenAction.None)]
    [InlineData(ShelfKind.Link, "https://ex.com", true, ShelfOpenAction.OpenUrl)]
    [InlineData(ShelfKind.Link, "javascript:alert(1)", true, ShelfOpenAction.None)]           // shelf.json manipulado
    [InlineData(ShelfKind.Text, "calc.exe", true, ShelfOpenAction.CopyText)]                  // el texto NUNCA se ejecuta
    public void Click_decides_without_ever_running_text_or_programs(ShelfKind kind, string value, bool available,
        ShelfOpenAction expected)
    {
        Assert.Equal(expected, ShelfOpen.Decide(Item(kind, value), available));
    }

    [Fact]
    public void Run_calls_only_the_decided_action()
    {
        var shell = new FakeShell();
        Assert.True(ShelfOpen.Run(Item(ShelfKind.Text, "calc.exe"), true, shell));
        Assert.True(ShelfOpen.Run(Item(ShelfKind.File, @"C:\a.bat"), true, shell));
        Assert.True(ShelfOpen.Run(Item(ShelfKind.Link, "https://ex.com"), true, shell));
        Assert.False(ShelfOpen.Run(Item(ShelfKind.File, @"C:\gone.txt"), false, shell));
        Assert.Equal(new[] { "copy:calc.exe", @"reveal:C:\a.bat", "url:https://ex.com" }, shell.Calls);

        shell.Result = false;
        Assert.False(ShelfOpen.Run(Item(ShelfKind.Folder, @"C:\docs"), true, shell));
    }

    // ---------------------------------------------------------------- ShelfDefaults

    [Fact]
    public void Default_store_lives_in_the_notch_folder()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeBarWin", "notch", "shelf.json");
        Assert.Equal(expected, ShelfDefaults.StorePath);
    }

    [Fact]
    public void Exists_checks_local_paths_and_never_blocks_on_network_shares()
    {
        Directory.CreateDirectory(_dir);
        var f = Path.Combine(_dir, "a.txt");
        File.WriteAllText(f, "x");
        Assert.True(ShelfDefaults.Exists(f));
        Assert.True(ShelfDefaults.Exists(_dir));
        Assert.False(ShelfDefaults.Exists(Path.Combine(_dir, "nada.txt")));
        // Un recurso de red caído colgaría el hilo de UI segundos: no se toca, y como la bandeja no acepta rutas de red
        // (NTLM contra un host ajeno), se da por NO disponible.
        Assert.False(ShelfDefaults.Exists(@"\\servidor-que-no-existe-cbw\c$\a.txt"));
        Assert.False(ShelfDefaults.Exists(""));
    }
}
