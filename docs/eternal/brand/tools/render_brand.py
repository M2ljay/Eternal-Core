#!/usr/bin/env python3
"""Render Core brand rasters (PNG/ICO) from SVG built here, with "CORE" outlined from Inter.

Usage: render_brand.py <out_dir>
Writes svg sources and rasters into out_dir; nothing in the repo is touched.
"""
import io
import os
import subprocess
import sys

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.pens.boundsPen import BoundsPen
from fontTools.ttLib import TTFont
from PIL import Image

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
FONT = "/usr/share/fonts/opentype/inter/Inter-Medium.otf"

GUNMETAL, UNLIT, CRIMSON, STEEL, BOOT = "#1A1D21", "#3A3F46", "#D7263D", "#C9D1D9", "#0A0B0D"
CHROME = ('<linearGradient id="chrome" x1="0" y1="0" x2="0" y2="1">'
          '<stop offset="0" stop-color="#F4F7FA"/><stop offset="0.48" stop-color="#AEB8C2"/>'
          '<stop offset="0.52" stop-color="#5B636C"/><stop offset="1" stop-color="#C9D1D9"/></linearGradient>')

TICKS = ('<line x1="0" y1="-48" x2="0" y2="-38"/><line x1="0" y1="38" x2="0" y2="48"/>'
         '<line x1="-48" y1="0" x2="-38" y2="0"/><line x1="38" y1="0" x2="48" y2="0"/>'
         '<line x1="-33.9" y1="-33.9" x2="-26.9" y2="-26.9"/><line x1="26.9" y1="26.9" x2="33.9" y2="33.9"/>'
         '<line x1="33.9" y1="-33.9" x2="26.9" y2="-26.9"/><line x1="-26.9" y1="26.9" x2="-33.9" y2="33.9"/>')


def mark(charged=False):
    """The ring mark centred on 0,0, as in core-logo-mark.svg (outer radius 66.5 before scaling)."""
    lit = ('<circle r="62" fill="none" stroke="%s" stroke-width="9" stroke-linecap="round" transform="rotate(180)"%s/>'
           % (CRIMSON, "" if charged else ' stroke-dasharray="389.6" stroke-dashoffset="194.8"'))
    return ('<circle r="62" fill="none" stroke="%s" stroke-width="9"/>%s'
            '<g stroke="%s" stroke-width="5" stroke-linecap="round">%s</g>'
            '<circle r="22" fill="%s"/><circle r="9" fill="%s"/>' % (UNLIT, lit, STEEL, TICKS, STEEL, CRIMSON))


def small_mark():
    """Simplified mark for 16-32 px icons: thicker ring, no ticks, bigger core."""
    return ('<circle r="58" fill="none" stroke="%s" stroke-width="18"/>'
            '<circle r="58" fill="none" stroke="%s" stroke-width="18" stroke-linecap="round" transform="rotate(180)"'
            ' stroke-dasharray="364.4" stroke-dashoffset="182.2"/>'
            '<circle r="27" fill="%s"/><circle r="11" fill="%s"/>' % (UNLIT, CRIMSON, STEEL, CRIMSON))


def tile_svg(size, small=False, charged=False):
    inner = small_mark() if small else mark(charged)
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 200 200">'
            '<rect width="200" height="200" rx="40" fill="%s"/>'
            '<g transform="translate(100,100) scale(1.15)">%s</g></svg>' % (size, size, GUNMETAL, inner))


def mark_svg(size, pad=0.0):
    """Ring mark without a tile, transparent, filling the square minus pad (fraction)."""
    s = (1 - 2 * pad) * size / 133.0
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 %d %d">'
            '<g transform="translate(%g,%g) scale(%g)">%s</g></svg>' % (size, size, size, size, size / 2, size / 2, s, mark()))


_font = TTFont(FONT)
_glyphs = _font.getGlyphSet()
_cmap = _font.getBestCmap()
_upm = _font["head"].unitsPerEm
_cap = _font["OS/2"].sCapHeight


def text_path(text, size, tracking):
    """Outline text: returns (svg path d, width, cap height) with baseline at y=0, origin at x=0."""
    k = size / _upm
    pen = SVGPathPen(_glyphs)
    x = 0.0
    last_adv = 0.0
    for i, ch in enumerate(text):
        g = _cmap[ord(ch)]
        tp = TransformPen(pen, (k, 0, 0, -k, x, 0))
        _glyphs[g].draw(tp)
        last_adv = _glyphs[g].width * k
        x += last_adv + (tracking * size if i < len(text) - 1 else 0)
    # Tight right edge: use the last glyph's ink bounds rather than its advance.
    bp = BoundsPen(_glyphs)
    _glyphs[_cmap[ord(text[-1])]].draw(bp)
    width = x - last_adv + bp.bounds[2] * k
    return pen.getCommands(), width, _cap * k


def wordmark_svg(w, h, fill="chrome", text_scale=0.42, mark_scale=0.86, gap=0.20, tracking=0.42):
    """Mark on the left, CORE to the right, the group centred in w x h (transparent)."""
    mark_d = mark_scale * h                     # rendered diameter of the ring
    ms = mark_d / 133.0
    size = text_scale * h / (_cap / _upm)       # font size so cap height = text_scale * h
    d, tw, cap = text_path("CORE", size, tracking)
    total = mark_d + gap * h + tw
    if total > 0.94 * w:                        # keep inside the canvas
        f = 0.94 * w / total
        return wordmark_svg(w, h, fill, text_scale * f, mark_scale * f, gap * f, tracking)
    x0 = (w - total) / 2
    cx = x0 + mark_d / 2
    tx = x0 + mark_d + gap * h
    ty = h / 2 + cap / 2
    paint = "url(#chrome)" if fill == "chrome" else fill
    grad = ('<defs><linearGradient id="chrome" gradientUnits="userSpaceOnUse" x1="0" y1="%g" x2="0" y2="%g">'
            '<stop offset="0" stop-color="#F4F7FA"/><stop offset="0.48" stop-color="#AEB8C2"/>'
            '<stop offset="0.52" stop-color="#5B636C"/><stop offset="1" stop-color="#C9D1D9"/></linearGradient></defs>'
            % (-cap, 0)) if fill == "chrome" else ""
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 %d %d">%s'
            '<g transform="translate(%g,%g) scale(%g)">%s</g>'
            '<path transform="translate(%g,%g)" fill="%s" d="%s"/></svg>'
            % (w, h, w, h, grad, cx, h / 2, ms, mark(), tx, ty, paint, d))


def render(svg, path, w, h):
    src = path + ".svg"
    with open(src, "w") as f:
        f.write(svg)
    subprocess.run(["rsvg-convert", "-w", str(w), "-h", str(h), "-f", "png", "-o", path, src], check=True)
    os.remove(src)
    im = Image.open(path)
    assert im.size == (w, h), (path, im.size)


def png(name, svg, w, h):
    render(svg, os.path.join(OUT, name), w, h)


def ico(name):
    sizes = [16, 24, 32, 48, 64, 128, 256]
    frames = []
    for s in sizes:
        p = os.path.join(OUT, "_ico_%d.png" % s)
        render(tile_svg(s, small=s <= 32), p, s, s)
        frames.append(Image.open(p).convert("RGBA"))
    frames[-1].save(os.path.join(OUT, name), format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[:-1])
    for s in sizes:
        os.remove(os.path.join(OUT, "_ico_%d.png" % s))


# Square icons on the gunmetal tile.
ico("core.ico")
png("tile-512.png", tile_svg(512), 512, 512)
png("tile-1024.png", tile_svg(1024), 1024, 1024)
# Badge glyph: the mark alone, for the app's store badge (drawn on a gunmetal disc) and the Big Picture source badge.
png("mark-450.png", mark_svg(450, pad=0.02), 450, 450)
# Big Picture source badge (shown as is): mark on its tile.
png("tile-450.png", tile_svg(450), 450, 450)
# Wordmarks (transparent).
png("wordmark-500x132-white.png", wordmark_svg(500, 132, fill="#F4F7FA"), 500, 132)
png("wordmark-1600x371.png", wordmark_svg(1600, 371), 1600, 371)
png("wordmark-2257x595.png", wordmark_svg(2257, 595), 2257, 595)
# SVG sources kept with the brand masters (text already outlined).
with open(os.path.join(OUT, "core-wordmark.svg"), "w") as f:
    f.write(wordmark_svg(1600, 371))
with open(os.path.join(OUT, "core-logo-decky.svg"), "w") as f:
    f.write(mark_svg(450, pad=0.02))
print("rendered into", OUT)
