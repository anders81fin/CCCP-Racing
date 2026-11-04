using System;
using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

public partial class WorldBackdrop
{
    static bool Exit(TrackData t, RiverDef river, int end, out Vector2 origin, out Vector2 direction)
    {
        origin = direction = default;
        if (river.Points == null || river.Points.Length < 2) return false;
        int i = end == 0 ? 0 : river.Points.Length - 1, next = end == 0 ? 1 : i - 1;
        origin = new Vector2(river.Points[i][0], river.Points[i][1]);
        direction = (origin - new Vector2(river.Points[next][0], river.Points[next][1])).Normalized();
        // Authored endpoints can lie a little beyond the grid (Siberia's south end is at 322).
        // Start the continuation at the actual edge, so its height and width meet the playable river.
        float back = 0;
        if (origin.X < 0 && direction.X < 0) back = Math.Max(back, origin.X / direction.X);
        if (origin.X > t.WorldSize && direction.X > 0) back = Math.Max(back, (origin.X - t.WorldSize) / direction.X);
        if (origin.Y < 0 && direction.Y < 0) back = Math.Max(back, origin.Y / direction.Y);
        if (origin.Y > t.WorldSize && direction.Y > 0) back = Math.Max(back, (origin.Y - t.WorldSize) / direction.Y);
        origin -= direction * back;
        if (origin.X < -.01f || origin.Y < -.01f || origin.X > t.WorldSize + .01f || origin.Y > t.WorldSize + .01f) return false;
        return (origin.X <= 0.01f && direction.X < 0) || (origin.Y <= 0.01f && direction.Y < 0)
            || (origin.X >= t.WorldSize - 0.01f && direction.X > 0) || (origin.Y >= t.WorldSize - 0.01f && direction.Y > 0);
    }

    // Match the exit tangent exactly, then ease into the valley's irregular course.
    // Only Ilyich's liquid river is reshaped; frozen/industrial continuations keep their layout.
    static Vector2 OuterCourse(TrackData t, Vector2 origin, Vector2 direction, float along,
        out Vector2 tangent, out float widthScale)
    {
        float bend = 0, derivative = 0; widthScale = 1;
        if (t.Id == "ilyich" && !t.Water.Frozen && along > 0)
        {
            float u = Math.Clamp(along / 20f, 0, 1), ease = u * u * (3 - 2 * u);
            float de = along < 20 ? 6 * u * (1 - u) / 20 : 0;
            bend = 12 * MathF.Sin(along / 30) * ease;
            derivative = .4f * MathF.Cos(along / 30) * ease + 12 * MathF.Sin(along / 30) * de;
            widthScale += .18f * MathF.Sin(along / 17) * ease;
        }
        var side = new Vector2(-direction.Y, direction.X);
        tangent = direction + side * derivative;
        return origin + direction * along + side * bend;
    }

    static bool RiverAt(TrackData t, Vector2 p, out float distance, out float width, out float depth)
    {
        distance = float.MaxValue; width = depth = 0;
        return RiverAt(t, p, out distance, out width, out depth, out _);
    }

    /// <summary>The river bed beyond the map edge, including frozen rivers at their exit height.</summary>
    static bool RiverAt(TrackData t, Vector2 p, out float distance, out float width, out float depth, out float level)
    {
        distance = float.MaxValue; width = depth = level = 0;
        if (t.Water == null || (!t.Water.Frozen && float.IsNaN(t.WaterLevel))) return false;
        foreach (var river in t.Water.Rivers)
        for (int end = 0; end < 2; end++)
        {
            if (!Exit(t, river, end, out var origin, out var direction)) continue;
            float along = Math.Max(0, (p - origin).Dot(direction));
            // Project onto the same curved course used by the water mesh and banks.
            int projectionSteps = t.Id == "ilyich" && !t.Water.Frozen ? 4 : 0;
            for (int step = 0; step < projectionSteps; step++)
            {
                var centre = OuterCourse(t, origin, direction, along, out var tangent, out _);
                along = Math.Max(0, along + (p - centre).Dot(tangent) / tangent.LengthSquared());
            }
            var nearest = OuterCourse(t, origin, direction, along, out _, out float scale);
            float d = p.DistanceTo(nearest);
            if (d >= distance) continue;
            distance = d; width = river.EndWidth(end) * scale; depth = t.Water.Frozen ? 0 : river.Depth;
            var edge = origin.Clamp(Vector2.Zero, Vector2.One * t.WorldSize);
            level = t.Water.Frozen ? t.MeshHeight(edge.X, edge.Y) : t.WaterLevel;
        }
        return distance < float.MaxValue;
    }

    MeshInstance3D BuildWater(TrackData t)
    {
        if (t.Water == null || t.Water.Frozen || float.IsNaN(t.WaterLevel)) return null;
        var mesh = new SurfaceTool(); mesh.Begin(Mesh.PrimitiveType.Triangles);
        bool any = false;
        foreach (var river in t.Water.Rivers)
        for (int end = 0; end < 2; end++)
        {
            if (!Exit(t, river, end, out var origin, out var direction)) continue;
            // The river meets the map edge on its authored tangent before gently bending.
            Vector2 outward = origin.X <= 0.01f ? Vector2.Left : origin.X >= t.WorldSize - 0.01f ? Vector2.Right
                : origin.Y <= 0.01f ? Vector2.Up : Vector2.Down;
            float boundary = origin.Dot(outward);
            float Edge(Vector2 p) => p.Dot(outward) - boundary;
            void Triangle(Vector2 a, Vector2 b, Vector2 c)
            {
                var polygon = new List<Vector2> { a, b, c };
                // Clip to both the map boundary and the continuing river bed's waterline.
                foreach (bool land in new[] { false, true })
                {
                    var result = new List<Vector2>(5);
                    float D(Vector2 p) => land ? t.WaterLevel - HeightAt(t, p) : Edge(p);
                    if (polygon.Count == 0) return;
                    var previous = polygon[^1]; float pd = D(previous);
                    foreach (var current in polygon)
                    {
                        float cd = D(current);
                        if ((pd >= 0) != (cd >= 0)) result.Add(previous.Lerp(current, pd / (pd - cd)));
                        if (cd >= 0) result.Add(current);
                        previous = current; pd = cd;
                    }
                    polygon = result;
                }
                void Vertex(Vector2 p)
                {
                    mesh.SetNormal(Vector3.Up);
                    mesh.SetUV2(new Vector2(0, Mathf.SmoothStep(100, 200, OutsideDistance(t, p))));
                    mesh.AddVertex(new Vector3(p.X, t.WaterLevel, p.Y));
                }
                for (int i = 1; i + 1 < polygon.Count; i++)
                {
                    Vertex(polygon[0]); Vertex(polygon[i]); Vertex(polygon[i + 1]); any = true;
                }
            }
            float rw = river.EndWidth(end);      // the river goes on past the map edge as wide as it leaves
            for (float along = -rw; along < Extent * 1.5f; along += 2)
            for (float across = -rw * 0.5f; across < rw * 0.5f; across += 1)
            {
                var centre0 = OuterCourse(t, origin, direction, along, out var tan0, out float scale0);
                var centre1 = OuterCourse(t, origin, direction, along + 2, out var tan1, out float scale1);
                var side0 = new Vector2(-tan0.Y, tan0.X).Normalized();
                var side1 = new Vector2(-tan1.Y, tan1.X).Normalized();
                var a = centre0 + side0 * (across * scale0);
                var b = centre1 + side1 * (across * scale1);
                var c = centre1 + side1 * ((across + 1) * scale1);
                var d = centre0 + side0 * ((across + 1) * scale0);
                if (Math.Min(OutsideDistance(t, a), OutsideDistance(t, c)) > Extent) continue;
                Triangle(a, b, c); Triangle(a, c, d);
            }
        }
        if (!any) return null;
        _water = new ShaderMaterial { Shader = new Shader { Code = WaterShader } };
        if (t.Id == "atomgrad")
        {
            _water.SetShaderParameter("industrial", true);
            _water.SetShaderParameter("surface_pattern", WaterMesh.IndustrialTexture());
        }
        SetLook(Colors.White, new Color(0.62f, 0.64f, 0.62f));
        return new MeshInstance3D { Name = "OuterRiver", Mesh = mesh.Commit(), MaterialOverride = _water, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }

    const string WaterShader = @"
shader_type spatial;
render_mode unshaded, cull_disabled;
uniform vec4 water_color : source_color = vec4(0.30, 0.44, 0.50, 0.62);
uniform vec4 horizon : source_color = vec4(0.62, 0.64, 0.62, 1.0);
uniform bool industrial = false;
uniform sampler2D surface_pattern : source_color, repeat_enable, filter_linear;
varying vec2 world_xz;
void vertex() {
    world_xz = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}
void fragment() {
    float fade = max(UV2.y, smoothstep(175.0, 245.0, length(VERTEX)));
    vec3 water = water_color.rgb;
    if (industrial) water *= texture(surface_pattern, world_xz * 0.0275).rgb;
    ALBEDO = mix(water, horizon.rgb, fade);
    ALPHA = mix(industrial ? water_color.a : 0.62, 1.0, fade);
}
";
}
