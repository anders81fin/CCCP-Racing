using System;
using System.Linq;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;
namespace CCCPRacing.Tests;

/// <summary>Deterministic visual review of Bonus scenery, troops, impact effects and the live norm stamp.</summary>
public partial class BonusPolishReview : Node
{
    int _frame;
    TrackData _track;
    Camera3D _camera;
    Hud _hud;
    Vector3 _shell,_crash,_gai;
    public override void _Ready()
    {
        GetTree().Root.Size=new Vector2I(1280,800);
        AddChild(GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate());
        var def=TrackDef.Load("res://tracks/bonus.json");
        _track=TrackBuilder.Build(def);_gai=_track.CenterPos((int)def.SpeedTrap.At);
        _camera=Find<Camera3D>(this);_hud=Find<Hud>(this);
        _shell=Ground(138,43);_crash=Ground(142,143);
        RenderingServer.FramePreDraw+=Pose;RenderingServer.FramePostDraw+=Capture;
    }
    static T Find<T>(Node n) where T:Node
    {
        if(n is T found) return found;
        foreach(Node c in n.GetChildren()) {var match=Find<T>(c);if(match!=null)return match;}
        return null;
    }
    Node3D Trooper(Node n)
    {
        if(n is Node3D p && p.Name.ToString().StartsWith("SupplyTrooper"))return p;
        foreach(Node c in n.GetChildren()){var p2=Trooper(c);if(p2!=null)return p2;}
        return null;
    }
    Vector3 Ground(float x,float z)=>new(x,_track.MeshHeight(x,z),z);
    void Pose()
    {
        if(_camera==null)return;
        Vector3 eye=new(160,175,205),at=new(160,10,30);float fov=68;
        if(_frame>=110 && _frame<180) {eye=new(105,64,98);at=new(105,12,22);fov=62;}
        if(_frame>=180 && _frame<260) {at=Ground(161,90)+Vector3.Up;eye=at+new Vector3(8,9,14);fov=62;}
        if(_frame>=260 && _frame<330) {at=Ground(270,149)+Vector3.Up*1.4f;eye=at+new Vector3(-8,5,-10);fov=53;
            Find<BonusExerciseLife>(this)?.Tick(0,null,null,true);}
        if(_frame>=330 && _frame<365) {at=Ground(216,221)+Vector3.Up*2f;eye=at+new Vector3(8,6,-12);fov=48;}
        if(_frame>=365 && _frame<410 && Trooper(this) is {} soldier) {at=soldier.GlobalPosition+Vector3.Up*1.2f;eye=at+soldier.GlobalBasis.Orthonormalized()*new Vector3(3,2.8f,-5);fov=48;}
        if(_frame>=410 && _frame<540) {at=_shell;eye=at+new Vector3(10,10,15);fov=55;}
        if(_frame>=540 && _frame<680) {at=_crash;eye=at+new Vector3(18,17,26);fov=58;}
        if(_frame>=680 && _frame<750) {at=Ground(136,104);eye=at+new Vector3(12,36,46);fov=62;}
        if(_frame>=750) {
            _hud.EndNormMissCard();_hud.Clean=true;at=_gai;eye=at+new Vector3(24,31,35);fov=65;
        }
        _camera.GlobalPosition=eye;_camera.LookAt(at);_camera.Fov=fov;_camera.Far=650;_camera.ForceUpdateTransform();
        if(_frame>=680 && _frame<750) {_hud.Clean=false;_hud.CarOnScreen=new Vector2(256,210);}
    }
    void Capture()
    {
        _frame++;
        if(_frame==410) AddChild(GroundImpactEffects.Create(_track,_shell));
        if(_frame==540) {
            AddChild(GroundImpactEffects.Create(_track,_crash,true));
            var hinds=Find<HindSorties>(this);
            if(hinds!=null && hinds.HasMethod("BuildModel")) {
                var wreck=hinds.Call("BuildModel","42").As<Node3D>();AddChild(wreck);
                wreck.GlobalPosition=_crash+Vector3.Up*.8f;wreck.Rotation=new Vector3(.25f,-.7f,1.25f);
            }
        }
        if(_frame==680) _hud.MissNorm(78.25f,75,true,3);
        string tag=_frame switch {90=>"north",150=>"cliff",230=>"camp",300=>"buk",350=>"combine",380=>"trooper",420=>"shell-flash",465=>"shell-dust",560=>"hind-impact",635=>"hind-smoke",705=>"stamp",780=>"gai",_=>null};
        if(tag!=null) {GetTree().Root.GetTexture().GetImage().SavePng("/tmp/cccp-bonus-polish-"+tag+".png");GD.Print("review: "+tag);}
        if(_frame>=790)GetTree().Quit();
    }
    public override void _ExitTree() {RenderingServer.FramePreDraw-=Pose;RenderingServer.FramePostDraw-=Capture;}
}
