namespace CCCPRacing.Track;

/// <summary>
/// The bonus track's walking soldiers: the babushka walkers' behaviour (road crossing, dodging, canisters, launched by a hit)
/// in uniform, with their own shouts. Two of them carry the revenge roles: the pilot's close friend (<see cref="PilotCompanion"/>,
/// whose death brings the Hinds) and the tank crew's comrade (<see cref="TankComrade"/>).
/// </summary>
public partial class Troopers : Babushkas
{
    protected override bool Troops => true;
    /// <summary>Index of the Hind pilot's close friend this run (the tragic one: a hit calls the Hinds).</summary>
    public int PilotCompanion => PilotMother;
    /// <summary>Index of the tank crew's comrade this run.</summary>
    public int TankComrade => TankMother;
}
