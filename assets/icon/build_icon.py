"""Regenerates src/NovaMacro.App/Assets/NovaMacro.ico and the PNG preview from NovaMacro.svg.

Requires `rsvg-convert` (librsvg) and Pillow:  python build_icon.py
"""
import pathlib
import subprocess
import tempfile

from PIL import Image

HERE = pathlib.Path(__file__).resolve().parent
SVG = HERE / "NovaMacro.svg"
ICO = HERE.parent.parent / "src" / "NovaMacro.App" / "Assets" / "NovaMacro.ico"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def render(size: int, target: pathlib.Path) -> Image.Image:
    subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), "-o", str(target), str(SVG)], check=True)
    return Image.open(target).convert("RGBA")


def main() -> None:
    with tempfile.TemporaryDirectory() as tmp:
        images = [render(s, pathlib.Path(tmp) / f"{s}.png") for s in SIZES]
        largest = images[-1]
        largest.save(HERE / "NovaMacro-256.png")
        ICO.parent.mkdir(parents=True, exist_ok=True)
        largest.save(ICO, format="ICO", sizes=[(s, s) for s in SIZES], append_images=images[:-1])
    print("wrote", ICO)


if __name__ == "__main__":
    main()
