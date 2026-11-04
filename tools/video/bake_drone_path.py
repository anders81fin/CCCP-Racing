#!/usr/bin/env python3
"""Bake the Kolkhoz drone prelude: waypoints JSON -> per-frame camera CSV, metrics and a map.

The eye flies a centripetal Catmull-Rom curve at a constant speed; the look-at target rides a second
curve through matching waypoints (same knots), both lightly Gaussian-smoothed. The game plays the CSV
back frame by frame (--drone-path=FILE): eyeX eyeZ eyeAGL tgtX tgtZ tgtAGL fov clock. Heights are metres
above the terrain (the game adds the ground). Prints speed / turn-rate metrics so the move is judged
by numbers, writes <out>.marks.json (when the drone passes each named waypoint) and <out>.map.png.

  bake_drone_path.py WAYPOINTS.json OUT.csv [--probe-log LOG] [--cap-yaw 14]
"""
import argparse
import json
import math
import re
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
FPS = 60


def knots(points):
    t = [0.0]
    for a, b in zip(points[:-1], points[1:]):
        t.append(t[-1] + max(np.linalg.norm(b - a), 1e-6) ** 0.5)
    return np.array(t)


def barry_goldman(p, t, tau):
    p0, p1, p2, p3 = p
    t0, t1, t2, t3 = t
    a1 = (t1 - tau) / (t1 - t0) * p0 + (tau - t0) / (t1 - t0) * p1
    a2 = (t2 - tau) / (t2 - t1) * p1 + (tau - t1) / (t2 - t1) * p2
    a3 = (t3 - tau) / (t3 - t2) * p2 + (tau - t2) / (t3 - t2) * p3
    b1 = (t2 - tau) / (t2 - t0) * a1 + (tau - t0) / (t2 - t0) * a2
    b2 = (t3 - tau) / (t3 - t1) * a2 + (tau - t1) / (t3 - t1) * a3
    return (t2 - tau) / (t2 - t1) * b1 + (tau - t1) / (t2 - t1) * b2


def extend(points):
    return np.vstack([2 * points[0] - points[1], points, 2 * points[-1] - points[-2]])


def spline(points, tk, samples=160):
    """Dense samples (tau, values) through points on the knot vector tk (both already extended)."""
    taus, values = [], []
    for i in range(1, len(points) - 2):
        for k in range(samples):
            tau = tk[i] + (tk[i + 1] - tk[i]) * k / samples
            taus.append(tau)
            values.append(barry_goldman(points[i - 1:i + 3], tk[i - 1:i + 3], tau))
    taus.append(tk[-2])
    values.append(points[-2])
    return np.array(taus), np.array(values)


def gauss(arr, sigma_s):
    if sigma_s <= 0:
        return arr
    n = int(sigma_s * FPS * 3)
    k = np.exp(-0.5 * (np.arange(-n, n + 1) / (sigma_s * FPS)) ** 2)
    k /= k.sum()
    out = np.empty_like(arr)
    for c in range(arr.shape[1]):
        padded = np.pad(arr[:, c], n, mode='reflect', reflect_type='odd')
        out[:, c] = np.convolve(padded, k, mode='valid')
    return out


def smoothstep(x):
    x = min(max(x, 0.0), 1.0)
    return x * x * (3 - 2 * x)


def clock_at(keys, t):
    if t <= keys[0][0]:
        return keys[0][1]
    for (t0, c0), (t1, c1) in zip(keys[:-1], keys[1:]):
        if t <= t1:
            return c0 + (c1 - c0) * (t - t0) / (t1 - t0)
    return keys[-1][1]


def bake(spec, cap_yaw):
    wps = spec['waypoints']
    eye = np.array([w['eye'] for w in wps], float)
    fov = np.array([[w.get('fov', 56)] for w in wps], float)
    names = [w.get('name') for w in wps]
    ee, fe = extend(eye), extend(fov)
    tk = knots(ee)
    tau, eyes = spline(ee, tk)
    seg0 = np.linalg.norm(np.diff(eyes, axis=0), axis=1)
    arc0 = np.concatenate([[0], np.cumsum(seg0)])

    def on_path(sv):
        sv = min(max(sv, 0.0), arc0[-1])
        pt = np.array([np.interp(sv, arc0, eyes[:, c]) for c in range(3)])
        a, b = max(sv - 2, 0.0), min(sv + 2, arc0[-1])
        pa = np.array([np.interp(a, arc0, eyes[:, c]) for c in range(3)])
        pb = np.array([np.interp(b, arc0, eyes[:, c]) for c in range(3)])
        d = pb - pa
        d[2] = 0
        d /= max(np.linalg.norm(d), 1e-6)
        return pt, d

    tgt = []
    for i, w in enumerate(wps):
        if 'target' in w:
            tgt.append(w['target'])
            continue
        look, agl, lateral = w['ahead']
        j = int(np.argmin(np.abs(tau - tk[i + 1])))
        sv = arc0[j] + look
        pt, d = on_path(sv)
        if sv > arc0[-1]:
            pt = pt + d * (sv - arc0[-1])
        right = np.array([-d[1], d[0], 0.0])
        tgt.append([pt[0] + right[0] * lateral, pt[1] + right[1] * lateral, agl])
    tgt = np.array(tgt, float)
    te = extend(tgt)
    _, tgts = spline(te, tk)
    _, fovs = spline(fe, tk)
    seg = np.linalg.norm(np.diff(eyes, axis=0), axis=1)
    arc = np.concatenate([[0], np.cumsum(seg)])
    speed = spec.get('speed', 12.0)
    # Resample at uniform arc steps, smooth in arc length (corners round off by the same radius at any
    # speed, and the speed stays constant), then fly the smoothed curve at the constant speed.
    ds = 0.25
    grid = np.arange(0, arc[-1], ds)
    lerp = lambda arr: np.stack([np.interp(grid, arc, arr[:, c]) for c in range(arr.shape[1])], axis=1)
    e0, g0, f0 = lerp(eyes), lerp(tgts), lerp(fovs)

    def smooth(arr, sigma_m):
        n = int(sigma_m / ds * 3)
        k = np.exp(-0.5 * (np.arange(-n, n + 1) / (sigma_m / ds)) ** 2)
        k /= k.sum()
        out = np.empty_like(arr)
        for c in range(arr.shape[1]):
            out[:, c] = np.convolve(np.pad(arr[:, c], n, mode='reflect', reflect_type='odd'), k, mode='valid')
        return out

    e0 = smooth(e0, spec.get('smooth_eye_m', 24.0))
    g0 = smooth(g0, spec.get('smooth_target_m', 24.0))
    f0 = smooth(f0, 20.0)
    arc2 = np.concatenate([[0], np.cumsum(np.linalg.norm(np.diff(e0, axis=0), axis=1))])
    total = arc2[-1] / speed
    frames = int(total * FPS) + 1
    s = np.minimum(np.arange(frames) / FPS * speed, arc2[-1])
    pick = lambda arr: np.stack([np.interp(s, arc2, arr[:, c]) for c in range(arr.shape[1])], axis=1)
    e, g, f = pick(e0), pick(g0), pick(f0)
    times = np.arange(frames) / FPS
    # when the drone passes each named waypoint (arc length at the knot)
    marks = {}
    for i, name in enumerate(names):
        if not name:
            continue
        k = i + 1  # extended index
        j = int(np.argmin(np.abs(tau - tk[k])))
        marks[name] = round(float(arc[j] / arc[-1] * total), 2)

    def when(x):
        if isinstance(x, str):
            name, _, off = x.partition('+')
            return marks[name] + (float(off) if off else 0.0)
        return float(x)

    keys = [(when(t), c) for t, c in spec['clock']]
    clocks = np.array([clock_at(keys, t) for t in times])
    return times, e, g, f, clocks, marks


def metrics(times, e, g, cap_yaw):
    v = np.linalg.norm(np.diff(e, axis=0), axis=1) * FPS
    a = np.diff(v) * FPS
    d = g - e
    yaw = np.unwrap(np.arctan2(d[:, 0], d[:, 1]))
    pitch = np.arctan2(d[:, 2], np.hypot(d[:, 0], d[:, 1]))
    yr = np.degrees(np.abs(np.diff(yaw))) * FPS
    pr = np.degrees(np.abs(np.diff(pitch))) * FPS
    print(f'duration {times[-1]:.1f} s, speed {v.min():.2f}-{v.max():.2f} m/s, accel max {np.abs(a).max():.2f} m/s2')
    print(f'yaw rate max {yr.max():.1f} deg/s (p95 {np.percentile(yr, 95):.1f}), pitch rate max {pr.max():.1f} (p95 {np.percentile(pr, 95):.1f})')
    ys = np.where(yr > cap_yaw)[0]
    if len(ys):
        runs, start = [], ys[0]
        for a0, b0 in zip(ys[:-1], ys[1:]):
            if b0 != a0 + 1:
                runs.append((start, a0))
                start = b0
        runs.append((start, ys[-1]))
        for r0, r1 in runs:
            print(f'  yaw rate over {cap_yaw}: t {r0/FPS:.1f}-{r1/FPS:.1f} s peak {yr[r0:r1+1].max():.1f}')
    return yr


def draw_map(spec, times, e, g, out, probe, track_name='ilyich'):
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    text = re.sub(r'^\s*//.*$', '', (ROOT / f'tracks/{track_name}.json').read_text(), flags=re.M)
    track = json.loads(re.sub(r',(\s*[}\]])', r'\1', text))
    fig, ax = plt.subplots(figsize=(12, 12))
    pts = np.array([[p['x'], p['z']] for p in track['points']] + [[track['points'][0]['x'], track['points'][0]['z']]])
    ax.plot(pts[:, 0], pts[:, 1], color='#8a6d3b', lw=5, alpha=.5, label='road')
    colors = {'statue': 'red', 'church': 'purple', 'silo': 'gray', 'bigbarn': 'brown', 'dormitory': 'brown', 'barn': 'brown',
              'izba': 'orange', 'radar': 'red', 'hangar': 'brown', 'barracks': 'brown', 'lorrywreck': 'black', 'balalaika': 'cyan'}
    for p in track['props']:
        c = colors.get(p['type'])
        if c:
            ax.plot(p['x'], p['z'], 's', color=c, ms=7)
            ax.annotate(p['type'], (p['x'], p['z']), fontsize=7)
    if track_name == 'ilyich':
        pole = [(133, 25), (110, 29), (87, 33), (65, 43), (48, 61), (33, 81), (26, 103), (26, 129), (26, 157), (26, 183), (25, 210), (26, 237)]
        ax.plot(*zip(*pole), '-o', color='gold', ms=3, lw=1, label='power line')
        ax.plot(220, 205, 'o', color='blue', ms=30, alpha=.2)
        ax.annotate('pond', (205, 205))
        ax.plot(220, 176, 'D', color='green')
        ax.annotate('dacha', (222, 172))
        ax.plot(81, 60, '*', color='black', ms=14)
        ax.annotate('START BARN', (70, 52))
    else:
        for name, (x0, z0, x1, z1) in (spec.get('map_boxes') or {}).items():
            ax.add_patch(plt.Rectangle((x0, z0), x1 - x0, z1 - z0, fill=False, ec='purple'))
            ax.annotate(name, (x0, z0), fontsize=8, color='purple')
        fr = track.get('water', {}).get('rivers', [{}])[0].get('points')
        if fr:
            ax.plot([q[0] for q in fr], [q[1] for q in fr], color='#88ccee', lw=6, alpha=.5)
        ax.plot(81, 287, '*', color='black', ms=14)
        ax.annotate('START', (70, 280))
    if probe:
        for name, col in (('tractor', 'brown'), ('slurry', 'olive'), ('boat', 'blue'), ('duck', 'cyan'), ('plough0', 'orange'), ('plough1', 'olive'), ('jet1', 'magenta'), ('jet2', 'red')):
            xy = probe.get(name)
            if xy is None or len(xy) == 0:
                continue
            ax.plot(xy[:, 1], xy[:, 2], '.', color=col, ms=3, alpha=.5)
            for t0, x, z in xy[::10]:
                ax.annotate(f'{int(t0)}', (x, z), fontsize=6, color=col)
    ax.plot(e[:, 0], e[:, 1], color='red', lw=1.5, label='eye')
    ax.plot(g[:, 0], g[:, 1], color='cyan', lw=.8, alpha=.7, label='target')
    for k in range(0, len(times), 5 * FPS):
        ax.plot([e[k, 0], g[k, 0]], [e[k, 1], g[k, 1]], color='gray', lw=.6)
        ax.annotate(f'{times[k]:.0f}s', (e[k, 0], e[k, 1]), fontsize=8, color='red')
    ax.set_xlim(0, 320)
    ax.set_ylim(320, 0)
    ax.set_aspect('equal')
    ax.legend(loc='lower right')
    fig.savefig(out, dpi=80, bbox_inches='tight')


def load_probe(path):
    out = {n: [] for n in ('tractor', 'slurry', 'boat', 'duck', 'plough0', 'plough1', 'jet1', 'jet2')}
    for line in Path(path).read_text().splitlines():
        p = line.split()
        if line.startswith('ACT '):
            t = float(p[1])
            for i, w in enumerate(p):
                if w in out:
                    out[w].append((t, float(p[i + 1]), float(p[i + 2])))
        elif line.startswith('SIB '):
            t = float(p[1])
            ip, ij = p.index('ploughs'), p.index('jets')
            pl = [float(x) for x in p[ip + 1:ij]]
            for k in range(0, len(pl) - 1, 2):
                out[f'plough{k // 2}'].append((t, pl[k], pl[k + 1]))
            # jets: id:x,y,z for every jet that exists; the two lowest ids are drawn as jet1 / jet2
            found = [tok.split(':') for tok in p[ij + 1:]]
            for n, (jid, xyz) in enumerate(found[-2:], start=1):
                x, y, z = (float(v) for v in xyz.split(','))
                out[f'jet{n}'].append((t, x, z))
    return {k: np.array(v) for k, v in out.items()}


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('waypoints')
    ap.add_argument('out')
    ap.add_argument('--probe-log')
    ap.add_argument('--track', default='ilyich')
    ap.add_argument('--cap-yaw', type=float, default=14.0)
    args = ap.parse_args()
    spec = json.loads(Path(args.waypoints).read_text())
    times, e, g, f, clocks, marks = bake(spec, args.cap_yaw)
    metrics(times, e, g, args.cap_yaw)
    out = Path(args.out)
    with out.open('w') as fh:
        fh.write('# eyeX eyeZ eyeAGL tgtX tgtZ tgtAGL fov clock (60 fps)\n')
        for k in range(len(times)):
            fh.write(f'{e[k,0]:.3f} {e[k,1]:.3f} {e[k,2]:.3f} {g[k,0]:.3f} {g[k,1]:.3f} {g[k,2]:.3f} {f[k,0]:.2f} {clocks[k]:.4f}\n')
    out.with_suffix('.marks.json').write_text(json.dumps({'duration': round(float(times[-1]), 2), 'marks': marks}, indent=1))
    draw_map(spec, times, e, g, out.with_suffix('.map.png'), load_probe(args.probe_log) if args.probe_log else None, args.track)
    print('marks:', marks)


if __name__ == '__main__':
    main()
