"""Builds the app icon and the tray icons into src/ScreenTune/Assets.

The app icon redraws the macOS AppIcon.icon layers (screen gradient, stand,
slider track, knob) flat, on a transparent background, the way Windows icons
look. The tray icon is a three-slider glyph like the macOS menu bar symbol,
drawn per size so its 1px strokes land on whole pixels.

Run: python tools/make_icons.py
"""

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ASSETS = Path(__file__).resolve().parent.parent / "src" / "ScreenTune" / "Assets"
APP_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
TRAY_SIZES = [16, 20, 24, 32, 40, 48, 64]
SS = 4  # supersampling for the app icon canvas


def hex_rgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i : i + 2], 16) for i in (0, 2, 4)], dtype=np.float64)


def gradient(stops, t):
    """stops: [(offset, '#rrggbb')], t: array 0..1 -> HxWx3 array."""
    t = np.clip(t, 0, 1)
    out = np.zeros(t.shape + (3,))
    for (o0, c0), (o1, c1) in zip(stops, stops[1:]):
        mask = (t >= o0) & (t <= o1)
        f = ((t - o0) / (o1 - o0))[..., None]
        out[mask] = (hex_rgb(c0) * (1 - f) + hex_rgb(c1) * f)[mask]
    return out


def rounded_mask(size, box, radius):
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle(box, radius=radius, fill=255)
    return np.asarray(m, dtype=np.float64) / 255


def polygon_mask(size, points):
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).polygon(points, fill=255)
    return np.asarray(m, dtype=np.float64) / 255


def composite(dst_rgb, dst_a, src_rgb, src_a):
    out_a = src_a + dst_a * (1 - src_a)
    safe = np.where(out_a == 0, 1, out_a)[..., None]
    out_rgb = (src_rgb * src_a[..., None] + dst_rgb * dst_a[..., None] * (1 - src_a[..., None])) / safe
    return out_rgb, out_a


def app_icon_master():
    n = 1024 * SS
    s = lambda v: v * SS
    yy, xx = np.mgrid[0:n, 0:n].astype(np.float64) / SS
    rgb = np.zeros((n, n, 3))
    a = np.zeros((n, n))

    # screen.svg: objectBoundingBox gradient from (0,0) to (1,0.35)
    x0, y0, w, h = 162, 226, 700, 460
    u, v = (xx - x0) / w, (yy - y0) / h
    t = (u * 1 + v * 0.35) / (1 + 0.35**2)
    screen = gradient([(0, "#FFA41B"), (0.48, "#FF3B6B"), (1, "#5856D6")], t)
    rgb, a = composite(rgb, a, screen, rounded_mask(n, [s(x0), s(y0), s(x0 + w), s(y0 + h)], s(52)))

    # stand.svg: neck trapezoid and base, vertical gradients
    neck = gradient([(0, "#7C7C82"), (1, "#A9A9B0")], (yy - 690) / 72)
    rgb, a = composite(rgb, a, neck, polygon_mask(n, [(s(474), s(690)), (s(550), s(690)), (s(566), s(762)), (s(458), s(762))]))
    base = gradient([(0, "#D8D8DE"), (1, "#B4B4BB")], (yy - 752) / 40)
    rgb, a = composite(rgb, a, base, rounded_mask(n, [s(346), s(752), s(678), s(792)], s(20)))

    # track.svg (white at 50%) and knob.svg
    white = np.full((n, n, 3), 255.0)
    rgb, a = composite(rgb, a, white, 0.5 * rounded_mask(n, [s(262), s(432), s(762), s(480)], s(24)))
    knob = Image.new("L", (n, n), 0)
    ImageDraw.Draw(knob).ellipse([s(636 - 74), s(456 - 74), s(636 + 74), s(456 + 74)], fill=255)
    rgb, a = composite(rgb, a, white, np.asarray(knob, dtype=np.float64) / 255)

    img = Image.fromarray(np.dstack([rgb, a * 255]).clip(0, 255).astype(np.uint8), "RGBA")
    img = img.resize((1024, 1024), Image.LANCZOS)
    # Content spans x 162..862, y 226..792. Center it in a square with a small margin.
    cx, cy, half = 512, 509, 380
    return img.crop((cx - half, cy - half, cx + half, cy + half))


def tray_glyph(size, color, alpha):
    """Three horizontal sliders with hollow knobs, on a 16-unit grid snapped per size."""
    k = 8
    s = size / 16
    stroke = max(1, round(s))
    img = Image.new("L", (size * k, size * k), 0)
    d = ImageDraw.Draw(img)

    def snap(v):
        # Centers on half pixels for odd strokes, whole pixels for even ones.
        p = v * s
        return (np.floor(p) + 0.5) if stroke % 2 else np.round(p)

    left, right = 1 * s, 15 * s
    r_out = 2.5 * s
    for y, knob_x in ((2.5, 10.5), (7.5, 5.0), (12.5, 8.5)):
        cy, cx = snap(y), snap(knob_x)
        gap_l, gap_r = cx - r_out - s, cx + r_out + s
        for a0, a1 in ((left, gap_l), (gap_r, right)):
            if a1 - a0 >= s:
                d.rectangle([a0 * k, (cy - stroke / 2) * k, a1 * k - 1, (cy + stroke / 2) * k - 1], fill=255)
        d.ellipse([(cx - r_out) * k, (cy - r_out) * k, (cx + r_out) * k - 1, (cy + r_out) * k - 1], fill=255)
        r_in = r_out - stroke
        d.ellipse([(cx - r_in) * k, (cy - r_in) * k, (cx + r_in) * k - 1, (cy + r_in) * k - 1], fill=0)

    mask = img.resize((size, size), Image.BOX)
    out = Image.new("RGBA", (size, size), color + (0,))
    out.putalpha(mask.point(lambda p: round(p * alpha)))
    return out


def save_ico(path, images):
    images = sorted(images, key=lambda im: im.width, reverse=True)
    images[0].save(path, format="ICO", sizes=[(im.width, im.height) for im in images], append_images=images[1:])


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)

    master = app_icon_master()
    app = [master.resize((n, n), Image.LANCZOS) for n in APP_SIZES]
    save_ico(ASSETS / "ScreenTune.ico", app)
    master.resize((256, 256), Image.LANCZOS).save(ASSETS / "ScreenTune.png")

    variants = {
        "TrayDark": ((255, 255, 255), 1.0),     # white glyph for a dark taskbar
        "TrayLight": ((27, 27, 27), 1.0),       # near-black glyph for a light taskbar
        "TrayDarkOff": ((255, 255, 255), 0.4),
        "TrayLightOff": ((27, 27, 27), 0.4),
    }
    for name, (color, alpha) in variants.items():
        save_ico(ASSETS / f"{name}.ico", [tray_glyph(n, color, alpha) for n in TRAY_SIZES])

    # Zoomed preview sheet for eyeballing pixel snapping (not shipped).
    sheet = Image.new("RGBA", (16 * 12 + 24 * 12 + 32 * 12 + 40, 32 * 12 + 20), (32, 32, 32, 255))
    x = 10
    for n in (16, 24, 32):
        g = tray_glyph(n, (255, 255, 255), 1.0).resize((n * 12, n * 12), Image.NEAREST)
        sheet.alpha_composite(g, (x, 10))
        x += n * 12 + 10
    preview = Path(__file__).resolve().parent / "preview"
    preview.mkdir(exist_ok=True)
    sheet.save(preview / "tray-preview.png")
    master.resize((256, 256), Image.LANCZOS).save(preview / "app-preview.png")
    print("icons written to", ASSETS)


if __name__ == "__main__":
    main()
