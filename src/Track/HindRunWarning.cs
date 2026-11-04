using Godot;

namespace CCCPRacing.Track;

/// <summary>Temporary amber perimeter beacons show the locked firing crossing without covering the road.</summary>
public partial class HindRunWarning : Node3D
{
    float _age;
    readonly System.Collections.Generic.List<MeshInstance3D> _lamps=new();
    /// <summary>The world tape restores the warning by its age (its lamps blink by it, it ends at 14 s).</summary>
    public void SetAge(float age) => _age=age;
    public void Init(TrackData track,Vector2 left,Vector2 right)
    {
        foreach(Vector2 p in new[]{left,right})
        {
            Vector3 at=new(p.X,track.MeshHeight(p.X,p.Y),p.Y);
            AddChild(Props.Part(Props.Box(.10f,1.1f,.10f),new Color(.22f,.24f,.18f),at+Vector3.Up*.55f));
            var lamp=new MeshInstance3D {Mesh=Props.Ball(.24f,8),Position=at+Vector3.Up*1.18f,
                MaterialOverride=NightGlow.Keep(new StandardMaterial3D {AlbedoColor=new(1,.58f,.08f),
                    ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,EmissionEnabled=true,Emission=new(1,.4f,.02f),EmissionEnergyMultiplier=2})};
            AddChild(lamp);_lamps.Add(lamp);
        }
    }
    public override void _Process(double delta)
    {
        _age+=(float)delta;
        if(_age>14) {QueueFree();return;}
        foreach(var lamp in _lamps) lamp.Visible=(int)(_age*5)%2==0;
    }
}
