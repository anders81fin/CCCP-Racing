using System;
using System.Collections.Generic;
using System.Linq;
using CCCPRacing.Race;
using Godot;

namespace CCCPRacing.Tests;



public delegate TapePairResult TapeSavePair(string ghostPath, string tapePath, Ghost ghost, WorldTape tape, ITapeFileOps ops);

/// <summary>Everything the tests create goes through here. Replace a delegate (and add its name to Real) to plug in a real implementation.</summary>
public static class TapeSeams
{
    public static Func<IReadOnlyList<(string Key, Node3D Root)>, IWorldTapeIndex> NewIndex;
    public static Action<Node3D, string> MarkPart;                     // Tape.Part(node, name)
    public static Action<Node3D, ITapeSpinner> RegisterSpinner;        // Tape.Spinner(node, spinner)
    public static Action<Node3D, TapeSoundChannel, Func<float>, Action<float>> RegisterSound;   // sound property get/set of a node (spec gap: no sound accessor in Types)
    public static Func<IWorldTapeRecorder> NewRecorder;
    public static Func<IWorldTapeRecorder, int> UnregisteredSpinCount; // the safety-net counter
    public static Func<WorldTape, IWorldTapeIndex, IWorldTapeSink, IWorldTapePlayer> NewPlayer;
    public static Func<WorldTape, byte[]> EncodeTape;
    public static Func<byte[], WorldTape> DecodeTape;                  // null when bad
    public static Func<Ghost, ulong> Fingerprint = WorldTapeFingerprint.Of;
    public static TapeSavePair SavePair;
    public static Func<string, Ghost, WorldTape> LoadTape;             // tape path, ghost -> null when absent/incompatible
    public static Func<string, int> CleanStale;                        // directory -> number of stale .tmp files removed
    public static Func<ITapeFileOps> RealOps;                          // File.Replace / File.Move

    public static readonly string[] Names = { "NewIndex", "MarkPart", "RegisterSpinner", "RegisterSound", "NewRecorder", "UnregisteredSpinCount", "NewPlayer", "EncodeTape", "DecodeTape", "Fingerprint", "SavePair", "LoadTape", "CleanStale", "RealOps" };
    /// <summary>Names of seams that a real implementation has replaced.</summary>
    public static readonly HashSet<string> Real = new();
    public static bool ReferenceInUse => Names.Any(n => !Real.Contains(n));
    public static bool RecorderIsReal => Real.Contains("NewRecorder");

    static TapeSeams() { TapeReference.Install(); }

    public static object[] Save() => new object[] { NewIndex, MarkPart, RegisterSpinner, RegisterSound, NewRecorder, UnregisteredSpinCount, NewPlayer, EncodeTape, DecodeTape, Fingerprint, SavePair, LoadTape, CleanStale, RealOps };
    public static void Restore(object[] s)
    {
        NewIndex = (Func<IReadOnlyList<(string Key, Node3D Root)>, IWorldTapeIndex>)s[0]; MarkPart = (Action<Node3D, string>)s[1]; RegisterSpinner = (Action<Node3D, ITapeSpinner>)s[2];
        RegisterSound = (Action<Node3D, TapeSoundChannel, Func<float>, Action<float>>)s[3]; NewRecorder = (Func<IWorldTapeRecorder>)s[4]; UnregisteredSpinCount = (Func<IWorldTapeRecorder, int>)s[5];
        NewPlayer = (Func<WorldTape, IWorldTapeIndex, IWorldTapeSink, IWorldTapePlayer>)s[6]; EncodeTape = (Func<WorldTape, byte[]>)s[7]; DecodeTape = (Func<byte[], WorldTape>)s[8];
        Fingerprint = (Func<Ghost, ulong>)s[9]; SavePair = (TapeSavePair)s[10]; LoadTape = (Func<string, Ghost, WorldTape>)s[11]; CleanStale = (Func<string, int>)s[12]; RealOps = (Func<ITapeFileOps>)s[13];
    }
}
