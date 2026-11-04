using System;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Net;

/// <summary>
/// Another comrade's car in a LAN race: a CarSim puppet never stepped by physics, posed from the
/// snapshot buffer, drawn in the slot's paint, with a quieter engine of its own. The host simulates a separate physical car; this puppet only draws its snapshots.
/// </summary>
public class RemoteCar
{
    /// <summary>At most this share of the own engine's volume, right next to it; silent beyond FarAway m.</summary>
    public const float MaxVolume = 0.35f, FarAway = 120f;

    public readonly long Id;
    public readonly string Name;
    public readonly int Slot, FxSlot;
    public readonly SnapshotBuffer Buffer = new();
    public readonly CarSim Puppet;
    public readonly CarSim ContactProxy; // scratch prediction only, never rendered or sent back to the host
    public readonly CarVisual Vis;
    public readonly EngineSound Sound;
    public readonly EngineHealth Health=new();
    public readonly EngineEffects Effects=new();
    bool _wasDead, _wasDestroyed;
    int _repairSerial;
    /// <summary>Its lamps after dark (one combined beam: up to three of these on screen).</summary>
    public readonly CarLights Lights = new(true);
    public float AppliedTime = float.NegativeInfinity;

    public void UpdateLights(float dt, float level)
    {
        byte broken=Buffer.Latest?.BrokenHeadlights ?? 0;
        Lights.Level=level;
        Lights.Tick(dt,Puppet.Brake,(broken&1)==0,(broken&2)==0);
    }

    public RemoteCar(long id, string name, int slot, int fxSlot, CarParams p, TrackData track, Node parent, TyreEffects fx)
    {
        Id = id;
        Name = name;
        Slot = slot;
        FxSlot = fxSlot;
        Puppet = new CarSim(p, track);
        ContactProxy = new CarSim(p, track);
        Vis = CarVisual.Create(p, LanRace.Paint[slot] ?? new Color(0.7f, 0.13f, 0.1f));
        Vis.Name = "Remote" + id;
        parent.AddChild(Vis);
        if (LanRace.Paint[slot] is Color paint) Vis.Repaint(paint);
        if (Vis.Damage != null) Vis.Damage.Enabled = true;
        Health.Reset(true);parent.AddChild(Effects);Effects.Init(track,fx);
        Vis.AddChild(Lights);
        Sound = new EngineSound { Volume = 0f, Name = "RemoteEngine" + id };
        parent.AddChild(Sound);
    }

    public void TickEngine(float dt)
    {
        if(Buffer.Latest is {} s) {
            Health.NetworkStatus(s.Health,s.Destroyed);
            if(s.RepairSerial>_repairSerial) { Vis.Damage?.Reset(); _repairSerial=s.RepairSerial; }
        }
        if(Health.Dead && (!_wasDead || Health.Destroyed&&!_wasDestroyed)) {
            if(Health.Destroyed) Effects.Explode(Puppet,Vis,Puppet.Pos); else Effects.Seize(Puppet);
        }
        if(!Health.Dead && _wasDead) {Effects.Reset();Vis.Damage?.Reset();}
        _wasDead=Health.Dead;_wasDestroyed=Health.Destroyed;Effects.Tick(dt,Puppet,Health);
    }

    /// <summary>Engine and tyres from the puppet, attenuated by the distance to the listener's car.</summary>
    public void UpdateSound(Vector3 listener, float ownVolume, bool paused)
    {
        float d = Puppet.Pos.DistanceTo(listener);
        float k = Math.Clamp(1f - d / FarAway, 0f, 1f);
        Sound.Volume = paused ? 0f : MaxVolume * ownVolume * k * k;
        Sound.EngineLevel=Health.Dead || Buffer.Latest is { FuelLitres: 0 }?0:1;Sound.Misfire=Health.Smoking?.5f:0;
        Sound.Rpm = Puppet.Rpm;
        Sound.Throttle = Puppet.Throttle;
        Sound.Slide = Puppet.Slide;
        Sound.Loose = Puppet.LooseSurface;
        Sound.Ice = Puppet.OnIce;
        Sound.Ground = Puppet.GroundSurface ?? Sound.Ground;
        Sound.Scrape = Puppet.BodyScrape;
        Sound.Limiter = Puppet.Limiter;
        Sound.BrakeSqueal = Math.Clamp((Puppet.Speed - 1f) / 9f, 0f, 1f) * Math.Max(Puppet.Brake, Puppet.Handbrake * 0.5f);
    }

    /// <summary>Rewind an isolated packet viewer without allocating another car/audio/effects hierarchy.</summary>
    public void ResetPlayback(TyreEffects fx)
    {
        Sound.ProcessMode=Node.ProcessModeEnum.Disabled;Sound.Volume=0;
        Sound.ResetPlaybackBuffer();
        Sound.ClearCollision();Sound.ClearDrivetrain();Sound.ClearBackfire();
        Buffer.Clear();AppliedTime=float.NegativeInfinity;
        Effects.Reset();Health.Reset(true);_wasDead=_wasDestroyed=false;_repairSerial=0;
        Vis.Damage?.ReleaseTape(fx);Vis.Damage?.Reset();
    }

    public void Free()
    {
        Vis.QueueFree();
        Sound.QueueFree();Effects.QueueFree();
    }
}
