#!/usr/bin/env python3
"""Capturas de la ficha de la Store (store/screenshots.md) a partir de las que pinta OnboardingShotsTests.

    $env:SHOTS_DIR = "C:\\shots"; $env:SHOTS_LANGS = "all"
    dotnet test ClaudeBarWin.Tests -c Release --filter OnboardingShotsTests
    python store/compose-screenshots.py C:\\shots C:\\shots\\store            (todos los idiomas que haya)
    python store/compose-screenshots.py C:\\shots C:\\shots\\store --langs es,en

Necesita Pillow (pip install pillow). Cada captura va centrada a su tamaño (si no cabe, se reduce), con las esquinas
redondeadas y sombra, sobre un lienzo de 2560 × 1440 con el negro y el violeta de la marca: la Store pide PNG de al menos
1366 × 768 y admite hasta 3840 × 2160, y las del notch solo miden 1400 × 544. Salida: <salida>/<idioma>/NN-nombre.png, en
el orden de store/screenshots.md.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

CANVAS = (2560, 1440)
MARGIN = 120
RADIUS = 28
TOP = (0x1A, 0x14, 0x3A)      # violeta muy oscuro, hacia #7B4DFF
BOTTOM = (0x0A, 0x0A, 0x12)   # el negro de la marca

# El orden y el origen de cada captura (store/screenshots.md). {lang}: código de la app (es, en, nl, fr, de, ja, ko, zh-Hant).
ORDER = [
    ("01-sesiones", "tutorial-{lang}-02-sesiones.png"),
    ("02-permisos", "tutorial-{lang}-03-permisos-armandose-pro.png"),
    ("03-uso", "uso-{lang}-5-cuota-hover.png"),
    ("04-musica", "tutorial-{lang}-05-musica-letra-hecho.png"),
    ("05-tareas", "tutorial-{lang}-06-tareas-recordatorio.png"),
    ("06-bandeja", "tutorial-{lang}-07-bandeja-arrastrando.png"),
    ("07-foco", "foco-{lang}-1-en-marcha.png"),
    ("08-gasto", "uso-{lang}-1-gasto.png"),
    ("09-bienvenida", "onboarding-{lang}-2-perfiles.png"),
    ("10-pro", "pro-{lang}-4-tarjeta-store.png"),
]
LANGS = ["en", "es", "nl", "fr", "de", "ja", "ko", "zh-Hant"]


def background() -> Image.Image:
    w, h = CANVAS
    column = Image.new("RGB", (1, h))
    for y in range(h):
        t = y / (h - 1)
        column.putpixel((0, y), tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM)))
    return column.resize(CANVAS).convert("RGBA")


def rounded_mask(size: tuple[int, int], radius: int) -> Image.Image:
    mask = Image.new("L", size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, size[0] - 1, size[1] - 1), radius=radius, fill=255)
    return mask


def compose(shot: Image.Image) -> Image.Image:
    shot = shot.convert("RGBA")
    max_w, max_h = CANVAS[0] - 2 * MARGIN, CANVAS[1] - 2 * MARGIN
    scale = min(1.0, max_w / shot.width, max_h / shot.height)
    if scale < 1.0:
        shot = shot.convert("RGBa").resize((round(shot.width * scale), round(shot.height * scale)), Image.LANCZOS).convert("RGBA")
    canvas = background()
    x, y = (CANVAS[0] - shot.width) // 2, (CANVAS[1] - shot.height) // 2

    shadow = Image.new("RGBA", CANVAS, (0, 0, 0, 0))
    shadow_box = Image.new("RGBA", shot.size, (0, 0, 0, 150))
    shadow.paste(shadow_box, (x, y + 16), rounded_mask(shot.size, RADIUS))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(28)))

    framed = Image.new("RGBA", shot.size, (0, 0, 0, 0))
    framed.paste(shot, (0, 0), rounded_mask(shot.size, RADIUS))
    canvas.alpha_composite(framed, (x, y))
    return canvas.convert("RGB")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("shots", type=Path, help="la carpeta SHOTS_DIR de OnboardingShotsTests")
    parser.add_argument("out", type=Path, help="dónde dejar las capturas de la Store")
    parser.add_argument("--langs", default="", help="códigos separados por comas (por defecto, todos los que haya)")
    args = parser.parse_args()

    langs = [l.strip() for l in args.langs.split(",") if l.strip()] or LANGS
    missing, written = [], 0
    for lang in langs:
        sources = [(name, args.shots / pattern.format(lang=lang)) for name, pattern in ORDER]
        if not any(src.exists() for _, src in sources):
            continue
        target = args.out / lang
        target.mkdir(parents=True, exist_ok=True)
        for name, src in sources:
            if not src.exists():
                missing.append(str(src))
                continue
            with Image.open(src) as shot:
                compose(shot).save(target / f"{name}.png", format="PNG", optimize=True)
            written += 1
    print(f"{written} capturas en {args.out}")
    for m in missing:
        print(f"falta: {m}", file=sys.stderr)
    return 1 if missing or written == 0 else 0


if __name__ == "__main__":
    sys.exit(main())
