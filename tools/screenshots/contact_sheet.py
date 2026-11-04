#!/usr/bin/env python3
"""Make labelled review sheets without modifying the original PNG captures."""
import argparse
from pathlib import Path
from PIL import Image, ImageDraw

parser = argparse.ArgumentParser()
parser.add_argument("pattern")
parser.add_argument("output")
args = parser.parse_args()
files = sorted(Path("media/marketing-stills-2026-10-15/candidates").glob(args.pattern))
if not files:
    raise SystemExit("No captures match")
sheet = Image.new("RGB", (1200, 250 * ((len(files) + 2) // 3)), (22, 22, 22))
draw = ImageDraw.Draw(sheet)
for i, path in enumerate(files):
    with Image.open(path) as source:
        source.thumbnail((400, 225))
        x, y = i % 3 * 400, i // 3 * 250
        sheet.paste(source, (x, y))
        draw.text((x + 5, y + 230), path.stem, fill="white")
sheet.save(args.output)
