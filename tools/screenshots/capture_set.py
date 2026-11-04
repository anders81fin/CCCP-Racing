#!/usr/bin/env python3
"""Capture candidate stills serially, preserving original rendered PNG frames."""
import argparse
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "media/marketing-stills-2026-10-15"
TAKES = {
    "ilyich-player-northwest-powerslide": ["--track=ilyich", "--start", "--autodrive", "--at=900", "--launch=80", "--ap-speed=1.4", "--clean-hud",
                                             "--time-of-day=0.26", "--key-at=5.9:Shift:0.25",
                                             "--still-frames=360,375,390,405,420,435,450,465,480"],
    "ilyich-player-rollers": ["--track=ilyich", "--start", "--autodrive", "--at=225", "--launch=90", "--clean-hud",
                                "--time-of-day=0.23", "--still-frames=210,225,240,255,270,285,300,315,330,345,360,375,390"],
    "ilyich-player-jump-refined": ["--track=ilyich", "--start", "--autodrive", "--at=310", "--launch=75", "--clean-hud",
                                     "--time-of-day=0.23", "--still-frames=225,240,255,270,285,300,315,330,345,360,375,390"],
    "ilyich-player-ford-splash": ["--track=ilyich", "--start", "--autodrive", "--at=355", "--launch=65", "--clean-hud",
                                   "--time-of-day=0.20", "--still-frames=210,220,230,240,250,260,270,280,290,300,310,320"],
    "ilyich-player-jump": ["--track=ilyich", "--start", "--autodrive", "--at=300", "--launch=95", "--ap-speed=1.2", "--clean-hud",
                             "--time-of-day=0.23", "--still-frames=240,255,270,285,300,315,330,345,360,375,390,405,420,450,480"],
    "ilyich-player-ford": ["--track=ilyich", "--start", "--autodrive", "--at=365", "--launch=65", "--clean-hud",
                             "--time-of-day=0.20", "--still-frames=240,255,270,285,300,315,330,345,360,375,390,405,420,450,480"],
    "ilyich-player-northwest": ["--track=ilyich", "--start", "--autodrive", "--at=900", "--launch=80", "--ap-speed=1.4", "--clean-hud",
                                  "--time-of-day=0.26", "--still-frames=240,270,300,330,360,390,420,450,480,510,540,570,600"],
    "ilyich-player-handbrake": ["--track=ilyich", "--trailer-shots=hairpin",
                                  "--still-frames=120,150,180,210,240,270,300,330,360,390,420,450,480,510,540,570,600"],
    "ilyich-church": ["--track=ilyich", "--trailer-shots=hairpin", "--marketing-shot=church"],
    "atomgrad-mixers": ["--track=atomgrad", "--start", "--autodrive", "--at=585", "--phase=sunset", "--clean-hud"],
    "ilyich-barn": ["--track=ilyich", "--trailer-shots=hairpin", "--marketing-shot=barn"],
    "ilyich-village": ["--track=ilyich", "--trailer-shots=hairpin", "--marketing-shot=village"],
    "siberia-ice": ["--track=siberia", "--trailer-shots=snow_exit", "--marketing-shot=ice"],
    "siberia-plough": ["--track=siberia", "--trailer-shots=snow_exit", "--marketing-shot=plough"],
    "atomgrad-plant": ["--track=atomgrad", "--start", "--autodrive", "--at=360", "--phase=sunset", "--clean-hud"],
    "atomgrad-crossing": ["--track=atomgrad", "--start", "--autodrive", "--at=25", "--phase=dawn", "--clean-hud"],
    "atomgrad-forest-night": ["--track=atomgrad", "--start", "--autodrive", "--at=135", "--time-of-day=0.76", "--clean-hud",
                               "--still-eye=-18,13,21", "--still-aim=5,1,-7"],
    "atomgrad-forest-night-high": ["--track=atomgrad", "--start", "--autodrive", "--at=135", "--time-of-day=0.76", "--clean-hud"],
    "atomgrad-plant-dusk": ["--track=atomgrad", "--start", "--autodrive", "--at=340", "--time-of-day=0.375", "--clean-hud",
                            "--still-eye=20,15,27", "--still-aim=-7,3,-10"],
    "ilyich-church-golden": ["--track=ilyich", "--start", "--autodrive", "--at=778", "--time-of-day=0.275", "--clean-hud",
                             "--still-eye=17,17,25", "--still-aim=-5,1,-5"],
    "siberia-mountains": ["--track=siberia", "--start", "--autodrive", "--at=1040", "--time-of-day=0.28", "--clean-hud",
                           "--still-eye=19,15,25", "--still-aim=-8,2,-10"],
    "atomgrad-forest-curve": ["--track=atomgrad", "--start", "--autodrive", "--at=195", "--time-of-day=0.791667", "--clean-hud",
                              "--still-eye=12,24,22", "--still-aim=0,0,-4"],
    "atomgrad-forest-blue-hour": ["--track=atomgrad", "--start", "--autodrive", "--at=195", "--time-of-day=0.405", "--clean-hud",
                                  "--still-eye=12,24,22", "--still-aim=0,0,-4"],
    "ilyich-church-drift": ["--track=ilyich", "--start", "--autodrive", "--at=778", "--launch=65", "--ap-speed=1.2",
                            "--time-of-day=0.275", "--clean-hud", "--still-eye=17,17,25", "--still-aim=-5,1,-5",
                            "--still-frames=240,270,300,330,360,390,420,450,480"],
}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("takes", nargs="*", choices=list(TAKES))
    args = parser.parse_args()
    (OUTPUT / "candidates").mkdir(parents=True, exist_ok=True)
    for name in args.takes or TAKES:
        command = ["godot-mono", "--path", str(ROOT), "--windowed", "--resolution", "1280x720",
                   "--disable-vsync", "--fixed-fps", "60", "--audio-driver", "Dummy",
                   "--log-file", f"/tmp/cccp-stills-{name}-engine.log",
                   "--script", "res://tools/screenshots/capture.gd", "--",
                   *TAKES[name], "--no-intro", "--no-record", "--comments=0",
                   "--data-dir=/tmp/cccp-stills-20261015", "--rng-seed=1", "--babushka-seed=1",
                   f"--still-label={name}"]
        print(f"CAPTURING {name}", flush=True)
        with (OUTPUT / f"{name}.log").open("w") as log:
            subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        print(f"FINISHED {name}", flush=True)

if __name__ == "__main__":
    main()
