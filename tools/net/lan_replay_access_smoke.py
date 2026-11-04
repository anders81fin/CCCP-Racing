#!/usr/bin/env python3
"""Exercise the production independent viewer's live race/session access lease.
Does not create a LAN transport or alter the original journal. Outputs stay under media/.
"""
import argparse, datetime, hashlib, json, os, signal, subprocess, tempfile, time, uuid
from pathlib import Path

root=Path(__file__).resolve().parents[2]
a=argparse.ArgumentParser();a.add_argument('source',type=Path);a.add_argument('--out',type=Path)
a.add_argument('--only',choices=('watch-revoked','session-ended-export','export-revoked','loading-revoked'));args=a.parse_args()
source=args.source.resolve();before=hashlib.sha256(source.read_bytes()).hexdigest()
out=args.out or root/'media'/('lan-replay-access-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'))
out=out.resolve();out.mkdir(parents=True,exist_ok=False)
results=[]
for mode in ((args.only,) if args.only else ('watch-revoked','session-ended-export','export-revoked','loading-revoked')):
    folder=out/mode;folder.mkdir();token=uuid.uuid4().hex
    lease=Path(tempfile.gettempdir())/('cccp-lan-replay-'+token+'.access')
    def publish(permission):
        temp=Path(str(lease)+'.tmp');temp.write_text(json.dumps(dict(Version=1,Token=token,OwnerPid=os.getpid(),Round=1,Access=permission)));os.replace(temp,lease)
    publish(1)
    log=folder/'viewer.log';env={**os.environ,'XDG_DATA_HOME':str(folder/'profile')}
    cmd=['godot-mono','--path',str(root),'--audio-driver','Dummy','--max-fps','60','--',
         '--lan-replay='+str(source),'--lan-replay-access='+str(lease),
         '--lan-export-ui-run='+str(folder/'ui.png'),'--export-dir='+str(folder)]
    started=time.monotonic();stage='watch';rendered=False
    with log.open('w') as stream:
        p=subprocess.Popen(cmd,stdout=stream,stderr=subprocess.STDOUT,env=env,start_new_session=True)
        try:
            while p.poll() is None:
                text=log.read_text()
                if mode=='loading-revoked' and stage=='watch' and 'Godot Engine' in text:
                    publish(0);stage='revoked'
                if stage=='watch' and 'LANEXPORT UI PASS: export blocked until the LAN session ends' in text:
                    assert not list(folder.glob('*.mp4')) and not list((folder/'jobs').glob('*')),'blocked E created an export job'
                    publish(0 if mode=='watch-revoked' else 2);stage='revoked' if mode=='watch-revoked' else 'export'
                if mode=='export-revoked' and stage=='export':
                    for frames in (folder/'jobs').glob('*/frames.jsonl'):
                        if len(frames.read_text().splitlines())>=5:
                            rendered=True;publish(0);stage='revoked';break
                if time.monotonic()-started>240:raise TimeoutError(mode+' did not finish')
                time.sleep(.02)
            assert p.returncode==0,(mode,p.returncode,log.read_text()[-2000:])
        finally:
            if p.poll() is None:
                os.killpg(p.pid,signal.SIGTERM)
                try:p.wait(timeout=3)
                except subprocess.TimeoutExpired:os.killpg(p.pid,signal.SIGKILL);p.wait()
            lease.unlink(missing_ok=True);Path(str(lease)+'.tmp').unlink(missing_ok=True)
    text=log.read_text();assert not any(x in text for x in ('ERROR:','WARNING:','Exception:')),(mode,text[-2000:])
    if mode!='loading-revoked':assert 'export blocked until the LAN session ends' in text
    assert not list((folder/'jobs').glob('*')),'owned job remains'
    assert not list(folder.glob('*.part')) and not list(folder.glob('.*.part*')),'owned part remains'
    videos=list(folder.glob('*.mp4'))
    if mode=='session-ended-export':
        assert 'start, Done' in text and len(videos)==1
        probe=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_streams','-of','json',str(videos[0])]))
        video=next(s for s in probe['streams'] if s['codec_type']=='video');assert video['nb_frames']=='120' and video['duration']=='2.000000'
    else:
        assert stage=='revoked' and 'access ended; closing the viewer' in text and not videos
        if mode=='export-revoked':assert rendered,'revocation did not exercise an active renderer'
    assert hashlib.sha256(source.read_bytes()).hexdigest()==before
    results.append(dict(case=mode,exit=p.returncode,seconds=round(time.monotonic()-started,2),rendered_before_revoke=rendered,original_unchanged=True))
    print(json.dumps(results[-1]),flush=True)
(out/'results.json').write_text(json.dumps(results,indent=2));print('PASS: watching/export/session revocation and owned cleanup',flush=True)
