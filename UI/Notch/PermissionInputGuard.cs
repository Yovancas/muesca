namespace ClaudeBarWin.UI.Notch;

/// <summary>
/// Refuerzo del clic en Permitir / Denegar (revisión cruzada, punto 2): un <c>PostMessage</c> de pulsar y soltar sobre el
/// HWND del notch, sin ratón de verdad, no aprueba nada. PURO: <see cref="NotchForm"/> le pasa lo que devuelven
/// <c>GetAsyncKeyState(VK_LBUTTON)</c> (en el DOWN) y <c>GetMessagePos</c> (en el DOWN y en el UP).
/// <list type="bullet">
/// <item>Un mensaje encolado con <c>PostMessage</c> lleva como posición la del cursor al encolarlo, no las coordenadas que
/// el remitente puso en el lParam: tiene que caer en el MISMO botón.</item>
/// <item>El botón izquierdo tiene que estar pulsado de verdad (bit alto) o haberse pulsado desde la consulta anterior (bit
/// bajo): un clic muy rápido o el toque de un touchpad pueden estar ya soltados cuando se atiende el DOWN.</item>
/// </list>
/// No sustituye a la comprobación de integridad del helper (que es la que impide el salto de UAC); solo encarece el clic
/// falso a un proceso del mismo nivel.
/// </summary>
internal static class PermissionInputGuard
{
    /// <summary>¿<c>GetAsyncKeyState(VK_LBUTTON)</c> dice que el botón izquierdo se pulsó de verdad?</summary>
    public static bool RealPress(short asyncKeyState) => (asyncKeyState & unchecked((short)0x8001)) != 0;

    /// <summary>
    /// ¿La posición de <c>GetMessagePos</c> (pantalla; x e y con signo en 16 bits: los monitores a la izquierda o arriba
    /// del principal son negativos) cae dentro de <paramref name="button"/>, que está en coordenadas de la superficie del
    /// notch con su esquina en <paramref name="surfaceTopLeft"/>? Un botón vacío nunca.
    /// </summary>
    public static bool MessagePosInside(uint messagePos, Point surfaceTopLeft, Rectangle button)
    {
        if (button.IsEmpty) return false;
        int x = unchecked((short)(messagePos & 0xFFFF)), y = unchecked((short)(messagePos >> 16));
        return button.Contains(x - surfaceTopLeft.X, y - surfaceTopLeft.Y);
    }
}
