namespace CCCPRacing.Track;

/// <summary>The mechanic's service emblem stays at his post while he walks to a car.</summary>
public partial class MechanicMarker : ServiceMarker
{
    public const string IconPath = "res://assets/ui/mechanic-emblem.svg";

    public MechanicMarker() : base(IconPath) => Name = "MechanicMarker";
}
