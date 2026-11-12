using System;
using Godot;

namespace CCCPRacing.Race;

// A separate actor extends old tapes without changing the car or world hierarchy.
// State channels share the established sampled-channel codec (as AircraftState does).
public partial class PlayerPowerTape : Node3D
{
    public readonly record struct State(float BoostLeft, float Power, float Overdrive, bool Golden, float RadBoost = 0f);
    public bool Driven { get; private set; }
    readonly Func<State> _read;
    readonly Action<State> _write;
    State _saved;

    public PlayerPowerTape() { }
    public PlayerPowerTape(Func<State> read, Action<State> write)
    {
        _read = read; _write = write;
        Name = "PlayerPower";
        Tape.Part(this, "root"); Tape.Version(this, 1);
        Tape.Sound(this, TapeSoundChannel.PlayerBoostLeft, () => _read().BoostLeft,
            v => Write(s => s with { BoostLeft = v }));
        Tape.Sound(this, TapeSoundChannel.PlayerPower, () => _read().Power,
            v => Write(s => s with { Power = v }));
        Tape.Sound(this, TapeSoundChannel.PlayerOverdrive, () => _read().Overdrive,
            v => Write(s => s with { Overdrive = v }));
        Tape.Sound(this, TapeSoundChannel.PlayerGolden, () => _read().Golden ? 1 : 0,
            v => Write(s => s with { Golden = v > .5f }));
        Tape.Sound(this, TapeSoundChannel.PlayerRadBoost, () => _read().RadBoost,
            v => Write(s => s with { RadBoost = v }));
    }
    void Write(Func<State, State> change) { if (Driven) _write(change(_read())); }
    public void SetTapeDriven(bool on)
    {
        if (Driven == on) return;
        if (on) _saved = _read();
        Driven = on;
        if (!on) _write(_saved);
    }
}
