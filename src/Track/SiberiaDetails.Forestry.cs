using System;
using Godot;

namespace CCCPRacing.Track;

public partial class SiberiaDetails
{
    static readonly Color WoodBark = new(0.28f, 0.20f, 0.14f), WoodEnd = new(0.79f, 0.64f, 0.43f);
    static readonly Color YardSteel = new(0.22f, 0.25f, 0.25f), YardSnow = new(0.93f, 0.95f, 0.98f);

    static void AddForestry(TrackData t, TrackDef def, ActivityLayout plan)
    {
        // Reproduce PlaceLogPiles' accepted anchor before LogPiles itself is populated.
        LogPileDef source = null;
        foreach (var pile in def.LogPiles)
            if (Math.Abs(pile.At - 200) < 0.1f && pile.Side == "right") { source = pile; break; }
        if (source == null) return;
        int i = ((int)source.At % t.Center.Length + t.Center.Length) % t.Center.Length;
        Vector2 along = t.CenterDir(i), outward = new(-along.Y, along.X);
        bool banked = t.Banks != null && t.Banks.Closed(i, false) && t.Banks.Taper(i, false) > 0.9f;
        float lat = banked ? t.Banks.Inner(i, false) + Snowbanks.Width + 1.4f + source.Out
            : t.HalfWidth[i] + 2.5f + source.Out;
        Vector2 pileCenter = t.Center[i] + outward * lat;
        Vector2 bench = pileCenter + outward * 5.4f;
        float yaw = TrackData.YawFor(outward);
        plan.Reserve(bench + along * 1.8f, 7f);
        plan.AddProp("forestry-trestle", bench, yaw, SawingTrestle,
            (new Vector2(0, -0.7f), 0.7f), (new Vector2(0, 0.7f), 0.7f));
        plan.AddSaw("forestry-crosscut", bench, yaw);
        foreach (int side in new[] { -1, 1 })
        {
            Vector2 p = bench + along * (side * 1.65f);
            plan.Workers.Add(new WorkerSpec(side < 0 ? "forestry-sawyer-a" : "forestry-sawyer-b",
                WinterJob.Saw, p, p, TrackData.YawFor(-along * side), side < 0 ? 0 : 1));
        }
        Vector2 sled = bench + along * 4.8f + outward * 0.8f;
        plan.AddProp("forestry-sled", sled, TrackData.YawFor(along), WoodSled,
            (new Vector2(0, -0.6f), 0.8f), (new Vector2(0, 0.6f), 0.8f));
        // The loaded journey stays on the far side of both the saw and the old pile.
        Vector2 pickup = bench + outward * 2.4f + along * 0.8f;
        Vector2 delivery = pickup + along * 3.4f;
        plan.Workers.Add(new WorkerSpec("forestry-carrier", WinterJob.CarryWood,
            pickup, delivery, TrackData.YawFor(along), 2, 22, 6));
        plan.AddProp("forestry-cut-wood", pickup + outward * 1.7f, yaw, SplitWoodHeap,
            (Vector2.Zero, 0.7f));
    }

    static void AddCamp(TrackData t, TrackDef def, ActivityLayout plan)
    {
        if (def.Gulag == null) return;
        GulagCamp.HutSite? selected = null;
        // Use the second right-hand barrack, leaving the first hut's mechanic undisturbed.
        foreach (var hut in GulagCamp.Huts(t, def.Gulag))
            if (hut.Side < 0 && hut.Offset == 36) { selected = hut; break; }
        if (selected is not { } site) return;
        Vector2 along = t.CenterDir(site.Index), outward = new(-along.Y, along.X);
        int gapIndex = (site.Index - 11 + t.Center.Length) % t.Center.Length;
        Vector2 gapDir = t.CenterDir(gapIndex), gapOut = new(-gapDir.Y, gapDir.X);
        Vector2 yard = t.Center[gapIndex] + gapOut * 19.1f;
        float yaw = TrackData.YawFor(gapDir);
        plan.Reserve(yard, 7.5f);
        plan.AddProp("camp-chopping-block", yard, yaw, ChoppingBlock, (Vector2.Zero, 0.48f));
        Vector2 chopper = yard - gapDir * 1.25f;
        plan.Workers.Add(new WorkerSpec("camp-chopper", WinterJob.Chop,
            chopper, chopper, TrackData.YawFor(gapDir), 3, 7, 1.4f));
        Vector2 shed = t.Center[gapIndex] + gapOut * 21.9f;
        // Its open front faces the road; fence remains behind the shallow roof.
        plan.AddProp("camp-woodshed", shed, TrackData.YawFor(-gapOut), WoodShelter,
            (new Vector2(-0.85f, 0), 0.9f), (new Vector2(0.85f, 0), 0.9f));
        plan.AddProp("camp-wood-sled", yard - gapDir * 4.3f, yaw, WoodSled,
            (new Vector2(0, -0.6f), 0.8f), (new Vector2(0, 0.6f), 0.8f));
        Vector2 carryA = yard + gapDir * 2.1f;
        Vector2 carryB = carryA + gapDir * 3.3f + gapOut * 0.9f;
        plan.Workers.Add(new WorkerSpec("camp-wood-carrier", WinterJob.CarryWood,
            carryA, carryB, TrackData.YawFor(carryB - carryA), 4, 24, 10));
        // A rear service door on the selected barrack, away from the road-side repair bench.
        // Its z=-along coordinate is near the east end; the worker clears its short step apron.
        Vector2 door = site.Position - along * 6.3f + outward * 3.05f;
        plan.AddProp("camp-service-step", door, TrackData.YawFor(outward), CampServiceStep,
            (new Vector2(0, -0.5f), 0.6f));
        Vector2 shovelA = door + outward * 1.9f - along * 0.2f;
        Vector2 shovelB = shovelA + along * 1.1f;
        plan.Workers.Add(new WorkerSpec("camp-step-shoveller", WinterJob.Shovel,
            shovelA, shovelB, TrackData.YawFor(-outward), 5, 19, 4));
    }

    static void YardLog(Node3D root, Vector3 center, float radius, float length, float yaw = 0)
    {
        var log = new Node3D { Position = center, Rotation = new Vector3(0, yaw, 0) };
        root.AddChild(log);
        log.AddChild(Props.Part(Props.Cyl(radius, length, 7), WoodBark, Vector3.Zero, new Vector3(90, 0, 0)));
        foreach (float side in new[] { -1f, 1f })
        {
            log.AddChild(Props.Part(Props.Cyl(radius * 0.96f, 0.015f, 7), WoodEnd,
                new Vector3(0, 0, side * (length / 2 + 0.008f)), new Vector3(90, 0, 0)));
            log.AddChild(Props.Part(Props.Cyl(radius * 0.64f, 0.018f, 7), WoodEnd * 0.87f,
                new Vector3(0, 0, side * (length / 2 + 0.018f)), new Vector3(90, 0, 0)));
        }
    }

    static Node3D SawingTrestle()
    {
        var n = new Node3D();
        foreach (float z in new[] { -0.65f, 0.65f })
        {
            foreach (float s in new[] { -1f, 1f })
                n.AddChild(Props.Part(Props.Box(0.13f, 1.45f, 0.13f), WoodBark,
                    new Vector3(s * 0.26f, 0.65f, z), new Vector3(0, 0, s * 24)));
            n.AddChild(Props.Part(Props.Box(0.7f, 0.12f, 0.15f), WoodEnd * 0.7f, new Vector3(0, 0.35f, z)));
        }
        n.AddChild(Props.Part(Props.Box(0.13f, 0.14f, 1.6f), WoodBark, new Vector3(0, 0.65f, 0)));
        YardLog(n, new Vector3(0, 1.25f, 0), 0.24f, 2.6f);
        DrunkTractor.BatchStatic(n, null);
        return n;
    }

    static Node3D WoodSled()
    {
        var n = new Node3D();
        foreach (float x in new[] { -0.52f, 0.52f })
        {
            n.AddChild(Props.Part(Props.Box(0.13f, 0.14f, 2.6f), YardSteel, new Vector3(x, 0.13f, 0.1f)));
            n.AddChild(Props.Part(Props.Box(0.13f, 0.14f, 0.65f), YardSteel,
                new Vector3(x, 0.27f, -1.4f), new Vector3(-30, 0, 0)));
            foreach (float z in new[] { -0.75f, 0.75f })
                n.AddChild(Props.Part(Props.Box(0.11f, 0.4f, 0.11f), WoodBark, new Vector3(x, 0.4f, z)));
        }
        for (int k = 0; k < 6; k++)
            n.AddChild(Props.Part(Props.Box(1.3f, 0.1f, 0.25f), WoodEnd * 0.65f,
                new Vector3(0, 0.54f, -0.83f + k * 0.33f)));
        for (int row = 0; row < 2; row++)
            for (int k = 0; k < 3; k++) YardLog(n, new Vector3((k - 1) * 0.34f, 0.72f + row * 0.29f, 0), 0.16f, 1.5f);
        n.AddChild(Props.Part(Props.Box(1.15f, 0.06f, 0.5f), YardSnow, new Vector3(0, 0.59f, 0.85f)));
        DrunkTractor.BatchStatic(n, null);
        return n;
    }

    static Node3D SplitWoodHeap()
    {
        var n = new Node3D();
        for (int k = 0; k < 5; k++)
            YardLog(n, new Vector3((k % 3 - 1) * 0.26f, 0.16f + k / 3 * 0.24f, 0), 0.13f, 0.85f, k % 2 * 0.16f);
        DrunkTractor.BatchStatic(n, null);
        return n;
    }

    static Node3D ChoppingBlock()
    {
        var n = new Node3D();
        n.AddChild(Props.Part(Props.Cyl(0.43f, 0.7f, 8), WoodBark, new Vector3(0, 0.35f, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.41f, 0.025f, 8), WoodEnd, new Vector3(0, 0.71f, 0)));
        n.AddChild(Props.Part(Props.Cyl(0.15f, 0.42f, 6), WoodEnd * 0.8f, new Vector3(0, 0.94f, 0)));
        foreach (float x in new[] { -0.45f, 0.5f })
            YardLog(n, new Vector3(x, 0.12f, 0.3f), 0.1f, 0.5f, x);
        DrunkTractor.BatchStatic(n, null);
        return n;
    }

    static Node3D WoodShelter()
    {
        var n = new Node3D();
        foreach (float x in new[] { -1.35f, 1.35f })
        foreach (float z in new[] { -0.75f, 0.75f })
            n.AddChild(Props.Part(Props.Box(0.14f, 2.2f, 0.14f), WoodBark, new Vector3(x, 1.1f, z)));
        for (int k = 0; k < 5; k++)
            n.AddChild(Props.Part(Props.Box(2.8f, 0.17f, 0.1f), WoodEnd * 0.65f, new Vector3(0, 0.4f + k * 0.32f, 0.75f)));
        n.AddChild(Props.Part(Props.Box(3.1f, 0.16f, 1.95f), WoodBark, new Vector3(0, 2.18f, 0), new Vector3(-8, 0, 0)));
        n.AddChild(Props.Part(Props.Box(3.2f, 0.17f, 2.05f), YardSnow, new Vector3(0, 2.32f, 0), new Vector3(-8, 0, 0)));
        for (int row = 0; row < 4; row++)
        for (int k = 0; k < 7 - row % 2; k++)
            YardLog(n, new Vector3(-1.05f + k * 0.32f + row % 2 * 0.16f, 0.2f + row * 0.28f, 0.15f), 0.15f, 1.1f);
        DrunkTractor.BatchStatic(n, null);
        return n;
    }

    static Node3D CampServiceStep()
    {
        var n = new Node3D();
        // Local front -Z points away from the wall, into the service yard.
        n.AddChild(Props.Part(Props.Box(0.95f, 1.85f, 0.065f), WoodBark * 0.7f, new Vector3(0, 0.93f, 0)));
        n.AddChild(Props.Part(Props.Box(1.14f, 0.12f, 0.12f), WoodEnd * 0.7f, new Vector3(0, 1.9f, -0.04f)));
        n.AddChild(Props.Part(Props.Box(0.08f, 0.3f, 0.12f), YardSteel, new Vector3(0.34f, 0.95f, -0.08f)));
        n.AddChild(Props.Part(Props.Box(1.18f, 0.22f, 0.55f), WoodBark, new Vector3(0, 0.11f, -0.3f)));
        n.AddChild(Props.Part(Props.Box(1.35f, 0.11f, 0.45f), WoodBark * 0.8f, new Vector3(0, 0.055f, -0.76f)));
        n.AddChild(Props.Part(Props.Box(0.35f, 0.04f, 0.5f), YardSnow, new Vector3(-0.38f, 0.24f, -0.3f)));
        DrunkTractor.BatchStatic(n, null);
        n.AddChild(NightGlow.Part(Props.Box(.28f, .18f, .18f), new Color(.6f, .5f, .3f),
            new Color(1f, .66f, .25f), 1.2f, new Vector3(0, 2.08f, -.16f)));
        var porch = new OmniLight3D
        {
            Name = "DoorLight", Position = new Vector3(0, 2f, -.55f),
            LightColor = new Color(1f, .69f, .34f), OmniRange = 4f, LightEnergy = 0,
            ShadowEnabled = true, LightSpecular = 0, Visible = false,
        };
        porch.SetMeta("night_energy", .8f);
        n.AddChild(porch);
        return n;
    }
}
