#!/usr/bin/env python3
"""Camera metrics from a --drone-log: speed, turn rate and the worst moments, per segment.
  drone_metrics.py LOG [--cap-yaw 16]"""
import argparse
import numpy as np

ap = argparse.ArgumentParser()
ap.add_argument('log')
ap.add_argument('--cap-yaw', type=float, default=16.0)
a = ap.parse_args()
lines = open(a.log).read().splitlines()
rows = [l.split() for l in lines if l.startswith('CAM ')]
d = np.array([[float(x) for x in r[1:]] for r in rows])
# Ignore the initial unposed camera before its first render update.
d = d[np.linalg.norm(d[:, 1:4], axis=1) > 1e-3]
lap_at = next((float(l.split("t=")[1]) for l in lines if l.startswith("DRONE MARK lap_start t=")), 86.6)
# the drive ticks at 120 Hz, the camera at 60: keep one row per camera update
keep = np.ones(len(d), bool)
keep[1:] = np.any(d[1:, 1:4] != d[:-1, 1:4], axis=1)
d = d[keep]
t, eye, tgt, fov, car, metre, kmh = d[:, 0], d[:, 1:4], d[:, 4:7], d[:, 7], d[:, 8:10], d[:, 10], d[:, 11]
dt = np.diff(t)
v = np.linalg.norm(np.diff(eye, axis=0), axis=1) / dt
look = tgt - eye
yaw = np.unwrap(np.arctan2(look[:, 0], look[:, 2]))
pitch = np.arctan2(look[:, 1], np.hypot(look[:, 0], look[:, 2]))
yr = np.degrees(np.abs(np.diff(yaw))) / dt
pr = np.degrees(np.abs(np.diff(pitch))) / dt
acc = np.abs(np.diff(v)) / dt[1:]
for name, lo, hi in (('prelude', 0, lap_at), ('hand-over', lap_at, lap_at + 5.4), ('lap', lap_at + 5.4, t[-1])):
    m = (t[:-1] >= lo) & (t[:-1] < hi)
    if m.any():
        print(f'{name:10s} {lo:6.1f}-{min(hi, t[-1]):6.1f} s  speed {v[m].min():5.1f}-{v[m].max():5.1f} m/s  yaw max {yr[m].max():5.1f} p95 {np.percentile(yr[m], 95):5.1f} deg/s  pitch max {pr[m].max():5.1f}')
bad = np.where(yr > a.cap_yaw)[0]
if len(bad):
    runs, s = [], bad[0]
    for x, y in zip(bad[:-1], bad[1:]):
        if y != x + 1:
            runs.append((s, x)); s = y
    runs.append((s, bad[-1]))
    for r0, r1 in runs:
        if r1 - r0 >= 20:
            print(f'  yaw > {a.cap_yaw}: t {t[r0]:.1f}-{t[r1]:.1f} s peak {yr[r0:r1+1].max():.1f} deg/s (metre {metre[r0]:.0f})')
slow = np.where(v < 5.0)[0]
print('frames slower than 5 m/s:', len(slow), (f'first at t {t[slow[0]]:.1f}' if len(slow) else ''))
