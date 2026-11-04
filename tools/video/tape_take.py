#!/usr/bin/env python3
"""Capture a first-record world tape in an isolated folder, then shoot clips from it."""

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[2]
TAKES = ROOT / "media" / "tape-takes-v1" / "takes"
SESSIONS = ROOT / "media" / "tape-takes-v1" / "sessions"
NAME = re.compile(r"[A-Za-z0-9][A-Za-z0-9_-]*\Z")
TRACKS = ("ilyich", "siberia", "atomgrad", "bonus")
FPS = 30
ILYICH_REFERENCE = TAKES / "ilyich-run01" / "data" / "ghost_ilyich.bin"
ILYICH_ROAD_REFERENCE = TAKES / "ilyich-run02" / "data" / "ghost_ilyich.bin"
ILYICH_PLAN_MARKS = (6.65, 13.35, 19.6, 28.75, 36.5, 44.0)
ILYICH_PLAN_SHOTS = ("farm", "east-bend", "ford", "gravel-bend", "church", "statue", "village")
ILYICH_PLAN_V2_SHOTS = ("farm", "silo-bonnet", "first-bend-overview", "far-bank-low",
                        "mud-bend-crane", "church", "bus-stop-overview")
ILYICH_PLAN_V3_SHOTS = ("farm", "silo-bonnet", "first-bend-overview", "far-bank-low",
                        "mud-bend-crane", "church-approach", "church-inner-hairpin",
                        "church-exit", "bus-stop-overview")
# Road positions picked from the owner's run02. Ordered windows prevent a later
# pass near the same place from being mistaken for a shot boundary.
ILYICH_PLAN_V2_CUES = ((153, 55, 3.5, 6.0), (250, 52, 7.0, 9.0),
                       (275, 216, 12.5, 14.3), (230, 263, 22.5, 24.5),
                       (104, 201, 30.8, 33.0), (37, 120, 45.0, 47.5))
ILYICH_PLAN_V3_CUES = (*ILYICH_PLAN_V2_CUES[:5],
                       (60, 207, 40.4, 41.5), (58, 178, 43.1, 44.0),
                       ILYICH_PLAN_V2_CUES[-1])


def run(args, log):
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open("x") as out:
        out.write("COMMAND: " + " ".join(args) + "\n\n")
        out.flush()
        p = subprocess.Popen(args, cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace")
        for line in p.stdout:
            print(line, end="", flush=True)
            out.write(line)
        return p.wait()


def pair(folder, track):
    data = folder / "data"
    return data / f"ghost_{track}.bin", data / f"tape_{track}.bin"


def duration(ghost):
    with ghost.open("rb") as f:
        magic, lap, count = struct.unpack("<IfI", f.read(12))
    if magic not in (0x33434343, 0x34434343) or count < 30 or count > 100000:
        raise ValueError(f"Invalid ghost: {ghost}")
    return count / 30, lap


def ghost_path(ghost):
    """Read the v3/v4 car path used to align planned cuts to a new lap."""
    data = ghost.read_bytes()
    magic, _, count = struct.unpack_from("<IfI", data)
    if magic not in (0x33434343, 0x34434343) or len(data) < 12 + 142 * count:
        raise ValueError(f"Cannot align camera plan to this ghost format: {ghost}")
    positions = []
    for i in range(count):
        x, _, z = struct.unpack_from("<fff", data, 12 + i * 142)
        positions.append((x, z))
    travelled = [0.0]
    for i in range(1, count):
        x0, z0 = positions[i - 1]
        x1, z1 = positions[i]
        travelled.append(travelled[-1] + ((x1 - x0) ** 2 + (z1 - z0) ** 2) ** 0.5)
    return positions, [d / max(1.0, travelled[-1]) for d in travelled]


def align_ilyich_plan(ghost):
    if not ILYICH_REFERENCE.is_file():
        raise ValueError(f"Camera plan reference take is missing: {ILYICH_REFERENCE}")
    reference, ref_progress = ghost_path(ILYICH_REFERENCE)
    current, progress = ghost_path(ghost)
    mapped = []
    previous = -1.0
    for old_time in ILYICH_PLAN_MARKS:
        index = min(len(reference) - 1, round(old_time * 30))
        x, z = reference[index]
        fraction = ref_progress[index]
        candidates = (j for j, p in enumerate(progress) if abs(p - fraction) < 0.08 and j / 30 > previous + 1)
        best = min(candidates, key=lambda j: ((current[j][0] - x) ** 2 + (current[j][1] - z) ** 2) ** 0.5
                   + 100 * abs(progress[j] - fraction), default=None)
        if best is None:
            raise ValueError(f"Could not align Ilyich camera cut at reference {old_time:.2f} s")
        error = ((current[best][0] - x) ** 2 + (current[best][1] - z) ** 2) ** 0.5
        if error > 35:
            raise ValueError(f"Camera cut at reference {old_time:.2f} s is {error:.1f} m off the new drive; review needed")
        new_time = best / 30
        mapped.append({"reference_seconds": old_time, "take_seconds": new_time, "position_error_m": error})
        previous = new_time
    # A different driving pace can squeeze the monument orbit into a flash.
    # Give that intentional shot room by moving only the cuts on its sides;
    # retain the exact position matches in the EDL for review.
    monument_span = mapped[5]["take_seconds"] - mapped[4]["take_seconds"]
    if monument_span < 3.5:
        spread = (3.5 - monument_span) / 2
        for index, shift in ((4, -spread), (5, spread)):
            mapped[index]["matched_take_seconds"] = mapped[index]["take_seconds"]
            mapped[index]["take_seconds"] += shift
        if mapped[4]["take_seconds"] <= mapped[3]["take_seconds"] + 2 \
                or mapped[5]["take_seconds"] >= len(current) / 30 - 2:
            raise ValueError("This lap leaves too little time for the monument and village camera shots")
    return mapped


def align_ilyich_road_cues(ghost, cues):
    if not ILYICH_ROAD_REFERENCE.is_file():
        raise ValueError(f"Camera plan reference take is missing: {ILYICH_ROAD_REFERENCE}")
    reference, reference_progress = ghost_path(ILYICH_ROAD_REFERENCE)
    positions, progress = ghost_path(ghost)
    mapped = []
    previous = -1.0
    for x, z, start, end in cues:
        reference_candidates = range(max(0, round(start * FPS)), min(len(reference), round(end * FPS)))
        reference_index = min(reference_candidates,
                              key=lambda i: (reference[i][0] - x) ** 2 + (reference[i][1] - z) ** 2)
        fraction = reference_progress[reference_index]
        candidates = (i for i, p in enumerate(progress)
                      if abs(p - fraction) < .08 and i / FPS > previous + 1)
        best = min(candidates,
                   key=lambda i: ((positions[i][0] - x) ** 2 + (positions[i][1] - z) ** 2) ** .5
                   + 100 * abs(progress[i] - fraction), default=None)
        if best is None:
            raise ValueError(f"Could not align Ilyich camera cue ({x}, {z}) to this drive")
        error = ((positions[best][0] - x) ** 2 + (positions[best][1] - z) ** 2) ** .5
        if error > 25:
            raise ValueError(f"Camera cue ({x}, {z}) is {error:.1f} m from this drive; review alignment")
        mapped.append({"cue_x": x, "cue_z": z, "reference_seconds": reference_index / FPS,
                       "take_seconds": best / FPS, "position_error_m": error})
        previous = best / FPS
    if len(cues) == len(ILYICH_PLAN_V3_CUES):
        # The church's stationary insert is a short shot even on a slower lap.
        span = mapped[6]["take_seconds"] - mapped[5]["take_seconds"]
        if span < 2 or span > 3:
            apex = min(range(round(mapped[5]["take_seconds"] * FPS), round(mapped[6]["take_seconds"] * FPS) + 1),
                       key=lambda i: (positions[i][0] - 78) ** 2 + (positions[i][1] - 192) ** 2) / FPS
            start = max(mapped[4]["take_seconds"] + 1, apex - 1.25)
            end = min(mapped[7]["take_seconds"] - 1, start + 2.5)
            if end - start < 2:
                raise ValueError("This lap leaves too little room for the fixed church hairpin shot")
            for item, adjusted in ((mapped[5], start), (mapped[6], end)):
                item["matched_take_seconds"] = item["take_seconds"]
                item["take_seconds"] = adjusted
    if any(right["take_seconds"] - left["take_seconds"] < 1 for left, right in zip(mapped, mapped[1:])):
        raise ValueError("Ilyich camera cuts are too close together")
    return mapped


def loaded(folder):
    info = json.loads((folder / "take.json").read_text())
    ghost, tape = pair(folder, info["track"])
    if not ghost.is_file() or not tape.is_file() or not ghost.stat().st_size or not tape.stat().st_size:
        raise ValueError(f"No complete ghost/world-tape pair in {folder}")
    return info, ghost, tape


def git_output(*args):
    p = subprocess.run(["git", *args], cwd=ROOT, capture_output=True)
    return p.stdout.decode(errors="replace") if p.returncode == 0 else "unavailable\n"


def source_extras():
    # git diff cannot preserve new, untracked replay/audio implementations.
    untracked = git_output("ls-files", "--others", "--exclude-standard", "--", "src").splitlines()
    return sorted(set(("src/Race/RaceScene.TapeCameraPlan.cs", "tools/video/tape_take.py", *untracked)))


def record(a):
    folder = TAKES / a.name
    folder.mkdir(parents=True, exist_ok=False)
    (folder / "data").mkdir()
    working_tree = git_output("status", "--short")
    (folder / "source.patch").write_text(git_output("diff", "HEAD", "--binary"))
    # The camera and session tool may still be untracked; git diff omits them.
    extras = folder / "source-extra"
    extras.mkdir()
    for relative in source_extras():
        source = ROOT / relative
        if source.is_file():
            target = extras / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
    (folder / "build.txt").write_text(
        f"date_utc: {datetime.now(timezone.utc).isoformat()}\n"
        f"commit: {git_output('rev-parse', 'HEAD').strip()}\n"
        f"dirty: {'yes' if working_tree.strip() else 'no'}\n"
        f"game_version: {re.search(r'^config/version=\"([^\"]+)\"', (ROOT / 'project.godot').read_text(), re.M).group(1)}\n"
        "working_tree:\n" + working_tree
    )
    (folder / "take.json").write_text(json.dumps({"name": a.name, "track": a.track, "created_utc": datetime.now(timezone.utc).isoformat()}, indent=2) + "\n")
    display = ["--headless", "--fixed-fps", "60"] if a.autodrive else ["--windowed", "--resolution", "1280x720", "--disable-vsync"]
    cmd = ["godot-mono", "--path", ".", *display, "--",
           f"--track={a.track}", "--start", "--no-intro", f"--data-dir={folder / 'data'}"]
    if a.autodrive:
        cmd.extend(("--autodrive", "--tape-quit-laps=1"))
    print(f"Driving take {a.name} on {a.track}. Complete one valid lap, then close the game.")
    result = run(cmd, folder / "record.log")
    try:
        _, ghost, tape = loaded(folder)
        seconds, lap = duration(ghost)
    except (ValueError, FileNotFoundError) as e:
        raise ValueError(f"Take {a.name} is incomplete: {e}. Its folder and log were kept.") from e
    print(f"Saved: {ghost}\n       {tape}\nLap {lap:.2f} s, replay {seconds:.2f} s. Game exit code: {result}")


def record_session(a):
    """Drive continuously; archive every complete lap, regardless of record time."""
    folder = SESSIONS / a.name
    folder.mkdir(parents=True, exist_ok=False)
    data, laps = folder / "data", folder / "laps"
    data.mkdir()
    laps.mkdir()
    working_tree = git_output("status", "--short")
    (folder / "source.patch").write_text(git_output("diff", "HEAD", "--binary"))
    extras = folder / "source-extra"
    extras.mkdir()
    for relative in source_extras():
        source = ROOT / relative
        if source.is_file():
            target = extras / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
    (folder / "build.txt").write_text(
        f"date_utc: {datetime.now(timezone.utc).isoformat()}\n"
        f"commit: {git_output('rev-parse', 'HEAD').strip()}\n"
        f"dirty: {'yes' if working_tree.strip() else 'no'}\n"
        f"game_version: {re.search(r'^config/version=\"([^\"]+)\"', (ROOT / 'project.godot').read_text(), re.M).group(1)}\n"
        "working_tree:\n" + working_tree
    )
    display = ["--headless", "--fixed-fps", "60"] if a.autodrive else ["--windowed", "--resolution", "1280x720", "--disable-vsync"]
    command = ["godot-mono", "--path", ".", *display, "--", f"--track={a.track}", "--start", "--no-intro",
               "--no-record", f"--data-dir={data}", f"--tape-session-laps={laps}"]
    if a.autodrive:
        command.append("--autodrive")
    if a.laps:
        command.append(f"--tape-quit-laps={a.laps}")
    print(f"Drive as many {a.track} laps as you like; close the game when done. Every completed lap is kept.")
    result = run(command, folder / "record.log")
    log = (folder / "record.log").read_text()
    report = []
    for index, lap_folder in enumerate(sorted(laps.glob("lap-*")), 1):
        expected = f"lap-{index:03d}"
        if lap_folder.name != expected:
            raise ValueError(f"Unexpected lap sequence in {laps}: {lap_folder.name}; files kept for recovery")
        ghost, tape = lap_folder / f"ghost_{a.track}.bin", lap_folder / f"tape_{a.track}.bin"
        signature, layout = lap_folder / f"track_{a.track}.sig", lap_folder / f"records_{a.track}.ver"
        if not ghost.is_file() or not tape.is_file() or not ghost.stat().st_size or not tape.stat().st_size \
                or not signature.is_file() or not layout.is_file() \
                or not re.search(rf"tape session: {re.escape(expected)} [^\n]*ghost\+tape saved", log):
            report.append({"lap": index, "status": "incomplete", "source": str(lap_folder.relative_to(ROOT))})
            continue
        _, seconds = duration(ghost)
        take_name = f"{a.name}-lap{index:02d}"
        take_folder = TAKES / take_name
        take_folder.mkdir(parents=True, exist_ok=False)
        (take_folder / "data").mkdir()
        shutil.copy2(ghost, take_folder / "data" / ghost.name)
        shutil.copy2(tape, take_folder / "data" / tape.name)
        shutil.copy2(signature, take_folder / "data" / signature.name)
        shutil.copy2(layout, take_folder / "data" / layout.name)
        lap_info_path = lap_folder / "session-lap.json"
        lap_info = json.loads(lap_info_path.read_text()) if lap_info_path.is_file() else {}
        shutil.copy2(folder / "build.txt", take_folder / "build.txt")
        shutil.copy2(folder / "source.patch", take_folder / "source.patch")
        (take_folder / "take.json").write_text(json.dumps({
            "name": take_name, "track": a.track, "session": a.name, "lap": index,
            "ranked": lap_info.get("ranked"),
            "created_utc": datetime.now(timezone.utc).isoformat()}, indent=2) + "\n")
        report.append({"lap": index, "seconds": round(seconds, 3), "status": "saved", "take": take_name,
                       "ranked": lap_info.get("ranked")})
        print(f"Lap {index}: {seconds:.2f} s -> {take_folder}")
    (folder / "session.json").write_text(json.dumps({"name": a.name, "track": a.track,
        "game_exit_code": result, "laps": report}, indent=2) + "\n")
    if not report or any(lap["status"] != "saved" for lap in report):
        raise ValueError(f"Session has no complete take for every finished lap; inspect {folder / 'session.json'} and record.log")
    print(f"Saved {len(report)} separate laps; compare them before choosing a camera take.")


def capture(a):
    folder = TAKES / a.name
    info, ghost, tape = loaded(folder)
    total, lap = duration(ghost)
    if a.start < 0 or a.seconds <= 0 or a.start + a.seconds > total - 0.1:
        raise ValueError(f"Clip must fit inside 0..{total:.2f} s (lap {lap:.2f} s)")
    clip = folder / "clips" / a.clip
    clip.mkdir(parents=True, exist_ok=False)
    raw = clip / "source.avi"
    command = ["godot-mono", "--path", ".", "--windowed", "--resolution", "1280x720", "--disable-vsync",
               "--fixed-fps", str(FPS), "--write-movie", str(raw), "--script", "res://tools/video/capture.gd", "--",
               f"--track={info['track']}", "--watch", "--no-intro", "--clean-hud", "--trailer-sfx", "--tape-shoot",
               f"--data-dir={folder / 'data'}", f"--tape-replay-start={a.start}", f"--tape-replay-quit={a.seconds}"]
    if a.camera == "overview":
        command.append("--tape-camera=overview")
    elif a.camera.startswith("siberia-"):
        if info['track'] != 'siberia':
            raise ValueError('Siberia detail cameras require a Siberia tape')
        command.append(f"--tape-camera={a.camera}")
    elif a.camera in ("ilyich-impact", "ilyich-ford", "ilyich-east", "ilyich-sweep"):
        if info['track'] != 'ilyich':
            raise ValueError('Ilyich detail cameras require an Ilyich tape')
        command.append(f"--tape-camera={a.camera}")
    elif a.camera in ("ilyich-plan", "ilyich-plan-v2", "ilyich-plan-v3"):
        command.extend((f"--tape-camera={a.camera}", "--tape-camera-marks=" + ",".join(f"{x['take_seconds']:.6f}" for x in a.mapping)))
    result = run(command, clip / "capture.log")
    log = (clip / "capture.log").read_text()
    if result or "from tape: covers" not in log or "tape shoot: replay begins at movie frame" not in log or not raw.is_file() \
            or (a.camera in ("ilyich-plan", "ilyich-plan-v2", "ilyich-plan-v3")
                and log.count("tape camera:") != (9 if a.camera == "ilyich-plan-v3" else 7)):
        raise ValueError(f"Tape capture failed. Original output and log are in {clip}")
    frame = int(re.search(r"tape shoot: replay begins at movie frame (\d+)", log).group(1))
    # Movie Maker writes the boot frames too. Keep that untouched source; trim its
    # title pre-roll into a separate viewing/clip file using the logged frame.
    # The first rendered frame precedes CarVis.Sync after EnterTitle, so discard it.
    offset = (frame + 1) / FPS
    mp4 = clip / "clip.mp4"
    ffmpeg = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-i", str(raw), "-ss", f"{offset:.6f}",
              "-t", str(a.seconds), "-c:v", "libx264", "-crf", "17", "-preset", "medium", "-pix_fmt", "yuv420p",
              "-c:a", "aac", "-b:a", "192k", str(mp4)]
    with (clip / "encode.log").open("x") as out:
        enc = subprocess.run(ffmpeg, cwd=ROOT, stdout=out, stderr=subprocess.STDOUT)
    (clip / "edit-decision.json").write_text(json.dumps({
        "source": str(raw.relative_to(ROOT)), "source_in_seconds": offset, "source_out_seconds": offset + a.seconds,
        "tape_in_seconds": a.start, "tape_out_seconds": a.start + a.seconds, "fps": FPS,
        "camera": a.camera,
        "camera_cut_alignment": getattr(a, "mapping", None),
        "camera_sections": [
            {"shot": shot, "tape_in_seconds": start, "tape_out_seconds": end,
             "source_in_seconds": offset + start - a.start, "source_out_seconds": offset + end - a.start}
            for shot, start, end in zip(ILYICH_PLAN_SHOTS if a.camera == "ilyich-plan" else
                                        ILYICH_PLAN_V3_SHOTS if a.camera == "ilyich-plan-v3" else ILYICH_PLAN_V2_SHOTS,
                                         [a.start] + [m["take_seconds"] for m in a.mapping],
                                         [m["take_seconds"] for m in a.mapping] + [a.start + a.seconds])
        ] if a.camera in ("ilyich-plan", "ilyich-plan-v2", "ilyich-plan-v3") else None,
        "command": command, "encode_command": ffmpeg,
    }, indent=2) + "\n")
    if enc.returncode:
        raise ValueError(f"FFmpeg failed; untouched source remains at {raw}")
    print(f"Clip: {mp4}\nUntouched full source: {raw}")


def main():
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest="action", required=True)
    r = sub.add_parser("record", help="Drive a first record lap into a new named folder")
    r.add_argument("name", help="new take name, never reused")
    r.add_argument("track", choices=TRACKS)
    r.add_argument("--autodrive", action="store_true", help="quick smoke test; drive manually for trailer action")
    session = sub.add_parser("record-session", help="Drive many laps continuously and keep every completed lap")
    session.add_argument("name", help="new session name, never reused")
    session.add_argument("track", choices=TRACKS)
    session.add_argument("--laps", type=int, default=0, help="quit automatically after this many laps; default: close the game yourself")
    session.add_argument("--autodrive", action="store_true", help="smoke test only")
    c = sub.add_parser("capture", help="Shoot a new clip from a saved tape")
    c.add_argument("name")
    c.add_argument("clip", help="new clip name, never reused")
    c.add_argument("start", type=float, help="start on the lap's tape clock, in seconds")
    c.add_argument("seconds", type=float, help="clip length, in seconds")
    c.add_argument("--camera", choices=("director", "overview", "ilyich-impact", "ilyich-ford", "ilyich-east", "ilyich-sweep", "siberia-ridge", "siberia-runway", "siberia-ice", "siberia-descent", "siberia-bonnet", "siberia-outer-crane", "siberia-front-follow", "siberia-inner-bank", "siberia-hairpin-overhead", "siberia-airbase-wide", "siberia-jet-hero"), default="director", help="replay director, overview, or a continuous track detail camera")
    planned = sub.add_parser("capture-plan", help="Shoot the full Ilyich lap with the seven planned cameras")
    planned.add_argument("name")
    planned.add_argument("clip", nargs="?", default="ilyich-camera-plan-v1", help="new capture name, never reused")
    revised = sub.add_parser("capture-plan-v2", help="Shoot the revised Ilyich camera cuts from a saved lap")
    revised.add_argument("name")
    revised.add_argument("clip", nargs="?", default="ilyich-camera-plan-v2", help="new capture name, never reused")
    hairpin = sub.add_parser("capture-plan-v3", help="Add the fixed inside-church hairpin shot")
    hairpin.add_argument("name")
    hairpin.add_argument("clip", nargs="?", default="ilyich-camera-plan-v4", help="new capture name, never reused")
    a = p.parse_args()
    for value in (a.name, getattr(a, "clip", "valid")):
        if not NAME.fullmatch(value):
            p.error("Take and clip names must contain only letters, digits, _ or -")
    try:
        if a.action == "record":
            record(a)
        elif a.action == "record-session":
            if a.laps < 0 or a.autodrive and a.laps == 0:
                raise ValueError("Use --laps with a positive count for autodrive, or omit it for manual driving")
            record_session(a)
        elif a.action in ("capture-plan", "capture-plan-v2", "capture-plan-v3"):
            folder = TAKES / a.name
            info, ghost, _ = loaded(folder)
            if info["track"] != "ilyich":
                raise ValueError("The Ilyich camera plan needs an Ilyich tape")
            a.mapping = (align_ilyich_road_cues(ghost, ILYICH_PLAN_V3_CUES) if a.action == "capture-plan-v3"
                         else align_ilyich_road_cues(ghost, ILYICH_PLAN_V2_CUES) if a.action == "capture-plan-v2"
                         else align_ilyich_plan(ghost))
            a.start = 0.0
            a.seconds = round(duration(ghost)[0] - 0.15, 3)
            a.camera = ("ilyich-plan-v3" if a.action == "capture-plan-v3" else
                        "ilyich-plan-v2" if a.action == "capture-plan-v2" else "ilyich-plan")
            print("Camera cuts on this lap: " + ", ".join(f"{m['take_seconds']:.2f}s" for m in a.mapping))
            capture(a)
        else:
            capture(a)
    except (OSError, ValueError) as e:
        p.exit(1, f"Error: {e}\n")


if __name__ == "__main__":
    main()
