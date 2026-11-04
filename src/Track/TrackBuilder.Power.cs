using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class TrackBuilder
{
    // A small underground-fed pole transformer at the northern farm supplies the village.
    // The distribution line follows the populated lane, rather than circling the race course.
    static void PlaceVillagePower(TrackData t, SpeedTrapDef gai, List<Vector2> occupied)
    {
        Vector2[] route = {
            new(133, 25), new(110, 29), new(87, 33), new(65, 43),
            new(48, 61), new(33, 81), new(26, 103), new(26, 129),
            new(26, 157), new(26, 183), new(25, 210), new(26, 237)
        };
        var trunk = new List<int>();
        for (int i = 0; i < route.Length; i++)
        {
            Vector2 p = route[i];
            if (NearWater(t, p, 3f)) throw new InvalidOperationException("Village power pole is in water.");
            Vector2 direction = i + 1 < route.Length ? route[i + 1] - p : p - route[i - 1];
            trunk.Add(t.Props.Count);
            AddProp(t, "pole", p, TrackData.YawFor(direction.Normalized()), i == 0 ? "transformer" : null, occupied);
            if (i > 0) Connect(trunk[i - 1], trunk[i]);
        }

        int farmEast = Pole(new Vector2(154, 25), trunk[0]);

        // East-side houses and the grain store share a branch on their own side of the lane.
        int east = Pole(new Vector2(51, 103), trunk[6]);
        east = Pole(new Vector2(51, 130), east);
        int lampFeed = east;
        east = Pole(new Vector2(51, 156), east);
        east = Pole(new Vector2(51, 166), east);
        Pole(new Vector2(51, 230), trunk[^1]);

        int buildingCount = t.Props.Count;
        for (int i = 0; i < buildingCount; i++)
        {
            var p = t.Props[i];
            Vector3 anchor;
            switch (p.Type)
            {
                case "izba": anchor = new Vector3(0, 5.6f, 3.15f); break;
                case "church": anchor = new Vector3(-3.6f, 5.2f, 0); break;
                case "barn": anchor = new Vector3(-4.12f, 4.1f, -3); break;
                case "silo": anchor = new Vector3(0, 5.4f, -2.65f); break;
                case "dormitory": anchor = new Vector3(14f, 3.9f, Village.DormDepth / 2 + 0.1f); break;
                case "startbarn": anchor = new Vector3(-p.Size - 0.35f, 6.2f, 0); break;
                default: continue;
            }
            t.PowerAnchors[i] = anchor;
            Vector3 target = p.Position + new Basis(Vector3.Up, p.Rotation) * anchor;
            int closest = -1;
            float best = float.MaxValue;
            // Only actual distribution poles, never an unrelated building, can feed a service.
            for (int j = 0; j < t.Props.Count; j++)
                if (t.Props[j].Type == "pole")
                {
                    float d = t.Props[j].Position.DistanceSquaredTo(target);
                    if (d < best) { best = d; closest = j; }
                }
            Connect(closest, i);
        }

        foreach (var stop in t.BusStops)
        {
            var basis = new Basis(Vector3.Up, stop.Yaw);
            Vector3 offset = basis * new Vector3(-3.1f, 0, 0.6f);
            Vector2 pos = stop.Position + new Vector2(offset.X, offset.Z);
            if (NearWater(t, pos, 2f)) continue;
            int lamp = t.Props.Count;
            AddProp(t, "streetlamp", pos, stop.Yaw, null, occupied);
            t.PowerAnchors[lamp] = new Vector3(0, 5.5f, 0);
            Connect(lampFeed, lamp);
        }

        if (gai != null)
        {
            // Follow the north verge of the opening straight, then cross once at full height.
            // The last pole is behind the booth, clear of the inspector and the road.
            int feed = farmEast;
            // In front of the big barn and its long dormitory wing, between them and the road.
            foreach (var p in new Vector2[] { new(160, 40), new(184, 38), new(208, 37.5f), new(232, 38), new(249, 40), new(249, 72) })
                feed = Pole(p, feed);
            var booth = SpeedTrap.BoothTransform(t, gai);
            int service = t.Props.Count;
            AddProp(t, "gaiservice", new Vector2(booth.Origin.X, booth.Origin.Z), booth.Basis.GetEuler().Y, null, occupied);
            t.PowerAnchors[service] = new Vector3(0, 4.7f, 1.04f);
            Connect(feed, service);
            // A continuous branch follows the inside of the eastern road to the party rest home.
            foreach (var p in new Vector2[] { new(260, 92), new(264, 116), new(264, 140), new(260, 159), new(246, 160) })
                feed = Pole(p, feed);
            int dacha = t.Props.FindIndex(p => p.Type == "partydacha");
            if (dacha >= 0)
            {
                t.PowerAnchors[dacha] = new Vector3(9.5f, 8.0f, -4.5f);
                Connect(feed, dacha);
            }
        }

        int Pole(Vector2 p, int from)
        {
            if (NearWater(t, p, 3f)) throw new InvalidOperationException("Village power branch is in water.");
            int index = t.Props.Count;
            var a = t.Props[from].Position;
            AddProp(t, "pole", p, TrackData.YawFor((p - new Vector2(a.X, a.Z)).Normalized()), null, occupied);
            Connect(from, index);
            return index;
        }

        void Connect(int a, int b)
        {
            t.PowerSpans.Add((a, b));
            Vector3 start = t.Props[a].Position, end = t.Props[b].Position;
            // Keep trees out of the entire wire corridor, including service drops.
            int steps = Math.Max(1, (int)MathF.Ceiling(start.DistanceTo(end) / 3f));
            for (int k = 0; k <= steps; k++)
            {
                Vector3 p = start.Lerp(end, k / (float)steps);
                occupied.Add(new Vector2(p.X, p.Z));
            }
        }
    }
}
