#!/bin/bash
# Refactoring check: run the game headless through the rally, Stakhanov laps, the title replay, the
# track previews and the three schools (all their steps, fast-forwarded), tracing the world each
# physics step (RaceScene.TraceProbe.cs).
#   tools/trace/world-trace.sh OUTDIR              trace this build into OUTDIR (exit 1: a run failed)
#   tools/trace/world-trace.sh --compare OLD NEW   do NEW's traces match OLD's? (exit 1: one differs or is missing)
# Trace before and after a change: the same files = the world steps bit for bit alike. The real
# records are only copied (to a scratch data dir), never written. --babushka-seed / --rng-seed: the
# babushkas draw the same mothers and react alike, the backfires pop alike each run (else a run is
# not repeatable: a backfire's camera jolt can decide a babushka hit in the lens).
set -u
cd "$(dirname "$0")/../.."
if [ "${1:-}" = "--compare" ]; then
    bad=0
    for f in "$2"/*.txt; do
        n=$(basename "$f")
        if [ ! -f "$3/$n" ]; then echo "$n MISSING"; bad=1
        elif cmp -s "$f" "$3/$n"; then echo "$n same"
        else echo "$n DIFFERS from step $(( $(cmp "$f" "$3/$n" | head -1 | sed 's/.*line //') - 1 ))"; bad=1; fi
    done
    exit $bad
fi
out=$(realpath -m "$1"); mkdir -p "$out"
user="$HOME/.local/share/godot/app_userdata/CCCP Racing"
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
dotnet build -v q -nologo >/dev/null || { echo "build failed"; exit 1; }
names=(); ends=()
run() {   # name end frames [game args]; end: END (the step limit) or a line the log must show; RECORD=1 loads the record ghosts
    local name=$1 end=$2 frames=$3; shift 3
    mkdir -p "$scratch/$name"; for f in "$user"/*; do [ -f "$f" ] && cp -a "$f" "$scratch/$name/"; done    # records, signatures, settings
    local rec=--no-record; [ "${RECORD:-}" = 1 ] && rec=
    rm -f "$out/$name.txt"
    while [ "$(jobs -rp | wc -l)" -ge "${JOBS:-6}" ]; do wait -n; done     # memory: a few games at a time
    ( WORLDTRACE="$out/$name.txt" WORLDTRACE_FRAMES=$frames timeout 900 godot-mono --headless --fixed-fps 60 --path . -- \
        $rec --babushka-seed=1 --rng-seed=1 --data-dir="$scratch/$name" "$@" > "$out/$name.log" 2>&1; echo $? > "$scratch/$name.code" ) &
    names+=("$name"); ends+=("$end")
}
for t in ilyich siberia atomgrad bonus; do run rally-$t END 7200 --track=$t --start --autodrive --no-intro; done
for t in ilyich siberia atomgrad bonus; do RECORD=1 run title-$t END 3600 --track=$t --no-intro; done
run laps-siberia END 3600 --track=siberia --start --training --autodrive --no-intro
run laps-bonus END 3600 --track=bonus --start --training --autodrive --no-intro
run tour-ilyich END 9000 --track=ilyich --tour-movie
run tour-siberia END 9000 --track=siberia --tour-movie
RECORD=1 run watch-siberia END 3600 --track=siberia --watch --no-intro
# The schools to their end (the mechanic, the fuel, the GAI...): quit when done, the step limit only a guard.
for s in ilyich:rally siberia:rally ilyich:driving; do
    run school-${s#*:}-${s%:*} "school finished after" 2000000 --track=${s%:*} --school=${s#*:} --school-auto --school-quit --no-intro
done
wait
bad=0
for i in "${!names[@]}"; do
    n=${names[$i]}; code=$(cat "$scratch/$n.code" 2>/dev/null || echo "?")
    steps=$(grep -vc "^END$" "$out/$n.txt" 2>/dev/null || echo 0)
    if [ "$code" != 0 ]; then why="exit code $code"
    elif [ ! -s "$out/$n.txt" ]; then why="no trace"
    elif [ "${ends[$i]}" = END ] && [ "$(tail -1 "$out/$n.txt")" != END ]; then why="stopped before its last step"
    elif [ "${ends[$i]}" != END ] && ! grep -q "${ends[$i]}" "$out/$n.log"; then why="never reached \"${ends[$i]}\""
    else echo "$n: $steps steps, ok"; continue; fi
    echo "$n: FAILED ($why, $steps steps; see $out/$n.log)"; bad=1
done
exit $bad
