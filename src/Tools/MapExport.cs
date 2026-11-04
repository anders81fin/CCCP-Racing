using System.Text.Json;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Tools;

/// <summary>
/// Renders the whole track area straight from above (orthographic, 10 px per metre) and dumps the
/// track data as JSON for tools/map/annotate_map.py.
/// --data-only exports geometry without a viewport (also works headless).
/// Run (needs a window/GPU unless data-only): godot-mono --path . res://scenes/tools/MapExport.tscn -- --out=docs/map [--track=bonus]
/// </summary>
public partial class MapExport : Node
{
    const int PxPerMetre = 10;
    int _frames;
    SubViewport _vp;
    TrackData _t;
    string _out = "docs/map", _track = "bonus";

    public override void _Ready()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--out=")) _out = a["--out=".Length..];
            else if (a.StartsWith("--track=")) _track = a["--track=".Length..];

        var def = TrackDef.Load($"res://tracks/{_track}.json");
        _t = TrackBuilder.Build(def);
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--data-only") >= 0)
        { ExportData(); return; }
        int size = (int)(_t.WorldSize * PxPerMetre);

        _vp = new SubViewport
        {
            Size = new Vector2I(size, size),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa4X,
            OwnWorld3D = true,
        };
        AddChild(_vp);
        var root = new Node3D();
        _vp.AddChild(root);
        root.AddChild(TerrainMesh.Build(_t));
        root.AddChild(Props.Build(_t));
        root.AddChild(WaterMesh.Build(_t));
        root.AddChild(Fences.Build(_t));
        root.AddChild(Airbase.BuildRunway(_t));
        var veg = new Vegetation();
        root.AddChild(veg);
        veg.Init(_t, null);
        root.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.2f, 0.2f, 0.2f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.62f, 0.6f, 0.6f),
                AmbientLightEnergy = 0.9f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 0.9f };
        root.AddChild(sun);
        sun.GlobalTransform = new Transform3D(Basis.LookingAt(-TerrainMesh.SunDir, Vector3.Up), Vector3.Zero);

        // Straight down, -Z (the game's "up the screen") at the top of the image, +X to the right.
        float half = _t.WorldSize / 2f;
        var cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = _t.WorldSize,
            Near = 1f,
            Far = 500f,
            Current = true,
        };
        root.AddChild(cam);
        cam.GlobalTransform = new Transform3D(Basis.LookingAt(Vector3.Down, Vector3.Forward), new Vector3(half, 200f, half));
    }

    public override void _Process(double delta)
    {
        if (++_frames < 10) return;   // let the render target settle
        string dir = ProjectSettings.GlobalizePath("res://").PathJoin(_out);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        _vp.GetTexture().GetImage().SavePng(dir.PathJoin($"{_track}_render.png"));

        ExportData();
    }

    void ExportData()
    {
        string dir = ProjectSettings.GlobalizePath("res://").PathJoin(_out);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var heights = new float[_t.Heights.Length];
        System.Array.Copy(_t.Heights, heights, heights.Length);
        var centre = new float[_t.Center.Length][];
        for (int i = 0; i < _t.Center.Length; i++)
            centre[i] = new[] { _t.Center[i].X, _t.Center[i].Y, _t.CenterH[i], _t.HalfWidth[i], (float)_t.CenterSurf[i] };
        var def = TrackDef.Load($"res://tracks/{_track}.json");
        var props = new System.Collections.Generic.List<object>();
        foreach (var p in _t.Props)
            props.Add(new { type = p.Type, x = p.Position.X, z = p.Position.Z, text = p.Text });
        var data = new
        {
            name = _t.Name,
            worldSize = _t.WorldSize,
            n = _t.N,
            tile = _t.Tile,
            pxPerMetre = PxPerMetre,
            heights,
            centre,
            surfaces = System.Enum.GetNames(typeof(SurfaceType)),
            points = def.Points,
            features = def.Features,
            props,
        };
        System.IO.File.WriteAllText(dir.PathJoin($"{_track}_data.json"),
            JsonSerializer.Serialize(data, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        GD.Print($"Map exported to {dir}");
        GetTree().Quit();
    }
}
