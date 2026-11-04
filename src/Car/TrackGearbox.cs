using System;
using CCCPRacing.Track;

namespace CCCPRacing.Car;

public partial class CarParams
{
    /// <summary>One setup shared by the player's car, ghosts and LAN puppets on the loaded track.</summary>
    public static CarParams ForTrack(TrackDef track)
    {
        var p = new CarParams();
        if (track.Gearbox is not { } box) return p;
        if (box.Gears == null || box.Gears.Length != 5 || !float.IsFinite(box.FinalDrive) || box.FinalDrive <= 0
            || !float.IsFinite(box.ShiftDown) || box.ShiftDown <= p.IdleRpm || box.ShiftDown >= p.ShiftUp)
            throw new InvalidOperationException($"Invalid five-speed gearbox on {track.Id}");
        for (int i = 0; i < box.Gears.Length; i++)
            if (!float.IsFinite(box.Gears[i]) || box.Gears[i] <= 0 || i > 0 && box.Gears[i] >= box.Gears[i - 1])
                throw new InvalidOperationException($"Invalid gear {i + 1} on {track.Id}");
        // Retain the tall 500 % power range when changing the normal rally final drive/top gear.
        p.OverdriveGearing *= box.FinalDrive * box.Gears[^1] / (p.FinalDrive * p.Gears[^1]);
        p.Gears = (float[])box.Gears.Clone();
        p.FinalDrive = box.FinalDrive;
        p.ShiftDown = box.ShiftDown;
        return p;
    }
}
