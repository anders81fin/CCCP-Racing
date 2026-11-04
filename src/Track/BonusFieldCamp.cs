using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Track;

/// <summary>Temporary army equipment: canvas, vehicles and portable fuel rather than permanent airbase buildings.</summary>
public static class BonusFieldCamp
{
    static readonly Color Canvas=new(.34f,.38f,.22f), Sand=new(.49f,.45f,.30f), Dark=new(.10f,.12f,.09f),
        Steel=new(.25f,.27f,.23f), Olive=new(.29f,.34f,.20f), Rubber=new(.065f,.07f,.06f);
    static void Part(Node3D n,Vector3 size,Color colour,Vector3 at,Vector3? rot=null) => n.AddChild(Props.Part(Props.Box(size.X,size.Y,size.Z),colour,at,rot));
    static void Rod(Node3D n,Vector3 a,Vector3 b,float width,Color colour)
    {
        var mesh=Props.Part(Props.Box(width,width,a.DistanceTo(b)),colour,(a+b)*.5f);
        mesh.Basis=Basis.LookingAt((b-a).Normalized(),MathF.Abs((b-a).Normalized().Y)>.99f?Vector3.Right:Vector3.Up);
        n.AddChild(mesh);
    }
    public static Node3D Tent(string text)
    {
        var root=new Node3D { Name="FieldTent" };
        Part(root,new(6,.08f,8.6f),Dark,new(0,.04f,0));
        Part(root,new(.1f,3.4f,.1f),Steel,new(0,1.7f,-4.1f));
        Part(root,new(.1f,3.4f,.1f),Steel,new(0,1.7f,4.1f));
        foreach(float side in new[]{-1f,1f})
        {
            Part(root,new(.08f,1.9f,8.6f),Canvas,new(side*3,.95f,0));
            Part(root,new(3.38f,.08f,8.9f),side<0?Canvas:Sand,new(side*1.5f,2.64f,0),new(0,0,side*-27));
            for(int i=0;i<4;i++)
            {
                float z=-3.8f+i*2.55f;
                Rod(root,new(side*3,1.85f,z),new(side*4.3f,.1f,z),.025f,Sand);
                Part(root,new(.10f,.35f,.10f),Steel,new(side*4.3f,.1f,z));
            }
            Part(root,new(1.9f,2.2f,.06f),Canvas,new(side*2.02f,1.1f,-4.3f));
            for(int i=0;i<3;i++) Part(root,new(.055f,.55f,.75f),Dark,new(side*3.05f,1.25f,-2.6f+i*2.6f));
        }
        Part(root,new(6,2.1f,.07f),Canvas,new(0,1.05f,4.3f));
        Part(root,new(1.9f,1.9f,.05f),Dark,new(0,.95f,-4.25f));
        Part(root,new(.6f,2.1f,.06f),Sand,new(-.8f,1.05f,-4.38f),new(0,15,-8));
        // Canvas end gables close the triangular ends below the ridge.
        var st=new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        foreach(float z in new[]{-4.3f,4.3f})
            foreach(var p in new[]{new Vector3(-3,1.9f,z),new Vector3(0,3.4f,z),new Vector3(3,1.9f,z)})
            { st.SetNormal(z<0?Vector3.Forward:Vector3.Back); st.AddVertex(p); }
        st.SetMaterial(new StandardMaterial3D { AlbedoColor=Canvas,CullMode=BaseMaterial3D.CullModeEnum.Disabled,Roughness=1 });
        root.AddChild(new MeshInstance3D { Mesh=st.Commit() });
        // Sewn roof panels, reinforced hems and laced door flaps.
        for(int i=0;i<7;i++) {
            float z=-4.15f+i*1.38f;
            foreach(float side in new[]{-1f,1f}) {
                Rod(root,new(0,3.45f,z),new(side*3.03f,1.94f,z),.035f,Sand);
                Part(root,new(.10f,.09f,8.65f),Sand,new(side*3.02f,1.9f,0));
            }
        }
        for(int i=0;i<8;i++) {
            float y=.18f+i*.21f;
            Rod(root,new(-1.03f,y,-4.40f),new(-.81f,y+.13f,-4.40f),.018f,Sand);
        }
        foreach(float side in new[]{-1f,1f}) for(int i=0;i<3;i++) {
            float z=-2.6f+i*2.6f;
            Part(root,new(.065f,.04f,.83f),Sand,new(side*3.06f,1.56f,z));
            Part(root,new(.07f,.55f,.035f),Sand,new(side*3.07f,1.25f,z));
            Part(root,new(.11f,.11f,.86f),Canvas,new(side*3.10f,1.60f,z));
        }
        // Folded roof patch, stove chimney, pallets and an extinguisher by the entrance.
        Part(root,new(.7f,.025f,.8f),Olive,new(-1.8f,2.53f,2.4f),new(0,0,27));
        root.AddChild(Props.Part(Props.Cyl(.09f,2.0f,10),Steel,new(1.7f,3.25f,2.7f)));
        Part(root,new(.38f,.06f,.38f),Steel,new(1.7f,4.29f,2.7f));
        for(int i=0;i<5;i++) Part(root,new(.19f,.045f,1.5f),Sand,new(-.5f+i*.24f,.12f,-5.03f));
        for(int i=0;i<2;i++) {
            Part(root,new(.9f,.55f,.65f),Sand,new(2.0f+i*.14f,.3f+i*.56f,-4.85f));
            Part(root,new(.055f,.57f,.67f),Steel,new(1.72f+i*.14f,.3f+i*.56f,-4.85f));
        }
        root.AddChild(Props.Part(Props.Cyl(.12f,.55f,10),new Color(.62f,.08f,.04f),new(-2.6f,.45f,-4.7f)));
        Part(root,new(.16f,.07f,.13f),Steel,new(-2.6f,.76f,-4.7f));
        DrunkTractor.BatchStatic(root,null);
        if(!string.IsNullOrEmpty(text))
        {
            var texture=ImageTexture.CreateFromImage(PixelFont.RenderImage(text,new(.85f,.80f,.60f),new(.16f,.19f,.12f),1));
            root.AddChild(new MeshInstance3D { Mesh=new QuadMesh { Size=new(2.1f,.4f) },Position=new(0,2.05f,-4.37f),
                MaterialOverride=new StandardMaterial3D { AlbedoTexture=texture,TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest,Roughness=1 } });
        }
        return root;
    }
    public static Node3D Truck()
    {
        var root=new Node3D { Name="FieldServiceTruck" };
        Part(root,new(2.5f,.28f,7.5f),Steel,new(0,.8f,0));
        Part(root,new(2.5f,1.8f,2.2f),Olive,new(0,1.95f,-2.6f));
        Part(root,new(2.35f,.7f,1.3f),Olive,new(0,1.3f,-3.8f));
        Part(root,new(2.15f,.65f,.04f),new(.17f,.27f,.28f),new(0,2.35f,-3.72f));
        Part(root,new(2.8f,.18f,.3f),Steel,new(0,.85f,-4.45f));
        Part(root,new(1.6f,.45f,.04f),Dark,new(0,1.35f,-4.47f));
        for(int i=0;i<7;i++) Part(root,new(.055f,.4f,.045f),Steel,new(-.7f+i*.23f,1.35f,-4.5f));
        foreach(float side in new[]{-1f,1f})
        {
            Part(root,new(.04f,.65f,.95f),new(.17f,.27f,.28f),new(side*1.27f,2.35f,-2.65f));
            Part(root,new(.35f,.4f,.3f),Sand,new(side*1.05f,1.45f,-4.2f));
            Part(root,new(.15f,.6f,.3f),Steel,new(side*1.5f,2.25f,-3.2f));
            for(int axle=0;axle<3;axle++)
            {
                float z=axle==0?-2.9f:.9f+(axle-1)*1.55f;
                root.AddChild(Props.Part(Props.Cyl(.62f,.42f,12),Rubber,new(side*1.22f,.64f,z),new(0,0,90)));
                root.AddChild(Props.Part(Props.Cyl(.31f,.03f,10),Steel,new(side*1.45f,.64f,z),new(0,0,90)));
            }
            Part(root,new(.13f,.55f,4.4f),Olive,new(side*1.23f,1.4f,1.2f));
            for(int i=0;i<5;i++) Part(root,new(.14f,1.6f,.12f),Steel,new(side*1.23f,1.85f,-.9f+i*1.05f));
        }
        Part(root,new(2.5f,.16f,4.5f),Sand,new(0,1.1f,1.2f));
        for(int i=0;i<5;i++) Part(root,new(.9f,.75f,1.1f),i%2==0?Sand:Olive,new(i%2==0?-.6f:.6f,1.55f,-.5f+i*.7f));
        Part(root,new(2.5f,.12f,4.5f),Canvas,new(0,2.7f,1.2f));
        Rod(root,new(1.1f,2.7f,1),new(1.1f,4.4f,1.2f),.035f,Steel);
        // Doors, handles, wheel fasteners, mudguards and tail lamps.
        foreach(float side in new[]{-1f,1f}) {
            Part(root,new(.055f,.95f,1.4f),Olive,new(side*1.29f,1.65f,-2.4f));
            Part(root,new(.08f,.055f,.24f),Steel,new(side*1.34f,1.97f,-1.95f));
            Part(root,new(.30f,.09f,1.5f),Steel,new(side*1.38f,.9f,-2.5f));
            Part(root,new(.28f,.22f,.08f),new Color(.57f,.06f,.025f),new(side*.95f,1.1f,3.5f));
            for(int axle=0;axle<3;axle++) for(int bolt=0;bolt<6;bolt++) {
                float a=bolt*MathF.Tau/6,z=axle==0?-2.9f:.9f+(axle-1)*1.55f;
                Part(root,new(.025f,.055f,.055f),Sand,new(side*1.48f,.64f+MathF.Sin(a)*.20f,z+MathF.Cos(a)*.20f));
            }
        }
        Part(root,new(.65f,.16f,.04f),Dark,new(0,1.05f,3.53f));
        DrunkTractor.BatchStatic(root,null); return root;
    }
    public static Node3D Net()
    {
        var root=new Node3D { Name="CamouflageNet" };
        foreach(float x in new[]{-6f,6f}) foreach(float z in new[]{-6f,6f})
            Part(root,new(.11f,4.8f,.11f),Steel,new(x,2.4f,z));
        for(int x=0;x<24;x++) for(int z=0;z<24;z++)
        {
            if(Noise.Hash01(x,z,1984)<.22f) continue;
            float y=4.8f-MathF.Sin(x/23f*MathF.PI)*MathF.Sin(z/23f*MathF.PI)*.65f;
            Part(root,new(.49f,.025f,.49f),Noise.Hash01(z,x,17)<.5f?Canvas:Olive,new(-5.75f+x*.5f,y,-5.75f+z*.5f));
        }
        foreach(float side in new[]{-1f,1f}) {
            Rod(root,new(side*6,4.8f,-6),new(side*6,4.8f,6),.035f,Sand);
            Rod(root,new(-6,4.8f,side*6),new(6,4.8f,side*6),.035f,Sand);
            for(int i=0;i<4;i++) {
                float z=-5+i*3.3f;
                Rod(root,new(side*6,4.6f,z),new(side*6.6f,.07f,z),.022f,Sand);
            }
        }
        DrunkTractor.BatchStatic(root,null); return root;
    }
    public static Node3D Fuel()
    {
        var root=new Node3D { Name="PortableFuelBladder" };
        Part(root,new(4.8f,.10f,6.4f),Dark,new(0,.05f,0));
        var tank=Props.Part(Props.Ball(1,16),Canvas,new(0,.7f,0));tank.Scale=new(2.2f,.7f,2.7f);root.AddChild(tank);
        for(int i=0;i<2;i++)
        {
            float x=-2.7f-i*.8f;
            root.AddChild(Props.Part(Props.Cyl(.34f,.95f,12),i==0?Olive:Sand,new(x,.5f,1.8f)));
        }
        Part(root,new(.7f,.9f,.7f),Steel,new(2.5f,.5f,1.8f));
        Rod(root,new(2,.3f,0),new(2.6f,.12f,1.2f),.08f,Rubber);
        Rod(root,new(2.6f,.12f,1.2f),new(3.6f,.12f,2.4f),.08f,Rubber);
        for(int i=0;i<3;i++) Part(root,new(.8f,.55f,.8f),Sand,new(-2.3f+i, .3f,-3.1f));
        // Fill cap, hose clips and warning straps across the bladder.
        root.AddChild(Props.Part(Props.Cyl(.22f,.08f,12),Steel,new(0,1.43f,.4f)));
        for(int i=0;i<3;i++) Part(root,new(4.1f,.035f,.055f),Sand,new(0,1.06f,-1.6f+i*1.6f));
        foreach(float x in new[]{-2.7f,-3.5f}) for(int band=0;band<2;band++)
            root.AddChild(Props.Part(Props.Cyl(.35f,.035f,12),Steel,new(x,.25f+band*.5f,1.8f)));
        Part(root,new(.05f,.38f,.48f),Dark,new(2.87f,.65f,1.8f));
        DrunkTractor.BatchStatic(root,null); return root;
    }
    public static Node3D Generator()
    {
        var root=new Node3D { Name="FieldGenerator" };
        Part(root,new(2.8f,.2f,2.3f),Steel,new(0,.15f,0));
        Part(root,new(2.3f,1.3f,1.7f),Olive,new(0,.9f,0));
        for(int i=0;i<9;i++) Part(root,new(.06f,.75f,.04f),Dark,new(-.9f+i*.22f,.95f,-.87f));
        root.AddChild(Props.Part(Props.Cyl(.09f,1.1f,8),Steel,new(1,1.7f,.3f)));
        Part(root,new(.5f,.45f,.04f),Dark,new(0,1.0f,.87f));
        for(int i=0;i<3;i++) {
            root.AddChild(Props.Part(Props.Cyl(.07f,.025f,12),Sand,new(-.15f+i*.16f,1.1f,.90f),new(90,0,0)));
            Part(root,new(.04f,.10f,.03f),Steel,new(-.15f+i*.16f,1.08f,.92f));
        }
        for(int i=0;i<4;i++) Part(root,new(.05f,.05f,.05f),Sand,new(i%2==0?-1.05f:1.05f,i<2?.4f:1.4f,-.88f));
        Part(root,new(1.15f,.05f,.13f),Steel,new(0,1.63f,0));
        Part(root,new(.16f,.3f,.04f),new Color(.65f,.08f,.035f),new(.8f,.9f,.9f));
        DrunkTractor.BatchStatic(root,null); return root;
    }

    // Follow the graded camp between the roads and berms. The only opening is the middle-pad gate.
    static Vector2[] PerimeterLine=>BonusCampLayout.PerimeterLine;
    internal const float TowerScale=1.6f;
    internal const float GateZ=BonusCampLayout.GateZ,GateHalfWidth=BonusCampLayout.GateHalfWidth;
    internal static Vector2[] PerimeterTowers=>BonusCampLayout.Towers;
    internal static IEnumerable<(Vector2 From,Vector2 To)> PerimeterWalls()
    {
        for(int i=1;i<PerimeterLine.Length;i++) yield return (PerimeterLine[i-1],PerimeterLine[i]);
    }

    /// <summary>Identical continuous placements for visible bags and collision, including every corner.</summary>
    public static IEnumerable<(Vector2 Center,float Yaw,int Row)> PerimeterBags()
    {
        foreach(var (from,to) in PerimeterWalls())
        {
            Vector2 delta=to-from;
            float length=delta.Length(),yaw=-MathF.Atan2(delta.Y,delta.X);
            for(int row=0;row<2;row++)
            {
                int count=(int)MathF.Ceiling(length/2f)+row;
                for(int i=0;i<count;i++)
                {
                    Vector2 center=from+delta*((i+.5f)/count);
                    yield return (center,yaw,row);
                }
            }
        }
    }

    /// <summary>Terrain-following sandbag perimeter, open vehicle gate and three watchtowers.</summary>
    public static Node3D Perimeter(TrackData track, Vector3 anchor, float rotation)
    {
        var root=new Node3D { Name="FieldBasePerimeter" };
        Color[] bags={new(.48f,.43f,.30f),new(.53f,.48f,.34f),new(.42f,.40f,.30f)};
        Color timber=new(.25f,.27f,.22f),roof=new(.22f,.24f,.22f);
        var worldBasis=new Basis(Vector3.Up,rotation);
        float Ground(float x,float z)
        {
            Vector3 world=anchor+worldBasis*new Vector3(x,0,z);
            return track.MeshHeight(world.X,world.Z)-anchor.Y;
        }
        void Bag(float x,float z,float yaw,int row)
        {
            float bx=x,bz=z,ground=Ground(bx,bz);
            Part(root,new(2.15f,.55f,.82f),bags[(row+(int)(MathF.Abs(x+z)*7))%bags.Length],new(bx,ground+.28f+row*.52f,bz),new(0,yaw*180/MathF.PI,0));
            Part(root,new(1.8f,.045f,.86f),Sand,new(bx,ground+.57f+row*.52f,bz),new(0,yaw*180/MathF.PI,0));
        }
        foreach(var (center,yaw,row) in PerimeterBags())
            Bag(center.X,center.Y,yaw,row);
        foreach(var (from,to) in PerimeterWalls())
        {
            float dx=to.X-from.X,dz=to.Y-from.Y,len=from.DistanceTo(to);
            // A few dark timber braces and short lengths of barbed wire make the low wall read as a field fortification.
            for(float d=2;d<len-1;d+=8)
            {
                float x=from.X+dx*d/len,z=from.Y+dz*d/len;
                float ground=Ground(x,z),endX=x+dx/len*1.3f,endZ=z+dz/len*1.3f;
                Part(root,new(.08f,1.45f,.08f),Steel,new(x,ground+.69f,z));
                Rod(root,new(x,ground+1.42f,z),new(endX,Ground(endX,endZ)+1.42f,endZ),.025f,Dark);
            }
        }
        const float gateZ=GateZ,gateHalf=GateHalfWidth;

        void Tower(float x,float z)
        {
            // Built at life size, then scaled up about its foot to the world's 1.6 object scale (the guards are 1.6-scale people).
            var tower=new Node3D {Name="WatchTower"};
            float baseY=Ground(x,z);
            foreach(float sx in new[]{-1f,1f}) foreach(float sz in new[]{-1f,1f})
            {
                float foot=Ground(x+sx*.8f,z+sz*.8f);
                Rod(tower,new(x+sx*.8f,foot+.12f,z+sz*.8f),new(x+sx*.58f,baseY+3.35f,z+sz*.58f),.16f,timber);
                Part(tower,new(.38f,.18f,.38f),Sand,new(x+sx*.8f,foot+.16f,z+sz*.8f));
            }
            Part(tower,new(2.35f,.22f,2.35f),timber,new(x,baseY+3.2f,z));
            Part(tower,new(2.05f,.22f,2.05f),Sand,new(x,baseY+3.48f,z));
            // Waist-high parapet with a narrow firing slit on each face.
            foreach(float side in new[]{-1f,1f})
            {
                Part(tower,new(2.05f,.5f,.22f),bags[0],new(x,baseY+3.82f,z+side*.91f));
                Part(tower,new(.22f,.5f,2.05f),bags[1],new(x+side*.91f,baseY+3.82f,z));
                Part(tower,new(1.05f,.10f,.12f),Dark,new(x,baseY+3.88f,z+side*1.04f));
                Part(tower,new(.12f,.10f,1.05f),Dark,new(x+side*1.04f,baseY+3.88f,z));
            }
            Part(tower,new(2.5f,.16f,2.5f),roof,new(x,baseY+4.24f,z),new(0,0,4));
            Part(tower,new(1.65f,.08f,1.65f),Steel,new(x,baseY+4.38f,z));
            Part(tower,new(.42f,.18f,.34f),new(.75f,.68f,.42f),new(x,baseY+3.83f,z-1.14f));
            // A simple outside ladder is visible from the compound.
            float ladderGround=Ground(x-1.02f,z+1.18f);
            Rod(tower,new(x-1.02f,ladderGround+.25f,z+1.18f),new(x-1.02f,baseY+3.24f,z+1.18f),.08f,timber);
            Rod(tower,new(x-.40f,ladderGround+.25f,z+1.18f),new(x-.40f,baseY+3.24f,z+1.18f),.08f,timber);
            for(int rung=0;rung<8;rung++) Part(tower,new(.72f,.07f,.09f),Sand,new(x-.71f,ladderGround+.42f+rung*.38f,z+1.18f));
            root.AddChild(tower);
            tower.Scale=Vector3.One*TowerScale;
            tower.Position=new Vector3(x,baseY,z)*(1-TowerScale);
        }
        foreach(Vector2 tower in PerimeterTowers) Tower(tower.X,tower.Y);

        // Recess the entrance towards the middle helipad, north of the racing line.
        float lintelY=Math.Max(Ground(-gateHalf,gateZ),Ground(gateHalf,gateZ))+4.8f;
        var gate=new Node3D { Name="MiddleHelipadGate",Position=new Vector3(0,0,gateZ) };
        root.AddChild(gate);
        foreach(float x in new[]{-gateHalf,gateHalf})
        {
            float foot=Ground(x,gateZ),height=lintelY-foot;
            Part(gate,new(.9f,height,1.0f),Steel,new(x,foot+height*.5f,0));
            Part(gate,new(1.15f,.22f,1.25f),Sand,new(x,lintelY+.15f,0));
            Part(gate,new(1.25f,.3f,1.35f),Sand,new(x,foot+.1f,0));
            Part(gate,new(.26f,.3f,.34f),new(.75f,.12f,.06f),new(x,foot+2.2f,.55f));
        }
        Vector3 pivot=new(-gateHalf,Ground(-gateHalf,gateZ)+1.4f,.7f);
        Part(gate,new(.6f,.75f,.55f),Steel,pivot);   // the barrier boom itself is CampGate (it opens and breaks)
        Part(gate,new(12.9f,.65f,.5f),timber,new(0,lintelY,0));
        var label=ImageTexture.CreateFromImage(PixelFont.RenderImage(BonusCampLayout.Name,new(.86f,.80f,.62f),new(.16f,.19f,.13f),1));
        foreach(float face in new[]{-1f,1f})
            gate.AddChild(new MeshInstance3D { Mesh=new QuadMesh { Size=new(8.5f,.48f) },Position=new(0,lintelY,face*.26f),
                Rotation=new Vector3(0,face<0?Mathf.Pi:0,0),
                MaterialOverride=new StandardMaterial3D { AlbedoTexture=label,TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest,Roughness=1 } });
        DrunkTractor.BatchStatic(root,null);
        return root;
    }
}
