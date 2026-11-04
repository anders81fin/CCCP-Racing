using System;
using Godot;

namespace CCCPRacing.Track;

public static partial class Village
{
    static void AddKolkhozYard(Node3D barn, TrackData track, Vector3 origin, float rotation)
    {
        var basis = new Basis(Vector3.Up, rotation);
        float Ground(float x, float z)
        {
            Vector3 world = origin + basis * new Vector3(x, 0, z);
            return track.MeshHeight(world.X, world.Z) - origin.Y;
        }
        var storage = new Node3D { Name = "GrainPallets" };
        barn.AddChild(storage);
        // The stack sits inside the existing solid front-wall footprint, beside the door.
        float floor = Ground(-3.8f, 7.5f);
        void Box(Vector3 size, Color color, Vector3 at) => storage.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at));
        foreach (float x in new[] { -4.35f, -3.25f })
            Box(new Vector3(0.16f, 0.18f, 1.2f), BigTrim, new Vector3(x, floor + 0.09f, 7.5f));
        for (int i = 0; i < 5; i++)
            Box(new Vector3(1.65f, 0.10f, 0.18f), new Color(0.5f, 0.39f, 0.25f),
                new Vector3(-3.8f, floor + 0.23f, 7.02f + i * 0.24f));
        for (int layer = 0; layer < 3; layer++)
        for (int row = 0; row < 2; row++)
        for (int col = 0; col < (layer == 2 ? 1 : 2); col++)
        {
            Vector3 at = new(-4.15f + col * 0.7f + (layer == 2 ? 0.3f : 0),
                floor + 0.53f + layer * 0.48f, 7.2f + row * 0.6f);
            storage.AddChild(Props.Part(new SphereMesh { Radius = 0.34f, Height = 0.52f,
                RadialSegments = 8, Rings = 4 }, new Color(0.75f, 0.62f, 0.39f), at));
        }
        DrunkTractor.BatchStatic(storage, null);

        // Short terrain-following segments keep the two worn wheel tracks on the hillside.
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        surface.SetNormal(Vector3.Up);
        foreach (float side in new[] { -1f, 1f })
        for (int i = 0; i < 24; i++)
        {
            float z = 8.2f + i * 0.5f;
            float centre = side * 0.94f + MathF.Sin(z * 0.45f) * 0.06f;
            float width = 0.29f + 0.04f * MathF.Sin(z * 1.7f);
            surface.SetColor(i % 5 == 0 ? new Color(0.28f, 0.22f, 0.13f) : new Color(0.35f, 0.28f, 0.17f));
            Vector3 P(float x, float zz) => new(x, Ground(x, zz) + 0.045f, zz);
            Vector3 a = P(centre - width, z), b = P(centre + width, z);
            Vector3 c = P(centre + width, z + 0.5f), d = P(centre - width, z + 0.5f);
            surface.AddVertex(a); surface.AddVertex(c); surface.AddVertex(b);
            surface.AddVertex(a); surface.AddVertex(d); surface.AddVertex(c);
        }
        var tracks = Props.Part(surface.Commit(), Colors.White, Vector3.Zero);
        tracks.Name = "TractorWheelTracks";
        ((StandardMaterial3D)tracks.MaterialOverride).VertexColorUseAsAlbedo = true;
        tracks.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        barn.AddChild(tracks);
    }
}
