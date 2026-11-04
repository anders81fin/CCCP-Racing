using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class Village
{
    // Weathered kolkhoz timber, as on the cover art: faded red-brown planks, dark seams, dark tarred
    // frames and doors (no light Scandinavian trim), a grey shingle roof.
    static readonly Color BigWall = new(0.47f, 0.18f, 0.12f);
    static readonly Color BigPlank = new(0.33f, 0.12f, 0.08f);
    static readonly Color BigRoof = new(0.33f, 0.31f, 0.3f);
    static readonly Color BigTrim = new(0.22f, 0.13f, 0.09f);
    static readonly Color BigDoor = new(0.24f, 0.11f, 0.07f);
    static readonly Color SignCream = new(0.93f, 0.84f, 0.6f);

    /// <summary>
    /// The big red kolkhoz barn from the cover art: a 10 x 16 m weathered plank barn under a steep gable
    /// roof, dark corner boards and door frame, and a sliding gable door under a hayloft hatch.
    /// The joined version receives its roof from the dormitory junction. Gable and doors face local +Z.
    /// </summary>
    public static Node3D BigBarn(TrackData track, Vector3 origin, float rotation, bool joinedRoof = false)
    {
        var n = new Node3D();
        const float W = 10f, L = 16f, H = 4.2f, Ridge = 9f;
        void Add(Mesh m, Color c, Vector3 at, Vector3? rot = null) => n.AddChild(Props.Part(m, c, at, rot));
        void B(float x, float y, float z, Color c, Vector3 at, Vector3? rot = null) => Add(Props.Box(x, y, z), c, at, rot);

        // Walls, the gable fill and a steep (about 45 degree) roof of two sheets with dark eave and rake boards.
        B(W, H, 0.25f, BigWall, new Vector3(0, H / 2, -L / 2 + 0.125f));
        foreach (float side in new[] { -1f, 1f })
        {
            B(0.25f, H, L - 0.25f, BigWall, new Vector3(side * (W / 2 - 0.125f), H / 2, 0.125f));
            B(2.4f, H, 0.25f, BigWall, new Vector3(side * 3.8f, H / 2, L / 2 - 0.125f));
        }
        B(5.2f, 0.25f, 0.25f, BigWall, new Vector3(0, H - 0.125f, L / 2 - 0.125f));
        Add(new PrismMesh { Size = new Vector3(W - 0.05f, Ridge - H, L - 0.1f) }, BigWall, new Vector3(0, H + (Ridge - H) / 2, 0));
        float roofA = Mathf.RadToDeg(MathF.Atan2(Ridge - H, W / 2 + 0.4f));
        float roofLen = MathF.Sqrt(MathF.Pow(W / 2 + 0.5f, 2) + MathF.Pow(Ridge - H, 2)) + 0.1f;
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 at = new(s * (W / 2 + 0.4f) / 2, (H + Ridge) / 2 + 0.12f, 0);
            if (!joinedRoof) B(roofLen, 0.22f, L + 0.9f, BigRoof, at, new Vector3(0, 0, -s * roofA));
            foreach (float z in new[] { -L / 2 - 0.45f, L / 2 + 0.45f })
                B(roofLen, 0.24f, 0.12f, BigTrim, at + new Vector3(0, 0, z + Math.Sign(z) * 0.02f), new Vector3(0, 0, -s * roofA));
            B(0.2f, 0.22f, L + 0.9f, BigTrim, new Vector3(s * (W / 2 + 0.35f), H - 0.15f, 0));     // eave board
        }
        B(0.4f, 0.22f, L + 1f, BigRoof.Darkened(0.3f), new Vector3(0, Ridge + 0.12f, 0));   // ridge cap

        // Dark corner boards and a base course, vertical plank seams on the long walls.
        foreach (float x in new[] { -W / 2, W / 2 })
        foreach (float z in new[] { -L / 2, L / 2 })
            B(0.3f, H, 0.3f, BigTrim, new Vector3(x, H / 2, z));
        foreach (float s in new[] { -1f, 1f })
        {
            for (float z = -6.5f; z <= 6.6f; z += 1.3f)
                B(0.08f, H - 0.4f, 0.1f, BigPlank, new Vector3(s * 5.04f, H / 2, z));
            B(0.12f, 0.3f, L, BigTrim.Darkened(0.2f), new Vector3(s * 5.04f, 0.15f, 0));
        }

        // Gable (+Z): plank seams, a big sliding door on a rail with a cream frame, a hayloft hatch with hoist beam.
        float fz = L / 2 + 0.05f;
        for (float x = -4.5f; x <= 4.51f; x += 0.9f)
            if (MathF.Abs(x) > 2.7f) B(0.09f, H - 0.4f, 0.08f, BigPlank, new Vector3(x, H / 2, fz));
        n.AddChild(new BarnDoors(5.1f, 3.9f, fz, BigDoor, BigPlank));
        foreach (float x in new[] { -2.6f, 2.6f })
            B(0.3f, 3.95f, 0.2f, BigTrim, new Vector3(x, 1.98f, fz + 0.1f));
        B(5.5f, 0.3f, 0.22f, BigTrim, new Vector3(0, 4.0f, fz + 0.1f));
        B(6.4f, 0.14f, 0.16f, Timber, new Vector3(0, 4.35f, fz + 0.2f));
        n.AddChild(NightGlow.Part(Props.Box(0.5f, 0.14f, 0.3f), BigTrim,
            new Color(1f, 0.73f, 0.36f), 1.4f, new Vector3(0, 3.82f, fz + 0.4f)));
        var porchLight = new OmniLight3D { Position = new Vector3(0, 3.6f, fz + 0.65f),
            LightColor = new Color(1f, 0.74f, 0.43f), OmniRange = 9f, ShadowEnabled = false, LightEnergy = 0f };
        porchLight.SetMeta("night_energy", 1.7f);
        n.AddChild(porchLight);
        n.AddChild(Buildings.TextPanel("КОЛХОЗ", 3.4f, 0.5f, new Vector3(0, 4.75f, fz + 0.1f), Red, SignCream));
        B(1.5f, 1.7f, 0.1f, BigTrim, new Vector3(0, 6.2f, fz + 0.03f));
        B(1.2f, 1.4f, 0.14f, Timber, new Vector3(0, 6.2f, fz + 0.06f));
        B(0.08f, 1.4f, 0.16f, BigTrim, new Vector3(0, 6.2f, fz + 0.1f));
        B(1.2f, 0.08f, 0.16f, BigTrim, new Vector3(0, 6.2f, fz + 0.1f));
        B(0.14f, 0.14f, 1.0f, Timber, new Vector3(0, 7.35f, fz + 0.5f));                  // hay hoist beam
        // The back gable gets a loft hatch too.
        B(1.5f, 1.7f, 0.1f, BigTrim, new Vector3(0, 6.2f, -fz - 0.03f));
        B(1.2f, 1.4f, 0.14f, Timber, new Vector3(0, 6.2f, -fz - 0.06f));

        if (track.Id == "ilyich") AddKolkhozYard(n, track, origin, rotation);
        return n;
    }
}
