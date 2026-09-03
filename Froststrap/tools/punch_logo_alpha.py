"""Knock near-black plates off generated marks so the S sits on true alpha."""
from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image


def punch(src: Path, dst: Path) -> None:
    img = Image.open(src).convert("RGBA")
    pix = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = pix[x, y]
            redness = r - max(g, b)
            dark = r < 36 and g < 36 and b < 36
            gray = abs(r - g) < 12 and abs(r - b) < 12 and r < 70
            if dark or gray or redness < 12:
                pix[x, y] = (r, g, b, 0)
            elif redness < 28:
                pix[x, y] = (r, g, b, min(a, int(redness * 8)))
    img.save(dst, "PNG")


def main() -> int:
    if len(sys.argv) < 3:
        return 1
    punch(Path(sys.argv[1]), Path(sys.argv[2]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
