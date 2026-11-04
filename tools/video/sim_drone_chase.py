#!/usr/bin/env python3
"""Replay the chase drone's maths on a logged lap to judge its turn rate without recapturing.
Mirrors DroneKey / DroneDamp in src/Race/RaceScene.DroneTour.cs (terrain ignored: yaw only).
  sim_drone_chase.py LOG LAPKEYS [--lap-at 86.64] [--eye 1.0] [--target 0.55] [--cap 16]"""
import argparse
import numpy as np

ap = argparse.ArgumentParser()
ap.add_argument('log')
ap.add_argument('keys')
ap.add_argument('--lap-at', type=float, default=86.64)
ap.add_argument('--eye', type=float, default=1.0)
ap.add_argument('--target', type=float, default=0.55)
ap.add_argument('--cap', type=float, default=16.0)
ap.add_argument('--lead', type=float, default=0.0, help='feed-forward: seconds of car velocity added to the filter input (cancels the lag)')
a = ap.parse_args()

rows = [l.split() for l in open(a.log) if l.startswith('CAM ')]
d = np.array([[float(x) for x in r[1:]] for r in rows])
keep = np.ones(len(d), bool)
keep[1:] = np.any(d[1:, 1:4] != d[:-1, 1:4], axis=1)
d = d[keep]
d = d[d[:, 0] >= a.lap_at]
K = np.array([[float(x) for x in l.split()] for l in open(a.keys) if l.strip() and l[0] != '#'])
LAP = 1015


def key(m):
    if m <= K[0, 0]:
        return K[0]
    if m >= K[-1, 0]:
        return K[-1]
    b = int(np.searchsorted(K[:, 0], m, side='right')) - 1
    b = min(b, len(K) - 2)
    p0, p1, p2, p3 = K[max(b - 1, 0)], K[b], K[b + 1], K[min(b + 2, len(K) - 1)]
    u = (m - p1[0]) / (p2[0] - p1[0])
    row = np.zeros_like(p1)
    for c in range(1, len(row)):
        a0, a1, a2, a3 = p0[c], p1[c], p2[c], p3[c]
        if c == 1:
            wrap = lambda x, ref: ref + ((x - ref + 180) % 360) - 180
            a0, a2 = wrap(a0, a1), wrap(a2, a1)
            a3 = wrap(a3, a2)
        row[c] = 0.5 * (2 * a1 + (-a0 + a2) * u + (2 * a0 - 5 * a1 + 4 * a2 - a3) * u * u + (-a0 + 3 * a1 - 3 * a2 + a3) * u ** 3)
    row[0] = m
    return row


def damp(cur, want, vel, st, dt):
    om = 2 / st
    x = om * dt
    ex = 1 / (1 + x + 0.48 * x * x + 0.235 * x ** 3)
    ch = cur - want
    tmp = (vel + om * ch) * dt
    return want + (ch + tmp) * ex, (vel - om * tmp) * ex


half = False
prev = None
vel = np.zeros(3)
eye = tgt = None
ev = tv = np.zeros(3)
out = []
dt = 1 / 60
for t, *_r in d[:, :1].tolist() if False else [(row[0],) for row in d]:
    pass
for row in d:
    t, cx, cz, m = row[0], row[8], row[9], row[10]
    if 0.4 * LAP < m < 0.6 * LAP:
        half = True
    if not half and m > 0.8 * LAP:
        m = 0
    k = key(m)
    car = np.array([cx, 1.0, cz])
    if prev is not None:
        vel = vel * 0.8 + 0.2 * (car - prev) / dt
    prev = car
    sub = np.array([k[5], 2.0, k[6]]) if k[7] > 0 else car
    wt = car + (sub - car) * k[7]
    lead_e, lead_t = vel * a.lead * a.eye, vel * a.lead * a.target
    az = np.radians(k[1])
    we = wt + np.array([np.cos(az) * k[2], k[3], np.sin(az) * k[2]])
    if eye is None:
        eye, tgt = we.copy(), wt.copy()
    eye, ev = damp(eye, we + lead_e, ev, a.eye, dt)
    tgt, tv = damp(tgt, wt + lead_t, tv, a.target, dt)
    out.append((t, m, eye.copy(), tgt.copy(), row[11]))
t = np.array([o[0] for o in out])
E = np.array([o[2] for o in out])
T = np.array([o[3] for o in out])
L = T - E
yaw = np.unwrap(np.arctan2(L[:, 0], L[:, 2]))
yr = np.degrees(np.abs(np.diff(yaw))) / np.diff(t)
v = np.linalg.norm(np.diff(E, axis=0), axis=1) / np.diff(t)
print(f'frames {len(t)}; yaw max {yr.max():.1f} p95 {np.percentile(yr, 95):.1f} deg/s; eye speed {v.min():.1f}-{v.max():.1f} m/s')
bad = np.where(yr > a.cap)[0]
if len(bad):
    runs, s = [], bad[0]
    for x, y in zip(bad[:-1], bad[1:]):
        if y != x + 1:
            runs.append((s, x)); s = y
    runs.append((s, bad[-1]))
    for r0, r1 in runs:
        if r1 - r0 >= 8:
            print(f'  yaw>{a.cap}: t {t[r0]:.1f}-{t[r1]:.1f} peak {yr[r0:r1+1].max():.1f} at metre {out[r0][1]:.0f} ({out[r0][4]:.0f} km/h)')
