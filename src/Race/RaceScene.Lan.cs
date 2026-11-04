using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CCCPRacing.Net;
using CCCPRacing.Track;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>Host-authoritative LAN race with local prediction and shared rally actors.</summary>
public partial class RaceScene
{
    static readonly int[] LanLapChoices = { 3, 5, 10 };
    static readonly string[] LanMenuItems = { "HOST GAME", "JOIN GAME", "CHANGE NAME" };
    static bool? _lanTrackHosting;
    static (string track, string ip, string name)? _lanJoinResume;

    LanSession _net;
    LanDiscovery _find;
    LanPage _lanPage, _lanBack = LanPage.Menu;
    bool _lanWasIn;           // this session reached the lobby (a close is then the host leaving)
    string _lanGoAlert;
    string _lanName = "", _lanText = "", _lanMsgTitle = "", _lanMsg = "", _lanIp = "";
    int _lanSel, _lanChoice = 2, _lanBallotRow;
    bool _lanDamageVote = true, _lanFuelVote;
    bool? _lanTestDamage, _lanTestFuel;
    bool _lanVoted, _lanDone, _lanDnf, _lanGo;
    double _lanConnectAt = -1, _lanAutoVoteAt = -1, _lanAutoStartAt = -1, _lanQuitAt = -1, _lanStatAt;
    LanReveal _lanReveal;
    Hud.LanBallotShow _lanShow;
    int _lanGrid, _lanTick;
    float _lanRaceTime, _lanBest = -1, _lanLast = -1;
    double _lanResultsAt;
    int _lanScoreScroll;
    readonly Dictionary<long, RemoteCar> _remote = new();
    Func<int, float, Vector3, Vector3, bool> _breakSolo;
    // Tests: --lan-host, --lan-join=IP, --lan-name=NAME, --lan-vote=N, --lan-laps=N, --lan-autostart=K,
    // --lan-shots=DIR, --lan-shot-at=T:TAG, --lan-quit-after=S, --lan-quit-at=T, --lan-ap-delay=S, --lan-version=V.
    int _lanVoteKey, _lanAutostart, _lanRounds = 1, _lanRound;
    float _lanLeaveAt = -1;
    bool _lanAutoDone;
    bool _lanContactProbe, _lanContactProbeDone; // diagnostic fixture, never used by a normal LAN session
    string _lanShots;
    float _lanQuitAfter = -1, _lanQuitRace = -1, _lanApDelay;
    readonly List<(double at, string tag)> _lanShotQueue = new();

    bool LanDbg => LanSession.Debug;
    double LanNow => _net.HostNow;
    /// <summary>Race time on the host clock: s from GO, negative in the countdown.</summary>
    float LanRaceClock => _lanReveal == null ? 0 : _lanReveal.GoHost <= 0 ? -LanBallot.Countdown : (float)(LanNow - _lanReveal.GoHost);
    float LanPoseClock => _lanReveal == null ? 0 : (float)(LanNow-_lanReveal.StartHost);
    bool LanGridWaiting => InLanRace && _state==State.Countdown && _lanReveal?.GoHost<=0;
    int _lanReadyCount;

    async System.Threading.Tasks.Task InitLan()
    {
        _net=LanSession.For(GetTree());
        if(!_net.IsNodeReady()) await ToSignal(_net,Node.SignalName.Ready);
        _net.Signature=TrackSignature.Compute(_track);_net.TrackId=_track.Id;
        _find = new LanDiscovery { Name = "LanDiscovery" };
        AddChild(_find);
        _net.Closed += OnLanClosed;
        _net.LoadRequested+=OnLanLoadTrack;_net.LoadFailed+=OnLanLoadFailed;
        _net.TrackRequired += OnLanTrackRequired;
        _net.Changed += OnLanChanged;
        _net.BallotOpened += OnLanBallot;
        _net.RevealIn += OnLanReveal;
        _net.WinnerIn += OnLanWinner;
        _net.ResultsIn += OnLanResults;
        _net.SessionEnded += OnLanSessionEnded;
        _net.PlayerLeft += OnLanLeft;
        _net.SnapIn += LanSnapshotIn;
        _net.InputIn += LanInputIn;
        _net.RescueIn += LanRescueIn;
        _net.WorldIn += OnLanWorldBytes;
        _net.MeshIn += OnLanMeshBytes;
        _net.EffectIn += LanEffectIn;
        _net.BrokeIn += OnLanBroke;
        _wood.SpanBroken += (i,v)=> {if(InLanRace && _net.IsHost) _net.SendBroke(-1-i,1,v,Vector3.Zero);};
        _scenery.Broken += (i,v,p)=> {if(InLanRace && _net.IsHost) _net.SendBroke(i,24,v,p);};
        _lanName = _rallyBoard.LastName;      // the last name typed on the board (never written back from here)

        bool host = false;
        string join = null;
        int laps = 5;
        foreach (string a in InstructionTrack == null ? OS.GetCmdlineUserArgs() : Array.Empty<string>())
        {
            if (a == "--lan-host") host = true;                                      // test: host a LAN game at once
            else if (a.StartsWith("--lan-join=")) join = a["--lan-join=".Length..];   // test: join the LAN game at this IP at once
            else if (a.StartsWith("--lan-name=")) _lanName = Leaderboard.CleanName(a["--lan-name=".Length..]);  // test: the name in LAN games
            else if (a.StartsWith("--lan-vote=")) _lanVoteKey = Math.Clamp(int.Parse(a["--lan-vote=".Length..]), 1, Training.Count);  // test: vote this fuel key (1-3) when the ballot opens
            else if (a.StartsWith("--lan-damage=")) _lanTestDamage = a.EndsWith("=1");
            else if (a.StartsWith("--lan-fuel=")) _lanTestFuel = a.EndsWith("=1");
            else if (a.StartsWith("--lan-laps=")) laps = int.Parse(a["--lan-laps=".Length..]);  // test: laps of the LAN race (host)
            else if (a.StartsWith("--lan-autostart=")) _lanAutostart = int.Parse(a["--lan-autostart=".Length..]);  // test: the host opens the ballot once this many are in
            else if (a.StartsWith("--lan-shots=")) _lanShots = a["--lan-shots=".Length..];  // test: screenshots of the LAN pages and the race into this directory
            else if (a.StartsWith("--lan-quit-after=")) _lanQuitAfter = F(a["--lan-quit-after=".Length..]);  // test: quit this long after the results
            else if (a.StartsWith("--lan-quit-at=")) _lanQuitRace = F(a["--lan-quit-at=".Length..]);  // test: quit (leave) at this race time
            else if (a.StartsWith("--lan-ap-delay=")) _lanApDelay = F(a["--lan-ap-delay=".Length..]);  // test: --autodrive waits this long after GO
            else if (a=="--lan-contact-probe") _lanContactProbe=true;
            else if (a=="--lan-leave-probe") _lanLeaveProbe=true;
            else if(a.StartsWith("--lan-test-tracks=")) _lanTestTracks=a["--lan-test-tracks=".Length..].Split(',');
            else if(a.StartsWith("--lan-track-vote=")) _lanTrackVoteFlag=a["--lan-track-vote=".Length..];
            else if(a.StartsWith("--lan-track-sequence=")) _lanTrackSequence=a["--lan-track-sequence=".Length..].Split(',');
            else if(a.StartsWith("--lan-load-delay=")) _lanLoadDelay=F(a["--lan-load-delay=".Length..]);
            else if (a.StartsWith("--lan-rounds=")) _lanRounds = int.Parse(a["--lan-rounds=".Length..]);  // test: after the results Enter (back to the lobby, the host starts again) until this many races
            else if (a.StartsWith("--lan-leave-at=")) _lanLeaveAt = F(a["--lan-leave-at=".Length..]);  // test: in the last round, Esc at this race time, Enter (leave) 2.5 s later
            else if (a.StartsWith("--lan-version=")) _net.Version = a["--lan-version=".Length..];  // test: pretend to be this game version
            else if (a.StartsWith("--lan-shot-at="))                                                // test: with --lan-shots, a screenshot at this time, e.g. --lan-shot-at=4.5:menu
            {
                var ts = a["--lan-shot-at=".Length..].Split(':');
                _lanShotQueue.Add((F(ts[0]), ts[1]));
            }
        }
        if(!_net.Active) _net.Laps = laps;
        _lanRound=Math.Max(0,_net.RoundNumber-1);
        if (_lanShots != null) DirAccess.MakeDirRecursiveAbsolute(_lanShots);
        if(_lanWorldRebuild || _net.Active) {_lanName=_net.MyName;return;}
        if (_lanJoinResume.HasValue) return; // ResumeAfterReload reconnects once, with the preserved address/name.
        if (!host && join == null) return;
        _showLog = false;            // no TEST VERSION card
        if (_lanName == "") _lanName = Leaderboard.DefaultName;
        if (host) LanHost();
        else LanConnect(join);

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }

    void LanLog(string s)
    {
        if (LanDbg) GD.Print($"lan[{_lanName}] {LanNow:F3} {s}");
    }

    // ------------------------------------------------------------ pages

    void SetLanPage(LanPage p)
    {
        if (p == LanPage.Join && _lanPage != LanPage.Join) _find.StartListening();
        else if (p != LanPage.Join && _lanPage == LanPage.Join) _find.Stop();
        if (p == LanPage.Lobby || _net?.InLobby==true) _lanWasIn = true;
        _lanPage = p;
        _lanSel = 0;
        if (p == LanPage.None)
        {
            _overlayExitFrame = Engine.GetProcessFrames();     // the Esc or Enter that closed it does nothing else
            _hud.Mode = InLanRace ? HudMode.Race : HudMode.Title;
        }
        else _hud.Mode = HudMode.Lan;
    }

    /// <summary>Title menu LAN RACE: the name first if the board has none, then HOST / JOIN.</summary>
    void EnterLanMenu()
    {
        _lanTrackHosting=null;
        OpenMenu(MenuItem.Multi, MenuItem.Lan);     // closing the LAN pages leaves the MULTIPLAYER row on LAN RACE
        _lanText = "";
        _lanBack = LanPage.Menu;
        SetLanPage(_lanName == "" ? LanPage.Name : LanPage.Menu);
    }

    void LanChooseTrack(bool hosting)
    {
        if (!hosting) { SetLanPage(LanPage.Join); return; }
        _lanTrackHosting=hosting;
        SetLanPage(LanPage.None);
        OpenTrackCard(MenuItem.Lan);
    }

    void OnLanTrackRequired(string track)
    {
        // Only canonical built-in tracks supplied by the connected host may trigger a reload.
        if (_lanPage != LanPage.Connecting || !Tracks.Any(t => t.id == track) || track == _trackId)
        {
            _net.Leave();
            LanMessage("TRACK DATA MISMATCH", "THE HOST'S TRACK CANNOT BE LOADED", LanPage.Join);
            return;
        }
        _lanJoinResume = (track, _lanIp, _lanName);
        _net.Leave();
        _find.Stop();
        _lanTrackHosting = null;
        OnTrack(track, MenuItem.Lan);
    }

    bool ResumeLanJoin()
    {
        if (_lanJoinResume is not { } join) return false;
        _lanJoinResume = null;
        if (join.track != _trackId) return false;
        _lanName = join.name;
        LanConnect(join.ip);
        return true;
    }

    void FinishLanTrackChoice()
    {
        bool hosting=_lanTrackHosting==true;
        _lanTrackHosting=null;
        if(hosting) LanHost();
        else SetLanPage(LanPage.Join);
    }

    void LanHost()
    {
        Error err = _net.Host(_lanName);
        if (err != Error.Ok)
        {
            LanMessage("CANNOT OPEN THE KOLKHOZ", $"UDP PORT {LanSession.Port} IS TAKEN ({err.ToString().ToUpperInvariant()})", LanPage.Menu);
            return;
        }
        _net.ConfigureTracks(LanHostTracks());
        _find.StartBeacon(() => (_lanName, _net.Players.Count, LanSession.MaxPlayers, _net.Version, _net.Signature, LanSession.Port));
        SetLanPage(LanPage.Lobby);
    }

    void LanConnect(string ip)
    {
        _lanIp = ip;
        Error err = _net.Join(ip, _lanName);
        if (err != Error.Ok)
        {
            LanMessage("NO SUCH ADDRESS", $"CANNOT REACH {ip.ToUpperInvariant()}", LanPage.Join);
            return;
        }
        _lanMsgTitle = "CONNECTING";
        _lanMsg = $"TO {ip}...";
        _lanConnectAt = LanSession.Now;
        SetLanPage(LanPage.Connecting);
    }

    void LanMessage(string title, string text, LanPage back)
    {
        _lanMsgTitle = title;
        _lanMsg = text;
        _lanBack = back;
        SetLanPage(LanPage.Message);
    }

    /// <summary>Leave the game from a page: back to the LAN menu.</summary>
    void LanLeavePage()
    {
        CancelLanLeave();
        _net.Leave();
        _find.Stop();
        _lanWasIn = false;
        SetLanPage(LanPage.Menu);
    }

    // ------------------------------------------------------------ session events

    void OnLanChanged()
    {
        if(!_net.IsHost && _net.State==LanSession.Phase.Lobby && _lanPage is LanPage.Results or LanPage.SessionResults)
        {LanToLobby();return;}
        if (_lanPage == LanPage.Connecting && _net.InLobby)
        {
            _lanConnectAt = -1;
            SetLanPage(LanPage.Lobby);
        }
    }

    void OnLanSessionEnded()
    {
        _lanAutoDone=true;_lanResultsAt=LanSession.Now;_lanScoreScroll=0;
        SetLanPage(LanPage.SessionFinal);
        _hud.PlayLanWinnerCheer();
        LanLog("session winner: "+string.Join(", ",_net.Scores.Where(s=>s.Wins>0 && s.Wins==_net.Scores.First().Wins).Select(s=>s.Name)));
        LanShotIn(.8,"session-winner");
    }

    void OnLanClosed(string why)
    {
        if(!_booting) TrackLoadingScreen.Active?.Cancel();
        CancelLanLeave();
        _find.Stop();
        bool wasIn = _lanWasIn;
        _lanWasIn = false;
        _lanConnectAt = -1;
        LanLog($"session closed: {why}");
        if(_booting) return;
        if (wasIn || InLanRace)
        {
            ExitLanToTitle(why);
            return;
        }
        LanMessage("THE DOOR IS CLOSED", why, LanPage.Join);
    }

    void OnLanBallot()
    {
        if (InLanRace) { LanCleanup(); MenuPlayed(MenuItem.Lan); EnterTitle(); OpenMenu(MenuItem.Multi, MenuItem.Lan); }
        _lanVoted = false;
        _lanChoice = 2;
        _lanBallotRow = 0; _lanDamageVote = true; _lanFuelVote = false;
        _lanTrackChoice=0;
        string pick=_lanTrackSequence.Length>0?_lanTrackSequence[Math.Min(_net.RoundNumber,_lanTrackSequence.Length-1)]:_lanTrackVoteFlag;
        if(pick!=null && _net.TrackOptions.Contains(pick)) _lanTrackChoice=_net.TrackOptions.IndexOf(pick);
        SetLanPage(LanPage.Ballot);
        if (_lanVoteKey > 0) _lanAutoVoteAt = LanSession.Now + 1.6;
        LanShotIn(1.0, "ballot");
    }

    void OnLanReveal()
    {
        TrackLoadingScreen.Active?.Cancel();
        // Reliable mesh definitions may precede the reveal on another channel.
        // Keep them until LanAuthorityInit consumes them.
        var rv = _lanReveal = _net.Reveal;
        var show = _lanShow = new Hud.LanBallotShow { Winner = rv.WinnerIndex, Seed = rv.Seed, Power = rv.Power, Damage = rv.Damage, Fuel = rv.Fuel, Times = rv.Timeline,Track=TrackIds.Label(rv.Track),TrackDraw=rv.TrackTied.Count>1,FuelAvailable=rv.Track!="ilyich" };
        show.RuleVotes.AddRange(rv.RuleVotes);
        foreach (var b in rv.Ballots) show.Ballots.Add((b.name, LanRace.TagFor(b.slot), b.pct));
        LanBallot.Tally(rv.Ballots.Select(b => b.pct), out var counts);
        foreach (var (k, n) in counts) show.Counts[k] = n;
        show.Tied.AddRange(rv.Tied);
        foreach (long id in rv.Grid)
        {
            var b = rv.Ballots.Find(x => x.id == id);
            show.Grid.Add((b.name ?? "?", LanRace.TagFor(b.slot)));
        }
        // Warm replicas while the ballot reveal plays; hold every car on the grid.
        StartLanRace();
        SetLanPage(LanPage.Reveal);
        var tl = rv.Timeline;
        double at = rv.StartHost - LanNow;    // host clock to "from now"
        LanShotIn(at + tl.Tally + 0.3, "tally");
        if (rv.Drum) { LanShotIn(at + tl.Drum + 1.3, "drum_spin"); LanShotIn(at + tl.Drum + LanBallot.DrumSpin + 0.4, "drum_stop"); }
        LanShotIn(at + tl.Announce + 1.6, "chosen");
        LanShotIn(at + tl.Grid + 2f, "grid");
        LanLog($"reveal: {string.Join(", ", rv.Ballots.Select(b => $"{b.name} {b.pct}%"))} -> {rv.Power}%{(rv.Drum ? $" by the drum among [{string.Join(",", rv.Tied)}]" : "")}; grid {string.Join(" ", show.Grid.Select(g => g.name))}");
    }

    void OnLanWinner(long id, float time)
    {
        if (!InLanRace) return;
        string name = _net.Players.GetValueOrDefault(id)?.Name ?? (id == _net.MyId ? _lanName : "?");
        _hud.Alert($"{name} WINS!", id == _net.MyId ? $"{LapTimer.Format(time)} - GLORY TO THE WORKER" : "60 SECONDS TO FINISH, TOVARISH", 4.5f, Hud.Gold);
        LanLog($"winner {name} {LapTimer.Format(time)}");
        LanShotIn(5, "after_winner");
    }

    void OnLanResults()
    {
        LanReplayEnd();
        if (!InLanRace) return;
        _lanResultsAt = LanSession.Now;
        var ownResult=_net.Results.Find(r=>r.Id==_net.MyId);
        _lanDnf=ownResult==null || ownResult.Time<0;
        _lanRaceTime=_lanDnf?Math.Max(0,LanRaceClock):ownResult.Time;
        if(ownResult!=null) _lanBest=ownResult.Best;
        _lanInputs.Clear();
        _lanDone = true;           // a DNF's car rolls to a stop too
        _timer.Stop();
        _hud.ClearAlert();
        _hud.ClearMessages();
        SetLanPage(LanPage.Results);
        _hud.PlayLanWinnerCheer();
        LanLog("results: " + string.Join(", ", _net.Results.Select((r, i) => $"{i + 1}. {r.Name} {(r.Time < 0 ? "DNF" : LapTimer.Format(r.Time))}")));
        LanShotIn(1.5, "results");
        if (++_lanRound < _lanRounds) { QueueKey(_clock + 2.5f, Key.Enter); if(_net.IsHost){QueueKey(_clock + 4f, Key.Enter);QueueKey(_clock + 5.5f, Key.Enter);} }
        else if (_lanQuitAfter >= 0) { QueueKey(_clock + 1f, Key.Enter);if(_net.IsHost){QueueKey(_clock+2.5f,Key.Enter);QueueKey(_clock+3.5f,Key.Down);QueueKey(_clock+4.5f,Key.Enter);} _lanQuitAt = LanSession.Now + Math.Max(7,_lanQuitAfter); }
    }

    void OnLanLeft(long id, string name)
    {
        if (_remote.Remove(id, out var rc))
        {
            rc.Free();
            // In the countdown the lights have the alert's place: it waits for GO.
            if (_state == State.Countdown) _lanGoAlert = $"{name} HAS DEFECTED";
            else _hud.Alert($"{name} HAS DEFECTED", "THE RACE GOES ON", 3.5f, Hud.Red, Prio.Important);
            LanShotIn(0.7, "defected");
        }
        LanLog($"{name} left");
    }

    /// <summary>A comrade's car broke a tree, pole or wall: it falls here too, with its crack if near.</summary>
    void OnLanBroke(int i, float impact, Vector3 vel, Vector3 pos)
    {
        if(!InLanRace) return;
        if(i<0) {_wood.BreakNetwork(-1-i,vel);return;}
        if(i>=_track.Obstacles.Count) return;
        if (_scenery.TryBreak(i, impact, vel, pos)) RemoteCrack(pos);
    }

    /// <summary>The own car's BreakObstacle in a LAN race: breaks it as always, and tells the others.</summary>
    bool LanBreak(int i, float impact, Vector3 vel, Vector3 pos)
    {
        if(!_net.IsHost) return false;
        bool broke = _breakSolo(i, impact, vel, pos);
        if (broke) _net.SendBroke(i, impact, vel, pos);
        return broke;
    }

    // ------------------------------------------------------------ input

    /// <summary>Raw input for the LAN pages (typing, menus) and, in the race, the power keys. True: taken.</summary>
    bool LanRawInput(InputEvent e)
    {
        if(LanReplayAvailable && !_lanLeaveAsk && e is InputEventKey {Pressed:true,Echo:false,Keycode:Key.R}) {LanReplayOpen();return true;}
        if (InLanRace && _lanPage == LanPage.None)
        {
            if (e is InputEventKey { Pressed: true, Echo: false } pk && PowerKey(pk) > 0 && _state is State.Countdown or State.Racing)
            {
                _hud.ShowAlways("THE PEOPLE HAVE DECIDED", $"{_power}% FOR EVERY COMRADE", 2f, Hud.Red);
                return true;
            }
            return false;
        }
        if (_lanPage == LanPage.None) return false;
        if (e is not InputEventKey { Pressed: true } k || k.AltPressed) return true;
        bool first = !k.Echo;
        Key key = k.Keycode;
        bool enter = first && key is Key.Enter or Key.KpEnter, esc = first && key == Key.Escape;
        bool up = key is Key.Up, down = key is Key.Down, left = key is Key.Left, right = key is Key.Right;
        if (_lanPage is not (LanPage.Name or LanPage.Ip))
        {
            up |= key == Key.W; down |= key == Key.S; left |= key == Key.A; right |= key == Key.D;
        }
        switch (_lanPage)
        {
            case LanPage.Menu:
                if (esc) { _lanWasIn = false; SetLanPage(LanPage.None); }
                else if (up || down) _lanSel = (_lanSel + (up ? -1 : 1) + LanMenuItems.Length) % LanMenuItems.Length;
                else if (enter)
                {
                    int sel = _lanSel;
                    if (sel == 0) LanHost();
                    else if (sel == 1) LanChooseTrack(false);
                    else { _lanText = _lanName; SetLanPage(LanPage.Name); }
                }
                break;
            case LanPage.Name:
                if (enter)
                {
                    _lanName = Leaderboard.CleanName(_lanText);
                    if (_lanName == "") _lanName = Leaderboard.DefaultName;
                    SetLanPage(LanPage.Menu);
                }
                else if (esc) SetLanPage(_lanName == "" ? LanPage.None : LanPage.Menu);
                else Type(k, Leaderboard.MaxName, c => Leaderboard.IsNameChar(c) && !(c == ' ' && (_lanText.Length == 0 || _lanText[^1] == ' ')));
                break;
            case LanPage.Join:
            {
                int n = _find.Games.Count + 1;
                if (esc) SetLanPage(LanPage.Menu);
                else if (up || down) _lanSel = (_lanSel + (up ? -1 : 1) + n) % n;
                else if (enter)
                {
                    if (_lanSel < _find.Games.Count) LanConnect(_find.Games[_lanSel].Ip);
                    else { _lanText = _lanIp; SetLanPage(LanPage.Ip); }
                }
                break;
            }
            case LanPage.Ip:
                if (enter && _lanText.Length > 0) LanConnect(_lanText.ToLowerInvariant());
                else if (esc) SetLanPage(LanPage.Join);
                else Type(k, 15, c => c is >= '0' and <= '9' or '.' or '-' or ':' or >= 'A' and <= 'Z');
                break;
            case LanPage.Connecting:
                if (esc) { _net.Leave(); _lanConnectAt = -1; SetLanPage(LanPage.Join); }
                break;
            case LanPage.Message:
                if (first) SetLanPage(_lanBack);
                break;
            case LanPage.Lobby:
                if (esc) AskLanLeave();
                else if (_net.IsHost && (left || right))
                {
                    int i = Array.IndexOf(LanLapChoices, _net.Laps);
                    i = i < 0 ? 1 : Math.Clamp(i + (left ? -1 : 1), 0, LanLapChoices.Length - 1);
                    _net.SetLaps(LanLapChoices[i]);
                }
                else if (enter && _net.IsHost) _net.StartBallot();
                break;
            case LanPage.Ballot:
                if (esc) AskLanLeave();
                else if (_lanVoted) break;
                else if (up || down) _lanBallotRow = (_lanBallotRow + (up ? LanBallotRows-1 : 1)) % LanBallotRows;
                else if (_lanBallotRow == 0 && PowerKey(k) is > 0 and var pn && pn <= Training.Count && first) _lanChoice = pn;
                else if (left || right)
                {
                    if(_lanBallotRow==3) _lanTrackChoice=(_lanTrackChoice+(left?-1:1)+_net.TrackOptions.Count)%_net.TrackOptions.Count;
                    else if (_lanBallotRow == 0) _lanChoice = Math.Clamp(_lanChoice + (left ? -1 : 1), 1, Training.Count);
                    else if (_lanBallotRow == 1) _lanDamageVote = right;
                    else _lanFuelVote = right && _net.TrackOptions.Any(t=>t!="ilyich");
                }
                else if (enter) LanVote();
                break;
            case LanPage.Reveal:
                if (esc) AskLanLeave();
                break;
            case LanPage.SessionDecision:
                if(up || down) _lanSel=1-_lanSel;
                else if(enter) {if(_lanSel==0)LanToLobby();else _net.EndSession();}
                else if(esc) AskLanLeave();
                break;
            case LanPage.SessionFinal:
                if(enter || esc) AskLanLeave();
                break;
            case LanPage.SessionResults:
                if(up || down) _lanScoreScroll=Math.Clamp(_lanScoreScroll+(up?-1:1),0,Math.Max(0,_net.Scores.Count()-10));
                else if (enter && _net.IsHost) SetLanPage(LanPage.SessionDecision);
                else if (esc) AskLanLeave();
                break;
            case LanPage.Results:
                if (enter) { _lanScoreScroll=0; SetLanPage(LanPage.SessionResults); LanLog("session standings: "+string.Join(", ",_net.Scores.Select(s=>$"{s.Name} {s.Wins}"))); LanShotIn(.2,"session-results"); }
                else if (esc) AskLanLeave();
                break;
        }
        return true;
    }

    /// <summary>A text field: printable characters that pass the filter (upper case), Backspace.</summary>
    void Type(InputEventKey k, int max, Func<char, bool> ok)
    {
        if (k.Keycode == Key.Backspace) { if (_lanText.Length > 0) _lanText = _lanText[..^1]; return; }
        if (k.CtrlPressed || k.MetaPressed || k.Unicode > char.MaxValue) return;
        char c = k.Unicode > 0 ? char.ToUpperInvariant((char)k.Unicode) : k.Keycode is Key.Period or Key.KpPeriod ? '.' : '\0';
        if (c == '\0') return;
        if (_lanText.Length < max && ok(c)) _lanText += c;
    }

    void LanVote()
    {
        if (_lanVoted || _net.State != LanSession.Phase.Ballot) return;
        _lanVoted = true;
        _net.Vote(Training.PercentForKey(_lanChoice), _lanDamageVote, _lanFuelVote && _net.TrackOptions.Any(t=>t!="ilyich"),_net.TrackOptions[_lanTrackChoice]);
        LanLog($"voted (key {_lanChoice})");
    }

    /// <summary>HandleInput for LAN: the pages take raw input only; in the race Esc asks, never pauses. True: taken.</summary>
    bool LanInput()
    {
        if (_lanLeaveAsk) return true;
        if (_lanPage != LanPage.None) return true;
        if (!InLanRace) return false;
        if (Input.IsActionJustPressed("pause")) { AskLanLeave(); return true; }
        return Input.IsActionJustPressed("keys");     // no key card: the race does not stop for anyone
    }

    // ------------------------------------------------------------ the race

    /// <summary>The count is over: to the grid, the countdown runs to GO on the host's clock.</summary>
    void StartLanRace()
    {
        var rv = _lanReveal;
        _run = RunMode.Lan;
        _lanDone = _lanDnf = _lanGo = false;
        _lanGoAlert = null;
        _lanRaceTime = 0;
        _lanPad = false;
        _lanBest = _lanLast = -1;
        _lanTick = 0;
        SetLanPage(LanPage.None);
        _power = _lapPeak = rv.Power;
        _heroNodded = true;
        _carRadio.SetOn(false, false);
        _hud.SetComments(_commentsForce ?? false, confirm: false);
        _lanGrid = Math.Max(0, rv.Grid.IndexOf(_net.MyId));
        if (_breakSolo == null && _car.BreakObstacle != null)
        {
            _breakSolo = _car.BreakObstacle;
            _car.BreakObstacle = LanBreak;
        }
        EnterCountdown();
        _babushkasAll.Canisters = rv.Fuel && _fuelOnTrack;
        _health.Reset(rv.Damage);
        if (_carVis.Damage != null) _carVis.Damage.Enabled = rv.Damage;
        _tank.Enabled = rv.Fuel;
        _tank.Refill(_fuelStart);
        _hud.ClearMessages();
        _carVis.Repaint(LanRace.Paint[_net.Me?.Slot ?? 0]);
        int fx = 1;
        foreach (long id in rv.Grid)
        {
            if (id == _net.MyId || !_net.Players.TryGetValue(id, out var p)) continue;
            var rc = new RemoteCar(id, p.Name, p.Slot, fx++, _params, _track, this, _fx);
            LanRace.GridPose(_track, rv.Grid.IndexOf(id), out var pos, out float yaw);
            if (rc.Vis.Damage != null) rc.Vis.Damage.Enabled = rv.Damage;
            rc.Buffer.Add(Snapshot.Standing(pos, yaw, LanPoseClock, _params.IdleRpm));
            _remote[id] = rc;
        }
        LanReplayBegin();
        LanAuthorityInit();
        MinimapHud();
        bool mechanicSign=FindChildren("*","",true,false).OfType<MechanicMarker>().Any(m=>m.IsVisibleInTree());
        LanLog($"services: mechanic marker {mechanicSign}, fuel marker {_lanFuelMarker?.IsVisibleInTree() ?? false}, minimap mechanic {_hud.MinimapMechanic.HasValue}, fuel {_hud.MinimapFuel.HasValue}, canisters {_babushkasAll.Canisters}");
        LanLog($"to the grid: slot {_lanGrid + 1}, {rv.Laps} laps at {rv.Power}%, {_remote.Count} comrades");
        LanShotIn(1.2, "countdown");
    }

    /// <summary>PlaceAtStart in a LAN race: the own grid slot.</summary>
    void LanPlaceOnGrid()
    {
        LanRace.GridPose(_track, _lanGrid, out var pos, out float yaw);
        _car.Reset(pos, yaw);
    }

    /// <summary>StartRacing in a LAN race: GO, on every screen at the same host time.</summary>
    void LanStarted()
    {
        _lanReplayCapture?.MarkGo();
        _lanGo = true;
        _lanContactProbeDone=false;
        if(LanDbg)
        {
            LanRace.GridPose(_track,_lanGrid,out var grid,out _);
            LanLog($"grid hold: clutch {_car.Clutch}, drift {new Vector2(_car.Pos.X-grid.X,_car.Pos.Z-grid.Z).Length():F4} m");
            if(_net.IsHost) foreach(var (id,d) in _lanDrivers)
            {
                LanRace.GridPose(_track,_lanReveal.Grid.IndexOf(id),out grid,out _);
                LanLog($"authoritative grid hold {id}: clutch {d.Car.Clutch}, drift {new Vector2(d.Car.Pos.X-grid.X,d.Car.Pos.Z-grid.Z).Length():F4} m");
            }
        }
        if (_lanGoAlert != null) _hud.Alert(_lanGoAlert, "THE RACE GOES ON", 3.5f, Hud.Red);
        _lanGoAlert = null;
        LanLog($"GO at host time {LanNow:F4} (planned {_lanReveal.GoHost:F4}, late {(LanNow - _lanReveal.GoHost) * 1000:F1} ms), wall clock {Time.GetUnixTimeFromSystem():F4}");
        if (_lanLeaveAt >= 0 && _lanRound == _lanRounds - 1)
        {
            QueueKey(_clock + _lanLeaveAt, Key.Escape);
            LanShotIn(_lanLeaveAt + 1.2, "leave_ask");
            QueueKey(_clock + _lanLeaveAt + 2.0f, Key.Right);
            QueueKey(_clock + _lanLeaveAt + 2.5f, Key.Enter);
        }
        LanShotIn(3.5, "race_tags");
        LanShotIn(Math.Max(12, _lanApDelay + 8), "race_later");
    }

    void LanContactProbeTick()
    {
        if(_lanContactProbe && !_lanContactProbeDone && _autodrive && _net.IsHost && _lanDrivers.Count==2 && LanRaceClock>=5)
        {
            _lanContactProbeDone=true;
            // Two existing authoritative cars, one following the other on the start straight.
            // Clients learn the arrangement through normal corrections/snapshots, not a test-only hit message.
            LanRace.GridPose(_track,0,out var at,out float yaw);
            var forward=new Basis(Vector3.Up,yaw)*Vector3.Forward;
            foreach(var (id,d) in _lanDrivers)
            {
                var p=id==_net.MyId?at:at-forward*10;
                p.Y=_track.MeshHeight(p.X,p.Z)+.7f;
                d.Car.Reset(p,yaw);d.Car.Neutral=false;
                d.Car.Vel=id==_net.MyId?Vector3.Zero:forward*20;
                d.Timer.ResetMovementHistory();
            }
            LanLog("contact probe: following car approaches host on the same lane");
        }
    }

    /// <summary>A lap of a LAN race (void or not: R only costs time here). The last one finishes the race.</summary>
    void LanLap(float time)
    {
        _rec.Clear();
        _lapClock = 0;
        _lanRaceTime += time;
        _lanLast = time;
        if (_lanBest < 0 || time < _lanBest) _lanBest = time;
        int laps = _lanReveal.Laps, done = _timer.LapsDone;
        LanLog($"lap {done}/{laps} {LapTimer.Format(time)}, race time {LapTimer.Format(_lanRaceTime)}");
        if (done >= laps)
        {
            _lanDone = true;
            _timer.Stop();
            // Finish is validated by the host's per-driver lap timer.
            _hud.Alert("FINISHED!", $"{LapTimer.Format(_lanRaceTime)} - BEST LAP {LapTimer.Format(_lanBest)}", 3.5f, Hud.Gold);
            LanLog($"finish {LapTimer.Format(_lanRaceTime)}");
        }
        else if (done == laps - 1) _hud.ShowAlways("LAST LAP, TOVARISH", "LAP " + LapTimer.Format(time), 3f, Hud.Red);
        else _hud.Show(Slogans.Pick(Slogans.Lap), $"LAP {done}/{laps}  {LapTimer.Format(time)}", 3f, Hud.Paper, plain: "LAP DONE", plainSub: $"LAP {done}/{laps}  {LapTimer.Format(time)}");   // Important: the position in the race
    }

    /// <summary>
    /// The racing tick in a LAN race: once finished (or out of the race) the car coasts to a halt
    /// under the autopilot; --lan-ap-delay holds the autodriver after GO. True: the controls are set.
    /// </summary>
    bool LanDrive(float dt)
    {
        if (!InLanRace) return false;
        if (_lanQuitRace >= 0 && LanRaceClock >= _lanQuitRace) { LanLog("quitting mid-race (test)"); _net.Leave(); GetTree().Quit(); _lanQuitRace = -1; }
        if (_lanDone)
        {
            _car.Clutch = false;
            _ap.Drive(_car, dt);
            _car.ThrottleTarget = 0;
            _car.Handbrake = 0;
            _car.Brake = _car.Speed > 6f ? 0.12f : 0.4f;
            return true;
        }
        if(_lanLeaveAsk)
        {
            // Modal navigation must not steer/accelerate the car. Physics and the shared race continue.
            _car.SetControls(false,false,0f,false);
            return true;
        }
        if (_autodrive && LanRaceClock < _lanApDelay)
        {
            _car.SetControls(false, false, 0f, true);
            return true;
        }
        return false;
    }

    /// <summary>GO is due on the host's clock (checked every physics tick, not only every frame).</summary>
    bool LanGoDue() => InLanRace && _state == State.Countdown && _lanReveal is { GoHost: > 0 } && LanNow >= _lanReveal.GoHost;

    /// <summary>Per frame: timeouts, the count running into the race, the countdown clock, test automation, puppets drawn and heard.</summary>
    void LanFrame(float dt)
    {
        LanLeaveProbeTick();
        if(_lanReadyAt>0 && LanSession.Now>=_lanReadyAt) {_lanReadyAt=-1;ReportLanWorldReady();}
        if(InLanRace && !_net.IsHost) _lanWorld?.Frame(dt);
        double now = LanSession.Now;
        if (_lanConnectAt > 0 && now - _lanConnectAt > 6)
        {
            _lanConnectAt = -1;
            _net.Leave();
            LanMessage("NO ANSWER", $"NOBODY AT {_lanIp.ToUpperInvariant()}", LanPage.Join);
        }
        if (_net.Active && _net.IsHost && _lanAutostart > 0 && !_lanAutoDone && _lanPage == LanPage.Lobby && _net.State == LanSession.Phase.Lobby)
        {
            if (_net.Players.Count < _lanAutostart) _lanAutoStartAt = -1;
            else if (_lanAutoStartAt < 0)
            {
                _lanAutoStartAt = now + 1.5;       // a few pings first, for the clock sync
                LanShotIn(1.0, "lobby");
            }
            else if (now >= _lanAutoStartAt && _net.StartBallot()) _lanAutoDone = true;
        }
        if (_lanAutoVoteAt > 0 && now >= _lanAutoVoteAt && _lanPage == LanPage.Ballot)
        {
            _lanAutoVoteAt = -1;
            _lanChoice = _lanVoteKey;
            _lanDamageVote = _lanTestDamage ?? _lanDamageVote; _lanFuelVote = _lanTestFuel ?? _lanFuelVote;
            LanVote();
        }
        if (_lanQuitAt > 0 && now >= _lanQuitAt) { _net.Leave(); GetTree().Quit(); }
        if (_lanPage == LanPage.Reveal && _lanReveal != null && LanNow >= _lanReveal.StartHost + _lanReveal.Timeline.End)
        {
            SetLanPage(LanPage.None);
            _hud.Mode = _state == State.Countdown ? HudMode.Countdown : HudMode.Race;
        }
        if (InLanRace && _state == State.Countdown) _countdown = LanGridWaiting ? LanBallot.Countdown : (float)(_lanReveal.GoHost - LanNow);

        foreach (var rc in _remote.Values)
        {
            rc.Vis.Sync(rc.Puppet, _track);
            rc.UpdateSound(_car.Pos, _snd.Volume, _state is not (State.Countdown or State.Racing));
            rc.UpdateLights(dt, _dayNight.Beams);      // shared clock, each driver's actual lamp failures
        }
        if (LanDbg && InLanRace && _lanGo && now >= _lanStatAt)
        {
            if (_lanStatAt > 0)
            {
                var near = _remote.Values.OrderBy(r => r.Puppet.Pos.DistanceTo(_car.Pos)).FirstOrDefault();
                string engine = near == null ? "" : $", nearest comrade {near.Puppet.Pos.DistanceTo(_car.Pos):0} m, his engine at {near.Sound.Volume / Math.Max(_snd.Volume, 1e-3f) * 100:0} % of ours";
                LanLog($"authority: inputs {_lanInputCount}, contacts {_lanContactCount}, predicted {_lanPredictedContacts}, replay {_lanInputs.Count}, correction {_lanCorrectionError:F3} m, world {_lanWorld?.ReplicaCount ?? 0} meshes, {_net.WorldFramesIn/5f:F1} updates/s; " + $"stats: {Engine.GetFramesPerSecond():0} fps, snapshots sent {_net.SnapsSent / 5f:0.0}/s ({_net.SnapBytes} B), in {_net.SnapsIn / 5f:0.0}/s, {_remote.Count} puppets, pos {_hud.LanPos}/{_hud.LanCount}{engine}; rules damage {_lanReveal.Damage}, fuel {_lanReveal.Fuel}, tank {_tank.Litres:F3} l, health {_health.Health:F3}, light {_dayNight.Clock:F3}, shared lead {_lanSharedLightProgress:F3}");
            }
            _net.SnapsSent = _net.SnapsIn = _net.WorldFramesIn = 0;_lanCorrectionError=0;
            _lanStatAt = now + 5;
        }
        for (int i = _lanShotQueue.Count - 1; i >= 0; i--)
        {
            if (_clock < _lanShotQueue[i].at) continue;
            string path = $"{_lanShots}/{_lanName.Replace(' ', '_').ToLowerInvariant()}_{_lanShotQueue[i].tag}.png";
            GetViewport().GetTexture().GetImage().SavePng(path);
            LanLog($"screenshot {path}");
            _lanShotQueue.RemoveAt(i);
        }
    }

    void LanShotIn(double seconds, string tag)
    {
        if (_lanShots != null) _lanShotQueue.Add((_clock + seconds, tag));     // the game clock, like --key-at
    }

    /// <summary>Results, Enter: back to the lobby over the title (the host reopens it for everyone).</summary>
    void LanToLobby()
    {
        LanCleanup();
        MenuPlayed(MenuItem.Lan);
        EnterTitle();
        OpenMenu(MenuItem.Multi, MenuItem.Lan);
        if (_net.IsHost) _net.BackToLobby();
        _lanAutoDone = _lanRound >= _lanRounds;
        SetLanPage(_net.Active ? LanPage.Lobby : LanPage.Menu);
    }

    /// <summary>Leave the LAN race (Esc, Enter; or Esc on the results): to the title.</summary>
    void LeaveLan()
    {
        _net.Leave();
        _lanWasIn = false;
        ExitLanToTitle(null);
    }

    void ExitLanToTitle(string notice)
    {
        LanCleanup();
        _find.Stop();
        SetLanPage(LanPage.None);
        MenuPlayed(MenuItem.Lan);
        EnterTitle();                // the top row on MULTIPLAYER
        if (notice != null) { _hud.TitleNotice = notice; LanShotIn(1.0, "host_left"); }
    }

    /// <summary>Everything a LAN race changed goes back: puppets, paint, the break callback, the HUD.</summary>
    void LanCleanup()
    {
        LanRestoreServiceMarkers();
        LanRestoreExtraWorld();
        _babushkasAll.Canisters=false;
        _lanWorld?.Free();_lanWorld=null;_lanWorldPending.Clear();_lanDrivers.Clear();_lanInputs.Clear();_lanCorrection=null;
        _lanCars.Clear();
        foreach(var a in _actors) if(a.Node!=null) {a.Node.Visible=InWorld(a.Who); foreach(var child in a.Node.FindChildren("*","",true,false)) {if(child is AudioStreamPlayer3D p3) p3.StreamPaused=false;else if(child is AudioStreamPlayer p) p.StreamPaused=false;}}
        foreach (var rc in _remote.Values) rc.Free();
        _remote.Clear();
        if (_breakSolo != null) { _car.BreakObstacle = _breakSolo; _breakSolo = null; }
        _carVis.Repaint(null);
        _run = RunMode.Title;        // the title follows
        _lanDone = _lanDnf = _lanGo = false;
        if(!_net.Active) CancelLanLeave();
        _hud.LanLaps = 0;
        _hud.LanTags.Clear();
        _hud.ClearAlert();
        _hud.StopLanWinnerCheer();
    }

    // ------------------------------------------------------------ HUD

    void LanHud()
    {
        var v = _hud.Lan;
        v.Track = TrackName(_trackId);
        v.FuelAvailable = _net.TrackOptions.Any(t=>t!="ilyich");
        v.Page = _lanPage;
        if (_lanPage != LanPage.None) _hud.Mode = HudMode.Lan;
        v.Sel = _lanSel;
        v.IsHost = _net.IsHost;
        if(_net.Active) v.Track=_net.TrackOptions.Count==1?TrackIds.Label(_net.TrackOptions[0]):"TRACK BY BALLOT";
        v.TrackOptions=_net.TrackOptions.Select(TrackIds.Label).ToArray();v.TrackChoice=_lanTrackChoice;
        v.FuelAvailable=_net.TrackOptions.Any(t=>t!="ilyich");
        v.Laps = _lanReveal?.Laps ?? _net.Laps;
        v.Power = _lanReveal?.Power ?? 100;
        switch (_lanPage)
        {
            case LanPage.Menu:
                v.Text = _lanName;
                v.Items.Clear();
                v.Items.AddRange(LanMenuItems);
                break;
            case LanPage.Name or LanPage.Ip:
                v.Text = _lanText;
                break;
            case LanPage.Join:
                v.Items.Clear();
                string mine = _net.Version;
                foreach (var g in _find.Games)
                    v.Items.Add($"{g.Host}  {g.Players}/{g.Max}  {g.Ip}" + (g.Version != mine ? $"  V{g.Version}" : g.Players >= g.Max ? "  FULL" : ""));
                v.Items.Add("ENTER IP ADDRESS");
                v.Sub = _find.Deaf ? "CANNOT LISTEN HERE - ENTER THE IP" : null;
                _lanSel = Math.Min(_lanSel, v.Items.Count - 1);
                v.Sel = _lanSel;
                break;
            case LanPage.Loading:
                v.Title="LOADING TRACK";v.Message=TrackIds.Label(_net.SelectedTrack)+" - "+_net.LoadedCount+"/"+_net.Players.Count+" READY";
                break;
            case LanPage.Connecting or LanPage.Message:
                v.Title = _lanMsgTitle;
                v.Message = _lanMsg;
                break;
            case LanPage.Lobby or LanPage.Ballot:
                v.Laps = _net.Laps;
                v.Players.Clear();
                foreach (var p in _net.Players.Values.OrderBy(p => p.Slot))
                    v.Players.Add(new Hud.LanRow { Name = p.Name, Color = LanRace.TagFor(p.Slot), Ping = p.PingMs, Host = p.Id == 1, You = p.Id == _net.MyId, Voted = p.Voted, Note = p.Id == 1 ? "HOST" : null });
                v.Address = _net.IsHost ? LocalAddress() : null;
                v.Sub = $"HOST: {_net.Players.GetValueOrDefault(1)?.Name ?? "?"}";
                v.Choice = _lanChoice;
                v.Sel = _lanBallotRow; v.Damage = _lanDamageVote; v.Fuel = _lanFuelVote;
                v.Voted = _lanVoted;
                v.BallotLeft = _net.State == LanSession.Phase.Ballot ? (float)Math.Max(0, _net.BallotEndsHost - LanNow) : -1;
                break;
            case LanPage.Reveal:
                v.Count = _lanShow;
                v.T = _lanReveal == null ? 0 : (float)(LanNow - _lanReveal.StartHost);
                break;
            case LanPage.SessionDecision:
            case LanPage.SessionFinal:
            case LanPage.SessionResults:
                v.Sel=_lanSel;
                v.ResultsAge=(float)Math.Max(0,LanSession.Now-_lanResultsAt);
                v.SessionScores.Clear();
                v.SessionScroll=_lanScoreScroll;
                int place=0, sharedRank=0, lastWins=-1;
                foreach(var score in _net.Scores)
                {
                    place++;if(score.Wins!=lastWins)sharedRank=place;lastWins=score.Wins;
                    v.SessionScores.Add((sharedRank,score.Name,LanRace.TagFor(score.Slot),score.Wins,score.Connected,_net.OwnScore(score)));
                }
                break;
            case LanPage.Results:
                v.Results.Clear();
                int rank = 0;
                foreach (var r in _net.Results)
                    v.Results.Add((++rank, r.Name, LanRace.TagFor(r.Slot), r.Time, r.Best, r.Pad));
                var winner = _net.Results.Find(r => r.Id == _net.WinnerId) ?? _net.Results.FirstOrDefault(r => r.Time >= 0);
                v.WinnerName = winner?.Name;
                v.WinnerYou = winner?.Id == _net.MyId;
                v.ResultsAge = (float)Math.Max(0, LanSession.Now - _lanResultsAt);
                break;
        }

        _hud.LanLaps = InLanRace ? _lanReveal.Laps : 0;
        LanLeaveHud();
        _hud.LanTags.Clear();
        _hud.LanStandings.Clear();
        if (!InLanRace) return;
        bool tags = _lanPage == LanPage.None;     // not under the results
        _hud.LanLap = _timer.LapsDone + 1;
        _hud.LanDone = _lanDone;
        _hud.LanDnf = _lanDnf;
        _hud.LanTime = _lanGo ? (_lanDone ? _lanRaceTime : Math.Max(0, LanRaceClock)) : 0;
        _hud.LanBest = _lanBest;
        _hud.LanLast = _lanLast;
        _hud.LanDnfIn = _net.DnfAtHost > 0 ? (float)Math.Max(0, _net.DnfAtHost - LanNow) : -1;
        // Standings: laps and gates from the host's validated timers (the comrades' from their snapshots).
        int n = _track.Center.Length;
        var all = new List<LanRace.Standing>
        {
            new() { Id = _net.MyId, Finished = _lanDone && !_lanDnf, Dnf = _lanDnf, FinishTime = _lanRaceTime,
                LapsDone = _timer.LapsDone,
                Score = _state == State.Countdown ? LanRace.GridScore(_track, _lanGrid, LapTimer.GateStep)
                    : LanRace.Score(_timer.LapsDone, _timer.NextGate, _timer.Progress, n, LapTimer.GateStep) },
        };
        foreach (var rc in _remote.Values)
        {
            var s = rc.Buffer.Latest;
            if (s == null) continue;
            int g = _lanReveal.Grid.IndexOf(rc.Id);
            var result=_net.Results.Find(r=>r.Id==rc.Id);
            all.Add(new LanRace.Standing
            {
                Id = rc.Id, Finished = result!=null ? result.Time>=0 : s.Finished || (_net.Players.TryGetValue(rc.Id, out var player) && player.Finished && player.FinishTime >= 0),
                Dnf=result!=null ? result.Time<0 : _net.Players.TryGetValue(rc.Id, out var peer) && peer.Dnf,
                FinishTime = result?.Time ?? (_net.Players.TryGetValue(rc.Id, out var finisher) && finisher.FinishTime >= 0 ? finisher.FinishTime : s.FinishTime),
                LapsDone = s.Laps,
                Score = _state == State.Countdown ? LanRace.GridScore(_track, g, LapTimer.GateStep)
                    : LanRace.Score(s.Laps, s.NextGate, s.Progress, n, LapTimer.GateStep),
            });
            Vector3 head = rc.Puppet.Pos + Vector3.Up * 1.6f;
            bool behind = _cam.IsPositionBehind(head);
            Vector2 sp;
            if (!behind) sp = _cam.UnprojectPosition(head);
            else
            {
                Vector3 local = _cam.GlobalTransform.AffineInverse() * head;
                sp = _hud.GetViewportRect().Size / 2 + new Vector2(local.X, -local.Y).Normalized() * 1e4f;
            }
            if (tags) _hud.LanTags.Add((sp, rc.Name, LanRace.TagFor(rc.Slot), !behind));
        }
        // Keep every comrade in the order while the first snapshot is still in flight.
        foreach (var p in _net.Players.Values)
            if (p.Id != _net.MyId && !all.Any(s => s.Id == p.Id))
                all.Add(new LanRace.Standing { Id = p.Id, Finished = p.Finished && p.FinishTime >= 0, Dnf = p.Dnf,
                    FinishTime = p.FinishTime, Score = LanRace.GridScore(_track, Math.Max(0, _lanReveal.Grid.IndexOf(p.Id)), LapTimer.GateStep) });
        var ranked = LanRace.Rank(all);
        _hud.LanPos = ranked.FindIndex(s => s.Id == _net.MyId) + 1;
        _hud.LanCount = ranked.Count;
        for (int i = 0; i < ranked.Count; i++)
        {
            var s = ranked[i];
            var p = _net.Players.GetValueOrDefault(s.Id);
            _hud.LanStandings.Add(new Hud.LanStandingRow(i + 1, p?.Name ?? "?", LanRace.TagFor(p?.Slot ?? 0),
                s.Id == _net.MyId, s.Finished, s.Dnf, Math.Min(_lanReveal.Laps, s.LapsDone + 1)));
        }
    }

    string _lanAddress;
    double _lanAddressAt = -10;

    /// <summary>This machine's LAN address to tell the comrades (IPv4, not loopback), refreshed every few seconds.</summary>
    string LocalAddress()
    {
        if (LanSession.Now - _lanAddressAt < 5) return _lanAddress;
        _lanAddressAt = LanSession.Now;
        _lanAddress = IP.GetLocalAddresses().FirstOrDefault(a => a.Contains('.') && !a.StartsWith("127.") && !a.StartsWith("169.254.") && !a.StartsWith("172.17.")) ?? "127.0.0.1";
        return _lanAddress;
    }
}
