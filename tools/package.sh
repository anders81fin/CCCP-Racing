#!/bin/sh
# Export release builds, with licenses and Steam custom artwork next to the game.
#   tools/package.sh                 # Linux, Windows Vulkan, Windows D3D12 and macOS
#   tools/package.sh linux windows   # only some of them
set -eu
cd "$(dirname "$0")/.." || exit 1
V=$(sed -n 's/^config\/version="\(.*\)"/\1/p' project.godot)
LEGAL="LICENSE COPYING CC-BY-SA-4.0.txt CREDITS.md THIRD-PARTY-LICENSES.md third-party"
EXTRAS="$LEGAL steam-artwork"
[ $# -eq 0 ] && set -- linux windows windows-d3d12 macos

# Keep old exports out of Godot's resource scan while rebuilding packages.
mkdir -p build
touch build/.gdignore
# .NET universal exports can exceed a memory-backed /tmp quota.
mkdir -p "build/releases/$V/tmp"
TMPDIR="$PWD/build/releases/$V/tmp"
export TMPDIR

dotnet build -v q -nologo || exit 1
# The editor (a windowed export, see below) rewrites project.godot in its own format: keep the file as it is and put it back afterwards.
cp project.godot build/project.godot.keep
trap 'cp build/project.godot.keep project.godot' EXIT HUP INT TERM
godot-mono --headless --path . --import >build/import-release.log 2>&1 || exit 1

for p in "$@"; do
    case $p in
        linux)   preset=Linux;   dir=build/releases/$V/linux;   out=CCCPRacing.x86_64;  zip=linux-x86_64 ;;
        windows) preset=Windows; dir=build/releases/$V/windows; out=CCCPRacing.exe;     zip=windows-x86_64 ;;
        windows-d3d12) preset=Windows; dir=build/releases/$V/windows-d3d12; out=CCCPRacing.exe; zip=windows-x86_64-d3d12 ;;
        macos)   preset=macOS;   dir=build/releases/$V/macos;   out="CCCP Racing.app"; zip=macos-universal ;;
        *) echo "unknown platform: $p"; exit 1 ;;
    esac
    mkdir -p "$dir"
    # Not --headless: the shader baker (export option shader_baker/enabled) needs the GPU, so a window opens for the export.
    exportlog="build/releases/$V/export-$p.log"
    if ! godot-mono --path . --export-release "$preset" "$dir/$out" >"$exportlog" 2>&1; then
        cat "$exportlog"
        exit 1
    fi
    if grep -iE "^ERROR|failed" "$exportlog"; then exit 1; fi
    if [ "$p" = windows-d3d12 ]; then
        # The same Windows binaries select the backend through the runtime override.
        # Keep this a distinct package; the ordinary windows channel stays Vulkan.
        printf '[rendering]\nrendering_device/driver.windows="d3d12"\n' > "$dir/override.cfg"
        printf '@echo off\r\ncd /d "%%~dp0"\r\nstart "" "CCCPRacing.exe" --rendering-driver d3d12 %%*\r\n' > "$dir/Start-DirectX12.bat"
        cat > "$dir/DIRECTX12-README.txt" <<EOF
CCCP Racing $V - Direct3D 12

Extract the entire ZIP before starting.
Run Start-DirectX12.bat to explicitly select Direct3D 12.
CCCPRacing.exe also defaults to Direct3D 12 via override.cfg.
Requires a compatible Windows system and Direct3D 12 graphics driver.
Uses the Windows system Direct3D runtime (no bundled Agility SDK).
Runtime on Windows has not been tested on this Linux machine.
EOF
    fi
    case "$p" in
        windows|windows-d3d12)
            cat > "$dir/VIDEO-EXPORT.txt" <<EOF
CCCP Racing $V - replay video export

Open a saved replay, press V for EDIT / EXPORT, then choose EXPORT.
Video export requires FFmpeg with the libx264 video and AAC audio encoders.
Put ffmpeg.exe beside CCCPRacing.exe, or make it available on PATH.
If your FFmpeg distribution includes DLLs, keep those beside ffmpeg.exe too.
FFmpeg is not included in this game package.

The renderer runs as a separate hidden process and uses this package's
graphics backend. Keep the entire extracted game folder together.
The export panel reports progress and the destination of the MP4 file.
Windows runtime validation is still required; these packages were built on Linux.
EOF
            ;;
    esac
    zipfile="$PWD/build/CCCPRacing-$V-$zip.zip"
    rm -f "$zipfile"
    if [ "$p" = macos ]; then
        # Export the bundle directly onto disk: Godot's ZIP exporter stages the
        # whole app in its temporary directory, which may have a small quota.
        (cd "$dir" && zip -qry "$zipfile" "$out")
        zip -qr "$zipfile" $EXTRAS -x 'steam-artwork/.gdignore'
    else
        for f in $EXTRAS; do cp -r "$f" "$dir/"; done
        (cd "$dir" && zip -qr "$zipfile" . -x 'steam-artwork/.gdignore')
    fi
    echo "built $zipfile"
done

cp build/project.godot.keep project.godot
