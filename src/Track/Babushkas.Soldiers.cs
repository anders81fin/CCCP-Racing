using Godot;
namespace CCCPRacing.Track;

public partial class Babushkas
{
    /// <summary>Bonus-only supply troops, keeping the same walker pivots and canister interactions.</summary>
    void BuildSoldier(Walker w)
    {
        Color cloth=w.Index%3==0?new(.31f,.35f,.21f):new(.25f,.29f,.17f);
        Color webbing=new(.47f,.43f,.29f),boots=new(.07f,.075f,.065f),metal=new(.19f,.21f,.17f);
        w.Scarf=cloth;
        w.Root=new Node3D {Name="SupplyTrooper"+w.Index,Scale=Vector3.One*SizeScale};AddChild(w.Root);
        w.Body=new Node3D();w.Root.AddChild(w.Body);
        void Box(Node3D parent,Vector3 size,Color c,Vector3 at) => parent.AddChild(Props.Part(Props.Box(size.X,size.Y,size.Z),c,at));
        foreach(float side in new[]{-1f,1f}) {
            var leg=new Node3D {Position=new(side*.14f,.78f,0)};w.Body.AddChild(leg);
            if(side<0)w.LegL=leg;else w.LegR=leg;
            Box(leg,new(.18f,.61f,.20f),cloth,new(0,-.31f,0));
            Box(leg,new(.21f,.23f,.31f),boots,new(0,-.665f,-.045f));
            Box(leg,new(.18f,.07f,.21f),webbing,new(0,-.52f,0));
        }
        var torso=new Node3D {Position=new(0,.8f,0)};w.Body.AddChild(torso);
        Box(torso,new(.49f,.54f,.32f),cloth,new(0,.27f,0));
        Box(torso,new(.51f,.07f,.34f),webbing,new(0,.06f,0));
        Box(torso,new(.10f,.075f,.025f),metal,new(0,.06f,-.18f));
        for(int k=0;k<4;k++) Box(torso,new(.022f,.025f,.025f),webbing,new(0,.15f+k*.09f,-.17f));
        foreach(float side in new[]{-1f,1f}) {
            Box(torso,new(.14f,.12f,.035f),cloth.Lightened(.1f),new(side*.14f,.34f,-.18f));
            Box(torso,new(.045f,.49f,.025f),webbing,new(side*.15f,.27f,-.20f));
            Box(torso,new(.13f,.15f,.10f),webbing,new(side*.23f,.1f,-.13f));
            Box(torso,new(.14f,.025f,.14f),webbing,new(side*.20f,.56f,0));
        }
        torso.AddChild(Props.Part(Props.Ball(.13f,10),Skin,new(0,.67f,-.025f)));
        if(w.Index%3!=1) {
            var helmet=Props.Part(Props.Ball(.165f,12),cloth,new(0,.76f,0));helmet.Scale=new(1,.68f,1.1f);torso.AddChild(helmet);
            Box(torso,new(.34f,.035f,.34f),cloth,new(0,.72f,-.02f));
        } else {
            Box(torso,new(.29f,.10f,.27f),cloth,new(0,.80f,0));
            Box(torso,new(.29f,.035f,.15f),metal,new(0,.755f,-.17f));
        }
        foreach(float eye in new[]{-.052f,.052f}) Box(torso,new(.026f,.025f,.018f),boots,new(eye,.68f,-.145f));
        Box(torso,new(.045f,.045f,.04f),Skin.Lightened(.08f),new(0,.63f,-.15f));
        Box(torso,new(.045f,.045f,.02f),new(.65f,.1f,.055f),new(0,.78f,-.17f));
        w.ArmL=new Node3D {Position=new(-.30f,.50f,0)};w.ArmR=new Node3D {Position=new(.30f,.50f,0)};
        torso.AddChild(w.ArmL);torso.AddChild(w.ArmR);
        foreach(var arm in new[]{w.ArmL,w.ArmR}) {
            Box(arm,new(.14f,.48f,.15f),cloth,new(0,-.24f,0));
            Box(arm,new(.145f,.05f,.16f),webbing,new(0,-.46f,0));
            arm.AddChild(Props.Part(Props.Ball(.075f,8),Skin,new(0,-.53f,0)));
        }
        if(w.Index%2==0) {
            Box(w.ArmL,new(.25f,.24f,.15f),metal,new(0,-.67f,0));
            Box(w.ArmL,new(.11f,.04f,.045f),webbing,new(0,-.53f,0));
        }
        if(w.Index%3==0) {
            Box(torso,new(.24f,.31f,.12f),metal,new(0,.3f,.23f));
            Box(torso,new(.02f,.45f,.02f),boots,new(.09f,.64f,.23f));
        }
        w.GoldVest=new Node3D {Visible=false};torso.AddChild(w.GoldVest);
        foreach(float side in new[]{-1f,1f}) Box(w.GoldVest,new(.075f,.48f,.025f),new(.83f,.71f,.18f),new(side*.14f,.28f,-.22f));
        Box(w.GoldVest,new(.47f,.07f,.025f),new(.83f,.71f,.18f),new(0,.19f,-.23f));
        w.Carrier=w.Index%2==1;
        w.Can=BuildCan();w.Can.Position=new(0,-.66f,0);w.ArmR.AddChild(w.Can);
        w.GoldCan=BuildCan(true);w.GoldCan.Position=w.Can.Position;w.GoldCan.Visible=false;w.ArmR.AddChild(w.GoldCan);
    }
}
