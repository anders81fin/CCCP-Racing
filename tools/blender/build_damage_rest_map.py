#!/usr/bin/env python3
"""Map one known CarDamage rest geometry to a repaired model; original tapes stay untouched.
Inputs are --damage-rest-reference dumps from the production Godot mesh builder.
"""
import argparse
import collections
import json
import itertools
from pathlib import Path
import numpy as np

p = argparse.ArgumentParser()
p.add_argument('old', type=Path)
p.add_argument('new', type=Path)
p.add_argument('output', type=Path)
p.add_argument('--report', type=Path, required=True)
a = p.parse_args()
old, new = (json.loads(x.read_text()) for x in (a.old, a.new))
indices, report, source_surface = [], [], []
# A new surface takes its offsets from the old surface of the same material and the same ordinal among the surfaces of that material (-1: it has none, the old model
# did not have it: the rear wings, split out as parts, and the wing heights, which are new geometry).
seen = collections.Counter()
for number, t in enumerate(new['Surfaces']):
    nth = seen[t['Material']]; seen[t['Material']] += 1
    same = [i for i, x in enumerate(old['Surfaces']) if x['Material'] == t['Material']]
    if nth >= len(same):
        source_surface.append(-1)
        indices.append([-1] * len(t['Positions']))
        report.append(dict(surface=number, material=t['Material'], old=0, new=len(t['Positions']), exact=0, wing_nearest=0, lining_nearest=0, new_zero=len(t['Positions'])))
        continue
    source_surface.append(same[nth])
    s = old['Surfaces'][same[nth]]
    op, on = np.asarray(s['Positions']), np.asarray(s['Normals'])
    np_, nn = np.asarray(t['Positions']), np.asarray(t['Normals'])
    if op.shape == np_.shape and np.allclose(op, np_, atol=2e-5, rtol=0) and np.allclose(on, nn, atol=.002, rtol=0):
        row = list(range(len(op)))
        exact, nearest = len(row), 0
    else:
        def cell(pos):
            return tuple(np.floor(pos * 1000).astype(np.int64))
        buckets = collections.defaultdict(list)
        for i, pos in enumerate(op):
            buckets[cell(pos)].append(i)
        used = set()
        row, exact, nearest = [], 0, 0
        for pos, normal in zip(np_, nn):
            base = cell(pos)
            candidates = []
            for delta in itertools.product((-1, 0, 1), repeat=3):
                candidates.extend(buckets[tuple(base[j] + delta[j] for j in range(3))])
            match = next((i for i in sorted(candidates) if i not in used
                          and np.max(abs(op[i] - pos)) < 2e-5
                          and np.max(abs(on[i] - normal)) < .002), None)
            if match is not None:
                row.append(match); used.add(match); exact += 1
            elif s['Material'] == 'Wing_White':
                distances = np.sum((op - pos) ** 2, axis=1)
                distances[(on @ normal) < .8] = np.inf
                i = int(np.argmin(distances))
                if distances[i] < .20 ** 2:
                    row.append(i); nearest += 1
                else:
                    row.append(-1)
            else:
                row.append(-1)
    lining = 0
    if s['Material'] == 'Black_Plastic' and len(op) != len(np_):
        for j, source in enumerate(row):
            if source != -1:
                continue
            distances = np.sum((op - np_[j]) ** 2, axis=1)
            distances[(on @ nn[j]) < .999] = np.inf
            i = int(np.argmin(distances))
            if distances[i] < .10 ** 2:
                row[j] = i; lining += 1
    indices.append(row)
    report.append(dict(surface=number, material=s['Material'], old=len(op), new=len(np_),
                       exact=exact, wing_nearest=nearest, lining_nearest=lining, new_zero=row.count(-1)))
out = dict(SourceSignature=old['Signature'], TargetSignature=new['Signature'],
           SourceCounts=[len(s['Positions']) for s in old['Surfaces']], TargetIndices=indices, SourceSurface=source_surface)
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(out, separators=(',', ':')) + '\n')
a.report.write_text(json.dumps(dict(source=old['Signature'], target=new['Signature'], surfaces=report), indent=2)+'\n')
print(json.dumps(dict(source=old['Signature'], target=new['Signature'], mapped=sum(len(r) for r in indices),
                      exact=sum(r['exact'] for r in report), wing_nearest=sum(r['wing_nearest'] for r in report),
                      new_zero=sum(r['new_zero'] for r in report)), indent=2))
