using Godot;

namespace CCCPRacing.Track;

/// <summary>One site plan for the camp's props, surfaces, access, fortifications and terrain grading.</summary>
public static class BonusCampLayout
{
    public const string Name="POLIGON 17";
    public static readonly Vector2 Origin=new(136,104);
    public static readonly Rect2 GradingBounds=new(72,78,142,47),ProtectedBounds=new(70,72,148,66);
    public static readonly Vector2[] Pads={new(86,105),new(136,104),new(187,106)};
    internal static readonly Rect2 MaintenanceYard=new(101,94,18,31),CrewYard=new(155,84,12,27);
    internal static readonly Vector2 Generator=new(104.5f,110),Distribution=new(124,118),CableJunction=new(124,120);
    internal static readonly Vector2[] ServicePath={new(86,116),new(104,120),new(123,117),new(136,116),new(159,114),new(187,115),new(197,112)};
    internal const float GateZ=20,GateHalfWidth=6;
    internal static readonly Vector2[] PerimeterLine={
        new(-6,20),new(-8.5f,22.5f),new(-36,22.5f),new(-64,22),new(-64,-12),
        new(-35,-12),new(-14,-14),new(9,-14),new(17,-23),new(29,-22),
        new(45,-11),new(58,-11),new(78,-17),new(78,16),new(49,16),new(24,14),new(8,15),new(6,20)};
    internal static readonly Vector2[] Towers={new(74,-10),new(-61,19),new(74,13)};

    public static void Apply(TrackDef definition)
    {
        if(definition.Id!="bonus") return;
        definition.Name=Name;
        // Rebuilding a definition does not duplicate the owned site plan.
        definition.Pads.RemoveAll(p=>GradingBounds.HasPoint(new Vector2((p.X0+p.X1)*.5f,(p.Z0+p.Z1)*.5f)));
        definition.Props.RemoveAll(p=>p.Type is "helipad" or "fieldtent" or "fieldtruck" or "fieldnet" or "fieldfuel" or "fieldperimeter" or "windsock" or "flagpole");
        foreach(Vector2 p in Pads)
        {
            Rect2 rect=new(p-new Vector2(11,12),new Vector2(22,24));
            AddPad(rect,"concrete");Add("helipad",p);
        }
        AddPad(MaintenanceYard,"dirt");AddPad(CrewYard,"dirt");
        Add("fieldtent",new(161,90),-8,"COMMAND");Add("fieldtent",new(161,103),12,"CREW");
        Add("fieldtruck",new(111,102),165);Add("fieldnet",new(111,102));
        Add("fieldfuel",new(119,113));Add("fieldperimeter",Origin);
        Add("windsock",new(202,112));Add("flagpole",new(124,116));
        void Add(string type,Vector2 p,float rotation=0,string text=null) =>
            definition.Props.Add(new PropDef {Type=type,X=p.X,Z=p.Y,Rot=rotation,Text=text});
        void AddPad(Rect2 p,string surface) => definition.Pads.Add(new PadDef {
            X0=p.Position.X,Z0=p.Position.Y,X1=p.End.X,Z1=p.End.Y,Surface=surface});
    }
}
