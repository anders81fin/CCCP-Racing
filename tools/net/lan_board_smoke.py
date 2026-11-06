#!/usr/bin/env python3
"""SHARE BOARDS real-peer check (docs/lan-leaderboard-sync.md): seeds three isolated data roots with different boards and real
leaderboard recordings, runs three headless game processes through tools/net/lan_smoke.py (lobby sync, then one normal LAN race),
verifies every root, then runs the same roots again (a reconnect + resync must add nothing).

Usage: lan_board_smoke.py OUT_DIR SHARE [RERUN_SHARE]
  SHARE: per machine yes/no, e.g. no,yes,yes (host first). Uses UDP 24105: run one copy at a time. Requires dotnet build first.
Everything (seed originals, data roots, logs, verify JSON) stays under OUT_DIR; the player's own records are never read.
"""
import json, os, re, shutil, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
out = Path(sys.argv[1]).resolve(); share = sys.argv[2]; rerun = sys.argv[3] if len(sys.argv) > 3 else share
names = ['host', 'client1', 'client2']
data = out / 'data'
if data.exists(): raise SystemExit('refusing to reuse ' + str(data))
out.mkdir(parents=True, exist_ok=True)

def godot(args, log):
    cmd = ['godot-mono', '--headless', '--path', str(ROOT), '--log-file', str(out / (log + '.engine.log')), 'res://scenes/tests/PhysicsTest.tscn', '--'] + args
    r = subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True, timeout=900)
    (out / (log + '.log')).write_text(r.stdout + r.stderr)
    if r.returncode != 0: raise SystemExit(log + ' failed, see ' + str(out / (log + '.log')))
    return r.stdout

def verify(tag):
    rows = {}
    for line in godot(['--lan-board-verify=' + str(data)], 'verify-' + tag).splitlines():
        if line.startswith('LANBOARDVERIFY '):
            v = json.loads(line[len('LANBOARDVERIFY '):]); rows[v['root']] = v
    (out / ('verify-' + tag + '.json')).write_text(json.dumps(rows, indent=1))
    return rows

def race(tag, consent):
    on = consent.split(',')
    env = {**os.environ, 'LAN_RUN_PLAYERS': '3', 'LAN_RUN_TRACK': 'ilyich', 'LAN_RUN_TRACK_OPTIONS': 'ilyich', 'LAN_RUN_DATA_ROOT': str(data),
           'LAN_RUN_REPORT_DIR': str(out / tag), 'LAN_RUN_BOARD_SHARE': consent, 'LAN_RUN_BOARD_EXPECT': str(on.count('yes') + on.count('ui')),
           'LAN_RUN_RECORD': 'no', 'LAN_RUN_SECONDS': os.environ.get('LAN_BOARD_SECONDS', '420'), 'LAN_RUN_REQUIRE_FINISH': '1', 'LAN_RUN_SHOTS': str(out / (tag + '-shots'))}
    r = subprocess.run([sys.executable, str(ROOT / 'tools/net/lan_smoke.py')], cwd=ROOT, env=env, capture_output=True, text=True, timeout=1200)
    (out / (tag + '-smoke.txt')).write_text(r.stdout + r.stderr)
    logs = {n: (out / tag / (n + '.log')).read_text() for n in names}
    return r.returncode, logs

errors = []
def check(ok, what):
    print(('PASS ' if ok else 'FAIL ') + what, flush=True)
    if not ok: errors.append(what)

godot(['--lan-board-seed=' + str(data)], 'seed')
shutil.copytree(data, out / 'seed-originals')
pre = verify('pre')

def phase_checks(tag, logs, consent):
    on = dict(zip(names, ['yes' if v == 'ui' else v for v in consent.split(',')]))
    for n, text in logs.items():
        if consent.split(',')[names.index(n)] == 'ui': check('LANBOARDUITEST: PASS' in text and 'LANBOARDUITEST: FAIL' not in text, f'{tag} {n}: said YES through the lobby keys, pad and mouse (UI probe)')
        stats = re.findall(r'chunks sent (\d+), relayed (\d+), in (\d+), outside the lobby (\d+)', text)
        check(bool(stats) and all(s[3] == '0' for s in stats), f'{tag} {n}: zero board chunks arrived outside the lobby ({stats[-1] if stats else "no stats"})')
        tail = text.split('stats on leaving the lobby', 1)[1] if 'stats on leaving the lobby' in text else ''
        check(not re.search(r'lan-boards\[[^]]+\]: (asking|serving|sent|received|imported)', tail), f'{tag} {n}: no board transfer after the lobby was left')
        check(bool(re.search(r'GO at host time', text)), f'{tag} {n}: the LAN race started after the sync')
        check('results: ' in text, f'{tag} {n}: the LAN race finished with results')
        if on[n] == 'yes': check('lan-boards[' in text and 'consent YES' in text, f'{tag} {n}: said YES')
    host = logs['host']
    if tag == 'run1' and consent.count('yes') + consent.count('ui') >= 2:      # (a reconnect of synced boards has nothing to transfer, so nothing to wait for)
        if 'the ballot waits for board sync' in host: check(True, f'{tag}: the host asked for the ballot during the sync and the production barrier refused it')
        else: print(f'INFO {tag}: the sync finished before the automatic ballot asked (barrier not exercised in this run)', flush=True)

code, logs = race('run1', share)
check(code == 0, f'run1: lan_smoke exit {code}')
phase_checks('run1', logs, share)
post = verify('post1')
on = dict(zip(names, ['yes' if v == 'ui' else v for v in share.split(',')]))
sharing = [n for n in names if on[n] == 'yes']
for n in names:
    a, b = pre[n], post[n]
    check(b['staging'] == 0 and b['linked'] == b['open'] == b['prepared'], f'run1 {n}: every linked recording opens and passes the viewer preparation ({b["prepared"]}/{b["linked"]}), staging empty')
    if on[n] != 'yes':
        check(a['records'] == b['records'] and a['rows'] == b['rows'] and b['backups'] == 0, f'run1 {n} (NO): record files byte-identical, no backup made')
    else:
        others = [m for m in sharing if m != n]
        want = set()
        for m in others:
            for r in pre[m]['rows']:
                track, s, name, t = r.split('/')[:4]
                if track == 'atomgrad': continue      # client1's Atomgrad is on an old signature: never merged
                want.add((track, s, name, t))
        have = {tuple(r.split('/')[:4]) for r in b['rows']}
        missing = [w for w in want if w not in have and w[0] == 'ilyich']
        check(not missing, f'run1 {n}: every comrade Kolkhoz row arrived ({len(want)} offered, missing {missing})')
        check(not any(r.startswith('atomgrad/') and 'C1 ATOM OLD' in r for r in b['rows']) or n == 'client1', f'run1 {n}: the old-signature Atomgrad board was not merged')
        dup = [r for r in b['rows'] if b['rows'].count(r) > 1]
        check(not dup, f'run1 {n}: no duplicate rows ({dup[:3]})')
        check(b['ghost'] == a['ghost'], f'run1 {n}: the personal-best ghost slot is untouched')
        check(b['backups'] > 0, f'run1 {n}: changed boards were backed up first ({b["backups"]} files)')
if len(sharing) >= 2:
    a, b = post[sharing[0]], post[sharing[1]]
    ilyich = lambda v: sorted(r.rsplit('/', 1)[0] for r in v['rows'] if r.startswith('ilyich/'))
    check(ilyich(a) == ilyich(b), f'run1: the sharing machines\' Kolkhoz boards converged ({sharing})')

# reconnect: the same roots, a fresh session, the same answers; nothing may change
code2, logs2 = race('run2', rerun)
check(code2 == 0, f'run2: lan_smoke exit {code2}')
phase_checks('run2', logs2, rerun)
post2 = verify('post2')
for n in names:
    check(post2[n]['records'] == post[n]['records'] and post2[n]['rows'] == post[n]['rows'], f'run2 {n}: reconnect + sync again changed no record file (no duplicates, no new recordings)')
(out / 'summary.json').write_text(json.dumps({'share': share, 'rerun': rerun, 'errors': errors}, indent=1))
print(json.dumps({'errors': errors}), flush=True)
raise SystemExit(bool(errors))
