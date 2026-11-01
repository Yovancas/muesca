using System.Drawing;
using ClaudeBarWin.UI.Notch;

namespace ClaudeBarWin.Tests;

/// <summary>
/// <see cref="PermissionInputGuard"/> (revisión cruzada, punto 2): un <c>PostMessage</c> de pulsar y soltar sin ratón real
/// no aprueba. El DOWN exige el botón izquierdo pulsado de verdad y la posición real del cursor sobre el botón; el UP,
/// también la posición real.
/// </summary>
public class PermissionInputGuardTests
{
    private static uint Pos(int x, int y) => unchecked((uint)((ushort)(short)x | ((ushort)(short)y << 16)));

    [Theory]
    [InlineData(unchecked((short)0x8000), true)]   // pulsado ahora
    [InlineData(unchecked((short)0x8001), true)]
    [InlineData((short)0x0001, true)]              // pulsado y soltado desde la consulta anterior (clic rápido, toque)
    [InlineData((short)0, false)]                  // PostMessage sin ratón: el botón no se ha tocado
    [InlineData((short)0x0100, false)]
    public void Only_a_physical_press_counts(short state, bool real) =>
        Assert.Equal(real, PermissionInputGuard.RealPress(state));

    [Fact]
    public void Message_position_must_fall_on_the_button()
    {
        var topLeft = new Point(1000, 0);
        var button = new Rectangle(200, 60, 92, 26);   // en la superficie del notch
        Assert.True(PermissionInputGuard.MessagePosInside(Pos(1000 + 210, 70), topLeft, button));
        Assert.True(PermissionInputGuard.MessagePosInside(Pos(1000 + 200, 60), topLeft, button));     // esquina incluida
        Assert.False(PermissionInputGuard.MessagePosInside(Pos(1000 + 292, 70), topLeft, button));    // borde derecho excluido
        Assert.False(PermissionInputGuard.MessagePosInside(Pos(5, 5), topLeft, button));              // cursor real en otro sitio
    }

    [Fact]
    public void Negative_screen_coordinates_of_a_monitor_to_the_left_are_signed()
    {
        var topLeft = new Point(-1600, -200);
        var button = new Rectangle(300, 80, 92, 26);
        Assert.True(PermissionInputGuard.MessagePosInside(Pos(-1600 + 310, -200 + 90), topLeft, button));
        Assert.False(PermissionInputGuard.MessagePosInside(Pos(1600 + 310, 200 + 90), topLeft, button));
    }

    [Fact]
    public void Empty_button_never_matches() =>
        Assert.False(PermissionInputGuard.MessagePosInside(Pos(0, 0), Point.Empty, Rectangle.Empty));
}
