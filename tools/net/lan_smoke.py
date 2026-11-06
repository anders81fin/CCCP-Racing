#!/usr/bin/env python3
"""Local multi-process LAN check. Options through LAN_RUN_* environment variables.
Defaults: four headless players, 75 seconds, 5% unreliable packet loss, 25 ms each way.
Uses isolated /tmp game data. Run only one copy (UDP 24105); requires dotnet build first.
"""
import os,subprocess,time,re,json,datetime
from pathlib import Path
root=str(Path(__file__).resolve().parents[2])
procs=[]
overflow=os.environ.get('LAN_RUN_OVERFLOW')=='1'
count=int(os.environ.get('LAN_RUN_PLAYERS','4'))
track=os.environ.get('LAN_RUN_TRACK','bonus')
seconds=int(os.environ.get('LAN_RUN_SECONDS','75'))
gui=os.environ.get('LAN_RUN_GUI','0')=='1'
gui_all=os.environ.get('LAN_RUN_GUI_ALL','0')=='1'
report_dir=Path(os.environ['LAN_RUN_REPORT_DIR']) if os.environ.get('LAN_RUN_REPORT_DIR') else None
if report_dir:report_dir.mkdir(parents=True,exist_ok=True)
shots=os.environ.get('LAN_RUN_SHOTS',str(Path(root)/'media'/('lan-review-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))))
if not 2 <= count <= 6: raise SystemExit('LAN_RUN_PLAYERS must be 2–6')
try:
 for i in range(count+(1 if overflow else 0)):
  if i:
   ready_until=time.time()+120
   while time.time()<ready_until and procs[0].poll() is None and 'hosting on UDP' not in Path('/tmp/cccp-lan-host.log').read_text():time.sleep(.1)
   if procs[0].poll() is not None or 'hosting on UDP' not in Path('/tmp/cccp-lan-host.log').read_text():raise RuntimeError('host did not open the lobby')
  if overflow and i==count:
   full_until=time.time()+180
   while time.time()<full_until and procs[0].poll() is None and str(count)+' in the lobby' not in Path('/tmp/cccp-lan-host.log').read_text():time.sleep(.1)
   if str(count)+' in the lobby' not in Path('/tmp/cccp-lan-host.log').read_text():raise RuntimeError('accepted roster never filled before overflow probe')
  name='host' if i==0 else 'client'+str(i)
  log=open('/tmp/cccp-lan-'+name+'.log','w')
  args=['godot-mono','--path',root,'--max-fps','60','--audio-driver','Dummy','--log-file',str((report_dir/(name+'-engine.log')).resolve()) if report_dir else '/tmp/cccp-lan-'+name+'-engine.log']
  args+=['--windowed','--resolution','1280x800'] if gui and (i==1 or gui_all) else ['--headless']
  start_track=track if i==0 else os.environ.get('LAN_RUN_CLIENT_TRACK',track)
  args+=['--','--track='+start_track,'--autodrive','--no-intro','--no-record','--comments=0','--data-dir=/tmp/cccp-lan-'+name+'-data','--lan-name='+name,'--lan-vote='+os.environ.get('LAN_RUN_VOTE','3'),'--lan-laps=1','--lan-quit-after=3']
  args+=['--lan-test-tracks='+os.environ.get('LAN_RUN_TRACK_OPTIONS',track)]
  if os.environ.get('LAN_RUN_TRACK_SEQUENCE'):args+=['--lan-track-sequence='+os.environ['LAN_RUN_TRACK_SEQUENCE']]
  else:args+=['--lan-track-vote='+os.environ.get('LAN_RUN_VOTE_TRACK',track)]
  if os.environ.get('LAN_RUN_ROUNDS'):args+=['--lan-rounds='+os.environ['LAN_RUN_ROUNDS']]
  if i and os.environ.get('LAN_RUN_CLIENT_LOAD_DELAY'):args+=['--lan-load-delay='+os.environ['LAN_RUN_CLIENT_LOAD_DELAY']]
  args+=['--lan-damage='+os.environ.get('LAN_RUN_DAMAGE','1'),'--lan-fuel='+os.environ.get('LAN_RUN_FUEL','0')]
  args+=['--lan-obstacles='+os.environ.get('LAN_RUN_OBSTACLES','1')]
  if os.environ.get('LAN_RUN_OBSTACLE_SEQUENCE'):args+=['--lan-obstacles-sequence='+os.environ['LAN_RUN_OBSTACLE_SEQUENCE']]
  if os.environ.get('LAN_RUN_QUIT_AT'):args+=['--lan-quit-at='+os.environ['LAN_RUN_QUIT_AT']]
  if os.environ.get('LAN_RUN_START_HOLD')=='1':args+=['--clutch-kick=0.3']
  if os.environ.get('LAN_RUN_CONTACT_PROBE')=='1':args+=['--lan-contact-probe']
  if os.environ.get('LAN_RUN_LEAVE_PROBE')=='1':args+=['--lan-leave-probe']
  keep_probe=os.environ.get('LAN_RUN_KEEP_PROBE') if i==0 else os.environ.get('LAN_RUN_CLIENT_KEEP_PROBE',os.environ.get('LAN_RUN_KEEP_PROBE'))
  if keep_probe:args+=['--lan-keep-probe='+keep_probe]
  if i==0 and os.environ.get('LAN_RUN_REPLAY_PROBE')=='1':args+=['--lan-replay-auto-check']
  if i==0 and os.environ.get('LAN_RUN_REPLAY_EXPORT_PROBE')=='1':args+=['--lan-replay-export-check']
  if os.environ.get('LAN_RUN_DATA_ROOT'):args+=['--data-dir='+str(Path(os.environ['LAN_RUN_DATA_ROOT'])/name)]
  if os.environ.get('LAN_RUN_REPLAY_DISABLED')=='1':args+=['--lan-replay-disable']
  # RECORD LAN RACES? per machine: yes (default, the earlier always-recording runs), no, or unset (no flag: an unanswered machine records nothing)
  record=os.environ.get('LAN_RUN_RECORD','yes') if i==0 else os.environ.get('LAN_RUN_CLIENT_RECORD',os.environ.get('LAN_RUN_RECORD','yes'))
  if record in ('yes','no'):args+=['--lan-record='+record]
  if os.environ.get('LAN_RUN_CAPTURE_AT'):args+=['--lan-replay-capture-at='+os.environ['LAN_RUN_CAPTURE_AT']]
  # SHARE BOARDS per machine (comma list by index: yes, no or -): RaceScene.LanBoards.cs; the host's automatic ballot waits for N sharing comrades
  board_share=os.environ.get('LAN_RUN_BOARD_SHARE','').split(',')
  if i<len(board_share) and board_share[i] in ('yes','no'):args+=['--lan-board-share='+board_share[i]]
  if i<len(board_share) and board_share[i]=='ui':args+=['--lan-board-ui-probe']      # says YES through the real lobby keys, pad and mouse
  if os.environ.get('LAN_RUN_BOARD_SHARE_DELAY'):args+=['--lan-board-share-delay='+os.environ['LAN_RUN_BOARD_SHARE_DELAY'].split(',')[min(i,len(os.environ['LAN_RUN_BOARD_SHARE_DELAY'].split(','))-1)]]
  if i==0 and os.environ.get('LAN_RUN_BOARD_EXPECT'):args+=['--lan-board-expect='+os.environ['LAN_RUN_BOARD_EXPECT']]
  board_withdraw=os.environ.get('LAN_RUN_BOARD_WITHDRAW_AFTER','').split(',')
  if i<len(board_withdraw) and board_withdraw[i].isdigit():args+=['--lan-board-withdraw-after='+board_withdraw[i]]
  if i==0 and os.environ.get('LAN_RUN_HOST_AP_DELAY'):args+=['--lan-ap-delay='+os.environ['LAN_RUN_HOST_AP_DELAY']]
  if i and os.environ.get('LAN_RUN_CLIENT_AP_DELAY'):args+=['--lan-ap-delay='+os.environ['LAN_RUN_CLIENT_AP_DELAY']]
  args+=['--lan-host','--lan-autostart='+str(count)] if i==0 else ['--lan-join=127.0.0.1']
  if gui and (i==1 or gui_all):args+=['--lan-shots='+shots,'--lan-shot-at=30:race','--lan-shot-at=50:later']
  env={**os.environ,'LANDBG':'1','LAN_TEST_DROP':os.environ.get('LAN_RUN_DROP','0.05'),'LAN_TEST_DELAY_MS':os.environ.get('LAN_RUN_DELAY_MS','25')}
  procs.append(subprocess.Popen(args,cwd=root,stdout=log,stderr=subprocess.STDOUT,env=env))
  time.sleep(1)
 deadline=time.time()+seconds
 while time.time()<deadline and any(p.poll() is None for p in procs):time.sleep(1)
 print([(p.pid,p.poll()) for p in procs],flush=True)
finally:
 for p in procs:
  if p.poll() is None:p.terminate()
 for p in procs:
  try:p.wait(timeout=5)
  except subprocess.TimeoutExpired:p.kill()
errors=[]
results=[]
for i in range(count+(1 if overflow else 0)):
 name='host' if i==0 else 'client'+str(i)
 content=Path('/tmp/cccp-lan-'+name+'.log').read_text()
 if os.environ.get('LAN_RUN_REPORT_DIR'):
  report_dir=Path(os.environ['LAN_RUN_REPORT_DIR']);report_dir.mkdir(parents=True,exist_ok=True)
  (report_dir/(name+'.log')).write_text(content)
 if overflow and i==count:
  if 'GAME FULL' not in content:errors.append(name+': overflow player was not explicitly rejected')
  if re.search(r'lan\[[^]]+\] [0-9.]+ GO at host time',content):errors.append(name+': overflow player entered the race')
  print(name+': overflow rejection '+('PASS' if 'GAME FULL' in content else 'FAIL'),flush=True)
  continue
 if not re.search(r'lan\[[^]]+\] [0-9.]+ GO at host time',content):errors.append(name+': no GO')
 if 'LANKEEPTEST: FAIL' in content:errors.append(name+': KEEP probe failed')
 if os.environ.get('LAN_RUN_KEEP_PROBE') and content.count('LANKEEPTEST: PASS')<int(os.environ.get('LAN_RUN_ROUNDS','1')):errors.append(name+': KEEP probe did not pass every round')
 if 'ERROR:' in content or 'Exception' in content or 'above the MTU' in content:errors.append(name+': runtime error')
 if os.environ.get('LAN_RUN_START_HOLD')=='1':
  holds=re.findall(r'grid hold(?: \d+)?: clutch (\w+), drift ([0-9.]+) m',content)
  if not holds or any(clutch!='True' or float(drift)>.15 for clutch,drift in holds):errors.append(name+': countdown hill hold drifted')
 matches=[s.split('results: ',1)[1] for s in content.splitlines() if 'results: ' in s]
 if matches:results.append(matches[-1])
 elif os.environ.get('LAN_RUN_REQUIRE_FINISH')=='1':errors.append(name+': no final results')
 if os.environ.get('LAN_RUN_ROUNDS') and len(re.findall(r'lan\[[^]]+\] [0-9.]+ GO at host time',content))<int(os.environ['LAN_RUN_ROUNDS']):errors.append(name+': missing completed rounds')
 if os.environ.get('LAN_RUN_REQUIRE_SCORES')=='1':
  if 'session winner:' not in content:errors.append(name+': no session champion declaration')
  boards=[s.split('session standings: ',1)[1] for s in content.splitlines() if 'session standings: ' in s]
  if len(boards)<int(os.environ.get('LAN_RUN_ROUNDS','1')):errors.append(name+': missing session standings')
  elif sum(int(n) for n in re.findall(r' (\d+)(?:,|$)',boards[-1]))!=int(os.environ.get('LAN_RUN_ROUNDS','1')):errors.append(name+': session wins do not match finished races')
 stats=[s for s in content.splitlines() if 'authority:' in s]
 if not stats:errors.append(name+': no authority telemetry')
 if stats and not any(', '+str(count-1)+' puppets,' in row for row in stats):errors.append(name+': missing accepted player replicas')
 if os.environ.get('LAN_RUN_REQUIRE_CONTACTS')=='1':
  pattern=r'authority: inputs \d+, contacts (\d+)' if i==0 else r'predicted (\d+)'
  if not any(int(n)>0 for n in re.findall(pattern,content)):errors.append(name+': no '+('host' if i==0 else 'predicted')+' car contact')
 if i and not any(int(n)>0 for n in re.findall(r'world (\d+) meshes',content)):errors.append(name+': no world replicas')
 if os.environ.get('LAN_RUN_LEAVE_PROBE')=='1':
  for phase in ['lobby','ballot','reveal','grid','race','results']+(['session-results','session-final']+(['session-decision'] if i==0 else []) if os.environ.get('LAN_RUN_REQUIRE_SCORES')=='1' else [])+(['loading'] if os.environ.get('LAN_RUN_CLIENT_LOAD_DELAY') else []):
   if 'LANLEAVETEST: PASS '+phase+' ' not in content:errors.append(name+': no leave confirmation check at '+phase)
 print(name+': '+(stats[-1] if stats else content[-1000:]),flush=True)
if len(set(results))>1:errors.append('final results differ between processes')
print(json.dumps({'errors':errors,'results':results[:1]}),flush=True)
raise SystemExit(bool(errors))
