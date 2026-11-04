#!/bin/sh
# Build the C# assembly, import assets and launch CCCP Racing (fullscreen; Alt+Enter toggles window).
cd "$(dirname "$0")"
dotnet build -v q -nologo || exit 1
godot-mono --headless --path . --import >/dev/null 2>&1
exec godot-mono --path . "$@"
