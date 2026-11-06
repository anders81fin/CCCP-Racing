using System;
using System.Linq;
using CCCPRacing.Net;
using Godot;

namespace CCCPRacing.Race;

public partial class RaceScene
{
    string _lanPlaybackAccessPath;
    LanReplayAccess.Permission _lanPlaybackAccess=LanReplayAccess.Permission.Export;
    ulong _lanPlaybackAccessReadAt;
    bool _lanPlaybackClosing;
    bool LanPlaybackMayExport=>_lanPlaybackAccess==LanReplayAccess.Permission.Export && !_lanPlaybackClosing;
    void LanPlaybackInitAccess()
    {
        _lanPlaybackAccessPath=OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--lan-replay-access="))?["--lan-replay-access=".Length..];
        if(_lanPlaybackAccessPath!=null) LanPlaybackReadAccess();
    }
    void LanPlaybackReadAccess()
    {
        _lanPlaybackAccessReadAt=Time.GetTicksMsec();
        _lanPlaybackAccess=LanReplayAccess.Read(_lanPlaybackAccessPath,out var lease)?
            LanReplayAccess.Resolve(lease,LanReplayAccess.OwnerAlive(lease.OwnerPid)):LanReplayAccess.Permission.Closed;
    }
    bool LanPlaybackCheckAccess()
    {
        if(_lanPlaybackAccessPath==null) return true; // an offline journal has no active LAN session
        if(Time.GetTicksMsec()-_lanPlaybackAccessReadAt>=100) LanPlaybackReadAccess();
        if(_lanPlaybackAccess!=LanReplayAccess.Permission.Closed && !_lanPlaybackClosing) return true;
        if(!_lanPlaybackClosing) {
            _lanPlaybackClosing=true;_lanExportUi?.Dispose();
            if(LanReplayExportReady) {_lanPlaybackPaused=true;LanPlaybackPose(0);}
            GD.Print("LAN replay: access ended; closing the viewer and cancelling any owned export");
        }
        // Cooperatively cancel the owned job before exiting; do not orphan its renderer or parts.
        var job=_lanExportUi?.LastJob;
        if(job==null || job.Done.IsCompleted) CCCPRacing.Audio.AudioMix.Quit(this);
        return false;
    }
    void LanPlaybackReleaseAccess()
    {
        if(_lanPlaybackAccessPath!=null) LanReplayAccess.DeleteOwned(_lanPlaybackAccessPath);
    }
}
