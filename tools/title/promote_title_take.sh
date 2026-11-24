#!/bin/sh
# Puts a recorded title take into the game: the main menu's background lap of one track (assets/replays/TRACK-title-rally.*).
# Record a take first:  godot-mono --path . -- --title-rec=DIR [--track=ilyich|siberia|atomgrad|bonus]     (the kolkhoz without --track=; drive the lap; keys 1-4 change the car; DIR/lap-001 is the take)
# Then:                 tools/title/promote_title_take.sh DIR/lap-001 [TRACK]     (TRACK defaults to ilyich)
# The take's track signature goes with it: a changed track makes the game ignore it (the older demonstration plays instead).
set -e
take="${1:?usage: promote_title_take.sh TAKE_FOLDER [TRACK] (a lap-NNN folder of --title-rec)}"
track="${2:-ilyich}"
here="$(cd "$(dirname "$0")/../.." && pwd)"
out="$here/assets/replays/$track-title-rally"
for f in ghost_$track.bin tape_$track.bin fuels.txt track_$track.sig; do
  [ -f "$take/$f" ] || { echo "missing $take/$f" >&2; exit 1; }
done
cp "$take/ghost_$track.bin" "$out.bin"
cp "$take/tape_$track.bin" "$out.tape"
cp "$take/fuels.txt" "$out.fuels"
cp "$take/track_$track.sig" "$out.sig"
secs="$(sed -n 's/.*"seconds":\([0-9.]*\).*/\1/p' "$take/session-lap.json" 2>/dev/null || true)"
readme="README-$track-title-rally.md"; [ "$track" = ilyich ] && readme="README-title-rally.md"
cat > "$here/assets/replays/$readme" <<EOF
Bundled title background lap: $track, the rally's moving world, the fuel keys of the Stakhanov laps.
Fuels (seconds:grade, 0 diesel, 1 petrol, 2 jet fuel, 3 Sputnik): $(cat "$take/fuels.txt")
Lap: ${secs:-?} seconds, taken from $(basename "$take") on $(date +%F).
The track signature (.sig) keeps it from playing after incompatible terrain changes; the older demonstration is the fallback.
This is title-screen content only and never seeds player records, leaderboards or racing ghosts.
EOF
echo "promoted: $out.{bin,tape,fuels,sig}"
