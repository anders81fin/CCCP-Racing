using System;
using System.IO;
using System.Linq;
using CCCPRacing.Audio;
using CCCPRacing.Export;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    ExportUiController _lanExportUi;
    CanvasLayer _lanExportLayer;
    Label _lanExportText;
    ProgressBar _lanExportProgress;
    bool _lanExportWasPaused,_lanExportWasLabelVisible;

    void LanExportOpen()
    {
        if(_lanPlaybackAccessPath!=null) LanPlaybackReadAccess();
        if(!LanPlaybackMayExport) {_lanPlaybackLabel?.Show();return;}
        if(!LanReplayExportReady || _lanPlaybackExternallyDriven) return;
        if(_lanExportUi==null) {
            string root=OS.HasFeature("editor")?Path.Combine(ProjectSettings.GlobalizePath("res://"),"media","replay-exports"):ProjectSettings.GlobalizePath("user://exports");
            root=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--export-dir="))?["--export-dir=".Length..] ?? root;
            var environment=new ExportEnvironment(OS.GetExecutablePath(),OS.HasFeature("editor")?ProjectSettings.GlobalizePath("res://").TrimEnd('/'):null,
                Path.Combine(root,"jobs"),root,null,null,System.Environment.ProcessId);
            _lanExportUi=new ExportUiController(()=>new ExportContext {
                Source=new LanExportSource(LanReplayExportPath,LanReplayExportFocus,SoundBalance.Levels.ToArray(),_aaMode,_softLevel,AudioServer.IsBusMute(0),LanPlaybackSource),
                Cut=OS.GetCmdlineUserArgs().Any(a=>a.StartsWith("--lan-export-ui-run="))?(_lanPlaybackStart+1,Math.Min(_lanPlaybackStart+3,LanReplayExportEnd)):null,
                Name="lan-"+LanPlaybackSource.Metadata.Track,DefaultCamera="TopDown"
            },environment,path=>OS.ShellOpen(path));
            _lanExportLayer=new CanvasLayer {Layer=40};AddChild(_lanExportLayer);
            var background=new ColorRect {Position=new Vector2(12,12),Size=new Vector2(456,246),Color=new Color(0.025f,0.025f,0.025f,.96f),MouseFilter=Control.MouseFilterEnum.Ignore};
            _lanExportLayer.AddChild(background);
            _lanExportText=new Label {Position=new Vector2(20,20),Size=new Vector2(440,214),MouseFilter=Control.MouseFilterEnum.Ignore};
            _lanExportText.AddThemeFontSizeOverride("font_size",11);_lanExportLayer.AddChild(_lanExportText);
            _lanExportProgress=new ProgressBar {Position=new Vector2(20,237),Size=new Vector2(440,8),MaxValue=1,ShowPercentage=false,MouseFilter=Control.MouseFilterEnum.Ignore};
            _lanExportLayer.AddChild(_lanExportProgress);
        }
        _lanExportWasPaused=_lanPlaybackPaused;_lanExportWasLabelVisible=_lanPlaybackLabel.Visible;
        _lanPlaybackPaused=true;_lanPlaybackLabel.Hide();
        _lanExportUi.Open();LanExportTick();
    }
    void LanExportTick()
    {
        if(_lanExportUi==null) return;
        _lanExportUi.Tick();
        bool open=_lanExportUi.IsOpen;
        if(_lanExportLayer.Visible && !open) {_lanPlaybackPaused=_lanExportWasPaused;_lanPlaybackLabel.Visible=_lanExportWasLabelVisible;}
        _lanExportLayer.Visible=open;
        if(!open) return;
        var lines=_lanExportUi.Rows.Count>0?_lanExportUi.Rows.Select((row,i)=>(i==_lanExportUi.Selected?"> ":"  ")+row):_lanExportUi.Lines;
        _lanExportText.Text=_lanExportUi.Title+"\n\n"+string.Join("\n",lines)+"\n\nARROWS / ENTER / ESC";
        _lanExportProgress.Visible=_lanExportUi.Fraction>=0;
        _lanExportProgress.Value=Math.Max(0,_lanExportUi.Fraction);
    }
    bool LanExportInput(Key key)
    {
        if(_lanExportUi?.IsOpen!=true) {if(key==Key.E) {LanExportOpen();return true;}return false;}
        ExportUiAction? action=key switch {Key.Up=>ExportUiAction.Up,Key.Down=>ExportUiAction.Down,Key.Left=>ExportUiAction.Left,Key.Right=>ExportUiAction.Right,
            Key.Enter or Key.KpEnter=>ExportUiAction.Accept,Key.Escape=>ExportUiAction.Back,_=>null};
        if(action.HasValue) _lanExportUi.Handle(action.Value);
        LanExportTick();return true;
    }
}
