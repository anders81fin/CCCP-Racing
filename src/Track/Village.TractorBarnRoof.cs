using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class Village
{
    /// <summary>Thin aged zinc sheets over timber gables, with standing seams and finished edges.</summary>
    public static Node3D TractorBarnRoof()
    {
        var roof = new Node3D { Name = "AgedMetalRoof" };
        var zinc = new Color(0.48f, 0.48f, 0.46f);
        var edge = new Color(0.39f, 0.39f, 0.37f);
        var wood = new Color(0.30f, 0.21f, 0.14f);
        var gable = new Color(0.43f, 0.21f, 0.15f);
        const float Half = 4.5f, Eave = 4.25f, Ridge = 7.25f, End = 6.9f;
        float angle = Mathf.RadToDeg(MathF.Atan2(Ridge - Eave, Half));
        float slopeLength = MathF.Sqrt(Half * Half + (Ridge - Eave) * (Ridge - Eave));
        void B(Vector3 size, Color color, Vector3 at, Vector3? rotation = null) =>
            roof.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at, rotation));
        foreach (float end in new[] { -1f, 1f })
        {
            roof.AddChild(Props.Part(new PrismMesh { Size = new Vector3(8f, 3f, 0.18f) },
                gable, new Vector3(0, 5.75f, end * 6.47f)));
            for (float x = -3.5f; x <= 3.5f; x += 0.7f)
            {
                float h = 3f * (1 - MathF.Abs(x) / 4f);
                B(new Vector3(0.075f, h, 0.045f), wood,
                    new Vector3(x, Eave + h / 2, end * 6.58f));
            }
        }
        foreach (float side in new[] { -1f, 1f })
        {
            var rot = new Vector3(0, 0, -side * angle);
            for (int sheet = 0; sheet < 12; sheet++)
            {
                float z = -End + (sheet + 0.5f) * (End * 2 / 12);
                Color tone = sheet % 7 == 2 ? zinc.Darkened(0.06f)
                    : sheet % 5 == 0 ? zinc.Lightened(0.025f) : zinc;
                B(new Vector3(slopeLength, 0.10f, End * 2 / 12 + 0.015f), tone,
                    new Vector3(side * Half / 2, (Eave + Ridge) / 2, z), rot);
            }
            for (int seam = 0; seam <= 12; seam++)
                B(new Vector3(slopeLength, 0.065f, 0.065f), edge,
                    new Vector3(side * Half / 2, (Eave + Ridge) / 2 + 0.085f,
                        -End + seam * (End * 2 / 12)), rot);
            foreach (float end in new[] { -1f, 1f })
            {
                B(new Vector3(slopeLength + 0.12f, 0.24f, 0.16f), wood,
                    new Vector3(side * Half / 2, (Eave + Ridge) / 2 - 0.09f, end * (End + 0.03f)), rot);
                B(new Vector3(slopeLength + 0.15f, 0.045f, 0.23f), edge,
                    new Vector3(side * Half / 2, (Eave + Ridge) / 2 + 0.085f, end * (End + 0.04f)), rot);
            }
            B(new Vector3(0.15f, 0.24f, End * 2 + 0.2f), wood,
                new Vector3(side * Half, Eave - 0.14f, 0));
            B(new Vector3(0.19f, 0.10f, End * 2 + 0.2f), edge,
                new Vector3(side * (Half + 0.03f), Eave + 0.005f, 0));
            // Repairs and narrow rust traces stay subordinate to the dull zinc.
            float px = side * 2.9f, pz = side < 0 ? -2.6f : 3.2f;
            B(new Vector3(1.25f, 0.035f, 1.6f), zinc.Darkened(0.12f),
                new Vector3(px, Ridge - MathF.Abs(px) * 3f / Half + 0.085f, pz), rot);
            foreach (float z in new[] { -4.6f, 2.3f })
                B(new Vector3(0.75f, 0.025f, 0.12f), new Color(0.36f, 0.29f, 0.22f),
                    new Vector3(side * 4.08f, Ridge - 4.08f * 3f / Half + 0.075f, z), rot);
        }
        // Folded ridge flashing rather than a wooden bar over the sheets.
        foreach (float side in new[] { -1f, 1f })
            B(new Vector3(0.38f, 0.065f, End * 2 + 0.25f), edge,
                new Vector3(side * 0.14f, Ridge + 0.05f, 0), new Vector3(0, 0, -side * angle));
        DrunkTractor.BatchStatic(roof, null);
        return roof;
    }
}
