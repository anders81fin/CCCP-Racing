using System;
using System.IO;
using System.Linq;
using CCCPRacing.Audio;
using CCCPRacing.Car;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void IdleTest()
    {
        string output = Path.Combine("media", "idle-review-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(output);
        var ground = new FlatGround(); var car = NewCar(ground);
        Run(car, 3, () => car.SetControls(false, false, 0, false));
        Check(car.Neutral && car.Disengaged && car.Rpm >= 1199 && car.Rpm <= 1201 && car.Speed < .2f,
            $"idle: stationary car rests in N at {car.Rpm:F0} rpm, speed {car.Speed:F3} m/s");
        Run(car, 1, () => car.SetControls(true, false, 0, false));
        Check(!car.Neutral && car.Gear > 0 && car.ForwardSpeed > 2,
            "idle: keyboard throttle engages first gear and launches immediately");
        Run(car, 5, () => { car.SetControls(false, false, 0, true); car.Brake = 1; });
        Check(car.Neutral && car.Rpm >= 1199 && car.Rpm <= 1201,
            "idle: stopping after a pull returns to N and 1200 rpm");
        car.Neutral = true;
        Run(car, .5f, () => car.SetControls(true, false, 0, true));
        Check(car.Neutral && car.Rpm > 3500, "idle: explicit start-line N still allows free revs");
        car.Neutral = false; car.Clutch = true;
        Run(car, .4f, () => car.SetControls(true, false, 0, true));
        Check(car.Clutch && car.Rpm > 5000 && car.Speed < .2f, "idle: clutch-down launch preparation is preserved");
        car.Clutch = false;
        Run(car, 1, () => car.SetControls(true, false, 0, false));
        Check(car.ForwardSpeed > 2 && !car.Neutral, "idle: clutch release still drives the car");
        var pad = NewCar(ground);
        Run(pad, 2, () => pad.SetControls(0f, 0f, 0, false, true));
        Run(pad, 1, () => pad.SetControls(0f, 1f, 0, false, true));
        Check(pad.Gear == -1 && pad.ForwardSpeed < -1 && !pad.Neutral,
            "idle: controller brake trigger selects reverse from automatic N");
        var keyboard = NewCar(ground);
        Run(keyboard, 2, () => keyboard.SetControls(false, false, 0, false));
        Run(keyboard, 1, () => keyboard.SetControls(false, true, 0, false));
        Check(keyboard.Gear == -1 && keyboard.ForwardSpeed < -1 && !keyboard.Neutral,
            "idle: keyboard reverse leaves automatic N");
        var rolling = NewCar(ground); rolling.Vel = rolling.Forward * 8;
        Run(rolling, .1f, () => rolling.SetControls(false, false, 0, false));
        Check(!rolling.Neutral, "idle: lifting while rolling keeps engine braking engaged");

        AirborneEngineTest();

        const int rate = 22050;
        var sound = new EngineSound { Rpm = 1200, Throttle = 0, Speed = 0, Driven = 0, Gear = 1 };
        var samples = Enumerable.Range(0, rate * 8).Select(_ => sound.NextSample()).ToArray();
        sound.Free();
        using (var w = new BinaryWriter(File.Create(Path.Combine(output, "rally-idle-1200rpm.wav"))))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + samples.Length * 2);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(samples.Length * 2);
            foreach (float v in samples) w.Write((short)Math.Clamp((int)MathF.Round(v * 32767), -32767, 32767));
        }
        Check(samples.All(float.IsFinite) && samples.Max(v => Math.Abs(v)) < .95f
            && samples.Sum(v => (double)v * v) > 1, "idle: production 1200 rpm burble is audible and unclipped");
        var ghost = new Ghost();
        var idleCar = NewCar(ground); Run(idleCar, 2, () => idleCar.SetControls(false, false, 0, false));
        ghost.Record(0, idleCar, 0, 0, 0); string path = Path.Combine(output, "idle-ghost.bin"); ghost.Save(path);
        var loaded = Ghost.Load(path, new CarParams(), ground); var puppet = NewCar(ground);
        loaded?.Apply(0, puppet, 0);
        Check(loaded != null && puppet.Neutral && puppet.Rpm >= 1199 && puppet.Rpm <= 1201,
            "idle: saved tape restores actual N and idle rpm");
        GD.Print("IDLE REVIEW " + Path.GetFullPath(output));
    }
}
