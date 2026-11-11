using System;
using System.Linq;
using Godot;
namespace CCCPRacing.Race;
public partial class RaceScene
{
    // Real grid entry: before client originals are replaced, test the same mask on each peer.
    partial void LanWorldCheck()
    {
        if(!LanDbg) return;
        bool on=_lanReveal.Obstacles;
        bool mask=on ? _worldNow == (Actors.All & (_fuelOnTrack ? Actors.All : ~Actors.Depot)) : _emptyWorld;
        bool parked=_worldObstacles.Where(o=>!InWorld(o.who)).All(o=>_track.Obstacles[o.index].Center.X < -1000);
        bool hidden=_actors.Where(a=>!InWorld(a.Who) && a.Show==null && a.Node!=null).All(a=>!a.Node.Visible && a.Node.ProcessMode==ProcessModeEnum.Disabled);
        bool damage=_carVis.Damage==null || _carVis.Damage.Enabled==_lanReveal.Damage;
        bool services=on || !_hud.Minimap || (!_hud.MinimapMechanic.HasValue && !_hud.MinimapFuel.HasValue);
        if(!mask || !parked || !hidden || !damage || !services) GD.PushError($"LANWORLDTEST: FAIL mask {mask} parked {parked} hidden {hidden} damage {damage} services {services}");
        else GD.Print($"LANWORLDTEST: PASS round {_lanReveal.Round} obstacles {on}, mask {_worldNow}, parked {parked}, hidden {hidden}, independent damage {damage}, services {services}");
    }
}
