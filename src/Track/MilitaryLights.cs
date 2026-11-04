using System.Collections.Generic;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Night-only lamp glows and one inexpensive, shadowless beam per military vehicle.</summary>
public partial class MilitaryLights : Node3D
{
    readonly List<MeshInstance3D> _lamps=new();
    readonly List<SpotLight3D> _beams=new();
    readonly List<MeshInstance3D> _beamLamps=new();
    public bool Powered=true,BeamAllowed=true;
    public MilitaryLights() {Name="NightLights";}
    public MeshInstance3D Lamp(Vector3 at,Color color,float radius=.14f)
    {
        var lamp=new MeshInstance3D {Mesh=Props.Ball(radius,8),Position=at,Visible=false,
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride=NightGlow.Keep(new StandardMaterial3D {AlbedoColor=color,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded})};
        _lamps.Add(lamp);AddChild(lamp);return lamp;
    }
    public void Beam(Vector3 at,float dip,float range,float energy,float angle=32)
    {
        var beam=new SpotLight3D {Position=at,RotationDegrees=new(dip,0,0),SpotRange=range,SpotAngle=angle,
            SpotAttenuation=.7f,SpotAngleAttenuation=1.4f,LightColor=new(1,.91f,.7f),LightEnergy=0,
            LightSpecular=0,ShadowEnabled=false,Visible=false};
        beam.SetMeta("military_energy",energy);_beams.Add(beam);AddChild(beam);
    }
    public void UpdateLights(float level)
    {
        bool on=Powered && level>.01f;
        foreach(var lamp in _lamps) lamp.Visible=on && (BeamAllowed || !_beamLamps.Contains(lamp));
        foreach(var beam in _beams) {beam.Visible=on && BeamAllowed;beam.LightEnergy=beam.GetMeta("military_energy").AsSingle()*level;}
    }
    public override void _Process(double dt)=>UpdateLights(NightGlow.Level);
    public static MilitaryLights Vehicle(Node3D model,Vector3 front,Vector3 rear,float width,float range=26)
    {
        var lights=new MilitaryLights();model.AddChild(lights);
        foreach(float side in new[]{-1f,1f}) {
            lights.Lamp(front+Vector3.Right*(side*width),new(1,.94f,.75f));
            lights.Lamp(rear+Vector3.Right*(side*width),new(.75f,.045f,.025f),.10f);
        }
        lights.Beam(front+new Vector3(0,0,-.12f),-8,range,4);
        return lights;
    }
    public static MilitaryLights Helicopter(Node3D model)
    {
        var lights=new MilitaryLights();model.AddChild(lights);
        lights.Lamp(new(-3.75f,2,.6f),new(.95f,.035f,.02f),.17f);
        lights.Lamp(new(3.75f,2,.6f),new(.035f,.85f,.12f),.17f);
        lights.Lamp(new(0,3.5f,8.8f),new(.85f,.88f,.8f),.12f);
        lights._beamLamps.Add(lights.Lamp(new(.46f,1.65f,-6.4f),new(1,.94f,.78f),.13f));
        lights.Beam(new(.46f,1.4f,-6.5f),-58,42,7,30);
        return lights;
    }
}
