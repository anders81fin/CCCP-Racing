using System;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Track;

/// <summary>A well larger than life mallard sharing the party's pond with the rowing visitors.</summary>
public partial class PartyPondDuck : Node3D
{
    public const float ModelScale = 0.28f;
    public const float Radius = 3.6f * ModelScale;
    public const float Height = 3.4f * ModelScale;
    TrackData _track;
    Node3D _duck;
    float _clock, _tick;
    public bool Active => _duck != null;
    Node3D _camera;
    /// <summary>The bonnet camera standing on its tripod on the duck's back: seen until the player has found it.</summary>
    public void SetCameraVisible(bool visible) { if (_camera != null) _camera.Visible = visible; }
    public Vector3 WorldPosition => _duck?.GlobalPosition ?? Vector3.Zero;
    public static Vector2 PositionAt(float time) => new(220 + MathF.Cos(time * 0.045f + MathF.PI) * 5.5f,
        216 + MathF.Sin(time * 0.045f + MathF.PI));

    public static PartyPondDuck Build(TrackData track)
    {
        var root = new PartyPondDuck { Name = "PondMallard", _track = track };
        if (track.Id != "ilyich" || float.IsNaN(track.WaterLevel)) { root.SetProcess(false); return root; }
        root.BuildModel(); root.Animate(); return root;
    }

    void BuildModel()
    {
        _duck = new Node3D { Scale = Vector3.One * ModelScale }; AddChild(_duck);
        Color feather = new(0.52f, 0.46f, 0.33f), grey = new(0.48f, 0.49f, 0.43f);
        Color green = new(0.08f, 0.29f, 0.18f), brown = new(0.34f, 0.17f, 0.095f);
        Color white = new(0.82f, 0.80f, 0.64f), dark = new(0.075f, 0.085f, 0.065f);
        void Ball(Vector3 at, Vector3 scale, Color color)
        {
            var part = Props.Part(Props.Ball(1, 16), color, at); part.Scale = scale; _duck.AddChild(part);
        }
        void Box(Vector3 size, Vector3 at, Color color, Vector3 rotation = default) =>
            _duck.AddChild(Props.Part(Props.Box(size.X, size.Y, size.Z), color, at, rotation));
        Ball(new Vector3(0, 0.45f, 0.2f), new Vector3(1.65f, 0.95f, 2.35f), feather);
        Ball(new Vector3(0, 0.90f, -1.20f), new Vector3(1.15f, 1.05f, 1.0f), brown);
        Ball(new Vector3(0, 1.56f, -1.48f), new Vector3(0.62f, 1.12f, 0.66f), green);
        _duck.AddChild(Props.Part(Props.Cyl(0.65f, 0.15f, 16), white, new Vector3(0, 1.37f, -1.48f)));
        Ball(new Vector3(0, 2.59f, -1.77f), new Vector3(0.85f, 0.81f, 0.86f), green);
        Ball(new Vector3(0, 2.40f, -2.77f), new Vector3(0.61f, 0.19f, 0.81f), new Color(0.78f, 0.57f, 0.13f));
        Box(new Vector3(0.89f, 0.025f, 0.86f), new Vector3(0, 2.37f, -2.91f), brown);
        foreach (float side in new[] { -1f, 1f })
        {
            Ball(new Vector3(side * 1.36f, 0.74f, 0.40f), new Vector3(0.48f, 0.52f, 1.74f), grey);
            for (int i = 0; i < 4; i++)
                Ball(new Vector3(side * (1.45f - i * 0.06f), 0.94f + i * 0.07f, 0.68f + i * 0.26f), new Vector3(0.27f, 0.13f, 0.83f), feather.Lightened(i * 0.035f));
            Box(new Vector3(0.08f, 0.38f, 0.91f), new Vector3(side * 1.77f, 0.86f, 0.38f), white, new Vector3(0, 0, side * -12));
            Box(new Vector3(0.10f, 0.26f, 0.75f), new Vector3(side * 1.80f, 0.86f, 0.38f), new Color(0.12f, 0.24f, 0.49f), new Vector3(0, 0, side * -12));
            Ball(new Vector3(side * 0.73f, 2.74f, -2.12f), new Vector3(0.14f, 0.17f, 0.14f), new Color(0.59f, 0.45f, 0.15f));
            Ball(new Vector3(side * 0.80f, 2.75f, -2.14f), new Vector3(0.085f, 0.11f, 0.09f), dark);
        }
        foreach (float x in new[] { -0.5f, 0f, 0.5f })
            Ball(new Vector3(x, 0.65f, 2.42f), new Vector3(0.36f, 0.22f, 0.95f), dark);
        DrunkTractor.BatchStatic(_duck, null);
        // The easter-egg camera stands on its tripod on the mallard's back, lens forward (the duck's model is scaled down, so the camera is scaled up to a size that suits the bird).
        _camera = new Node3D { Name = GameplayView.Bonnet.ToString(), Position = new Vector3(0, 1.3f, 0.45f), Scale = Vector3.One * (0.75f / ModelScale) };
        CameraSecrets.AddCameraParts(_camera);
        DrunkTractor.BatchStatic(_camera, null);
        _duck.AddChild(_camera);
    }

    public override void _Process(double delta)
    {
        if (_duck == null) return;
        _clock += (float)delta; _tick += (float)delta;
        if (_tick < 1f / 12) return;
        _tick %= 1f / 12; Animate();
    }
    void Animate()
    {
        var p = PositionAt(_clock);
        float angle = _clock * 0.045f + MathF.PI;
        var direction = new Vector2(-5.5f * MathF.Sin(angle), MathF.Cos(angle));
        _duck.Position = new Vector3(p.X, _track.WaterLevel + MathF.Sin(_clock * 1.5f) * 0.018f, p.Y);
        _duck.Rotation = new Vector3(0, TrackData.YawFor(direction), MathF.Sin(_clock * 1.5f) * 0.025f);
    }
}
