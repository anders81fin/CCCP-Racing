#!/usr/bin/env python3
"""Validate and summarize a local LAN journal, without launching the game."""
import sys,struct,json,hashlib
from collections import Counter
from pathlib import Path
for arg in sys.argv[1:]:
    p=Path(arg)
    with p.open('rb') as f:
        magic,version,n=struct.unpack('<iii',f.read(12))
        assert magic==0x52504e4c and version==1 and 0<n<=65536
        raw=f.read(n);meta=json.loads(raw);digest=hashlib.sha256(raw)
        kinds=Counter();cars=set();last=0;own_magic=None
        while True:
            head=f.read(9);time,kind,length=struct.unpack('<fBi',head)
            if kind==0:
                assert time==float('inf') and length==sum(kinds.values())
                assert f.read(32)==digest.digest() and not f.read(1)
                break
            assert last<=time<float('inf') and 0<=length<=32*1024*1024 and kind in range(1,8)
            data=f.read(length);assert len(data)==length
            digest.update(head);digest.update(data);kinds[kind]+=1;last=time
            if kind==1:cars.add(struct.unpack('<q',data[:8])[0])
            if kind==6:own_magic=data[:4].decode('ascii')
        roster={x['Id'] for x in meta['Players']}
        assert cars<=roster
        print(json.dumps({'path':str(p),'bytes':p.stat().st_size,'track':meta['Track'],'players':len(roster),'all_car_ids':cars==roster,'duration':last,'packets':dict(kinds),'own_source':own_magic},ensure_ascii=False))
