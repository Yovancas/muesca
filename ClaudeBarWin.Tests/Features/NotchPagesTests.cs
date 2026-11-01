using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary><see cref="NotchPages"/>: páginas por defecto y a dónde va la rueda en el expandido.</summary>
public class NotchPagesTests
{
    [Fact]
    public void Default_pages_start_with_Claude_and_are_distinct()
    {
        Assert.Equal(NotchPage.Claude, NotchPages.Default[0]);
        Assert.Equal(new[] { NotchPage.Claude, NotchPage.Usage, NotchPage.Music, NotchPage.Tasks, NotchPage.Focus, NotchPage.Shelf },
            NotchPages.Default);
    }

    [Fact]
    public void Normalize_keeps_order_drops_repeats_and_always_has_Claude_first()
    {
        Assert.Equal(NotchPages.Default, NotchPages.Normalize(null));
        Assert.Equal(new[] { NotchPage.Claude }, NotchPages.Normalize(Array.Empty<NotchPage>()));
        Assert.Equal(new[] { NotchPage.Claude, NotchPage.Tasks, NotchPage.Music },
            NotchPages.Normalize(new[] { NotchPage.Tasks, NotchPage.Music, NotchPage.Tasks }));
        Assert.Equal(new[] { NotchPage.Claude, NotchPage.Shelf },
            NotchPages.Normalize(new[] { NotchPage.Shelf, NotchPage.Claude }));
    }

    [Theory]
    // Una sola página: la rueda desplaza la lista de sesiones (como antes de F5).
    [InlineData(NotchPage.Claude, 1, false, false, WheelTarget.SessionList)]
    // Varias páginas: por defecto cambia de página…
    [InlineData(NotchPage.Claude, 4, false, true, WheelTarget.Pages)]
    [InlineData(NotchPage.Claude, 4, true, false, WheelTarget.Pages)]
    // …salvo sobre la lista de sesiones de Claude cuando no cabe entera.
    [InlineData(NotchPage.Claude, 4, true, true, WheelTarget.SessionList)]
    [InlineData(NotchPage.Music, 4, true, true, WheelTarget.Pages)]
    // Bandeja (B4): sobre su lista cuando no cabe entera, la desplaza; si cabe, cambia de página.
    [InlineData(NotchPage.Shelf, 4, true, true, WheelTarget.ShelfList)]
    [InlineData(NotchPage.Shelf, 4, true, false, WheelTarget.Pages)]
    [InlineData(NotchPage.Shelf, 4, false, true, WheelTarget.Pages)]
    // Tareas (B5): igual que la Bandeja.
    [InlineData(NotchPage.Tasks, 4, true, true, WheelTarget.TaskList)]
    [InlineData(NotchPage.Tasks, 4, true, false, WheelTarget.Pages)]
    [InlineData(NotchPage.Tasks, 4, false, true, WheelTarget.Pages)]
    // Foco: no hay lista; la rueda siempre cambia de página.
    [InlineData(NotchPage.Focus, 6, true, true, WheelTarget.Pages)]
    [InlineData(NotchPage.Focus, 6, false, false, WheelTarget.Pages)]
    public void Wheel_routing(NotchPage current, int pages, bool overList, bool overflows, WheelTarget expected) =>
        Assert.Equal(expected, NotchPages.RouteWheel(current, pages, overList, overflows));

    /// <summary>Pedido del 29-oct: el deslizamiento horizontal del trackpad también cambia de página (una por gesto).</summary>
    [Fact]
    public void Horizontal_swipe_moves_pages_one_per_gesture_including_trackpad_steps()
    {
        var c = new PageCarousel(NotchPages.Default);
        var t = new DateTime(2026, 10, 29, 3, 0, 0, DateTimeKind.Utc);
        // Deslizar hacia la derecha en pasitos de trackpad de precisión (+12): siguiente página, y la inercia no salta más.
        bool moved = false;
        for (int i = 0; i < 40; i++)
            moved |= c.OnWheel(NotchPages.HorizontalToCarouselDelta(12), t.AddMilliseconds(i * 16));
        Assert.True(moved);
        Assert.Equal(NotchPages.Default[1], c.Current);
        // Nuevo gesto hacia la izquierda tras una pausa: vuelve a la anterior.
        Assert.True(c.OnWheel(NotchPages.HorizontalToCarouselDelta(-120), t.AddSeconds(2)));
        Assert.Equal(NotchPages.Default[0], c.Current);
    }

    // ---- Abrir una página pedida (anillo, arrastre a la Bandeja…): el carrusel pudo quedarse en ella al cerrar ----

    [Theory]
    [InlineData(NotchPage.Shelf)]
    [InlineData(NotchPage.Focus)]
    [InlineData(NotchPage.Usage)]
    public void A_page_the_carousel_stayed_on_jumps_when_the_expanded_is_opening(NotchPage page)
    {
        // Si no salta (y marca la apertura como suya), Sync devuelve el carrusel a Claude: el bug de ShowShelfPage.
        Assert.Equal(PageShow.Jump, NotchPages.HowToShow(page, current: page, wasExpanded: false));
    }

    [Fact]
    public void Showing_a_page_jumps_while_opening_slides_when_open_and_stays_if_already_there()
    {
        Assert.Equal(PageShow.Jump, NotchPages.HowToShow(NotchPage.Shelf, current: NotchPage.Claude, wasExpanded: false));
        Assert.Equal(PageShow.Slide, NotchPages.HowToShow(NotchPage.Shelf, current: NotchPage.Claude, wasExpanded: true));
        Assert.Equal(PageShow.Stay, NotchPages.HowToShow(NotchPage.Shelf, current: NotchPage.Shelf, wasExpanded: true));
    }
}
