#!/bin/sh
# Puts a recorded title take into the game: the main menu's background lap (assets/replays/ilyich-title-rally.*).
# Record a take first:  godot-mono --path . -- --title-rec=DIR     (drive the lap; keys 1-4 change the car; DIR/lap-001 is the take)
# Then:                 tools/title/promote_title_take.sh DIR/lap-001
# The take's track signature goes with it: a changed track makes the game ignore it (the older demonstration plays instead).
set -e
take="${1:?usage: promote_title_take.sh TAKE_FOLDER (a lap-NNN folder of --title-rec)}"
here="$(cd "$(dirname "$0")/../.." && pwd)"
out="$here/assets/replays/ilyich-title-rally"
for f in ghost_ilyich.bin tape_ilyich.bin fuels.txt track_ilyich.sig; do
  [ -f "$take/$f" ] || { echo "missing $take/$f" >&2; exit 1; }
done
cp "$take/ghost_ilyich.bin" "$out.bin"
cp "$take/tape_ilyich.bin" "$out.tape"
cp "$take/fuels.txt" "$out.fuels"
cp "$take/track_ilyich.sig" "$out.sig"
secs="$(sed -n 's/.*"seconds":\([0-9.]*\).*/\1/p' "$take/session-lap.json" 2>/dev/null || true)"
cat > "$here/assets/replays/README-title-rally.md" <<EOF
Bundled title background lap: KOLKHOZ (ilyich), the rally's moving world, the fuel keys of the Stakhanov laps.
Fuels (seconds:grade, 0 diesel, 1 petrol, 2 jet fuel, 3 Sputnik): $(cat "$take/fuels.txt")
Lap: ${secs:-?} seconds, taken from $(basename "$take") on $(date +%F).
The track signature (.sig) keeps it from playing after incompatible terrain changes; the older demonstration is the fallback.
This is title-screen content only and never seeds player records, leaderboards or racing ghosts.
EOF
echo "promoted: $out.{bin,tape,fuels,sig}"
