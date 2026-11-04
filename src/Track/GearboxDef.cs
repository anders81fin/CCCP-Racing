namespace CCCPRacing.Track;

/// <summary>Authored track setup. Gear values descend from first through fifth.</summary>
public class GearboxDef
{
    public float[] Gears { get; set; }
    public float FinalDrive { get; set; }
    public float ShiftDown { get; set; } = 3200;
}
