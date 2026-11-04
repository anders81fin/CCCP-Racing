using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CCCPRacing.Export;
using CCCPRacing.Net;
using CCCPRacing.Audio;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    /// <summary>An immutable, complete journal. Staging runs on the common export worker.</summary>
    public sealed class LanExportSource : IExportSource
    {
        readonly string _path;
        readonly long _focus;
        readonly LanReplayFile.Header _header;
        readonly Dictionary<string,string> _preferences;
        public string Kind => "lan-journal";
        public string Title => "LAN RACE";
        public string TrackId => _header.Track;
        public float DurationSeconds { get; }
        public float MinClockSeconds { get; }
        public LanExportSource(string path,long focus,int[] sounds=null,int aa=0,int soften=0,bool muted=false,LanReplayFile.Reader verifiedSource=null)
        {
            var reader=verifiedSource ?? new LanReplayFile.Reader(path);
            try {
            if(reader.SourcePath!=Path.GetFullPath(path)) throw new InvalidDataException("LAN export source path mismatch");
            _path=reader.SourcePath;_header=reader.Metadata;_focus=focus;
            if(!_header.Players.Any(p=>p.Id==focus)) throw new InvalidDataException("LAN export focus is not in the recorded roster");
            DurationSeconds=reader.Duration;
            MinClockSeconds=reader.Entries.FirstOrDefault(e=>e.Type==LanReplayFile.Kind.Go)?.Time ?? 0;
            _preferences=new Dictionary<string,string> {
                ["focus"]=_focus.ToString(CultureInfo.InvariantCulture),
                ["sounds"]=string.Join(",",sounds ?? SoundBalance.Levels.ToArray()),
                ["aa"]=aa.ToString(CultureInfo.InvariantCulture),["soften"]=soften.ToString(CultureInfo.InvariantCulture),["muted"]=muted?"1":"0"
            };
            } finally {if(verifiedSource==null) reader.Dispose();}
        }
        public ExportStaged Stage(string sourceDir)
        {
            const string name="race.lanreplay";
            string target=Path.Combine(sourceDir,name);
            using(var source=new FileStream(_path,FileMode.Open,System.IO.FileAccess.Read,FileShare.Read))
            using(var destination=new FileStream(target,FileMode.CreateNew,System.IO.FileAccess.Write,FileShare.None)) source.CopyTo(destination);
            // Validate the staged footer/hash and identity, not a mutable live reader shared across threads.
            using var staged=new LanReplayFile.Reader(target);
            if(staged.Metadata!=_header && (staged.Metadata.Track!=_header.Track || staged.Metadata.Signature!=_header.Signature ||
                staged.Metadata.Seed!=_header.Seed || staged.Metadata.LocalId!=_header.LocalId || !staged.Metadata.Players.SequenceEqual(_header.Players)))
                throw new InvalidDataException("LAN export source identity changed");
            if(staged.Duration!=DurationSeconds) throw new InvalidDataException("LAN export source duration changed");
            return new ExportStaged(name,new[]{name},new FileInfo(target).Length,new Dictionary<string,string>(_preferences));
        }
    }
    internal void ApplyLanExportPreferences(ExportPlan plan)
    {
        var values=plan.Extra["sounds"].Split(',');
        if(values.Length!=SoundBalance.Count) throw new InvalidDataException("Invalid LAN export sound settings");
        for(int i=0;i<values.Length;i++) SoundBalance.Levels[i]=Math.Clamp(int.Parse(values[i],CultureInfo.InvariantCulture),0,SoundBalance.Max);
        SoundBalance.Apply();
        _aaMode=Math.Clamp(int.Parse(plan.Extra["aa"],CultureInfo.InvariantCulture),0,AaNames.Length-1);
        _softLevel=Math.Clamp(int.Parse(plan.Extra["soften"],CultureInfo.InvariantCulture),0,1);
        _lanPlaybackOldMute=plan.Extra["muted"]=="1";
    }
}

public static partial class ExportChild
{
    static partial void RegisterExtraKinds(RaceScene live)
        => ExportViewerFactories.ByKind["lan-journal"]=plan=>new LanExportViewer(plan);
}

/// <summary>Runs in the private export child. The host/client and its transport are never accessed.</summary>
public sealed class LanExportViewer : IExportViewer
{
    readonly RaceScene _scene;
    readonly long _focus;
    public string Error {get;private set;}
    public bool Ready=>_scene?.LanReplayExportReady==true;
    public float Clock=>_scene?.LanReplayExportClock ?? 0;
    public float Duration=>_scene?.LanReplayExportEnd ?? 0;
    public bool Paused=>_scene?.LanReplayExportPaused ?? true;
    public string CameraName=>"TopDown";
    public LanExportViewer(ExportPlan plan)
    {
        LanReplayFile.Reader reader=null;
        try {
            if(plan.SourceId!="race.lanreplay") throw new InvalidDataException("Invalid LAN staged source name");
            _focus=long.Parse(plan.Extra["focus"],CultureInfo.InvariantCulture);
            reader=new LanReplayFile.Reader(Path.Combine(plan.SourceDir,plan.SourceId));
            if(reader.Metadata.Track!=plan.TrackId) throw new InvalidDataException("LAN export track mismatch");
            _scene=RaceScene.CreateLanPlayback(reader,externallyDriven:true);
            _scene.ApplyLanExportPreferences(plan);
            // Only the new recorded scene supplies images and sound. This is the export
            // process's title world, not the player's independent host/client process.
            var live=ExportChild.Live;
            foreach(Node node in live.FindChildren("*","",true,false)) {
                if(node is AudioStreamPlayer a) {a.Stop();a.StreamPaused=true;}
                if(node is AudioStreamPlayer3D b) {b.Stop();b.StreamPaused=true;}
                if(node is CanvasLayer layer) layer.Hide();
            }
            live.ProcessMode=Node.ProcessModeEnum.Disabled;
            // Match the SP viewer's isolated world and native viewport. Sharing the
            // title World's environment/cameras is incorrect even when its meshes hide.
            var viewport=new SubViewport {
                Size=new Vector2I(ExportEnvironment.NativeW,ExportEnvironment.NativeH),OwnWorld3D=true,
                HandleInputLocally=false,AudioListenerEnable3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Always,
                CanvasItemDefaultTextureFilter=Viewport.DefaultCanvasItemTextureFilter.Nearest
            };
            live.GetParent().AddChild(viewport);
            var image=new TextureRect {Texture=viewport.GetTexture(),Size=viewport.Size,MouseFilter=Control.MouseFilterEnum.Ignore};
            live.GetParent().AddChild(image);
            image.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            viewport.AddChild(_scene);
            ExportChild.Live.Hide();
        } catch(Exception e) {reader?.Dispose();Error=e.Message;}
    }
    public void Configure(ExportPlan plan)
    {
        if(plan.Request.CameraKeys.Count!=0 || plan.Request.DefaultCamera!="TopDown")
            throw new InvalidDataException("LAN export currently supports the recorded race with the default top-down camera");
        _scene.PrepareLanReplayExport(plan.Request.InClock,_focus);
    }
    public void SeekPaused(float clock)=>_scene.PrepareLanReplayExport(clock,_focus);
    public void RenderFrame(float clock,int index)=>_scene.RenderLanReplayExportFrame(clock);
}
