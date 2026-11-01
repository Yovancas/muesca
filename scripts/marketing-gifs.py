"""
Marketing GIF frame generator for ClaudeBar for Windows README.
Produces two frame sequences (move/ and opacity/) -- all synthetic, no screen recording.
Canvas 760x480, GitHub-dark gradient background, floating panel with soft shadow.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

BASE = os.path.dirname(os.path.abspath(__file__))
SRC = r"C:/Users/zorro/Proyectos/claudebar-win/assets/dashboard-dark.png"

W, H = 760, 480
FPS = 30

# GitHub dark tones
TOP = (13, 17, 23)      # #0d1117
BOT = (22, 27, 34)      # #161b22


# ---------- helpers ----------

def lerp(a, b, t):
    return a + (b - a) * t


def ease_in_out_cubic(t):
    if t < 0.5:
        return 4 * t * t * t
    return 1 - pow(-2 * t + 2, 3) / 2


def ease_in_out_sine(t):
    return -(math.cos(math.pi * t) - 1) / 2


def find_font(size, mono=False):
    candidates_mono = [
        "C:/Windows/Fonts/consola.ttf",
        "C:/Windows/Fonts/cour.ttf",
    ]
    candidates_ui = [
        "C:/Windows/Fonts/segoeui.ttf",
        "C:/Windows/Fonts/arial.ttf",
    ]
    for p in (candidates_mono if mono else candidates_ui):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()


def make_background():
    """Vertical dark gradient + very subtle dot grid."""
    bg = Image.new("RGB", (W, H))
    px = bg.load()
    for y in range(H):
        t = y / (H - 1)
        r = int(round(lerp(TOP[0], BOT[0], t)))
        g = int(round(lerp(TOP[1], BOT[1], t)))
        b = int(round(lerp(TOP[2], BOT[2], t)))
        for x in range(W):
            px[x, y] = (r, g, b)
    bg = bg.convert("RGBA")
    # subtle dot grid overlay (very low alpha)
    overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    od = ImageDraw.Draw(overlay)
    step = 24
    for gy in range(step // 2, H, step):
        for gx in range(step // 2, W, step):
            od.ellipse([gx - 1, gy - 1, gx + 1, gy + 1], fill=(255, 255, 255, 10))
    bg = Image.alpha_composite(bg, overlay)
    return bg


def load_panel(scale_to_height):
    """Load source PNG, scale to a target height with LANCZOS, return RGBA."""
    src = Image.open(SRC).convert("RGBA")
    ratio = scale_to_height / src.height
    new_w = int(round(src.width * ratio))
    new_h = scale_to_height
    return src.resize((new_w, new_h), Image.LANCZOS)


def make_shadow(panel_size, blur=18, alpha=110):
    """Soft rounded shadow sized to panel, returned as its own RGBA layer (bigger to fit blur)."""
    pw, ph = panel_size
    pad = blur * 3
    sh = Image.new("RGBA", (pw + pad * 2, ph + pad * 2), (0, 0, 0, 0))
    sd = ImageDraw.Draw(sh)
    sd.rounded_rectangle(
        [pad, pad, pad + pw, pad + ph],
        radius=14,
        fill=(0, 0, 0, alpha),
    )
    sh = sh.filter(ImageFilter.GaussianBlur(blur))
    return sh, pad


def paste_panel(canvas, panel, shadow, shadow_pad, x, y, panel_alpha=255):
    """Paste shadow (offset 0,6) then panel at (x,y). panel_alpha 0-255 multiplies panel opacity."""
    # shadow: panel top-left at (x,y), shadow layer origin so panel area aligns; offset +0,+6
    sx = x - shadow_pad
    sy = y - shadow_pad + 6
    canvas.alpha_composite(shadow, (sx, sy))
    if panel_alpha >= 255:
        canvas.alpha_composite(panel, (x, y))
    else:
        p = panel.copy()
        a = p.getchannel("A")
        a = a.point(lambda v: int(v * panel_alpha / 255))
        p.putalpha(a)
        canvas.alpha_composite(p, (x, y))


def draw_cursor(canvas, cx, cy, alpha=255):
    """Draw a simple white arrow cursor with black outline at (cx,cy) = tip point."""
    if alpha <= 0:
        return
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    # arrow polygon (classic pointer), tip at top-left
    pts = [
        (cx, cy),
        (cx, cy + 18),
        (cx + 5, cy + 13),
        (cx + 9, cy + 21),
        (cx + 12, cy + 19),
        (cx + 8, cy + 11),
        (cx + 14, cy + 11),
    ]
    # black outline
    d.polygon(pts, fill=(0, 0, 0, alpha))
    # white fill slightly inset
    inset = [
        (cx + 1, cy + 2),
        (cx + 1, cy + 15),
        (cx + 5, cy + 11),
        (cx + 8.5, cy + 18),
        (cx + 10, cy + 17),
        (cx + 6.5, cy + 10),
        (cx + 11, cy + 10),
    ]
    d.polygon(inset, fill=(255, 255, 255, alpha))
    canvas.alpha_composite(layer)


# ---------- GIF 1: MOVE ----------

def build_move():
    out = os.path.join(BASE, "move")
    os.makedirs(out, exist_ok=True)
    for f in os.listdir(out):
        os.remove(os.path.join(out, f))

    bg = make_background()
    panel_h = int(H * 0.46)
    panel = load_panel(panel_h)
    pw, ph = panel.size
    shadow, spad = make_shadow((pw, ph))

    # 3 stop positions (top-left of panel), keep on-canvas with margins
    margin = 28
    p_right_bottom = (W - pw - margin, H - ph - margin)
    p_left_top = (margin, margin)
    p_center = ((W - pw) // 2, (H - ph) // 2)
    stops = [p_right_bottom, p_left_top, p_center, p_right_bottom]

    hold_frames = 15      # ~0.5s
    move_frames = 26      # ~0.87s per leg
    frames = []

    # initial hold at first stop
    for _ in range(hold_frames):
        frames.append((stops[0], None))  # no cursor during hold

    for i in range(len(stops) - 1):
        a = stops[i]
        b = stops[i + 1]
        for k in range(move_frames):
            t = ease_in_out_cubic(k / (move_frames - 1))
            x = lerp(a[0], b[0], t)
            y = lerp(a[1], b[1], t)
            # cursor stuck to top edge of panel (slightly inside)
            cur = (x + pw * 0.32, y + 6)
            frames.append(((x, y), cur))
        # hold at arrival (cursor fades out -> None)
        for _ in range(hold_frames):
            frames.append((b, None))

    idx = 0
    for (pos, cur) in frames:
        canvas = bg.copy()
        x, y = int(round(pos[0])), int(round(pos[1]))
        paste_panel(canvas, panel, shadow, spad, x, y, 255)
        if cur is not None:
            draw_cursor(canvas, int(round(cur[0])), int(round(cur[1])), alpha=235)
        canvas.convert("RGB").save(os.path.join(out, f"frame_{idx:03d}.png"))
        idx += 1
    print(f"move: {idx} frames")
    return idx


# ---------- GIF 2: OPACITY ----------

def draw_backdrop(canvas):
    """Terminal-style text lines + soft colored window rects BEHIND the panel."""
    d = ImageDraw.Draw(canvas)

    # two soft 'window' rectangles
    def soft_rect(box, color, a=46):
        layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        ld = ImageDraw.Draw(layer)
        ld.rounded_rectangle(box, radius=10, fill=color + (a,))
        # title bar
        ld.rounded_rectangle([box[0], box[1], box[2], box[1] + 22], radius=10, fill=color + (a + 24,))
        canvas.alpha_composite(layer)

    soft_rect([70, 70, 330, 250], (88, 101, 242))      # blurple window left
    soft_rect([430, 250, 690, 420], (35, 134, 54))     # green window right

    mono = find_font(18, mono=True)
    mono_small = find_font(16, mono=True)

    # terminal lines on the left window
    lines_left = [
        ("$ claude  -  running...", (126, 231, 135)),
        ("> analyzing repo tree", (139, 148, 158)),
        ("> 248 files indexed", (139, 148, 158)),
    ]
    ly = 104
    for txt, col in lines_left:
        d.text((90, ly), txt, font=mono, fill=col)
        ly += 30

    # green window lines (right)
    lines_right = [
        ("checks passed", (126, 231, 135)),
        ("build  ok", (139, 148, 158)),
        ("tokens: 41%", (210, 168, 96)),
    ]
    ry = 286
    for txt, col in lines_right:
        d.text((452, ry), txt, font=mono_small, fill=col)
        ry += 28

    # a couple of free-floating terminal lines top-right / bottom-left for fill
    d.text((430, 90), "* deploy queued", font=mono_small, fill=(139, 148, 158))
    d.text((90, 300), "tokens: 41%", font=mono, fill=(210, 168, 96))


def build_opacity():
    out = os.path.join(BASE, "opacity")
    os.makedirs(out, exist_ok=True)
    for f in os.listdir(out):
        os.remove(os.path.join(out, f))

    bg = make_background()
    # bake the backdrop into the background once
    backdrop = bg.copy()
    draw_backdrop(backdrop)

    panel_h = int(H * 0.46)
    panel = load_panel(panel_h)
    pw, ph = panel.size
    shadow, spad = make_shadow((pw, ph))
    px = (W - pw) // 2
    py = (H - ph) // 2

    cap_font = find_font(17)

    # opacity keyframes: 100 -> 70 -> 40 -> 100, with holds
    keys = [100, 70, 40, 100]
    hold_frames = 18      # ~0.6s
    trans_frames = 18     # ~0.6s

    seq = []  # list of opacity percentages
    # hold first
    for _ in range(hold_frames):
        seq.append(keys[0])
    for i in range(len(keys) - 1):
        a, b = keys[i], keys[i + 1]
        for k in range(trans_frames):
            t = ease_in_out_sine(k / (trans_frames - 1))
            seq.append(lerp(a, b, t))
        # hold at arrival (skip final loop-back hold duplication except show it)
        for _ in range(hold_frames):
            seq.append(b)

    idx = 0
    for op in seq:
        canvas = backdrop.copy()
        alpha = int(round(op / 100 * 255))
        paste_panel(canvas, panel, shadow, spad, px, py, alpha)
        # caption bottom-right
        d = ImageDraw.Draw(canvas)
        label = f"opacity {int(round(op))}%"
        bbox = d.textbbox((0, 0), label, font=cap_font)
        tw = bbox[2] - bbox[0]
        th = bbox[3] - bbox[1]
        cx2 = W - tw - 24
        cy2 = H - th - 22
        # subtle chip behind caption
        chip = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
        cd = ImageDraw.Draw(chip)
        cd.rounded_rectangle([cx2 - 10, cy2 - 7, cx2 + tw + 10, cy2 + th + 9], radius=8, fill=(0, 0, 0, 90))
        canvas.alpha_composite(chip)
        d = ImageDraw.Draw(canvas)
        d.text((cx2, cy2), label, font=cap_font, fill=(180, 188, 198))
        canvas.convert("RGB").save(os.path.join(out, f"frame_{idx:03d}.png"))
        idx += 1
    print(f"opacity: {idx} frames")
    return idx


if __name__ == "__main__":
    mv = build_move()
    op = build_opacity()
    print("DONE", mv, op)
