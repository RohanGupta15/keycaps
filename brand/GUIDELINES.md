# Shutdown Timer: logo guidelines

**Idea:** every utility is a key you press once. Shutdown Timer's key shows a power symbol read as a clock at 3:00: the power stem is the minute hand, and a short hour hand points to 3.

## Files

| Use | File |
|---|---|
| App icon (exe, window, taskbar) | `shutdown-timer.ico`, with 16/20/24/32/40/48/64/96/256 frames |
| Symbol, colour (default) | `shutdown-timer-symbol.svg` |
| Symbol, flat colour (print, tools without gradients) | `shutdown-timer-symbol-flat.svg` |
| Symbol at 16–24 px | `shutdown-timer-symbol-small.svg`, drawn on the 16 px grid |
| One colour | `shutdown-timer-symbol-mono.svg`, `-black.svg`, `-white.svg` |
| Horizontal lockup | `shutdown-timer-horizontal-light.svg` / `-dark.svg` |
| Stacked lockup | `shutdown-timer-stacked-light.svg` / `-dark.svg` |
| PNGs | `png/shutdown-timer-{16…1024}.png` |
| Presentation | `presentation/board.html`, `presentation/slide-0N.png` |

Every SVG is pure filled outlines: no live text, strokes, filters or masks.

## Construction (256 grid; 1 Microsoft icon-grid unit = 5.33)
- **Key body:** 216 × 216, radius 32 (6/48).
- **Key face:** 180 × 170, radius 22 (4/48). It sits 10 below the body's top edge, leaving an 18 margin at each side and a 36 lip at the bottom.
- **Glyph:** open ring with radius 54 and a 20 stroke, opening ±35° at 12 o'clock. The stem (minute hand) rises 6 past the ring. The hour hand is 0.55 × the radius, with round ends and a round joint.

## Colour
| Role | HEX | RGB |
|---|---|---|
| Face gradient, top-left | `#7377E8` | 115 119 232 |
| Face gradient, bottom-right | `#5A5ED0` | 90 94 208 |
| Face, flat | `#6468DC` | 100 104 220 |
| Body gradient | `#3A3DA6` → `#2A2C82` | 58 61 166 → 42 44 130 |
| Body, flat | `#32349A` | 50 52 154 |
| Glyph | `#FFFFFF` | 255 255 255 |
| Wordmark on light / dark | `#1B1B1F` / `#FFFFFF` | |

- Gradients run at 120°, lighter at the top-left, as Microsoft's app-icon guidance recommends.
- The face makes up 66% of the icon, and every face colour passes 3:1 against the Windows dark (#202020) and light (#F3F3F3) backgrounds. The lowest result is 3.06:1 (`#5A5ED0` on dark).
- The palette is digital-only. No CMYK or Pantone values are defined yet.

## Wordmark
- Selawik Semibold (SIL OFL 1.1), Microsoft's open-source companion to Segoe UI, converted to outlines. The font has no kerning table; spacing was checked by eye.
- Caps are centred on the glyph's centre, with the gap between symbol and text set to 44/256 of the symbol height.

## Clear space and minimum size
- **Clear space:** keep at least the lip height (36/256 ≈ 14% of the symbol) empty on every side.
- **Symbol:** use the small cut from 16 to 24 px and the full drawing from 32 px up. The `.ico` already follows this.
- **Horizontal lockup:** the symbol should be at least 24 px tall; below that, use the symbol alone.

## Backgrounds
- The colour symbol works on light, dark, Mica and wallpaper backgrounds.
- On busy or mid-indigo backgrounds, use the one-colour white or black version.

## Don't
- Recolour the glyph (it's always white) or remove the key body.
- Stretch or rotate the mark, or add shadows or glows.
- Use the full drawing below 24 px.
- Set "Shutdown Timer" in another font, or retype it instead of using the outlined lockup.

## Suite system (for future tools)
- Every tool uses the **same key**: same body and face geometry and radii, the same 36 lip, and a white glyph with the same stroke weight (20/256).
- Each tool gets **its own hue**. Its face base colour must have a relative luminance between **0.143 and 0.265**, which gives at least 3:1 on both themes and at least 3.8:1 for the white glyph. The body uses the same hue, darker.
- Example face colours, each checked against both themes (about 3.8:1 on each):

| Hue | Face base |
|---|---|
| indigo | `#6E70DD` |
| teal | `#228791` |
| green | `#218C4E` |
| amber | `#A57127` |
| red | `#D54D59` |
| magenta | `#D039B7` |
| blue | `#357DD0` |
- Glyphs follow Microsoft's app-icon guidance: one literal metaphor, no letters, drawn straight-on. Each tool also needs a hand-drawn 16 px cut.
- The **suite key** (the umbrella icon) is designed once the suite has a name. Don't use a four-squares glyph, because it reads as the Windows logo.

## Regenerating
From `brand/build/`:

```
python kit.py
python icons.py
```

`kit.py` needs fontTools and `selawksb.ttf`. Get the font from the Selawik 1.01 release at github.com/microsoft/Selawik and set `SELAWIK_SB` to its path, and `FONTTOOLS_PATH` if fontTools isn't installed. `icons.py` uses the logo-design skill's `render_png.py`, which renders through Chrome.

## Notes
- Trademark clearance hasn't been checked. Run a professional search before any commercial release.
