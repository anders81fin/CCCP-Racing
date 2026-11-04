#!/bin/sh
# Regenerates docs/map/<track>_map.png and <track>_height.png (default bonus): export_map.sh [track]
# Opens a window for a moment (the render needs the GPU).
cd "$(dirname "$0")/../.."
TRACK=${1:-bonus}
dotnet build -v q -nologo || exit 1
godot-mono --path . --windowed res://scenes/tools/MapExport.tscn -- --out=docs/map --track="$TRACK" || exit 1
python3 tools/map/annotate_map.py docs/map "$TRACK"
