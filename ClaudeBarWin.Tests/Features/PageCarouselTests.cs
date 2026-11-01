using ClaudeBarWin.Services.Notch.Features;

namespace ClaudeBarWin.Tests.Features;

/// <summary>
/// <see cref="PageCarousel"/> es PURO: rueda (WM_MOUSEWHEEL, 120 por muesca) → como mucho una página por gesto,
/// también con la rueda libre o un trackpad que manda deltas pequeños.
/// </summary>
public class PageCarouselTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 12, 0, 0);
    private static readonly NotchPage[] All = { NotchPage.Claude, NotchPage.Music, NotchPage.Tasks, NotchPage.Shelf };

    [Fact]
    public void Starts_on_first_page()
    {
        var c = new PageCarousel(All);
        Assert.Equal(NotchPage.Claude, c.Current);
        Assert.Equal(0, c.Index);
    }

    [Fact]
    public void One_notch_moves_one_page()
    {
        var c = new PageCarousel(All);
        Assert.True(c.OnWheel(-120, T0));          // hacia abajo → siguiente
        Assert.Equal(NotchPage.Music, c.Current);
        Assert.True(c.OnWheel(120, T0.AddSeconds(1))); // hacia arriba → anterior
        Assert.Equal(NotchPage.Claude, c.Current);
    }

    [Fact]
    public void Trackpad_small_deltas_accumulate()
    {
        var c = new PageCarousel(All);
        var t = T0;
        for (int i = 0; i < 5; i++)
        {
            Assert.False(c.OnWheel(-20, t)); // 5 × 20 = 100 → 0,83 < 0,9
            t = t.AddMilliseconds(16);
        }
        Assert.True(c.OnWheel(-20, t));       // 120 → 1,0 ≥ 0,9
        Assert.Equal(1, c.Index);
    }

    [Fact]
    public void Accumulator_resets_after_a_pause()
    {
        var c = new PageCarousel(All);
        Assert.False(c.OnWheel(-100, T0));
        // Más de 180 ms sin girar: lo acumulado se olvida.
        Assert.False(c.OnWheel(-20, T0.AddMilliseconds(181)));
        Assert.Equal(0, c.Index);
    }

    [Fact]
    public void Fast_spin_moves_once_per_gesture()
    {
        var c = new PageCarousel(All);
        var t = T0;
        int moves = 0;
        // Rueda libre: 60 muescas a 8 ms (casi medio segundo girando sin parar).
        for (int i = 0; i < 60; i++, t = t.AddMilliseconds(8))
            if (c.OnWheel(-120, t)) moves++;
        Assert.Equal(1, moves);
        Assert.Equal(NotchPage.Music, c.Current);

        // Tras 180 ms de calma empieza otro gesto.
        Assert.True(c.OnWheel(-120, t.AddMilliseconds(180)));
        Assert.Equal(NotchPage.Tasks, c.Current);
    }

    [Fact]
    public void Opposite_direction_cancels_accumulation()
    {
        var c = new PageCarousel(All);
        Assert.False(c.OnWheel(-90, T0));
        Assert.False(c.OnWheel(60, T0.AddMilliseconds(10)));
        Assert.Equal(0, c.Index);
    }

    [Fact]
    public void Wraps_around()
    {
        var c = new PageCarousel(All);
        Assert.True(c.OnWheel(120, T0));
        Assert.Equal(NotchPage.Shelf, c.Current);
        Assert.True(c.OnWheel(-120, T0.AddSeconds(1)));
        Assert.Equal(NotchPage.Claude, c.Current);
    }

    [Fact]
    public void GoTo()
    {
        var c = new PageCarousel(All);
        c.GoTo(NotchPage.Tasks);
        Assert.Equal(NotchPage.Tasks, c.Current);
        Assert.Equal(2, c.Index);
        Assert.Throws<ArgumentException>(() => new PageCarousel(new[] { NotchPage.Claude }).GoTo(NotchPage.Music));
    }

    [Fact]
    public void GoTo_clears_pending_wheel()
    {
        var c = new PageCarousel(All);
        Assert.False(c.OnWheel(-100, T0));
        c.GoTo(NotchPage.Tasks);
        Assert.False(c.OnWheel(-20, T0.AddMilliseconds(10)));
        Assert.Equal(NotchPage.Tasks, c.Current);
    }

    [Fact]
    public void Single_page_never_moves()
    {
        var c = new PageCarousel(new[] { NotchPage.Claude });
        Assert.False(c.OnWheel(-120, T0));
        Assert.Equal(NotchPage.Claude, c.Current);
    }

    [Fact]
    public void Rejects_empty_or_duplicate_pages()
    {
        Assert.Throws<ArgumentException>(() => new PageCarousel(Array.Empty<NotchPage>()));
        Assert.Throws<ArgumentException>(() => new PageCarousel(new[] { NotchPage.Music, NotchPage.Music }));
    }
}
