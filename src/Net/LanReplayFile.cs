using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CCCPRacing.Net;

/// <summary>Local, versioned journal of packets already produced by LAN replication. No scene traversal.</summary>
public static class LanReplayFile
{
    public const int Version=1, MaxPacket=32*1024*1024;
    public const long MaxFile=512L*1024*1024;
    public enum Kind : byte { Car=1, Mesh=2, World=3, Effect=4, Break=5, OwnCar=6, Go=7 }
    public record Player(long Id,string Name,int Slot);
    public record Header(string Track,string Signature,string GameVersion,int Protocol,int Seed,long LocalId,Player[] Players);
    public record Packet(float Time,Kind Type,byte[] Data);
    public record Entry(float Time,Kind Type,long Offset,int Length);

    public static void Validate(Header h)
    {
        if(h==null || h.Protocol!=11 || h.Track is not ("ilyich" or "siberia" or "atomgrad" or "bonus") ||
            string.IsNullOrWhiteSpace(h.Signature) || h.Signature.Length>256 || h.Players==null || h.Players.Length is <1 or >6 ||
            h.Players.Select(p=>p.Id).Distinct().Count()!=h.Players.Length || !h.Players.Any(p=>p.Id==h.LocalId) ||
            h.Players.Any(p=>p.Id<=0 || p.Slot is <0 or >5 || p.Name==null || p.Name.Length>80) ||
            h.Players.Select(p=>p.Slot).Distinct().Count()!=h.Players.Length)
            throw new InvalidDataException("Incompatible LAN replay header");
    }

    /// <summary>Immutable payload copies; bounded queue; no waits or disk writes in Add. Completed files only after footer+flush.</summary>
    public sealed class Writer : IDisposable
    {
        readonly BlockingCollection<Packet> _queue=new(new ConcurrentQueue<Packet>());
        readonly long _queueLimit;
        readonly string _path;
        long _pending;
        float _last;
        int _closed;
        Func<byte[]> _finalData;
        string _error;
        public string Error=>Volatile.Read(ref _error);
        public Task<string> Completion {get;}
        public Writer(string path,Header header,long queueLimit=64L*1024*1024)
        {
            Validate(header);_path=path;_queueLimit=queueLimit;
            Completion=Task.Run(()=>Run(header));
        }
        public bool Add(float time,Kind kind,byte[] data)
        {
            if(Volatile.Read(ref _closed)!=0 || Error!=null) return false;
            if(!float.IsFinite(time) || time<0 || !Enum.IsDefined(kind) || data==null || data.Length>MaxPacket)
            {Fail("Invalid LAN replay packet");return false;}
            if(Interlocked.Add(ref _pending,data.Length+16)>_queueLimit)
            {Interlocked.Add(ref _pending,-data.Length-16);Fail("LAN replay writer queue full");return false;}
            // Arrival time is monotonic; original car snapshot times remain in the payload.
            _last=Math.Max(_last,time);
            var packet=new Packet(_last,kind,(byte[])data.Clone());
            try {if(_queue.TryAdd(packet)) return true;} catch(InvalidOperationException) { }
            Interlocked.Add(ref _pending,-data.Length-16);return false;
        }
        public void Abort(string reason)=>Fail(reason);
        void Fail(string message) {Interlocked.CompareExchange(ref _error,message,null);Finish();}
        public Task<string> Finish(Func<byte[]> finalData=null) {if(Volatile.Read(ref _closed)==0) {_finalData=finalData;if(Interlocked.Exchange(ref _closed,1)==0) _queue.CompleteAdding();}return Completion;}
        public void Dispose()=>Finish(); // never block the race waiting for disk/compression
        string Run(Header header)
        {
            string partial=_path+".partial";
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                using(var stream=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                using(var w=new BinaryWriter(stream))
                using(var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256)) {
                    byte[] json=JsonSerializer.SerializeToUtf8Bytes(header);
                    w.Write(0x52504E4C);w.Write(Version);w.Write(json.Length);w.Write(json);digest.AppendData(json);
                    int count=0;
                    foreach(var p in _queue.GetConsumingEnumerable()) {
                        Interlocked.Add(ref _pending,-p.Data.Length-16);
                        if(Error!=null) continue;
                        if(stream.Position+p.Data.Length+64>MaxFile) {Fail("LAN replay file limit reached");continue;}
                        using var record=new MemoryStream();using(var rw=new BinaryWriter(record,System.Text.Encoding.UTF8,true))
                        {rw.Write(p.Time);rw.Write((byte)p.Type);rw.Write(p.Data.Length);rw.Write(p.Data);}
                        byte[] bytes=record.ToArray();w.Write(bytes);digest.AppendData(bytes);count++;
                    }
                    if(Error!=null) return null;
                    if(_finalData!=null) {
                        byte[] own=_finalData();
                        if(own==null || own.Length>MaxPacket || stream.Position+own.Length+64>MaxFile) throw new InvalidDataException("LAN own-car recording limit");
                        using var record=new MemoryStream();using(var rw=new BinaryWriter(record,System.Text.Encoding.UTF8,true))
                        {rw.Write(_last);rw.Write((byte)Kind.OwnCar);rw.Write(own.Length);rw.Write(own);}
                        byte[] bytes=record.ToArray();w.Write(bytes);digest.AppendData(bytes);count++;
                    }
                    w.Write(float.PositiveInfinity);w.Write((byte)0);w.Write(count);w.Write(digest.GetHashAndReset());w.Flush();stream.Flush(true);
                }
                File.Move(partial,_path,false);return _path;
            } catch(Exception e) {Interlocked.CompareExchange(ref _error,"LAN replay save failed: "+e.Message,null);return null;}
            finally {
                Interlocked.Exchange(ref _closed,1);
                if(!_queue.IsAddingCompleted) _queue.CompleteAdding();
                while(_queue.TryTake(out var p)) Interlocked.Add(ref _pending,-p.Data.Length-16);
                _queue.Dispose();
            }
        }
    }

    /// <summary>Index only in memory. Validate complete footer/hash before accepting any playback.</summary>
    public sealed class Reader : IDisposable
    {
        readonly FileStream _stream;
        public Header Metadata {get;}
        public string SourcePath {get;}
        public IReadOnlyList<Entry> Entries {get;}
        public float Duration=>Entries.Count==0?0:Entries[^1].Time;
        public Reader(string path)
        {
            SourcePath=Path.GetFullPath(path);
            _stream=new FileStream(SourcePath,FileMode.Open,FileAccess.Read,FileShare.Read);
            try {
                using var r=new BinaryReader(_stream,System.Text.Encoding.UTF8,true);
                if(_stream.Length>MaxFile || r.ReadInt32()!=0x52504E4C || r.ReadInt32()!=Version) throw new InvalidDataException("Incompatible LAN replay version");
                int n=r.ReadInt32();if(n is <1 or >65536) throw new InvalidDataException("LAN replay header limit");
                byte[] json=Exact(r,n);Metadata=JsonSerializer.Deserialize<Header>(json);Validate(Metadata);
                using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);hash.AppendData(json);
                var entries=new List<Entry>();float last=0;
                while(true) {
                    long start=_stream.Position;float time=r.ReadSingle();byte kind=r.ReadByte();int size=r.ReadInt32();
                    if(float.IsPositiveInfinity(time) && kind==0) {
                        byte[] expected=Exact(r,32);
                        if(size!=entries.Count || _stream.Position!=_stream.Length || !CryptographicOperations.FixedTimeEquals(expected,hash.GetHashAndReset())) throw new InvalidDataException("LAN replay integrity failure");
                        break;
                    }
                    if(!float.IsFinite(time) || time<last || !Enum.IsDefined((Kind)kind) || size<0 || size>MaxPacket || entries.Count>=2000000) throw new InvalidDataException("Invalid LAN replay record");
                    last=time;entries.Add(new Entry(time,(Kind)kind,_stream.Position,size));
                    _stream.Position=start;hash.AppendData(Exact(r,size+9));
                }
                Entries=entries;
            } catch {_stream.Dispose();throw;}
        }
        static byte[] Exact(BinaryReader r,int n) {byte[] b=r.ReadBytes(n);if(b.Length!=n) throw new EndOfStreamException("Truncated LAN replay");return b;}
        public byte[] Read(Entry e) {_stream.Position=e.Offset;using var r=new BinaryReader(_stream,System.Text.Encoding.UTF8,true);return Exact(r,e.Length);}
        public void Dispose()=>_stream.Dispose();
    }
}
