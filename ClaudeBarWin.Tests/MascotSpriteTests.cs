using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.Tests;

public class MascotSpriteTests
{
    [Fact]
    public void Every_phase_has_at_least_one_multiline_frame()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
        {
            var frames = MascotSprite.Frames(p);
            Assert.NotEmpty(frames);
            // Cada frame es un array de líneas no vacío y todas sus líneas tienen contenido.
            Assert.All(frames, f =>
            {
                Assert.NotEmpty(f);
                Assert.All(f, line => Assert.False(string.IsNullOrWhiteSpace(line)));
            });
        }
    }

    [Fact]
    public void Idle_is_a_single_static_frame()
        => Assert.Single(MascotSprite.Frames(SessionPhase.Idle));

    [Fact]
    public void Processing_animates_with_multiple_frames()
        => Assert.True(MascotSprite.Frames(SessionPhase.Processing).Count > 1);

    [Fact]
    public void Label_key_is_defined_for_every_phase()
    {
        foreach (SessionPhase p in Enum.GetValues<SessionPhase>())
            Assert.False(string.IsNullOrEmpty(MascotSprite.LabelKey(p)));
    }
}
