#!/usr/bin/env python3
"""Recursos visuales del MSIX de Muesca (installer/msix/Assets) a partir de branding/.

Reproducible: mismas entradas, mismos PNG. Necesita Pillow (pip install pillow).

    python installer/msix/make-assets.py

- Si branding/ ya tiene el PNG exacto (muesca-44.png, muesca-150.png, la ancha, el splash…), se copia tal cual: son los
  retocados a mano.
- Lo que falta se genera desde branding/muesca-1024.png, con la misma composición que los de branding/:
  * iconos de hasta 64 px: el logo recortado a su contorno y a sangre (como muesca-16…64);
  * iconos mayores: el logo entero, con su margen (como muesca-128…512);
  * ancha y splash: el logo al 73,3 % del alto, centrado sobre #0A0A12 (como muesca-wide-310x150 y muesca-splash-620x300).

Qué genera (las escalas 100/200/400 y los targetsize que pide Windows; el manifiesto nombra el archivo sin calificador y
resources.pri, que hace build-msix.ps1 con makepri, elige el que toque):
  Square44x44Logo.scale-{100,125,150,200,400}.png
  Square44x44Logo.targetsize-{16,24,32,48,256}.png y .targetsize-*_altform-unplated.png
  Square150x150Logo.scale-{100,200,400}.png
  Wide310x150Logo.scale-{100,200,400}.png
  StoreLogo.scale-{100,200,400}.png
  SplashScreen.scale-{100,200,400}.png
"""
from __future__ import annotations

import shutil
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
BRANDING = ROOT / "branding"
OUT = Path(__file__).resolve().parent / "Assets"
SOURCE = BRANDING / "muesca-1024.png"

BACKGROUND = (0x0A, 0x0A, 0x12, 255)   # el negro de la marca (branding/README.md)
WIDE_LOGO_RATIO = 110 / 150            # alto del logo en la ancha y el splash de branding/
FULL_BLEED_MAX = 64                    # hasta aquí, el logo a sangre (como los iconos pequeños de branding/)

SCALES = (100, 200, 400)


def resize(img: Image.Image, size: tuple[int, int]) -> Image.Image:
    """Redimensiona con el alfa premultiplicado (sin halos oscuros en los bordes)."""
    return img.convert("RGBa").resize(size, Image.LANCZOS).convert("RGBA")


def square(logo: Image.Image, px: int) -> Image.Image:
    if px <= FULL_BLEED_MAX:
        return resize(logo.crop(logo.getbbox()), (px, px))
    return resize(logo, (px, px))


def banner(logo: Image.Image, width: int, height: int) -> Image.Image:
    canvas = Image.new("RGBA", (width, height), BACKGROUND)
    side = round(height * WIDE_LOGO_RATIO)
    mark = resize(logo, (side, side))
    canvas.alpha_composite(mark, ((width - side) // 2, (height - side) // 2))
    return canvas


def save(img: Image.Image, name: str) -> None:
    # Sin metadatos ni fecha: el mismo archivo cada vez.
    img.save(OUT / name, format="PNG", optimize=True)


def from_branding(name: str, target: str, size: tuple[int, int]) -> bool:
    src = BRANDING / name
    if not src.exists():
        return False
    with Image.open(src) as im:
        if im.size != size:
            sys.exit(f"{src} mide {im.size} y se esperaba {size}")
    shutil.copyfile(src, OUT / target)
    return True


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*.png"):
        old.unlink()
    logo = Image.open(SOURCE).convert("RGBA")

    def icon(target: str, px: int) -> None:
        if not from_branding(f"muesca-{px}.png", target, (px, px)):
            save(square(logo, px), target)

    def wide(target: str, width: int, height: int, branding_name: str | None) -> None:
        if branding_name is None or not from_branding(branding_name, target, (width, height)):
            save(banner(logo, width, height), target)

    for scale in (100, 125, 150, 200, 400):
        icon(f"Square44x44Logo.scale-{scale}.png", round(44 * scale / 100))
    for px in (16, 24, 32, 48, 256):
        icon(f"Square44x44Logo.targetsize-{px}.png", px)
        icon(f"Square44x44Logo.targetsize-{px}_altform-unplated.png", px)
    for scale in SCALES:
        icon(f"Square150x150Logo.scale-{scale}.png", 150 * scale // 100)
        icon(f"StoreLogo.scale-{scale}.png", 50 * scale // 100)
        w, h = 310 * scale // 100, 150 * scale // 100
        wide(f"Wide310x150Logo.scale-{scale}.png", w, h, f"muesca-wide-{w}x{h}.png")
        w, h = 620 * scale // 100, 300 * scale // 100
        wide(f"SplashScreen.scale-{scale}.png", w, h, f"muesca-splash-{w}x{h}.png")

    for path in sorted(OUT.glob("*.png")):
        with Image.open(path) as im:
            print(f"{path.name:55} {im.size[0]}x{im.size[1]}")


if __name__ == "__main__":
    main()
