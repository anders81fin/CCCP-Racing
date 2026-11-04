using System;
using System.IO;
using System.Text.Json;

namespace CCCPRacing.Net;

/// <summary>Small parent/viewer control lease, separate from immutable race journals and LAN packets.</summary>
public static class LanReplayAccess
{
    public enum Permission { Closed, Watch, Export }
    public sealed record Lease(int Version,string Token,int OwnerPid,int Round,Permission Access);
    public static Permission For(bool active,LanSession.Phase phase)=>!active?Permission.Closed:
        phase==LanSession.Phase.SessionFinished?Permission.Export:
        phase==LanSession.Phase.Results?Permission.Watch:Permission.Closed;
    public static string PathFor(string token)=>Path.Combine(Path.GetTempPath(),"cccp-lan-replay-"+token+".access");
    public static void Write(Lease lease)
    {
        if(!Valid(lease)) throw new InvalidDataException("Invalid LAN replay access lease");
        string path=PathFor(lease.Token),temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(lease));File.Move(temp,path,true);
    }
    static bool Valid(Lease lease)=>lease is {Version:1,OwnerPid:>0,Round:>=0} &&
        Guid.TryParseExact(lease.Token,"N",out _) && Enum.IsDefined(lease.Access);
    public static bool Read(string path,out Lease lease)
    {
        lease=null;
        try {
            if(new FileInfo(path).Length>1024) return false;
            var value=JsonSerializer.Deserialize<Lease>(File.ReadAllText(path));
            if(!Valid(value) || Path.GetFullPath(path)!=Path.GetFullPath(PathFor(value.Token))) return false;
            lease=value;return true;
        } catch {return false;}
    }
    public static Permission Resolve(Lease lease,bool ownerAlive)=>lease.Access==Permission.Export?Permission.Export:
        ownerAlive?lease.Access:Permission.Closed;
    public static bool OwnerAlive(int pid)
    {
        try {using var owner=System.Diagnostics.Process.GetProcessById(pid);return !owner.HasExited;} catch {return false;}
    }
    public static void DeleteOwned(string path)
    {
        if(!Read(path,out var lease)) return;
        try {File.Delete(PathFor(lease.Token));File.Delete(PathFor(lease.Token)+".tmp");} catch { }
    }
}
