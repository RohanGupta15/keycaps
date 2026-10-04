"""Renders the app icon PNGs and packs a multi-size Windows .ico (PNG-compressed frames).

Sizes follow Windows' icon scaling needs (16-256). 16/20/24 use the pixel-fitted small cut; 32+ use the full drawing.
Run after kit.py:  python icons.py
"""
import os, struct, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
BRAND = os.path.abspath(os.path.join(HERE, ".."))
RENDER = os.environ.get("RENDER_PNG", os.path.expanduser("~/.claude/skills/logo-design/scripts/render_png.py"))
PNG_DIR = os.path.join(BRAND, "png")
SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 256]
SMALL = {16, 20, 24}


def render(src, dst, size):
    subprocess.run([sys.executable, RENDER, src, "-o", dst, "--width", str(size), "--height", str(size)],
                   check=True, capture_output=True)


def pack_ico(frames, dst):
    """frames: list of (size, png_bytes). ICONDIR + ICONDIRENTRY[] + PNG payloads."""
    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    entries, blobs = b"", b""
    for size, data in frames:
        dim = 0 if size >= 256 else size    # 0 means 256 in ICONDIRENTRY
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        blobs += data
        offset += len(data)
    with open(dst, "wb") as fh:
        fh.write(header + entries + blobs)


if __name__ == "__main__":
    os.makedirs(PNG_DIR, exist_ok=True)
    frames = []
    for s in SIZES:
        src = "shutdown-timer-symbol-small.svg" if s in SMALL else "shutdown-timer-symbol.svg"
        dst = os.path.join(PNG_DIR, f"shutdown-timer-{s}.png")
        render(os.path.join(BRAND, src), dst, s)
        frames.append((s, open(dst, "rb").read()))
    for s in (512, 1024):
        render(os.path.join(BRAND, "shutdown-timer-symbol.svg"), os.path.join(PNG_DIR, f"shutdown-timer-{s}.png"), s)
    pack_ico(frames, os.path.join(BRAND, "shutdown-timer.ico"))
    print("ico frames:", SIZES)
