using System;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    record HandbrakeResult(float[] Speeds,float MaxYawRate,int LockedPropulsive,int OverGrip,float RearSpinMax,bool Finite,int LockFlips,int GearChanges);

    /// <summary>Scripted run on wide flat ground (no world-edge clamp), controls re-sent every step like the game.</summary>
    HandbrakeResult HandbrakeRun(float power,float throttle,float brake,float steer,bool handbrake,float startSpeed,float[] at)
    {
        var car=NewCar(new FlatGround());car.PowerScale=power;
        for(int i=0;i<(int)(1/Dt);i++){car.SetControls(0,0,0,false,false);car.Step(Dt);} // settle suspension
        car.Vel=car.Forward*startSpeed;
        var speeds=new float[at.Length];float maxYaw=0,spinMax=0;bool finite=true;int next=0,lockedProp=0,overGrip=0,flips=0,gearChanges=0;bool lockWas=false;int gearWas=car.Gear;
        int steps=(int)(at[^1]/Dt)+1;
        for(int i=1;i<=steps;i++)
        {
            car.SetControls(throttle,brake,steer,handbrake,false);
            car.Step(Dt);
            maxYaw=Math.Max(maxYaw,Math.Abs(car.AngVel.Y));
            spinMax=Math.Max(spinMax,Math.Max(Math.Abs(car.Wheels[2].SpinSpeed),Math.Abs(car.Wheels[3].SpinSpeed))*car.P.WheelRadius*3.6f);
            if(car.Wheels[2].Locked!=lockWas){flips++;lockWas=car.Wheels[2].Locked;}
            if(car.Gear!=gearWas){gearChanges++;gearWas=car.Gear;}
            foreach(var w in car.Wheels)
            {
                if(w.Front||!w.Contact)continue;
                if(w.Locked&&w.ForceLong*w.VelLong>1f)lockedProp++;          // a locked wheel may only brake
                if(Math.Abs(w.ForceLong)>w.LongLimit*1.01f+1f)overGrip++;     // tyre force beyond the grip it was held to
            }
            if(!float.IsFinite(car.Speed)||!float.IsFinite(car.Pos.X)||!float.IsFinite(car.Pos.Z)||!float.IsFinite(car.AngVel.Y))finite=false;
            while(next<at.Length&&i*Dt>=at[next]-Dt/2)speeds[next++]=car.Speed*3.6f;
        }
        return new(speeds,maxYaw,lockedProp,overGrip,spinMax,finite,flips,gearChanges);
    }

    /// <summary>PadKeysUnchangedTest's scenario with the handbrake never pressed: must not move with this change.</summary>
    static string KeyPathNoHandbrake()
    {
        var car = NewCar(new FlatGround());
        Run(car, 1f);
        float t = 0;
        Run(car, 7f, () =>
        {
            if (t < 3f) car.SetControls(true, false, t > 1.5f ? 0.6f : 0f, false);
            else if (t < 4.5f) car.SetControls(false, true, -1f, false);
            else if (t < 5.5f) car.SetControls(false, true, 0.3f, false);
            else if (t < 6.2f) car.SetControls(true, false, -1f, false);
            else car.SetControls(false, false, 0f, false);
            t += Dt;
        });
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:R} {1:R} {2:R} {3:R} {4:R} {5}", car.Pos.X, car.Pos.Z, car.Vel.X, car.Vel.Z, car.SteerAngle, car.Gear);
    }

    /// <summary>Handbrake + gas recorded by the production Ghost, written to bytes, read back, sought and looped:
    /// the wheel Locked/SpinSpeed and handbrake the playback shows are the ones that were driven.</summary>
    void HandbrakeGhostRoundTrip()
    {
        var ground=new FlatGround();var car=NewCar(ground);
        for(int i=0;i<(int)(1/Dt);i++){car.SetControls(0,0,0,false,false);car.Step(Dt);}
        var rec=new Ghost();var expect=new System.Collections.Generic.List<(bool lk,float spin,float hb)>();
        float t=0;
        for(int i=0;i<(int)(40/Dt);i++)
        {
            car.SetControls(1,0,0,true,false);car.Step(Dt);t+=Dt;
            int before=rec.Count;rec.Record(t,car,0,0,0);
            if(rec.Count>before)expect.Add((car.Wheels[2].Locked,car.Wheels[2].SpinSpeed,car.Handbrake));
        }
        var loaded=Ghost.FromBytes(rec.ToBytes(),car.P,ground,out string err);
        Check(loaded!=null&&loaded.Count==rec.Count,$"handbrake ghost: bytes load back whole ({err})");
        if(loaded==null)return;
        var play=NewCar(ground);int bad=0,lockedFrames=0,rollingFrames=0;
        // Spin is interpolated between stored frames: a clock a rounding error past a sample (k/30) mixes in ~1e-4 of the next one, hence 0.02 rad/s.
        void Compare(int k){loaded.Apply(k/Ghost.SampleRate,play,Dt);var e=expect[k];
            if(play.Wheels[2].Locked!=e.lk||Math.Abs(play.Wheels[2].SpinSpeed-e.spin)>.02f||play.Handbrake!=e.hb){bad++;GD.Print($"  ghost diff k={k}: locked {play.Wheels[2].Locked}/{e.lk} spin {play.Wheels[2].SpinSpeed:F3}/{e.spin:F3} hb {play.Handbrake}/{e.hb}");}
            if(e.lk)lockedFrames++;else rollingFrames++;}
        for(int k=0;k<expect.Count;k++)Compare(k);
        Check(bad==0,$"handbrake ghost: playback shows the recorded wheel lock, spin and handbrake on all {expect.Count} frames ({bad} differ)");
        Check(lockedFrames>0&&rollingFrames>0,$"handbrake ghost: the lap holds both locked ({lockedFrames}) and rolling ({rollingFrames}) rear wheels");
        bad=0;var rng=new Random(7);
        for(int n=0;n<200;n++)Compare(rng.Next(expect.Count));
        loaded.Rewind();for(int k=0;k<expect.Count;k+=3)Compare(k);
        Check(bad==0,"handbrake ghost: seeking backwards and looping reproduces the recorded wheel state");
    }

    /// <summary>Full steering, gas and handbrake from 54 km/h must not sustain a high-speed broadside slide.</summary>
    void HandbrakeBroadsideTest()
    {
        // Full lock, full gas and the handbrake held from 54 km/h: the car goes broadside and, with the old arcade
        // scrub forgiveness, stayed there above 70 degrees of slip and 30 km/h for most of 20 s on flat ground.
        // A sliding car has to lose that speed to the tyres.
        foreach(float steer in new[]{1f,-1f})
        {
            var car=NewCar(new FlatGround());
            for(int i=0;i<(int)(1/Dt);i++){car.SetControls(0,0,0,false,false);car.Step(Dt);}
            car.Vel=car.Forward*15f;float sideways=0,maxSlip=0;bool finite=true;
            for(int i=0;i<(int)(20/Dt);i++)
            {
                car.SetControls(1,0,steer,true,false);car.Step(Dt);
                var h=new Vector3(car.Vel.X,0,car.Vel.Z);var f=new Vector3(car.Forward.X,0,car.Forward.Z);
                float slip=h.Length()>2?Mathf.RadToDeg(Math.Abs(f.SignedAngleTo(h,Vector3.Up))):0;maxSlip=Math.Max(maxSlip,slip);
                if(h.Length()>8.3f&&slip>70)sideways+=Dt;
                finite&=float.IsFinite(car.Speed)&&float.IsFinite(car.Pos.X);
            }
            GD.Print($"handbrake-throttle broadside full lock {steer:+0;-0}: {sideways:F1} s of 20 above 70 deg slip and 30 km/h, max slip {maxSlip:F0} deg, end speed {car.Speed*3.6f:F0} km/h");
            Check(finite&&sideways<3f,$"handbrake+throttle: full lock, full gas and handbrake does not carry a broadside slide ({sideways:F1} s of 20 above 70 deg and 30 km/h)");
        }
    }

    void HandbrakeTrace()
    {
        var car=NewCar(new FlatGround());
        for(int i=0;i<(int)(1/Dt);i++){car.SetControls(0,0,0,false,false);car.Step(Dt);}
        var ring=new System.Collections.Generic.Queue<string>();int printed=0,after=0;bool was=false;
        for(int i=1;i<=(int)(60/Dt);i++)
        {
            car.SetControls(1,0,0,true,false);car.Step(Dt);
            string line=$"hb-trace t={i*Dt:F3} gear={car.Gear} rpm={car.Rpm:F0} netAccel={car.DriveAcceleration:F2} v={car.Speed*3.6f:F1} spin={car.Wheels[2].SpinSpeed*car.P.WheelRadius*3.6f:F1} held={car.Wheels[2].HandbrakeHeld} locked={car.Wheels[2].Locked} fx={car.Wheels[2].ForceLong:F0}";
            ring.Enqueue(line);if(ring.Count>10)ring.Dequeue();
            if(after>0){GD.Print(line);after--;}
            if(car.Wheels[2].Locked!=was){was=car.Wheels[2].Locked;if(printed<4&&i*Dt>15){printed++;foreach(var l in ring)GD.Print(l);after=10;GD.Print("hb-trace ----");}}
        }
    }

    void HandbrakeThrottleTest()
    {
        if(Array.Exists(OS.GetCmdlineUserArgs(),a=>a=="--handbrake-trace")){HandbrakeTrace();return;}
        string keys=KeyPathNoHandbrake();
        GD.Print($"keypath-no-handbrake: {keys}");
        // Recorded on 2b2fde0, before the handbrake change: every handbrake branch is dormant without the key. Re-recorded 2026-11-02 for the steering defaults and the petrol's extra 25 % torque (the branches are still dormant: only the car's own numbers moved).
        Check(keys=="23.315125 -25.338913 -0.03218558 -2.6341937 0 1","handbrake change: the keyboard path without the handbrake is bit for bit the base trajectory");
        var at=new[]{5f,10f,20f,40f,60f};
        string F(HandbrakeResult r)=>$"km/h@5/10/20/40/60s [{string.Join(" ",Array.ConvertAll(r.Speeds,s=>s.ToString("F1")))}] maxYaw {r.MaxYawRate:F2} rad/s lockedPropulsiveSteps {r.LockedPropulsive} overGripSteps {r.OverGrip} rearWheelMax {r.RearSpinMax:F0} km/h lockFlips {r.LockFlips} gearChanges {r.GearChanges}";
        foreach(float power in new[]{.5f,1f,1.5f})
        {
            var gas=HandbrakeRun(power,1,0,0,false,0,at);
            var both=HandbrakeRun(power,1,0,0,true,0,at);
            var steered=HandbrakeRun(power,1,0,.5f,true,0,at);
            var moving=HandbrakeRun(power,1,0,0,true,15f,at);
            GD.Print($"handbrake-throttle power {power}: gas only        {F(gas)}");
            GD.Print($"handbrake-throttle power {power}: gas+handbrake   {F(both)}");
            GD.Print($"handbrake-throttle power {power}: steered .5      {F(steered)}");
            GD.Print($"handbrake-throttle power {power}: moving 15 m/s   {F(moving)}");
            Check(gas.Speeds[1]>20f,$"handbrake+throttle power {power}: fixture sanity, gas alone accelerates ({gas.Speeds[1]:F1} km/h at 10 s)");
            // The brake torque is 4 kN a wheel; the engine may or may not overpower it. Either way the
            // wheel state has to agree with the motion: a locked wheel does not push the car along.
            Check(both.LockedPropulsive+steered.LockedPropulsive+moving.LockedPropulsive==0,
                $"handbrake+throttle power {power}: a locked rear wheel never pushes the car (tyre force against patch speed)");
            Check(both.OverGrip+steered.OverGrip+moving.OverGrip+gas.OverGrip==0,
                $"handbrake+throttle power {power}: rear tyre force stays within the grip it was held to");
            float top=gas.Speeds[4]+1f;
            Check(Array.TrueForAll(both.Speeds,v=>v<=top)&&Array.TrueForAll(steered.Speeds,v=>v<=top)&&Array.TrueForAll(moving.Speeds,v=>v<=top),
                $"handbrake+throttle power {power}: never faster than gas alone ({top-1:F1} km/h)");
            Check(gas.Finite&&both.Finite&&steered.Finite&&moving.Finite,$"handbrake+throttle power {power}: states stay finite for 60 s");
        }
        // Short gas-off handbrake flick (the rally technique): speed 20 m/s, half lock, handbrake for 1 s.
        foreach(bool gasOn in new[]{false,true})
        {
            var car=NewCar(new FlatGround());
            for(int i=0;i<(int)(1/Dt);i++){car.SetControls(0,0,0,false,false);car.Step(Dt);}
            car.Vel=car.Forward*20f;float yaw0=car.Forward.SignedAngleTo(Vector3.Forward,Vector3.Up);float maxYaw=0;
            for(int i=0;i<(int)(1/Dt);i++){car.SetControls(gasOn?1:0,0,.5f,true,false);car.Step(Dt);maxYaw=Math.Max(maxYaw,Math.Abs(car.AngVel.Y));}
            for(int i=0;i<(int)(1/Dt);i++){car.SetControls(0,0,0,false,false);car.Step(Dt);}
            float turn=Mathf.RadToDeg(Math.Abs(Mathf.Wrap(car.Forward.SignedAngleTo(Vector3.Forward,Vector3.Up)-yaw0,-Mathf.Pi,Mathf.Pi)));
            GD.Print($"handbrake-throttle flick gas={(gasOn?"on":"off")}: 1 s handbrake at half lock from 72 km/h -> heading change {turn:F0} deg, peak yaw {maxYaw:F2} rad/s, speed after release {car.Speed*3.6f:F0} km/h");
            float expectedTurn=gasOn?111f:109f;      // (95 / 80 until HandbrakeRotate 1.2 "more dramatic", 0.82)
            Check(float.IsFinite(car.Speed)&&Math.Abs(turn-expectedTurn)<3f,
                $"handbrake flick gas={(gasOn?"on":"off")}: retains the reviewed short-turn response ({turn:F0} deg, expected {expectedTurn:F0} ±3)");
        }
        var weak=HandbrakeRun(.4f,1,0,0,true,0,at);      // (.5 until the petrol got 25 % more torque: .4 is the same engine against the handbrake)
        Check(weak.Speeds[4]<.05f,$"handbrake+throttle: an engine weaker than the handbrake leaves the car held ({weak.Speeds[4]:F1} km/h after 60 s)");
        var hold=HandbrakeRun(1,0,0,0,true,15f,at);
        GD.Print($"handbrake-throttle handbrake only from 15 m/s: {F(hold)}");
        Check(hold.Speeds[2]<1f,"handbrake alone stops the car");
        HandbrakeBroadsideTest();
        HandbrakeGhostRoundTrip();
        LanHandbrakeStateTest();
    }
}
