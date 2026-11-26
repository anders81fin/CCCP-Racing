using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CCCPRacing.Car;
using CCCPRacing.Track;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Development-only (excluded from ExportRelease by its name): the world-tape coverage scene tests (PhysicsTest.Tape.Scene8.cs) drive the real game through these two flags.
///   --tape-force=worker@A,guard@B,cone@C,crew@D,engine@E   at lap second A... the named thing is hit through the production entry the car's collision uses (a kolkhoz worker's, a dacha
///                                                 guard's knock-over delegate; a cone and a crew member by their own Tick with a stand-in car beside them), once, in lap 1
///   --tape-cover-dump=PATH [--tape-cover-at=K,L]  writes the poses of the new actors' parts: PATH.I at the world's build (the rest the index took), PATH.rec for the recorded
///                                                 samples K, L (their numbers), PATH.rep at every seek of the title replay (with its clock), PATH.rel after the replay was released
/// </summary>
public partial class RaceScene
{
    static readonly string[] CoverKeys = { "None:KolkhozWorkers", "None:BarnDoorsTractor", "None:BarnDoorsSlurry", "None:DachaGuards", "None:TrafficCones", "Jets:ExerciseCrew", "Babushkas:Babushkas", "Babushkas:Troopers",
        "None:PartyOfficialRowing", "None:PondMallard", "None:VillageLife", "None:WaterMill", "None:Radar", "None:Beacon0", "None:Beacon1", "None:Beacon2", "None:Searchlight0", "None:Searchlight1", "None:Weather", "None:FerrisWheel", "Jets:FieldKitchen", "Jets:FieldCombine", "None:EngineEffects" };
    bool _cvInit;
    string _cvDump;
    int[] _cvAt = Array.Empty<int>();
    bool _cvAtAll;
    readonly List<(string What, float At, bool Done)> _cvForce = new();
    readonly HashSet<int> _cvDumped = new();
    int _cvSeeksSeen, _cvLastSample = -1;
    CarSim _cvCar;

    void CvInit()
    {
        if (_cvInit) return;
        _cvInit = true;
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--tape-force="))
                foreach (var item in a["--tape-force=".Length..].Split(',')) { var kv = item.Split('@'); _cvForce.Add((kv[0], float.Parse(kv[1], CultureInfo.InvariantCulture), false)); }
            else if (a.StartsWith("--tape-cover-dump=")) _cvDump = a["--tape-cover-dump=".Length..];
            else if (a == "--tape-cover-at=all") _cvAtAll = true;
            else if (a.StartsWith("--tape-cover-at=")) _cvAt = a["--tape-cover-at=".Length..].Split(',').Select(int.Parse).ToArray();
        }
    }

    partial void TapeCoverHook(string when)
    {
        CvInit();
        if (_cvDump == null && _cvForce.Count == 0) return;
        switch (when)
        {
            case "index": CvWrite("I", 0f); break;
            case "force": CvForce(); break;
            case "lap":
                if (_tapeOn && _tapeLaps == 0 && _tapeRec.Samples > 0 && _tapeRec.Samples - 1 < _cvLastSample)      // the recording began again (a void lap): that take is the one that counts, its hits are forced again
                {
                    _cvDumped.Clear(); try { File.Delete(_cvDump + ".rec"); } catch { }
                    for (int i = 0; i < _cvForce.Count; i++) _cvForce[i] = (_cvForce[i].What, _cvForce[i].At, false);
                }
                if (_tapeOn && _tapeRec.Samples > 0) _cvLastSample = _tapeRec.Samples - 1;
                if (_tapeOn && _tapeLaps == 0 && _tapeRec.Samples > 0 && (_cvAtAll || Array.IndexOf(_cvAt, _tapeRec.Samples - 1) >= 0) && _cvDumped.Add(_tapeRec.Samples - 1)) CvWrite("rec", _tapeRec.Samples - 1);
                break;
            case "replay":
                if (_tapeSeeks != _cvSeeksSeen) { _cvSeeksSeen = _tapeSeeks; CvWrite("rep", _replayClock); }
                break;
            case "release": CvWrite("rel", 0f); break;
        }
    }

    void CvForce()
    {
        if (_state != State.Racing || _tapeLaps != 0) return;
        for (int i = 0; i < _cvForce.Count; i++)
        {
            var (what, at, done) = _cvForce[i];
            if (done || _lapClock < at) continue;
            _cvForce[i] = (what, at, true);
            _cvCar ??= new CarSim(_params, _track);
            switch (what)
            {
                case "worker":
                {
                    var knocks = _workers?.Knocks().ToArray() ?? Array.Empty<(int, Func<Vector3, bool>)>();
                    bool ok = knocks.Length > 3 && knocks[3].Item2(new Vector3(5, 0, 2));
                    TapeLog($"force: worker 3 knocked at lap {_lapClock:F2} s: {ok}");
                    break;
                }
                case "guard":
                {
                    var knocks = _guards?.Knocks().ToArray() ?? Array.Empty<(int, Func<Vector3, bool>)>();
                    bool ok = knocks.Length > 0 && knocks[0].Item2(new Vector3(4, 0, 1));
                    TapeLog($"force: dacha guard 0 knocked at lap {_lapClock:F2} s: {ok}");
                    break;
                }
                case "cone":
                {
                    if (_cones == null) { TapeLog("force: no cones on this track"); break; }
                    var home = ((Node3D)_cones.GetChild(8)).Position;
                    _cvCar.Pos = home + new Vector3(0.8f, 0, 0.3f); _cvCar.Vel = new Vector3(14f, 0, 2f);
                    _cones.Tick(1e-4f, _cvCar);
                    _cvCar.Pos = new Vector3(-5000, 0, -5000); _cvCar.Vel = Vector3.Zero;
                    TapeLog($"force: cone 8 hit at lap {_lapClock:F2} s: knocked {_cones.Knocked}");
                    break;
                }
                case "canister":
                case "golden":
                {
                    // her can is lobbed at the car through the production Toss (no pass needed): the first carrier who holds a can
                    var b = _babushkas; if (b == null) { TapeLog("force: no babushkas"); break; }
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
                    var list = (System.Collections.IList)typeof(Babushkas).GetField("_w", flags).GetValue(b);
                    object walker = null;
                    foreach (var w in list) if ((bool)w.GetType().GetField("Carrier").GetValue(w) && (bool)w.GetType().GetField("HasCan").GetValue(w)) { walker = w; break; }
                    if (walker == null) { TapeLog("force: no babushka holds a can"); break; }
                    if (what == "golden") walker.GetType().GetField("Golden").SetValue(walker, true);
                    typeof(Babushkas).GetMethod("Toss", flags).Invoke(b, new[] { walker, _car });
                    TapeLog($"force: {what} thrown at lap {_lapClock:F2} s");
                    break;
                }
                case "engine":
                    _health.Set(0.15f);      // the engine is sick from here on (smoke), through the production health object; the tape's engine level follows it
                    TapeLog($"force: engine made sick at lap {_lapClock:F2} s: health {_health.Health:F2}, smoking {_health.Smoking}");
                    break;
                case "crew":
                {
                    var crew = _hindsAll?.ExerciseCrew;
                    if (crew == null || !_tapeIndex.TryFind(new TapePartId("Jets:ExerciseCrew", "1/figure"), out int part)) { TapeLog("force: no exercise crew on this track"); break; }
                    _cvCar.Pos = _tapeIndex.NodeOf(part).Position + new Vector3(0.5f, 0, 0); _cvCar.Vel = new Vector3(6, 0, 0);
                    crew.Tick(0f, _cvCar, null, false);
                    _cvCar.Pos = new Vector3(-5000, 0, -5000); _cvCar.Vel = Vector3.Zero;
                    TapeLog($"force: crew member 1 hit at lap {_lapClock:F2} s");
                    break;
                }
            }
        }
    }

    void CvWrite(string tag, float at)
    {
        if (_cvDump == null || _tapeIndex == null) return;
        var sb = new StringBuilder();
        string head = tag == "rec" ? ((int)at).ToString() : at.ToString("F5", CultureInfo.InvariantCulture);
        string F(float v) => v.ToString("F4", CultureInfo.InvariantCulture);
        for (int i = 0; i < _tapeIndex.Count; i++)
        {
            var id = _tapeIndex.IdOf(i);
            if (Array.IndexOf(CoverKeys, id.ActorKey) < 0) continue;
            var n = _tapeIndex.NodeOf(i); var t = n.Transform;
            sb.Append(head).Append('|').Append(id.ActorKey).Append('|').Append(id.Part).Append('|')
              .Append(string.Join(' ', new[] { t.Origin.X, t.Origin.Y, t.Origin.Z, t.Basis.X.X, t.Basis.X.Y, t.Basis.X.Z, t.Basis.Y.X, t.Basis.Y.Y, t.Basis.Y.Z, t.Basis.Z.X, t.Basis.Z.Y, t.Basis.Z.Z }.Select(F)))
              .Append('|').Append(n.Visible ? 1 : 0).Append('\n');
        }
        File.AppendAllText(_cvDump + "." + tag, sb.ToString());
        if (tag != "rec") TapeLog($"cover dump {tag} {head}: {sb.ToString().Count(c => c == '\n')} parts");
    }
}
