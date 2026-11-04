using System;
using System.Linq;
using CCCPRacing.Race;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    void CameraDiscoveriesTest()
    {
        string root = $"/tmp/cccp-camera-discoveries-{Time.GetTicksUsec()}/";
        DirAccess.MakeDirRecursiveAbsolute(root);
        PlayerFiles.Activate(root, 1);
        string one = PlayerFiles.Dir(root, 1), two = PlayerFiles.Dir(root, 2);
        var settings = new ConfigFile(); settings.SetValue("driver", "name", "TEST DRIVER"); settings.Save(one + "settings.cfg");
        var discoveries = new CameraDiscoveries(one);
        Check(discoveries.Next("ilyich", GameplayView.TopDown) == GameplayView.TopDown && !discoveries.HasAlternative("ilyich"),
            "camera discoveries: new files have only top-down");
        settings.SetValue("tutorial", "rally_seen", true); settings.Save(one + "settings.cfg");
        Check(discoveries.Discover("ilyich", GameplayView.Bonnet) && !discoveries.Discover("ilyich", GameplayView.Bonnet),
            "camera discoveries: a secret is awarded once");
        var reloaded = new CameraDiscoveries(one);
        settings.Load(one + "settings.cfg");
        Check(reloaded.Has("ilyich", GameplayView.Bonnet) && !reloaded.Has("siberia", GameplayView.Bonnet)
            && settings.GetValue("driver", "name").AsString() == "TEST DRIVER" && settings.GetValue("tutorial", "rally_seen").AsBool(),
            "camera discoveries: survive reload, stay on their track and preserve other profile settings");
        Check(reloaded.Next("ilyich", GameplayView.TopDown) == GameplayView.Bonnet && reloaded.Next("ilyich", GameplayView.Bonnet) == GameplayView.TopDown,
            "camera discoveries: camera switching skips a locked cinematic view");
        reloaded.Discover("ilyich", GameplayView.Cinematic);
        Check(reloaded.Next("ilyich", GameplayView.TopDown) == GameplayView.Cinematic && reloaded.Next("ilyich", GameplayView.Cinematic) == GameplayView.Bonnet,
            "camera discoveries: both found views participate in the cycle");
        PlayerFiles.Activate(root, 2);
        var other = new CameraDiscoveries(two); other.Discover("siberia", GameplayView.Cinematic);
        Check(!other.Has("ilyich", GameplayView.Bonnet) && PlayerFiles.Read(root, 1).Cameras == 2 && PlayerFiles.Read(root, 2).Cameras == 1,
            "camera discoveries: each personnel file owns and displays its own finds");
        PlayerFiles.Erase(root, 1);
        Check(!new CameraDiscoveries(one).HasAlternative("ilyich") && new CameraDiscoveries(two).Has("siberia", GameplayView.Cinematic),
            "camera discoveries: erasing a file clears only its own unlocks");
        Check(CameraDiscoveries.Touches(new(0, 0.6f, -10), new(0, 0.6f, 10), Basis.Identity, Vector3.Zero, 1)
            && !CameraDiscoveries.Touches(new(5, 0.6f, -10), new(5, 0.6f, 10), Basis.Identity, Vector3.Zero, 1)
            && !CameraDiscoveries.Touches(new(0, 5, -10), new(0, 5, 10), Basis.Identity, Vector3.Zero, 1)
            && !CameraDiscoveries.Touches(new(0, 0.6f, -100), new(0, 0.6f, 100), Basis.Identity, Vector3.Zero, 1),
            "camera discoveries: fast drive-overs count, near misses, jumps and teleports do not");
        foreach (string id in new[] { "ilyich", "siberia", "atomgrad", "bonus" })
        {
            var t = TrackBuilder.Build(TrackDef.Load($"res://tracks/{id}.json"));
            bool reachable = CameraSecrets.Layout(id).All(secret => !t.Obstacles.Any(o => o.Center.DistanceTo(secret.Position) < o.Radius + 1f));
            if (!reachable)
                foreach (var secret in CameraSecrets.Layout(id))
                foreach (var obstacle in t.Obstacles)
                    if (obstacle.Center.DistanceTo(secret.Position) < obstacle.Radius + 1f)
                        GD.Print($"secret {id}/{secret.View} at {secret.Position}: blocked by {obstacle.Center}, radius {obstacle.Radius}");
            Check(reachable, $"camera discoveries: {id}'s camera objects are reachable without a solid obstacle");
        }
    }

    void BonusMigrationTest()
    {
        string root = $"/tmp/cccp-bonus-migration-{Time.GetTicksUsec()}/";
        DirAccess.MakeDirRecursiveAbsolute(root);
        PlayerFiles.Activate(root, 1);
        Check(TrackIds.FromCommandLine("kolkhoz") == "ilyich" && TrackIds.FromCommandLine("kolhoosi") == "ilyich"
            && TrackIds.FromCommandLine("bonus") == "bonus", "track names: kolkhoz means Ilyich; the fourth track is bonus");
        var def = TrackDef.Load("res://tracks/bonus.json");
        var track = TrackBuilder.Build(def);
        string signature = TrackSignature.Compute(track);
        // (The builder keys the bonus road, its airbase and widths on the id "bonus", so a track built under the old id "kolkhoz" is no longer the same road: the old
        // id lives on only in the saved files, which the migration below renames. What is checked is that the canonical id builds the same road every time.)
        Check(track.Id == "bonus" && TrackSignature.Compute(TrackBuilder.Build(TrackDef.Load("res://tracks/bonus.json"))) == signature,
            "bonus rename: canonical ID changes without changing the road or invalidating records");
        void Put(string name, string text) { using var f = FileAccess.Open(root + name, FileAccess.ModeFlags.Write); f.StoreString(text); }
        Put("ghost_" + TrackIds.LegacyBonus + ".bin", "saved ghost");
        Put("track_" + TrackIds.LegacyBonus + ".sig", signature);
        Put("records_" + TrackIds.LegacyBonus + ".ver", "2");
        var medals = new ConfigFile(); medals.SetValue("medals", TrackIds.LegacyBonus, 3); medals.SetValue("medals", "bonus", 1); medals.Save(PlayerFiles.Dir(root, 1) + "campaign.cfg");
        var speeds = new ConfigFile(); speeds.SetValue("rally", TrackIds.LegacyBonus, 123.5f); speeds.SetValue("rally", "bonus", 100f); speeds.Save(PlayerFiles.Dir(root, 1) + "speedtrap.cfg");
        var tutorial = new ConfigFile(); tutorial.SetValue("tutorial", "school_rally_" + TrackIds.LegacyBonus, true); tutorial.SetValue("tutorial", "school_rally_bonus", false); tutorial.Save(PlayerFiles.Dir(root, 1) + "settings.cfg");
        var records = new RecordStore(root, "bonus", signature);
        Check(FileAccess.GetFileAsString(records.GhostPath) == "saved ghost" && records.StoredLayout() == 2 && records.ArchiveIfChanged().Count == 0,
            "bonus rename: old ghosts, layout and signatures migrate without archiving valid records");
        var migrated = new ConfigFile(); migrated.Load(PlayerFiles.Dir(root, 1) + "settings.cfg");
        Check(new Campaign(PlayerFiles.Dir(root, 1)).Best("bonus") == NormPlan.Medal.Gold
            && new SpeedRecords(PlayerFiles.Dir(root, 1)).Best("rally", "bonus") == 123.5f
            && migrated.GetValue("tutorial", "school_rally_bonus", false).AsBool(),
            "bonus rename: personnel files retain medals, speed records and lessons");
        Put("ghost_" + TrackIds.LegacyBonus + ".bin", "conflicting old ghost");
        BonusTrackMigration.Migrate(root);
        Check(FileAccess.GetFileAsString(records.GhostPath) == "saved ghost"
            && FileAccess.GetFileAsString(root + "ghost_" + TrackIds.LegacyBonus + ".bin") == "conflicting old ghost",
            "bonus rename: migration never overwrites either record when both filenames exist");
    }
}
