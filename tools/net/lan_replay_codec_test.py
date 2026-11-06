#!/usr/bin/env python3
"""Run standalone journal integrity/failure tests. No Godot or live player data."""
from pathlib import Path
import subprocess
root=Path(__file__).resolve().parents[2]
out=root/'build/lan-replay/codec-tests'
out.mkdir(parents=True,exist_ok=True)
(out/'test.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><Compile Include="../../../src/Net/LanReplayFile.cs" Link="LanReplayFile.cs" /></ItemGroup></Project>')
program=r"""using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CCCPRacing.Net;
class Test {
 static int count;
 static void Check(bool ok,string name) {if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);count++;}
 static bool Reject(string path) {try{using var r=new LanReplayFile.Reader(path);return false;}catch{return true;}}
 static async Task Main() {
 var root=Path.Combine(Path.GetTempPath(),"cccp-lan-replay-"+Guid.NewGuid());Directory.CreateDirectory(root);
 try {
 var h=new LanReplayFile.Header("bonus","signature","0.79",11,15,1,Enumerable.Range(1,6).Select(i=>new LanReplayFile.Player(i,"PLAYER"+i,i-1)).ToArray());
 foreach(int fuel in new[]{50,100,200,201}) {
  string fuelPath=Path.Combine(root,"fuel-"+fuel);
  var fw=new LanReplayFile.Writer(fuelPath,h with {Protocol=12,Fuel=fuel});
  fw.Add(0,LanReplayFile.Kind.Go,Array.Empty<byte>());
  Check(await fw.Finish()==fuelPath,"fuel recording published "+fuel);
  using var fr=new LanReplayFile.Reader(fuelPath);
  Check(fr.Metadata.Protocol==12 && fr.Metadata.Fuel==fuel,"fuel survives save/load "+fuel);
 }
 bool badFuel=false;try{LanReplayFile.Validate(h with {Protocol=12,Fuel=175});}catch(InvalidDataException){badFuel=true;}
 Check(badFuel,"unknown recorded fuel rejected");
 string p=Path.Combine(root,"six.lanreplay");var w=new LanReplayFile.Writer(p,h);byte[] bytes={1,2,3};
 Check(w.Add(0,LanReplayFile.Kind.Mesh,bytes),"mesh queued");bytes[0]=99;
 for(int i=0;i<6;i++)Check(w.Add(i,LanReplayFile.Kind.Car,new byte[]{(byte)i}),"car "+(i+1));
 Check(w.Add(2,LanReplayFile.Kind.World,new byte[]{4}),"late arrival accepted");
 Check(await w.Finish()==p,"atomic publication");Check(!w.Add(7,LanReplayFile.Kind.Car,bytes),"closed writer rejects");
 using(var r=new LanReplayFile.Reader(p)) {Check(r.Metadata.Players.Length==6,"six-player roster");Check(r.Entries.Count==8,"all packets");Check(r.Read(r.Entries[0])[0]==1,"immutable copy");Check(r.Duration==5,"monotonic arrival clock");Check(r.Read(r.Entries[2])[0]==1,"indexed random access");}
 byte[] good=File.ReadAllBytes(p);for(int n=0;n<good.Length;n+=Math.Max(1,good.Length/15)){string bad=Path.Combine(root,"truncated");File.WriteAllBytes(bad,good[..n]);Check(Reject(bad),"truncation "+n);}
 byte[] altered=(byte[])good.Clone();altered[^40]^=1;string corrupt=Path.Combine(root,"bad");File.WriteAllBytes(corrupt,altered);Check(Reject(corrupt),"tamper rejected");
 byte[] version=(byte[])good.Clone();version[4]=2;File.WriteAllBytes(corrupt,version);Check(Reject(corrupt),"future version rejected");
 string overflow=Path.Combine(root,"overflow");var limited=new LanReplayFile.Writer(overflow,h,1);Check(!limited.Add(0,LanReplayFile.Kind.World,new byte[5]),"queue budget enforced");Check(await limited.Finish()==null && !File.Exists(overflow),"overflow not published");
 string invalid=Path.Combine(root,"invalid");var invalidWriter=new LanReplayFile.Writer(invalid,h);Check(!invalidWriter.Add(float.NaN,LanReplayFile.Kind.World,bytes),"nonfinite time rejected");Check(await invalidWriter.Finish()==null,"invalid capture not published");
 Directory.CreateDirectory(Path.Combine(root,"missing","x.partial"));var disk=new LanReplayFile.Writer(Path.Combine(root,"missing","x"),h);Check(await disk.Finish()==null,"disk error reported");
 Check(disk.Error!=null,"disk error reason");
 var ownPath=Path.Combine(root,"own");var ownWriter=new LanReplayFile.Writer(ownPath,h);ownWriter.Add(0,LanReplayFile.Kind.Go,Array.Empty<byte>());ownWriter.Add(1,LanReplayFile.Kind.Car,new byte[]{1});
 Check(await ownWriter.Finish(()=>new byte[]{9,8})==ownPath,"worker final source published");
 using(var own=new LanReplayFile.Reader(ownPath)){Check(own.Entries[0].Type==LanReplayFile.Kind.Go && own.Entries[0].Length==0,"GO marker preserved");Check(own.Entries[^1].Type==LanReplayFile.Kind.OwnCar,"own source indexed");Check(own.Read(own.Entries[^1]).SequenceEqual(new byte[]{9,8}),"own source unchanged");}
 var failedSource=new LanReplayFile.Writer(Path.Combine(root,"failed-source"),h);
 Check(await failedSource.Finish(()=>throw new IOException("source failed"))==null,"source failure not published");
 var aborted=new LanReplayFile.Writer(Path.Combine(root,"abort"),h);aborted.Abort("cancelled");Check(await aborted.Finish()==null,"abort not published");
Console.WriteLine("PASS total "+count);
 }finally{Directory.Delete(root,true);}
 }
}
"""
(out/'Program.cs').write_text(program)
raise SystemExit(subprocess.call(['dotnet','run','--project',str(out/'test.csproj')],cwd=root))
