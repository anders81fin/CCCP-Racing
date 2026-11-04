using System.Collections.Generic;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Small abandoned cameras off the racing line. Ilyich's bonnet reward belongs to the live duck instead.</summary>
public static class CameraSecrets
{
    public readonly record struct Spot(GameplayView View, Vector2 Position);
    public static IReadOnlyList<Spot> Layout(string track) => track switch
    {
        "ilyich" => new[] { new Spot(GameplayView.Cinematic, new(56, 164)) },
        "siberia" => new[] { new Spot(GameplayView.Bonnet, new(188, 265)), new Spot(GameplayView.Cinematic, new(260, 156)) },
        "atomgrad" => new[] { new Spot(GameplayView.Bonnet, new(155, 249)), new Spot(GameplayView.Cinematic, new(86, 145)) },
        "bonus" => new[] { new Spot(GameplayView.Bonnet, new(94, 48)), new Spot(GameplayView.Cinematic, new(266, 159)) },
        _ => System.Array.Empty<Spot>()
    };
    public static Node3D Build(TrackData track)
    {
        var root = new Node3D { Name = "CameraSecrets" };
        foreach (var spot in Layout(track.Id))
        {
            var camera = new Node3D { Name = spot.View.ToString(),
                Position = new Vector3(spot.Position.X, track.MeshHeight(spot.Position.X, spot.Position.Y), spot.Position.Y) };
            var dark = new Color(0.12f, 0.14f, 0.13f); var metal = new Color(0.57f, 0.59f, 0.54f);
            camera.AddChild(Props.Part(Props.Box(0.64f, 0.4f, 0.28f), dark, new Vector3(0, 0.65f, 0)));
            camera.AddChild(Props.Part(Props.Box(0.66f, 0.08f, 0.3f), metal, new Vector3(0, 0.89f, 0)));
            camera.AddChild(Props.Part(Props.Cyl(0.16f, 0.18f, 12), metal, new Vector3(0.1f, 0.66f, -0.2f), new Vector3(90, 0, 0)));
            camera.AddChild(Props.Part(Props.Cyl(0.12f, 0.02f, 12), new Color(0.12f, 0.3f, 0.32f), new Vector3(0.1f, 0.66f, -0.3f), new Vector3(90, 0, 0)));
            foreach (float angle in new[] { 0f, 120f, 240f })
            {
                var leg = Props.Part(Props.Box(0.04f, 0.55f, 0.04f), metal, new Vector3(0, 0.26f, 0));
                leg.RotationDegrees = new Vector3(18, angle, 0); camera.AddChild(leg);
            }
            DrunkTractor.BatchStatic(camera, null);
            root.AddChild(camera);
        }
        return root;
    }
}
