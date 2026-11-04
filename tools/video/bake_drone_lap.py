#!/usr/bin/env python3
"""Bake the chase drone's keys for the Kolkhoz film's lap (--drone-lap=FILE) from a probe run's log.

The camera's compass azimuth is the road heading smoothed over a long stretch (so it turns slowly
through the hairpins) plus a hand-keyed side/angle; distance, height and field of view are keyed by road
metre. Rows: metre azimuth dist height fov subjX subjZ blend. Usage: bake_drone_lap.py PROBE.log|TRACK_DATA.json OUT.txt [LAP_START] [kolkhoz|siberia|atomgrad|bonus]
"""
import math
import sys
from pathlib import Path

import numpy as np

# (metre, side angle deg (+ = camera swings to the car's left), dist, height, fov, subject, blend)
KEYS = [
    (0,     0, 12, 17, 56, None, 0.0),    # the drone is ~25 m back on the lane as the car starts: the car pulls away ahead of it
    (40,   14, 20, 15, 56, None, 0.0),
    (90,   26, 30, 11, 56, None, 0.0),
    (150,  35, 32, 15, 56, None, 0.0),    # first bend, the radar post
    (230,  10, 30, 14, 56, None, 0.0),    # the ridge along its crest (from the side the crest blocks the view)
    (330,  25, 32, 14, 54, None, 0.0),    # the jump
    (390, -45, 30, 13, 56, None, 0.0),    # the ford
    (470,  70, 38, 20, 58, None, 0.0),    # the mud hairpin from above
    (580,  35, 32, 12, 56, None, 0.0),    # the field bends
    (700,  35, 32, 12, 56, None, 0.0),
    (780, 110, 40, 24, 58, (57, 192), 0.30),   # the church hairpin, high and wide
    (850, 105, 36, 20, 58, (57, 192), 0.20),
    (900, -35, 30, 12, 56, None, 0.0),    # the climb
    (960,  30, 26, 14, 56, (81, 60), 0.25),    # the barn
    (1015, 25, 24, 16, 56, (81, 60), 0.25),
]
MAX_SLOPE = float(__import__('os').environ.get('DRONE_SLOPE', '0.5'))   # deg per metre the camera may swing round the car
HEIGHT_SLOPE = float(__import__('os').environ.get('DRONE_HSLOPE', '0.08'))   # metres of height per metre of road
DIST_SLOPE = float(__import__('os').environ.get('DRONE_DSLOPE', '0.12'))
SIBERIA_KEYS = [
    (0,     0, 12, 17, 56, None, 0.0),    # the drone arrives from the north: the car pulls away below it
    (40,   14, 20, 15, 56, None, 0.0),
    (100,  28, 30, 12, 56, None, 0.0),    # the start straight and its ice
    (200,  40, 34, 15, 56, None, 0.0),    # the fast left
    (320,  60, 36, 19, 58, None, 0.0),    # the switchbacks from above
    (450,  70, 36, 21, 58, (270, 156), 0.25),   # the radar
    (560,  40, 34, 16, 56, None, 0.0),
    (650,  70, 34, 13, 56, None, 0.0),    # the runway: the MiK passes
    (790,  30, 32, 15, 56, None, 0.0),
    (950, -30, 32, 14, 56, None, 0.0),    # the ice leg
    (1110, 70, 38, 22, 58, (195, 246), 0.30),   # the hairpin and the timber wreck
    (1207, 50, 34, 21, 58, (121, 216), 0.25),   # the camp
    (1300, 25, 30, 15, 56, None, 0.0),
    (1381, 20, 26, 14, 56, None, 0.0),
]
ATOMGRAD_KEYS = [
    (0,     0, 12, 17, 56, None, 0.0),    # the drone arrives from the north: the car pulls away below it
    (40,   14, 20, 15, 56, None, 0.0),
    (100,  28, 30, 13, 56, None, 0.0),    # the south straight, the level crossing
    (180,  38, 34, 15, 56, None, 0.0),    # the red forest (on the left)
    (280,  45, 34, 14, 56, None, 0.0),    # the graveyard beyond the car
    (350,  45, 36, 18, 58, (233, 118), 0.25),   # the plant and the sarcophagus
    (445,  30, 32, 14, 56, None, 0.0),    # the channel straight
    (515,  30, 32, 14, 56, None, 0.0),
    (560,  15, 26, 22, 56, None, 0.0),    # the mixers: up over the town's roofs (the blocks stand either side of its streets)
    (620,   5, 16, 30, 56, None, 0.0),
    (700,   5, 14, 31, 58, None, 0.0),    # the town and the ferris wheel, looking down into the streets
    (790,   5, 14, 30, 58, None, 0.0),
    (850,  15, 22, 22, 56, None, 0.0),    # the decontamination wash
    (910,  25, 28, 14, 56, None, 0.0),
    (948,  20, 26, 14, 56, None, 0.0),
]
BONUS_KEYS = [
    (0, 0, 12, 17, 56, None, 0.0),
    (60, 15, 25, 18, 56, None, 0.0),
    (150, 55, 34, 24, 58, (80, 255), 0.20),
    (240, 70, 34, 24, 58, (80, 215), 0.20),
    (330, 35, 29, 20, 56, None, 0.0),
    (430, 20, 25, 24, 56, None, 0.0),
    (500, 35, 30, 25, 58, (144, 210), 0.15),
    (600, 50, 34, 21, 58, None, 0.0),
    (730, 30, 30, 20, 56, None, 0.0),
    (845, 55, 34, 23, 58, (187, 106), 0.20),
    (970, 35, 30, 20, 56, (86, 105), 0.20),
    (1080, 15, 25, 18, 56, None, 0.0),
]
SIGMA_M = float(__import__("os").environ.get("DRONE_SIGMA", "150"))   # heading smoothing along the road


def main(probe, out, lap_at=100.0, film='kolkhoz'):
    rows = []
    for line in ([] if Path(probe).suffix == '.json' else Path(probe).read_text().splitlines()):
        p = line.split()
        if p[0] == 'CAM' and float(p[1]) > lap_at + 0.5:
            rows.append((int(p[11]), float(p[9]), float(p[10])))
    if Path(probe).suffix == '.json':
        import json
        rows = [(m, p[0], p[1]) for m, p in enumerate(json.loads(Path(probe).read_text())['centre'])]
    by_metre = {}
    for m, x, z in rows:
        by_metre.setdefault(m, (x, z))
    ms = sorted(by_metre)
    lap = max(ms)
    pts = np.array([by_metre[m] for m in ms])
    mm = np.array(ms, float)
    grid = np.arange(0, lap + 1, 1.0)
    gx, gz = np.interp(grid, mm, pts[:, 0]), np.interp(grid, mm, pts[:, 1])
    # heading as a unit vector smoothed along the road (averaging angles, no wrap trouble)
    dx, dz = np.gradient(gx), np.gradient(gz)
    n = np.maximum(np.hypot(dx, dz), 1e-6)
    dx, dz = dx / n, dz / n
    k = int(SIGMA_M * 3)
    w = np.exp(-0.5 * (np.arange(-k, k + 1) / SIGMA_M) ** 2)
    w /= w.sum()
    sm = lambda a: np.convolve(np.pad(a, k, mode='edge'), w, mode='valid')
    hx, hz = sm(dx), sm(dz)
    heading = np.degrees(np.arctan2(hz, hx))                # compass: 0 east, 90 south (z down)
    KEYS_ = {'siberia': SIBERIA_KEYS, 'atomgrad': ATOMGRAD_KEYS, 'bonus': BONUS_KEYS}.get(film, KEYS)
    km = np.array([key[0] for key in KEYS_], float)

    def interp(col):
        return np.interp(grid, km, [key[col] for key in KEYS_])

    side, dist, height, fov = interp(1), interp(2), interp(3), interp(4)

    def slew(a, limit):
        # smooth along the road until the value changes by at most `limit` per metre (a gimbal drone climbs gently)
        sg = 4.0
        while True:
            kk = int(sg * 3)
            ww = np.exp(-0.5 * (np.arange(-kk, kk + 1) / sg) ** 2)
            ww /= ww.sum()
            out = np.convolve(np.pad(a, kk, mode='reflect', reflect_type='odd'), ww, mode='valid')
            if np.abs(np.gradient(out)).max() <= limit or sg > 300:
                return out
            sg *= 1.15

    height = slew(height, HEIGHT_SLOPE)
    dist = slew(dist, DIST_SLOPE)
    blend = interp(6)
    sx = np.interp(grid, km, [key[5][0] if key[5] else 0 for key in KEYS_])
    sz = np.interp(grid, km, [key[5][1] if key[5] else 0 for key in KEYS_])
    # camera azimuth: behind the car (heading + 180), swung by 'side' towards the car's left (compass 0 east, 90 south)
    az = np.degrees(np.unwrap(np.radians(heading + 180.0 + side)))
    # Slew limit by smoothing along the road until the camera swings at most MAX_SLOPE deg per metre.
    sigma = 4.0
    while True:
        k2 = int(sigma * 3)
        w2 = np.exp(-0.5 * (np.arange(-k2, k2 + 1) / sigma) ** 2)
        w2 /= w2.sum()
        smoothed = np.convolve(np.pad(az, k2, mode='reflect', reflect_type='odd'), w2, mode='valid')
        if np.abs(np.gradient(smoothed)).max() <= MAX_SLOPE or sigma > 300:
            break
        sigma *= 1.15
    print(f'azimuth smoothing sigma {sigma:.0f} m, max slope {np.abs(np.gradient(smoothed)).max():.2f} deg/m')
    az = smoothed % 360.0
    with Path(out).open('w') as f:
        f.write('# metre azimuth dist height fov subjX subjZ blend  (from %s, lap %d m)\n' % (probe, lap))
        for m in range(0, int(lap) + 1, 20):
            f.write(f'{m} {az[m]:.1f} {dist[m]:.1f} {height[m]:.1f} {fov[m]:.1f} {sx[m]:.1f} {sz[m]:.1f} {blend[m]:.2f}\n')
    print('keys written, lap', lap, 'm; heading range', heading.min(), heading.max())


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 100.0, sys.argv[4] if len(sys.argv) > 4 else 'kolkhoz')
