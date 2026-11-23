using System;
using System.Reflection;
using CCCPRacing.Audio;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    async System.Threading.Tasks.Task RunGeneratorOwnership() { await GeneratorOwnershipTest(); GetTree().Quit(_failures == 0 ? 0 : 1); }

    /// <summary>The generator playback owns its generator (GeneratorOwnership in AudioMix.cs): the real generator stays alive, readable through its playback, after every other holder has let go, and the objects are all
    /// freed once the playback has gone (the engine's object count returns to where it was). The control without the ownership frees the generator at once, while its playback still exists (the 0.916 hazard), so the test tells the two apart.
    /// A freed-looking object is never probed by id: asking for a wrapper of a resource waiting for its managed wrapper's finalisation crashes the runtime.</summary>
    async System.Threading.Tasks.Task GeneratorOwnershipTest()
    {
        double Objects() => Performance.GetMonitor(Performance.Monitor.ObjectCount);
        async System.Threading.Tasks.Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree().CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout); }
        // the managed wrapper of a resource released natively is finalised by the GC, as in play; frames pass so that the AudioServer retires a stopped playback in its main-thread update
        async System.Threading.Tasks.Task<bool> Back(double baseline, double seconds)
        {
            double until = Time.GetTicksUsec() / 1e6 + seconds;
            while (Objects() > baseline && Time.GetTicksUsec() / 1e6 < until) { GC.Collect(); GC.WaitForPendingFinalizers(); await Frames(1); }
            return Objects() <= baseline;
        }
        // the id of the generator a playback holds, read through its meta (a wrapper made for the check is released at once)
        ulong HeldId(AudioStreamGeneratorPlayback pb)
        {
            using var v = pb.GetMeta(GeneratorOwnership.MetaKey);
            using var held = v.AsGodotObject();
            return held is AudioStreamGenerator ? held.GetInstanceId() : 0;
        }
        await Frames(5); GC.Collect(); GC.WaitForPendingFinalizers(); await Frames(2);
        double baseline = Objects();

        // control: the playback taken directly, the generator disposed by its last managed holder: gone at once (what the playback's raw pointer then read)
        {
            var player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.1f } }; AddChild(player);
            player.Play();
            var generator = (AudioStreamGenerator)player.Stream; ulong id = generator.GetInstanceId();
            var playback = player.GetStreamPlayback();
            player.Stop(); player.Stream = null; generator.Dispose();
            Check(!GodotObject.IsInstanceIdValid(id), "control: without the ownership the generator is freed while its playback still exists");
            playback.Dispose(); RemoveChild(player); player.Free();
        }
        await Back(baseline, 6);
        baseline = Objects();

        // 2D: owned
        {
            var player = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.1f } }; AddChild(player);
            player.Play();
            ulong id; { var g = (AudioStreamGenerator)player.Stream; id = g.GetInstanceId(); }
            var playback = GeneratorOwnership.Playback(player);
            Check(playback != null && HeldId(playback) == id, "the playback holds this very generator (the same object id)");
            player.Stop(); player.Stream = null;
            await Frames(12);      // (the mixer runs a good many times: the fade-out of the stopped playback)
            Check(HeldId(playback) == id, "owned: after the player has stopped and let go and the mixer has run on, the playback still holds the same live generator");
            playback.Dispose(); RemoveChild(player); player.Free();
        }
        Check(await Back(baseline, 8), $"owned: every object is freed once the playback has gone ({Objects() - baseline:F0} left over)");
        baseline = Objects();

        // 3D and a restart: every playback made by a Play holds the generator; the paused one too
        {
            var player = new AudioStreamPlayer3D { Stream = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.1f } }; AddChild(player);
            player.Play();
            ulong id; { var g = (AudioStreamGenerator)player.Stream; id = g.GetInstanceId(); }
            var first = GeneratorOwnership.Playback(player);
            player.StreamPaused = true;
            player.Stop(); player.Play();      // a restart: a new playback
            var second = GeneratorOwnership.Playback(player);
            Check(first != null && second != null && first.GetInstanceId() != second.GetInstanceId() && HeldId(first) == id && HeldId(second) == id, "3D: each playback of a restarted (and paused) player holds the same generator");
            player.StreamPaused = false; player.Stop(); player.Stream = null;
            await Frames(12);
            first.Dispose();
            Check(HeldId(second) == id, "3D: the second playback still holds it after the first has gone");
            second.Dispose(); RemoveChild(player); player.Free();
        }
        Check(await Back(baseline, 8), $"3D: every object is freed after both playbacks ({Objects() - baseline:F0} left over)");
        baseline = Objects();

        // a real sound: the node freed (its _ExitTree stops the player and lets go of the generator and of the playback wrapper), the generator readable through the playback until the mixer lets it go
        {
            var sound = new DieselSound(); AddChild(sound);
            var field = typeof(DieselSound).GetField("_pb", BindingFlags.Instance | BindingFlags.NonPublic);
            var playback = (AudioStreamGeneratorPlayback)field.GetValue(sound);
            ulong id = HeldId(playback);
            Check(id != 0, "a real sound (DieselSound): its playback holds the sound's generator");
            RemoveChild(sound); sound.Free();
        }
        Check(await Back(baseline, 8), $"a real sound is gone and every object with it once the mixer has let the playback go ({Objects() - baseline:F0} left over)");
        baseline = Objects();

        // repeated changes: many sounds made and freed, the object count stays bounded
        for (int round = 0; round < 40; round++)
        {
            var sound = new DieselSound(); AddChild(sound);
            await Frames(1);
            RemoveChild(sound); sound.Free();
        }
        Check(await Back(baseline + 2, 10), $"forty sounds made and freed: the object count is back to where it was ({Objects() - baseline:F0} over)");

        // the diagnostics of an old world leave the tree's signals with it, and a call it had queued finds it gone (0.916: 276 calls into a freed AudioDiag after four track changes)
        {
            var tree = GetTree();
            int Connections() => tree.GetSignalConnectionList(SceneTree.SignalName.NodeAdded).Count + tree.GetSignalConnectionList(SceneTree.SignalName.NodeRemoved).Count;
            int before = Connections();
            var host = new Node { Name = "OldWorld" }; AddChild(host);
            CCCPRacing.Race.AudioDiag.Install(host);
            Check(Connections() == before + 2, "the diagnostics listen to the tree's node signals while it lives");
            var voice = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.1f } };
            host.AddChild(voice);      // (queues a deferred call into the diagnostics)
            RemoveChild(host); host.Free();      // the old world goes before that call runs
            Check(Connections() == before, "the old world's diagnostics are off the tree's signals again once it has gone");
            var late = new AudioStreamPlayer { Stream = new AudioStreamGenerator { MixRate = 22050, BufferLength = 0.1f } }; AddChild(late);      // a new player added after: nobody is called
            await Frames(3);
            RemoveChild(late); late.Free();
        }
    }
}
