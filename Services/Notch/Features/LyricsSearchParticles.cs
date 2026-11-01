namespace ClaudeBarWin.Services.Notch.Features;

/// <summary>Una letra suelta de la animación «buscando la letra»: centro (px dentro del área), alfa 0..1, escala relativa
/// al tamaño base y el carácter que se pinta.</summary>
public readonly record struct LyricsParticle(float X, float Y, float Alpha, float Scale, string Glyph);

/// <summary>
/// Animación de la vista de letra mientras se busca (<see cref="LyricsState.Loading"/>, pedido del 29-oct): letras sueltas
/// (A–Z, tildes, ñ, ¿, ♪…) que suben flotando desde abajo y se desvanecen arriba, como si la letra se estuviera
/// «buscando». Cada partícula tiene su ciclo (1,6–2,4 s), escalonado respecto a las demás para que nunca vayan en fase;
/// en cada vuelta cambia de carácter, de columna y de tamaño. Subida con ease-out suave (sale rápido y frena), entrada de
/// opacidad rápida y, con un leve vaivén horizontal, se apagan SEGÚN LA ALTURA mientras suben (del 50 % al 90 % del recorrido): así no
/// se quedan quietas y visibles en una raya arriba mientras frenan (revisión del 29-oct).
///
/// <para>PURO: el instante llega por parámetro y la «aleatoriedad» es un hash del índice y de la vuelta (nada de reloj ni
/// <see cref="Random"/> dentro): el mismo instante da siempre el mismo fotograma. Con <c>still</c> (reduce-motion) se
/// devuelve SIEMPRE el mismo fotograma, sin movimiento. <see cref="Fill"/> escribe en un búfer del llamador (sin arrays
/// por fotograma); <see cref="At"/> es la versión cómoda que crea uno.</para>
/// </summary>
public static class LyricsSearchParticles
{
    /// <summary>Partículas que se pintan a la vez.</summary>
    public const int Count = 12;

    /// <summary>Duración mínima y máxima de la vuelta de una partícula.</summary>
    public const double MinCycle = 1.6, MaxCycle = 2.4;

    /// <summary>Instante fijo que se enseña con reduce-motion (un momento con varias letras a la vista).</summary>
    public const double StillAt = 1.1;

    /// <summary>Caracteres posibles (cadenas estáticas: pintar no crea strings).</summary>
    public static readonly string[] Glyphs =
    {
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V",
        "W", "X", "Y", "Z", "a", "e", "o", "s", "r", "á", "é", "í", "ó", "ú", "ñ", "Ñ", "¿", "?", "¡", "♪", "♫",
    };

    // Entra en el primer 15 % de la vuelta (por tiempo) y se apaga por ALTURA: del 50 % al 90 % del recorrido. El último
    // 10 % lo recorre ya invisible, así que la franja de arriba (pegada al aviso integrado, donde asomarían tildes y ¿¡)
    // queda libre.
    // Con el apagado por tiempo y la subida cúbica, a mitad de vuelta ya iba por el 91 % de la altura y pasaba casi media
    // vuelta quieta y visible pegada arriba; atar el alfa a la altura evita esa raya de letras amontonadas.
    private const float FadeIn = 0.15f, FadeOutFromRise = 0.5f, FadeOutToRise = 0.9f;
    private const float MinScale = 0.75f, MaxScale = 1.2f;

    /// <summary>Las <paramref name="count"/> partículas en el instante <paramref name="seconds"/> dentro de un área de
    /// <paramref name="width"/> × <paramref name="height"/> px (Y crece hacia abajo; suben de abajo a arriba).</summary>
    public static LyricsParticle[] At(double seconds, int count, float width, float height, bool still = false)
    {
        var p = new LyricsParticle[Math.Max(0, count)];
        Fill(p, seconds, width, height, still);
        return p;
    }

    /// <summary>Como <see cref="At"/> pero escribiendo en <paramref name="dst"/> (tantas partículas como su longitud).</summary>
    public static void Fill(Span<LyricsParticle> dst, double seconds, float width, float height, bool still = false)
    {
        if (still) seconds = StillAt;
        int count = dst.Length;
        float w = Math.Max(0f, width), h = Math.Max(0f, height);
        // Margen lateral para que el vaivén y el ancho del carácter no se salgan.
        float margin = Math.Min(w / 2f, Math.Max(4f, w * 0.06f));
        float sway = Math.Min(margin, Math.Max(1.5f, w * 0.025f));
        for (int i = 0; i < count; i++)
        {
            double cycle = MinCycle + (MaxCycle - MinCycle) * Unit(i, 0, 1);
            // Escalonado: cada partícula arranca en su fracción de la vuelta (más un poco de azar) ⇒ nunca todas en fase.
            double offset = ((double)i / count + 0.35 * Unit(i, 0, 2)) * cycle;
            double t = Math.Max(0.0, seconds) + offset;
            long lap = (long)Math.Floor(t / cycle);
            float u = (float)(t / cycle - lap);                       // 0..1 dentro de la vuelta

            float rise = 1f - (1f - u) * (1f - u);                    // ease-out cuadrático (frena menos que el cúbico)
            float y = h - rise * h;                                   // de abajo (h) a arriba (0)

            float alpha = Math.Min(1f, u / FadeIn);
            if (rise > FadeOutFromRise) alpha *= Math.Max(0f, 1f - (rise - FadeOutFromRise) / (FadeOutToRise - FadeOutFromRise));
            alpha *= 0.55f + 0.4f * (float)Unit(i, lap, 3);           // unas más presentes que otras

            float baseX = margin + (w - 2f * margin) * (float)Unit(i, lap, 4);
            float x = baseX + sway * MathF.Sin(2f * MathF.PI * (u * 0.8f + (float)Unit(i, lap, 5)));

            float scale = MinScale + (MaxScale - MinScale) * (float)Unit(i, lap, 6);
            string glyph = Glyphs[(int)(Hash(i, lap, 7) % (uint)Glyphs.Length)];
            dst[i] = new LyricsParticle(Math.Clamp(x, 0f, w), Math.Clamp(y, 0f, h), Math.Clamp(alpha, 0f, 1f), scale, glyph);
        }
    }

    /// <summary>
    /// ¿Hay que mantener vivo el repintado por esta animación? Solo buscando la letra (<see cref="LyricsState.Loading"/>)
    /// con la vista de letra a la vista (<paramref name="lyricsShown"/>), la ventana visible, sin ocultar por pantalla
    /// completa y sin reduce-motion (entonces el fotograma es fijo y basta con pintarlo una vez).
    /// </summary>
    public static bool Animates(LyricsState state, bool lyricsShown, bool visible, bool hiddenForFullscreen, bool reduceMotion) =>
        state == LyricsState.Loading && lyricsShown && visible && !hiddenForFullscreen && !reduceMotion;

    /// <summary>Número pseudoaleatorio 0..1 de (partícula, vuelta, canal).</summary>
    private static double Unit(int i, long lap, int salt) => Hash(i, lap, salt) / (double)uint.MaxValue;

    /// <summary>Mezcla entera (estilo «lowbias32») de partícula, vuelta y canal: determinista y sin estado.</summary>
    private static uint Hash(int i, long lap, int salt)
    {
        unchecked
        {
            uint x = (uint)i * 0x9E3779B1u ^ (uint)lap * 0x85EBCA77u ^ (uint)(lap >> 32) * 0xC2B2AE3Du ^ (uint)salt * 0x27D4EB2Fu;
            x ^= x >> 16; x *= 0x7FEB352Du;
            x ^= x >> 15; x *= 0x846CA68Bu;
            x ^= x >> 16;
            return x;
        }
    }
}
