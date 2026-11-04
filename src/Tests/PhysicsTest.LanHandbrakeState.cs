using System;
using System.Linq;
using CCCPRacing.Car;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void LanHandbrakeStateTest()
    {
        var a=NewCar(new FlatGround());
        a.Wheels[2].HandbrakeHeld=true;
        a.Wheels[3].HandbrakeHeld=true;
        // A stationary held wheel has no visual Locked flag. That flag cannot substitute for the latch.
        a.Wheels[2].Locked=a.Wheels[3].Locked=false;
        var state=a.NetworkState();
        var b=NewCar(new FlatGround());b.RestoreNetwork(state);
        Check(b.Wheels[2].HandbrakeHeld && b.Wheels[3].HandbrakeHeld && !b.Wheels[2].Locked
              && state.SequenceEqual(b.NetworkState()),
            "LAN handbrake: correction preserves a held wheel even at rest, byte for byte");

        byte[] legacy=state[..^5];
        b.RestoreNetwork(legacy);
        Check(b.Wheels.All(w=>!w.HandbrakeHeld) && legacy.SequenceEqual(b.NetworkState()[..^5]),
            "LAN handbrake: legacy integrator prefix loads and clears stale hold latches");

        bool exact=true;
        // Both sides of the hysteresis band must resume identically after an authoritative correction.
        foreach(bool held in new[]{false,true})
        {
            a=NewCar(new FlatGround());a.PowerScale=.6f;
            for(int i=0;i<240;i++){a.SetControls(0,0,0,false,false);a.Step(Dt);}
            a.Vel=a.Forward*8;
            a.Wheels[2].HandbrakeHeld=a.Wheels[3].HandbrakeHeld=held;
            b=NewCar(new FlatGround());b.RestoreNetwork(a.NetworkState());
            for(int i=0;i<480;i++)
            {
                float gas=i<240?1f:.4f;
                bool brake=i<360;
                a.SetControls(gas,0,.35f,brake,false);b.SetControls(gas,0,.35f,brake,false);
                a.Step(Dt);b.Step(Dt);
                exact &= a.NetworkState().SequenceEqual(b.NetworkState());
            }
        }
        Check(exact,"LAN handbrake: held and released corrections replay the same inputs without integrator drift");
    }
}
