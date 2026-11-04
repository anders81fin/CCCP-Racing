using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public static partial class Village
{
    public static bool DormitoryJoinsBarn(PropInstance barn, Vector3 origin, float rotation)
    {
        Vector3 offset = new Basis(Vector3.Up, rotation).Inverse() * (barn.Position - origin);
        return MathF.Abs(barn.Rotation - rotation) < 0.001f
            && MathF.Abs(offset.X) < 0.05f && MathF.Abs(offset.Z) <= 2f;
    }

    /// <summary>The upper envelope of the perpendicular roofs; trimmed faces meet along two real valleys.</summary>
    static Node3D DormitoryRoofJoin(Vector3 barn, float length, float floor, float slope, float ridge)
    {
        var root = new Node3D { Name = "JoinedRoof" };
        const float BarnHalf = 5.5f, BarnPitch = 4.8f / 5.4f, DormHalf = DormDepth / 2 + 0.6f;
        float barnRidge = barn.Y + 9.2f;
        float BarnY(float x) => barnRidge - BarnPitch * MathF.Abs(x);
        float DormY(float x, float z) => floor + ridge + 0.20f + slope * x - 0.62f * MathF.Abs(z);
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);

        List<Vector3> Rectangle(float x0, float x1, float z0, float z1, Func<float, float, float> height) => new()
        {
            new(x0, height(x0, z0), z0), new(x1, height(x1, z0), z0),
            new(x1, height(x1, z1), z1), new(x0, height(x0, z1), z1),
        };
        List<Vector3> Clip(List<Vector3> polygon, Func<Vector3, float> distance)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector3 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                float da = distance(a), db = distance(b);
                if (da >= 0) result.Add(a);
                if ((da >= 0) != (db >= 0)) result.Add(a.Lerp(b, da / (da - db)));
            }
            return result;
        }
        void Face(List<Vector3> polygon)
        {
            // Keep the original material colour; vertex colours only darken selected strips.
            for (float z = -12; z < 12; z += 2.4f)
            {
                var strip = Clip(Clip(polygon, p => p.Z - z), p => z + 2.4f - p.Z);
                int band = (int)MathF.Round((z + 12) / 2.4f);
                float tone = band % 5 == 1 ? 0.96f : band % 4 == 0 ? 0.92f : 1f;
                surface.SetColor(new Color(tone, tone, tone));
                Triangles(strip);
            }
        }
        void Triangles(List<Vector3> polygon)
        {
            for (int i = 1; i + 1 < polygon.Count; i++)
            {
                Vector3 a = polygon[0], b = polygon[i], c = polygon[i + 1];
                Vector3 normal = (b - a).Cross(c - a);
                if (normal.LengthSquared() < 0.000001f) continue;
                if (normal.Y < 0) { (b, c) = (c, b); normal = -normal; }
                surface.SetNormal(normal.Normalized());
                // Godot front faces wind clockwise when viewed from outside.
                surface.AddVertex(a); surface.AddVertex(c); surface.AddVertex(b);
            }
        }
        void Cap(Vector3 a, Vector3 b, float width, float height, Color color)
        {
            Vector3 delta = b - a;
            if (delta.LengthSquared() < 0.000001f) return;
            var part = Props.Part(Props.Box(width, height, delta.Length()), color, (a + b) / 2);
            part.Quaternion = new Quaternion(Vector3.Back, delta.Normalized());
            root.AddChild(part);
        }

        float back = barn.Z - 8.45f, front = barn.Z + 8.45f;
        Face(Rectangle(-BarnHalf, 0, back, front, (x, z) => BarnY(x)));
        Face(Rectangle(0, BarnHalf, back, -DormHalf, (x, z) => BarnY(x)));
        Face(Rectangle(0, BarnHalf, DormHalf, front, (x, z) => BarnY(x)));
        foreach (float side in new[] { -1f, 1f })
        {
            var east = Rectangle(0, BarnHalf, 0, side * DormHalf, (x, z) => BarnY(x));
            Face(Clip(east, p => p.Y - DormY(p.X, p.Z)));
            var wing = Rectangle(0, BarnHalf, 0, side * DormHalf, DormY);
            var joined = Clip(wing, p => p.Y - BarnY(p.X));
            Face(joined);
            Face(Rectangle(BarnHalf, length + 0.4f, 0, side * DormHalf, DormY));
            var valley = joined.FindAll(p => MathF.Abs(p.Y - BarnY(p.X)) < 0.001f);
            if (valley.Count >= 2)
                Cap(valley[0] + Vector3.Up * 0.035f, valley[1] + Vector3.Up * 0.035f,
                    0.18f, 0.045f, DormPlinth.Darkened(0.35f));
        }
        var sheet = Props.Part(surface.Commit(), BigRoof, Vector3.Zero);
        // Duplicate the cached material so other white/roof meshes are unaffected.
        var roofMaterial = (StandardMaterial3D)sheet.MaterialOverride.Duplicate();
        roofMaterial.VertexColorUseAsAlbedo = true;
        sheet.MaterialOverride = roofMaterial;
        root.AddChild(sheet);

        // Four small repairs stay close to the dark grey-brown roofing palette.
        foreach (var patch in new[] { new Vector2(-3.2f, 5.8f), new Vector2(2.8f, 7.2f),
            new Vector2(length * 0.36f, 2.8f), new Vector2(length * 0.73f, -3.0f) })
        {
            float Y(float x, float z) => x <= BarnHalf && z >= back && z <= front
                ? MathF.Max(BarnY(x), x >= 0 && MathF.Abs(z) <= DormHalf ? DormY(x, z) : float.MinValue)
                : DormY(x, z);
            surface = new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            Triangles(Rectangle(patch.X - 0.65f, patch.X + 0.65f, patch.Y - 0.85f,
                patch.Y + 0.85f, (x, z) => Y(x, z) + 0.035f));
            root.AddChild(Props.Part(surface.Commit(), BigRoof.Darkened(0.13f), Vector3.Zero));
        }
        float joinX = Math.Clamp((barnRidge - floor - ridge - 0.20f) / (BarnPitch + slope), 0, BarnHalf);
        Cap(new Vector3(joinX, DormY(joinX, 0) + 0.08f, 0),
            new Vector3(length + 0.5f, DormY(length + 0.5f, 0) + 0.08f, 0),
            0.45f, 0.20f, BigRoof.Darkened(0.3f));
        return root;
    }
}
