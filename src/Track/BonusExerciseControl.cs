using System;

namespace CCCPRacing.Track;

/// <summary>Host-owned exercise timetable: quiet recovery periods separate ground and air hazards.</summary>
public sealed class BonusExerciseControl
{
    public const float Cycle=72,WarningLead=3;
    public float Clock { get; private set; }
    float _groundUntil,_airUntil,_quietUntil;
    int _aircraft=-1;
    public bool AirActive=>_aircraft>=0;
    public bool GroundWindow=>Clock%Cycle is >=14 and <34;
    public bool AirWindow=>Clock%Cycle is >=44 and <66;
    public bool AirDefenceAllowed=>AirWindow && !AirActive && Clock>=_quietUntil && Clock>=_groundUntil;
    public bool GroundAllowed=>GroundWindow && !AirActive && Clock>=_quietUntil;
    public void Reset() {Clock=0;_groundUntil=_airUntil=_quietUntil=0;_aircraft=-1;}
    public void Tick(float dt)
    {
        Clock+=Math.Max(0,dt);
        if(AirActive && Clock>=_airUntil) EndAir(_aircraft);
    }
    public void GroundBusy(bool busy)
    {if(busy) _groundUntil=Math.Max(_groundUntil,Clock+4);}
    public bool ReserveAir(int aircraft)
    {
        if(!AirWindow || AirActive || Clock<_groundUntil || Clock<_quietUntil) return false;
        _aircraft=aircraft;_airUntil=Clock+22;
        return true;
    }
    public void EndAir(int aircraft)
    {
        if(_aircraft!=aircraft) return;
        _aircraft=-1;_quietUntil=Clock+8;
    }
}
