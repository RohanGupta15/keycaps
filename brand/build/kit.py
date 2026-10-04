"""Builds the Shutdown Timer "Keycap" logo kit: SVG masters, lockups and the small-size cut.

Run:  python kit.py  (needs fontTools on sys.path and Selawik Semibold; see README.md in this folder)
All geometry is exact filled outlines -- no strokes, text, filters or masks in any output file.
"""
import math, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, ".."))
FONT = os.environ.get("SELAWIK_SB", os.path.join(HERE, "selawksb.ttf"))

# ---- palette (contrast-checked: face passes 3:1 on #202020 and #F3F3F3) ----
FACE_HI, FACE_LO = "#7377E8", "#5A5ED0"   # key face, 120-degree gradient, light at top-left
BODY_HI, BODY_LO = "#3A3DA6", "#2A2C82"   # key body (the lip)
FACE_FLAT, BODY_FLAT = "#6468DC", "#32349A"
GLYPH = "#FFFFFF"
INK_LIGHT, INK_DARK = "#1B1B1F", "#FFFFFF"  # wordmark on light / dark backgrounds

# ---- geometry, 256 canvas (48-grid unit = 5.333) ----
BODY = dict(x=20, y=20, w=216, h=216, r=32)   # 6/48 radius
FACE = dict(x=38, y=30, w=180, h=170, r=22)   # 4/48 radius; lip = 36 below, 18 sides, 10 top
CX, CY, R, W, GAP = 128, 117, 54, 20, 35      # power-hour glyph on the face


def f(v):
    return f"{round(v, 2):g}"


def rrect(x, y, w, h, r):
    """Rounded rectangle as a closed path (clockwise)."""
    return (f"M{f(x + r)} {f(y)}H{f(x + w - r)}A{f(r)} {f(r)} 0 0 1 {f(x + w)} {f(y + r)}"
            f"V{f(y + h - r)}A{f(r)} {f(r)} 0 0 1 {f(x + w - r)} {f(y + h)}"
            f"H{f(x + r)}A{f(r)} {f(r)} 0 0 1 {f(x)} {f(y + h - r)}"
            f"V{f(y + r)}A{f(r)} {f(r)} 0 0 1 {f(x + r)} {f(y)}Z")


def rrect_ccw(x, y, w, h, r):
    """Same rectangle wound counter-clockwise (a hole under the nonzero rule)."""
    return (f"M{f(x + r)} {f(y)}A{f(r)} {f(r)} 0 0 0 {f(x)} {f(y + r)}V{f(y + h - r)}"
            f"A{f(r)} {f(r)} 0 0 0 {f(x + r)} {f(y + h)}H{f(x + w - r)}"
            f"A{f(r)} {f(r)} 0 0 0 {f(x + w)} {f(y + h - r)}V{f(y + r)}"
            f"A{f(r)} {f(r)} 0 0 0 {f(x + w - r)} {f(y)}Z")


def pt(cx, cy, r, deg):
    a = math.radians(deg)
    return cx + r * math.sin(a), cy - r * math.cos(a)


def ring(cx, cy, r, w, gap):
    """Open ring (power symbol body) as an outline with round ends; opening centred at 12 o'clock."""
    ro, ri, h = r + w / 2, r - w / 2, w / 2
    o0, o1 = pt(cx, cy, ro, gap), pt(cx, cy, ro, 360 - gap)
    i0, i1 = pt(cx, cy, ri, gap), pt(cx, cy, ri, 360 - gap)
    return (f"M{f(o0[0])} {f(o0[1])}A{f(ro)} {f(ro)} 0 1 1 {f(o1[0])} {f(o1[1])}"
            f"A{f(h)} {f(h)} 0 0 1 {f(i1[0])} {f(i1[1])}"
            f"A{f(ri)} {f(ri)} 0 1 0 {f(i0[0])} {f(i0[1])}"
            f"A{f(h)} {f(h)} 0 0 1 {f(o0[0])} {f(o0[1])}Z")


def hands(cx, cy, r, w, overshoot=6, hand_ratio=0.55):
    """Minute hand at 12 (the power stem) + hour hand at 3, round caps and a round outer join."""
    h = w / 2
    top = cy - r - h - overshoot + h        # stem centreline top; the round cap adds h above it
    hx = cx + r * hand_ratio                 # hour-hand tip centre
    return (f"M{f(cx - h)} {f(top)}A{f(h)} {f(h)} 0 0 1 {f(cx + h)} {f(top)}"
            f"V{f(cy - h)}H{f(hx)}A{f(h)} {f(h)} 0 0 1 {f(hx)} {f(cy + h)}"
            f"H{f(cx)}A{f(h)} {f(h)} 0 0 1 {f(cx - h)} {f(cy)}Z")


def glyph(cx=CX, cy=CY, r=R, w=W, gap=GAP):
    return ring(cx, cy, r, w, gap) + hands(cx, cy, r, w)


def gradients(prefix=""):
    # 120 degrees, light at top-left (Microsoft app-icon guidance); userSpaceOnUse for identical rendering.
    return (f'<defs>'
            f'<linearGradient id="{prefix}face" gradientUnits="userSpaceOnUse" x1="60" y1="30" x2="196" y2="200">'
            f'<stop offset="0" stop-color="{FACE_HI}"/><stop offset="1" stop-color="{FACE_LO}"/></linearGradient>'
            f'<linearGradient id="{prefix}body" gradientUnits="userSpaceOnUse" x1="40" y1="20" x2="216" y2="236">'
            f'<stop offset="0" stop-color="{BODY_HI}"/><stop offset="1" stop-color="{BODY_LO}"/></linearGradient>'
            f'</defs>')


def symbol_layers(flat=False, prefix=""):
    body = BODY_FLAT if flat else f"url(#{prefix}body)"
    face = FACE_FLAT if flat else f"url(#{prefix}face)"
    return (("" if flat else gradients(prefix))
            + f'<path fill="{body}" d="{rrect(**BODY)}"/>'
            + f'<path fill="{face}" d="{rrect(**FACE)}"/>'
            + f'<path fill="{GLYPH}" d="{glyph()}"/>')


def symbol_mono():
    """One-colour: body, an 8-unit knockout gap around the face, the face, the glyph knocked out."""
    # The colour face sits 10 below the body top; a knockout gap there would leave a hairline, so the
    # one-colour face is lowered 6 and trimmed to keep every band at least 9 units thick.
    g = 7
    face = dict(x=FACE["x"] + 2, y=FACE["y"] + 6, w=FACE["w"] - 4, h=FACE["h"] - 6, r=FACE["r"] - 2)
    gap_hole = rrect_ccw(face["x"] - g, face["y"] - g, face["w"] + 2 * g, face["h"] + 2 * g, face["r"] + g)
    return (f'<path fill="#000000" fill-rule="evenodd" d="{rrect(**BODY)}{gap_hole}{rrect(**face)}{glyph()}"/>')


def doc(title, body, w=256, h=256):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" '
            f'role="img" aria-labelledby="t"><title id="t">{title}</title>{body}</svg>\n')


# ---- small-size cut, drawn on a 16-unit grid (used for 16/20/24 px) ----
def small_symbol():
    body = rrect(0, 0.5, 16, 15, 3)
    face = rrect(1, 1, 14, 11, 2.5)
    # 2-unit strokes centred on whole pixels: the stem covers columns 7-9, the hour hand rows 6-8.
    cx, cy, r, w = 8, 7, 3.5, 2
    return (f'<path fill="{BODY_FLAT}" d="{body}"/><path fill="{FACE_FLAT}" d="{face}"/>'
            f'<path fill="{GLYPH}" d="{ring(cx, cy, r, w, 40)}{hands(cx, cy, r, w, overshoot=0.5, hand_ratio=0.72)}"/>')


def gpos_pairs(font, pairs):
    """X-advance adjustments for the given glyph pairs from GPOS PairPos lookups (formats 1 and 2)."""
    out = {}
    lookups = font["GPOS"].table.LookupList.Lookup
    for lk in lookups:
        for st in lk.SubTable:
            if lk.LookupType == 9:   # extension
                st = st.ExtSubTable
            if getattr(st, "LookupType", 2) != 2 or not hasattr(st, "Format"):
                continue
            cov = st.Coverage.glyphs
            for a, b in pairs:
                if (a, b) in out or a not in cov:
                    continue
                if st.Format == 1:
                    ps = st.PairSet[cov.index(a)]
                    for rec in ps.PairValueRecord:
                        if rec.SecondGlyph == b and rec.Value1 is not None:
                            out[(a, b)] = getattr(rec.Value1, "XAdvance", 0) or 0
                elif st.Format == 2:
                    c1 = st.ClassDef1.classDefs.get(a, 0)
                    c2 = st.ClassDef2.classDefs.get(b, 0)
                    v = st.Class1Record[c1].Class2Record[c2].Value1
                    adv = getattr(v, "XAdvance", 0) if v is not None else 0
                    if adv:
                        out[(a, b)] = adv
    return out


# ---- wordmark: Selawik Semibold (OFL), outlined with fontTools ----
def wordmark(text, size, color):
    sys.path.insert(0, os.environ.get("FONTTOOLS_PATH", ""))
    from fontTools.ttLib import TTFont
    from fontTools.pens.svgPathPen import SVGPathPen
    from fontTools.pens.transformPen import TransformPen

    font = TTFont(FONT)
    gs = font.getGlyphSet()
    cmap = font.getBestCmap()
    upm = font["head"].unitsPerEm
    scale = size / upm
    names = [cmap[ord(c)] for c in text]
    kern = gpos_pairs(font, list(zip(names, names[1:])))
    pen = SVGPathPen(gs)
    x = 0
    for i, n in enumerate(names):
        tp = TransformPen(pen, (scale, 0, 0, -scale, x, 0))
        gs[n].draw(tp)
        x += gs[n].width * scale
        if i + 1 < len(names):
            x += kern.get((n, names[i + 1]), 0) * scale
    cap = font["OS/2"].sCapHeight * scale
    d = re.sub(r"-?\d+\.\d+", lambda m: f"{round(float(m.group()), 2):g}", pen.getCommands())
    return d, x, cap, kern


def lockups():
    d, width, cap, kerned = wordmark("Shutdown Timer", 100, INK_LIGHT)
    # Horizontal: symbol 256 high; cap height centred on the key face's optical centre.
    sym_h = 256
    gapx = 44
    base_y = round(CY + cap / 2)   # baseline so caps are centred on the glyph centre
    W_ = round(sym_h + gapx + width + 8)
    for tone, ink in (("light", INK_LIGHT), ("dark", INK_DARK)):
        body = (symbol_layers(prefix="h") +
                f'<path fill="{ink}" transform="translate({sym_h + gapx} {base_y})" d="{d}"/>')
        open(os.path.join(OUT, f"shutdown-timer-horizontal-{tone}.svg"), "w").write(
            doc("Shutdown Timer", body, w=W_, h=256))
    # Stacked: symbol centred above the wordmark.
    sw = max(256, round(width + 16))
    H = 256 + 40 + round(cap) + 24
    sx = (sw - 256) / 2
    for tone, ink in (("light", INK_LIGHT), ("dark", INK_DARK)):
        body = (f'<g transform="translate({f(sx)} 0)">{symbol_layers(prefix="s")}</g>'
                f'<path fill="{ink}" transform="translate({f((sw - width) / 2)} {256 + 40 + round(cap)})" d="{d}"/>')
        open(os.path.join(OUT, f"shutdown-timer-stacked-{tone}.svg"), "w").write(
            doc("Shutdown Timer", body, w=sw, h=H))
    return kerned


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    files = {
        "shutdown-timer-symbol.svg": doc("Shutdown Timer", symbol_layers()),
        "shutdown-timer-symbol-flat.svg": doc("Shutdown Timer", symbol_layers(flat=True)),
        "shutdown-timer-symbol-mono.svg": doc("Shutdown Timer", symbol_mono()),
        "shutdown-timer-symbol-small.svg": doc("Shutdown Timer (16 px)", small_symbol(), w=16, h=16),
    }
    for name, content in files.items():
        open(os.path.join(OUT, name), "w").write(content)
    kerned = lockups()
    print("GPOS kerning applied:", kerned)
    print("wrote:", sorted(n for n in os.listdir(OUT) if n.endswith(".svg")))
