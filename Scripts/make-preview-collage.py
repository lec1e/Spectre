from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
PREVIEW = ROOT / ".github" / "preview"
NEBULA = ROOT / "Froststrap" / "Assets" / "spectre-nebula-field.png"

CANVAS_W = 1400
PAD = 36
GAP = 20
RADIUS = 18
SHADOW = 18


def round_corners(im: Image.Image, radius: int) -> Image.Image:
    mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, im.width, im.height), radius=radius, fill=255)
    out = im.convert("RGBA")
    out.putalpha(mask)
    return out


def fit_width(im: Image.Image, width: int) -> Image.Image:
    height = max(1, round(im.height * width / im.width))
    return im.resize((width, height), Image.Resampling.LANCZOS)


def drop_shadow(size: tuple[int, int], radius: int) -> Image.Image:
    shadow = Image.new("RGBA", (size[0] + SHADOW * 2, size[1] + SHADOW * 2), (0, 0, 0, 0))
    draw = ImageDraw.Draw(shadow)
    draw.rounded_rectangle(
        (SHADOW, SHADOW + 6, SHADOW + size[0], SHADOW + size[1] + 6),
        radius=radius,
        fill=(0, 0, 0, 160),
    )
    return shadow.filter(ImageFilter.GaussianBlur(10))


def paste_framed(canvas: Image.Image, im: Image.Image, xy: tuple[int, int]) -> None:
    framed = round_corners(im, RADIUS)
    shadow = drop_shadow(framed.size, RADIUS)
    canvas.alpha_composite(shadow, (xy[0] - SHADOW, xy[1] - SHADOW))
    canvas.alpha_composite(framed, xy)
    overlay = Image.new("RGBA", framed.size, (0, 0, 0, 0))
    ImageDraw.Draw(overlay).rounded_rectangle(
        (0, 0, framed.width - 1, framed.height - 1),
        radius=RADIUS,
        outline=(225, 29, 72, 70),
        width=1,
    )
    canvas.alpha_composite(overlay, xy)


def nebula_background(size: tuple[int, int]) -> Image.Image:
    canvas = Image.new("RGBA", size, (7, 3, 8, 255))
    if NEBULA.exists():
        field = Image.open(NEBULA).convert("RGBA")
        field = field.resize(size, Image.Resampling.LANCZOS)
        canvas = Image.blend(canvas, field, 0.42)
    # Keep the center a little darker so the windows read clearly.
    vignette = Image.new("L", size, 0)
    ImageDraw.Draw(vignette).ellipse(
        (-int(size[0] * 0.15), -int(size[1] * 0.1), int(size[0] * 1.15), int(size[1] * 1.05)),
        fill=90,
    )
    vignette = vignette.filter(ImageFilter.GaussianBlur(80))
    dark = Image.new("RGBA", size, (5, 2, 4, 255))
    canvas = Image.composite(canvas, dark, ImageOps_invert(vignette))
    return canvas


def ImageOps_invert(mask: Image.Image) -> Image.Image:
    return Image.eval(mask, lambda p: 255 - p)


def main() -> None:
    home = Image.open(PREVIEW / "home.png").convert("RGBA")
    games = Image.open(PREVIEW / "games.png").convert("RGBA")
    library = Image.open(PREVIEW / "library.png").convert("RGBA")
    mods = Image.open(PREVIEW / "mods.png").convert("RGBA")
    settings = Image.open(PREVIEW / "settings.png").convert("RGBA")

    home_w = CANVAS_W - PAD * 2
    home_im = fit_width(home, home_w)
    small_w = (home_w - GAP) // 2
    games_im = fit_width(games, small_w)
    library_im = fit_width(library, small_w)
    mods_im = fit_width(mods, small_w)
    settings_im = fit_width(settings, small_w)
    small_h = games_im.height

    canvas_h = PAD + home_im.height + GAP + small_h + GAP + small_h + PAD
    canvas = nebula_background((CANVAS_W, canvas_h))

    y = PAD
    paste_framed(canvas, home_im, (PAD, y))
    y += home_im.height + GAP
    paste_framed(canvas, games_im, (PAD, y))
    paste_framed(canvas, library_im, (PAD + small_w + GAP, y))
    y += small_h + GAP
    paste_framed(canvas, mods_im, (PAD, y))
    paste_framed(canvas, settings_im, (PAD + small_w + GAP, y))

    out = PREVIEW / "app-preview.png"
    canvas.convert("RGB").save(out, "PNG", optimize=True)
    print(f"wrote {out} {canvas.size}")


if __name__ == "__main__":
    main()
