using System;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Track;

public partial class HindSorties
{
    /// <summary>Mi-24 silhouette: stepped tandem bubbles, broad cabin, stub wings and five-blade rotor.</summary>
    public static Node3D BuildModel(string number = "24")
    {
        var root = new Node3D { Name = "Mi24Hind" };
        var body = new Node3D { Name = "Airframe" }; root.AddChild(body);
        Color olive = new(.25f,.29f,.17f), sand = new(.52f,.43f,.29f), dark = new(.08f,.10f,.09f);
        Color metal = new(.29f,.31f,.28f), glass = new(.065f,.12f,.13f), belly = new(.48f,.51f,.46f);
        Color Paint(Vector3 p)
        {
            if(p.Y<1.15f+.12f*MathF.Sin(p.Z*.7f)) return belly;
            // Broad wrapping blotches: the brown ground and olive islands carry the silhouette.
            float waves=MathF.Sin(p.Z*1.65f+p.Y*.95f+MathF.Sin(p.X*2.1f))
                +.48f*MathF.Sin(p.Z*3.1f-p.Y*2.4f+p.X*.8f);
            return waves>.1f?olive:sand;
        }
        void Part(Node3D parent, Mesh mesh, Color colour, Vector3 at, Vector3? rotation = null) =>
            parent.AddChild(Props.Part(mesh,(colour==olive || colour==sand)?Paint(at):colour,at,rotation));
        void Ellipsoid(Color colour, Vector3 at, Vector3 scale)
        {
            var node = Props.Part(Props.Ball(1,16),colour,at); node.Scale=scale; body.AddChild(node);
        }
        void Rod(Node3D parent, Vector3 a, Vector3 b, float thickness, Color colour)
        {
            var node=Props.Part(Props.Box(thickness,thickness,a.DistanceTo(b)),colour,(a+b)*.5f);
            node.Basis=Basis.LookingAt((b-a).Normalized(),MathF.Abs((b-a).Normalized().Y)>.99f?Vector3.Right:Vector3.Up);
            parent.AddChild(node);
        }
        // Broad armoured cabin with flat side panels and a tapering nose and aft section.
        body.AddChild(MilitaryShape.Loft(new[] {
            new Vector3(0,1.9f,-5.7f),new Vector3(0,2.1f,-3.1f),new Vector3(0,2.1f,0),
            new Vector3(0,2.1f,2.5f),new Vector3(0,2.4f,4.7f)},
            new[] {new Vector2(.45f,.6f),new Vector2(1.15f,1.05f),new Vector2(1.35f,1.25f),
            new Vector2(1.13f,1.1f),new Vector2(.38f,.45f)},sand,Paint));
        Ellipsoid(belly,new(0,1.25f,-.2f),new(1.12f,.55f,3.9f));
        // Two separate stepped canopies: gunner in front, pilot behind and above.
        foreach(var seat in new[]{new Vector3(0,2.05f,-5.0f),new Vector3(0,2.95f,-3.05f)})
        {
            body.AddChild(MilitaryShape.Loft(new[] {seat+new Vector3(0,-.35f,-1.45f),seat+new Vector3(0,-.25f,-.55f),seat+new Vector3(0,-.25f,.65f),seat+new Vector3(0,-.30f,1.35f)},
                new[] {new Vector2(.34f,.37f),new Vector2(.84f,.55f),new Vector2(.82f,.55f),new Vector2(.48f,.35f)},sand,Paint));
            body.AddChild(MilitaryShape.Loft(new[] {seat+new Vector3(0,.22f,-1.20f),seat+new Vector3(0,.25f,-.55f),seat+new Vector3(0,.25f,.55f),seat+new Vector3(0,.12f,1.0f)},
                new[] {new Vector2(.30f,.28f),new Vector2(.70f,.65f),new Vector2(.68f,.61f),new Vector2(.40f,.35f)},glass));
            foreach(float side in new[]{-1f,1f})
                Part(body,Props.Box(.09f,.22f,1.7f),olive,new(side*.72f,seat.Y-.1f,seat.Z),new(0,side*4,0));
            Rod(body,seat+new Vector3(0,.95f,-.95f),seat+new Vector3(0,.95f,.85f),.095f,olive);
            foreach(float side in new[]{-1f,1f})
            {
                Rod(body,seat+new Vector3(side*.7f,.05f,-.75f),seat+new Vector3(side*.55f,.78f,.2f),.095f,olive);
                Rod(body,seat+new Vector3(side*.55f,.78f,.2f),seat+new Vector3(side*.7f,.05f,.7f),.095f,olive);
            }
        }
        // Armoured combat airframe, engines and short swept weapon wings.
        foreach(float side in new[]{-1f,1f})
        {
            body.AddChild(MilitaryShape.Loft(new[] {new Vector3(side*.77f,3.35f,-2.35f),new Vector3(side*.77f,3.4f,-1.9f),new Vector3(side*.77f,3.4f,.6f),new Vector3(side*.77f,3.25f,2.2f)},
                new[] {new Vector2(.43f,.43f),new Vector2(.56f,.55f),new Vector2(.54f,.53f),new Vector2(.35f,.35f)},sand,Paint));
            Part(body,Props.Cyl(.43f,.14f,12),dark,new(side*.77f,3.4f,-2.31f),new(90,0,0));
            Part(body,Props.Cyl(.36f,.75f,12),metal,new(side*1.05f,3.1f,2),new(75,side*25,0));
            Part(body,Props.Box(2.9f,.16f,1.55f),sand,new(side*2.3f,2,.4f),new(0,side*-12,side*-10));
            for(int station=0;station<2;station++)
            {
                float x=side*(2.2f+station*1.05f);
                Part(body,Props.Box(.16f,.45f,.9f),metal,new(x,1.65f,.5f));
                Part(body,Props.Cyl(.31f,1.9f,12),olive,new(x,1.18f,.4f),new(90,0,0));
                Part(body,Props.Cyl(.28f,.03f,12),metal,new(x,1.18f,-.57f),new(90,0,0));
                for(int tube=0;tube<7;tube++)
                {
                    float angle=tube*MathF.Tau/6;
                    Part(body,Props.Cyl(.055f,.04f,6),dark,new(x+(tube==6?0:MathF.Cos(angle)*.17f),1.18f+(tube==6?0:MathF.Sin(angle)*.17f),-.6f),new(90,0,0));
                }
            }
            root.AddChild(Props.Part(Props.Ball(.14f,6),side<0?new Color(.8f,.05f,.03f):new Color(.05f,.6f,.13f),new(side*3.75f,2,.6f)));
        }
        foreach(float side in new[]{-1f,1f})
        {
            // Irregular field-camouflage panels, access latches and wing-root reinforcement.
            for(int panel=0;panel<4;panel++)
                Part(body,Props.Box(.035f,.34f,.61f),panel%2==0?sand.Darkened(.2f):olive.Darkened(.23f),new(side*1.345f,1.7f+(panel%2)*.58f,-.4f+panel*.64f),new(0,0,side*9));
            Part(body,Props.Box(.22f,.30f,1.45f),olive.Darkened(.22f),new(side*1.43f,2.0f,.4f));
            for(int latch=0;latch<4;latch++)
                Part(body,Props.Box(.07f,.10f,.13f),metal,new(side*1.39f,2.92f,-.55f+latch*.56f));
        }
        // Tapered rising tail boom, fin and tailplane.
        var boom=Props.Part(new CylinderMesh { TopRadius=.2f, BottomRadius=.5f, Height=6.7f, RadialSegments=12 },olive,new Vector3(0,2.8f,6.0f),new Vector3(80,0,0));
        body.AddChild(boom);
        for(int band=0;band<5;band++)
            Part(body,new CylinderMesh {TopRadius=.43f-band*.045f,BottomRadius=.47f-band*.045f,Height=.62f,RadialSegments=12},band%2==0?sand:olive,
                new(0,2.5f+band*.16f,3.4f+band*1.15f),new(80,0,0));
        Part(body,Props.Box(.18f,2.7f,1.4f),sand,new(0,4.2f,8.9f),new(-22,0,0));
        Part(body,Props.Box(3.6f,.12f,.9f),olive,new(0,3.3f,7.9f));
        Part(body,Props.Cyl(.22f,.65f,10),metal,new(0,4.05f,0));
        // Chin turret and four-barrel gun.
        var gun=new Node3D {Name="Gun",Position=new(0,1.25f,-5.45f)};root.AddChild(gun);
        var chin=Props.Part(Props.Ball(.4f,12),dark,Vector3.Zero);chin.Scale=new(1,.8f,1.12f);gun.AddChild(chin);
        for(int i=0;i<4;i++)
        {
            float a=i*MathF.Tau/4;
            Part(gun,Props.Cyl(.045f,1.15f,8),metal,new(MathF.Cos(a)*.09f,MathF.Sin(a)*.09f,-.70f),new(90,0,0));
        }
        Part(gun,Props.Cyl(.15f,.12f,10),metal,new(0,0,-1.1f),new(90,0,0));
        DrunkTractor.BatchStatic(gun,null);
        // Recessed intake grilles, exhaust mouths and engine access fasteners.
        foreach(float side in new[]{-1f,1f})
        {
            Part(body,new TorusMesh { InnerRadius=.32f,OuterRadius=.45f,Rings=16,RingSegments=8 },
                metal,new(side*.77f,3.4f,-2.4f),new(90,0,0));
            for(int vane=-2;vane<=2;vane++)
                Part(body,Props.Box(.055f,.57f,.035f),metal,new(side*.77f+vane*.115f,3.4f,-2.43f));
            Part(body,Props.Cyl(.28f,.035f,12),dark,new(side*1.21f,3.2f,2.36f),new(75,side*25,0));
            for(int vent=0;vent<7;vent++)
                Part(body,Props.Box(.43f,.035f,.065f),dark,new(side*.77f,3.88f,-.65f+vent*.21f));
            // Closed side armour; no passenger door furniture, windows or boarding step.
            for(int rivet=0;rivet<11;rivet++)
                Part(body,Props.Ball(.028f,6),metal,new(side*1.37f,1.45f,-.45f+rivet*.2f));
            Part(body,Props.Box(.12f,.30f,1.1f),olive,new(side*.74f,3.25f,2.45f));
            for(int slot=0;slot<6;slot++)
                Part(body,Props.Box(.14f,.035f,.08f),dark,new(side*.78f,3.4f,2.0f+slot*.17f));
            // Framed side armour at the gunner's feet and a forward sensor blister.
            Part(body,Props.Box(.08f,.52f,.95f),olive,new(side*.77f,1.75f,-4.85f),new(0,side*8,0));
            Rod(body,new(side*.48f,1.85f,-5.8f),new(side*.58f,1.9f,-6.65f),.035f,metal);
        }
        Ellipsoid(olive,new(.46f,1.65f,-6.05f),new(.25f,.24f,.3f));
        Part(body,Props.Cyl(.13f,.025f,10),glass,new(.46f,1.65f,-6.34f),new(90,0,0));
        Rod(body,new(0,3.4f,2.2f),new(0,4.0f,3.4f),.035f,metal);
        Rod(body,new(0,3.65f,5.1f),new(0,4.35f,5.4f),.025f,metal);
        Rod(body,new(0,.9f,1.5f),new(0,.45f,2.1f),.035f,metal);
        // Painted stars on the aft cabin, with a pale border.
        var markings=new SurfaceTool(); markings.Begin(Mesh.PrimitiveType.Triangles);
        void Star(float side,float radius,float offset,Color colour)
        {
            Vector3 centre=new(side*(1.18f+offset),2.25f,2.35f);
            for(int i=0;i<10;i++)
            {
                float a=i*MathF.Tau/10,b=(i+1)*MathF.Tau/10;
                float ra=i%2==0?radius:radius*.43f,rb=i%2==0?radius*.43f:radius;
                foreach(var p in new[]{centre,centre+new Vector3(0,MathF.Cos(a)*ra,MathF.Sin(a)*ra),centre+new Vector3(0,MathF.Cos(b)*rb,MathF.Sin(b)*rb)})
                { markings.SetColor(colour); markings.SetNormal(Vector3.Right*side); markings.AddVertex(p); }
            }
        }
        foreach(float side in new[]{-1f,1f})
        { Star(side,.52f,0,new(.88f,.84f,.66f)); Star(side,.43f,.008f,new(.72f,.06f,.035f)); }
        markings.SetMaterial(new StandardMaterial3D { VertexColorUseAsAlbedo=true,CullMode=BaseMaterial3D.CullModeEnum.Disabled,Roughness=1 });
        body.AddChild(new MeshInstance3D { Mesh=markings.Commit() });
        DrunkTractor.BatchStatic(body,null);
        var rotor=new Node3D { Name="Rotor",Position=new(0,4.45f,0) };root.AddChild(rotor);
        Part(rotor,Props.Cyl(.35f,.18f,10),metal,Vector3.Zero);
        for(int i=0;i<5;i++)
        {
            float angle=i*MathF.Tau/5;
            Part(rotor,Props.Box(.40f,.065f,7.7f),dark,new(MathF.Sin(angle)*4.5f,.03f,MathF.Cos(angle)*4.5f),new(0,Mathf.RadToDeg(angle),0));
            Vector3 radial=new(MathF.Sin(angle),0,MathF.Cos(angle));
            Rod(rotor,radial*.28f+Vector3.Down*.12f,radial*1.1f+Vector3.Up*.04f,.10f,metal);
            Rod(rotor,radial*.42f+Vector3.Down*.25f,radial*.9f,.045f,metal);
            Part(rotor,Props.Box(.41f,.07f,.32f),sand,radial*8.12f+Vector3.Up*.03f,new(0,Mathf.RadToDeg(angle),0));
        }
        DrunkTractor.BatchStatic(rotor,null);
        var tail=new Node3D { Name="TailRotor",Position=new(.45f,4.5f,9.0f) };root.AddChild(tail);
        for(int i=0;i<3;i++)
        {
            float a=i*MathF.Tau/3;
            Part(tail,Props.Box(.065f,.20f,1.45f),dark,new(0,MathF.Sin(a)*.85f,MathF.Cos(a)*.85f),new(-Mathf.RadToDeg(a),0,0));
        }
        DrunkTractor.BatchStatic(tail,null);
        var gear=new Node3D { Name="Gear" };root.AddChild(gear);
        foreach(var at in new[]{new Vector3(0,.38f,-3.7f),new Vector3(-1.25f,.42f,1.2f),new Vector3(1.25f,.42f,1.2f)})
        {
            var leg=new Node3D { Position=at+new Vector3(0,.9f,.2f) }; gear.AddChild(leg);
            Vector3 pivot=leg.Position;
            void GearPart(Mesh mesh,Color colour,Vector3 position,Vector3? rotation=null) => Part(leg,mesh,colour,position-pivot,rotation);
            void GearRod(Vector3 a,Vector3 b,float width,Color colour) => Rod(leg,a-pivot,b-pivot,width,colour);
            GearRod(at,at+new Vector3(0,.9f,.2f),.10f,metal);
            GearRod(at+new Vector3(0,.22f,0),at+new Vector3(0,.7f,-.16f),.065f,new(.61f,.63f,.59f));
            GearRod(at+new Vector3(0,.1f,.13f),at+new Vector3(0,.65f,.35f),.055f,metal);
            GearPart(Props.Cyl(.37f,.25f,12),dark,at,new(0,0,90));
            foreach(float side in new[]{-1f,1f})
                GearPart(Props.Cyl(.19f,.025f,10),metal,at+new Vector3(side*.14f,0,0),new(0,0,90));
            DrunkTractor.BatchStatic(leg,null);
        }
        var image=PixelFont.RenderImage(number,new Color(.93f,.89f,.73f),new Color(0,0,0,0),1);
        var texture=ImageTexture.CreateFromImage(image);
        foreach(float side in new[]{-1f,1f}) root.AddChild(new MeshInstance3D {
            Mesh=new QuadMesh { Size=new Vector2(1,.55f) },Position=new(side*1.38f,2.1f,-1.5f),RotationDegrees=new(0,side*90,0),
            MaterialOverride=new StandardMaterial3D { AlbedoTexture=texture,Transparency=BaseMaterial3D.TransparencyEnum.AlphaScissor,TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest } });
        gun.AddChild(new MeshInstance3D { Name="Flash",Visible=false,Mesh=Props.Ball(.3f,6),Position=new(0,0,-1.35f),
            MaterialOverride=NightGlow.Keep(new StandardMaterial3D { AlbedoColor=new Color(1,.7f,.2f),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded }) });
        var beacons=new JetBeacons { Name="Beacons" };
        beacons.Add(NightGlow.Part(Props.Ball(.12f,6),new(.7f,.05f,.03f),Colors.Red,3,new(0,4.05f,2.1f)));
        root.AddChild(beacons);
        MilitaryLights.Helicopter(root);
        return root;
    }
}
