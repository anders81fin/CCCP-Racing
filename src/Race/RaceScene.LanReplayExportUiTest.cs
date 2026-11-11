using System;
using System.IO;
using System.Linq;
using CCCPRacing.Export;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    int _lanExportUiProbeStep;
    ExportJob _lanExportUiProbeJob;
    float _lanExportUiProbeClock;
    bool _lanExportUiProbePriorPause;
    partial void LanExportUiProbe()
    {
        string run=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--lan-export-ui-run="))?["--lan-export-ui-run=".Length..];
        string path=run ?? OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--lan-export-ui-check="))?["--lan-export-ui-check=".Length..];
        if(path==null || !LanReplayExportReady || _lanPlaybackExternallyDriven) return;
        try {
            if(!LanPlaybackMayExport) {
                if(_lanExportUiProbeStep==0) {
                    LanPlaybackInput(new InputEventKey {Keycode=Key.E,Pressed=true});
                    if(_lanExportUi?.IsOpen==true) throw new Exception("Export opened before LAN session ended");
                    GD.Print("LANEXPORT UI PASS: export blocked until the LAN session ends");_lanExportUiProbeStep=-1;
                }
                return;
            }
            if(_lanExportUiProbeStep==-1) _lanExportUiProbeStep=0;
            if(_lanExportUiProbeStep==0) {
                _lanExportUiProbePriorPause=_lanPlaybackPaused;_lanExportUiProbeClock=_lanPlaybackTime;
                LanPlaybackInput(new InputEventKey {Keycode=Key.E,Pressed=true});
                if(_lanExportUi?.IsOpen!=true || !_lanPlaybackPaused || _lanPlaybackLabel.Visible) throw new Exception("LAN export UI did not pause its own viewer");
                _lanExportUiProbeStep++;return;
            }
            if(_lanExportUiProbeStep==1) {
                if(DisplayServer.GetName()!="headless") {
                    using var image=GetViewport().GetTexture().GetImage();
                    if(image.SavePng(path)!=Error.Ok) throw new Exception("LAN export UI screenshot failed");
                }
                if(run!=null) LanPlaybackInput(new InputEventKey {Keycode=Key.Enter,Pressed=true}); // select THE CUT, two recorded seconds
                for(int i=0;i<5;i++) LanPlaybackInput(new InputEventKey {Keycode=Key.Down,Pressed=true});
                LanPlaybackInput(new InputEventKey {Keycode=Key.Enter,Pressed=true});
                _lanExportUiProbeJob=_lanExportUi.Job;
                if(_lanExportUiProbeJob==null) throw new Exception("LAN export UI did not start its job");
                if(run==null) {
                    LanPlaybackInput(new InputEventKey {Keycode=Key.Escape,Pressed=true});
                    if(_lanExportUi.Current!=ExportUiController.State.ConfirmCancel) throw new Exception("Missing cancel confirmation");
                    LanPlaybackInput(new InputEventKey {Keycode=Key.Enter,Pressed=true});
                }
                _lanExportUiProbeStep++;return;
            }
            if(_lanExportUiProbeStep==2 && _lanExportUiProbeJob.Done.IsCompleted) {
                var expected=run==null?ExportPhase.Cancelled:ExportPhase.Done;
                if(_lanExportUiProbeJob.Progress.Phase!=expected) throw new Exception("LAN export UI job did not finish as "+expected+": "+_lanExportUiProbeJob.Progress.Message);
                LanExportTick();
                LanPlaybackInput(new InputEventKey {Keycode=Key.Enter,Pressed=true});
                if(_lanExportUi.IsOpen || _lanPlaybackPaused!=_lanExportUiProbePriorPause || Math.Abs(_lanPlaybackTime-_lanExportUiProbeClock)>.001f)
                    throw new Exception("LAN export UI did not restore the exact prior viewer state");
                GD.Print("LANEXPORT UI PASS: open, paused clock, rendered settings, start, "+expected+", cleanup completion, restore pause/clock; output="+_lanExportUiProbeJob.Progress.OutputPath);
                _lanExportUiProbeStep++;GetTree().Quit();
            }
        }catch(Exception e) {GD.PushError("LANEXPORT UI FAIL: "+e.Message);GetTree().Quit(2);}
    }
}
