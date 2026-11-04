using System;
using System.Collections.Generic;
using System.Globalization;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Rally explanations stop the game once per KGB file and card type, across all tracks.
/// Later occurrences use short HUD comments. Flags are saved when a card is actually displayed,
/// so abandoning a queued card never consumes its first showing. --no-cards disables both paths.
/// </summary>
public partial class RaceScene
{
    /// <summary>One card: a big title, a few lines (the first in gold), the title's colour.</summary>
    record StopCard(string Kind, string Title, string[] Lines, Color Color, string Comment, string Hint);

    const float CardFuelAt = 0.25f, CardHealthAt = 0.25f;
    const float CardMinShow = 1.0f, AutoClose = 4f;       // s before a key closes it: a key pressed in the middle of a bend does not skip it unread

    readonly Queue<StopCard> _stopQueue = new();
    StopCard _stopCard;
    float _cardAge;
    bool _cardsOff, _cardFuelShown, _cardHealthShown, _cardLampShown;
    static readonly bool CardDbg = OS.HasEnvironment("CARDDBG");
    /// <summary>The player file's note that the FUEL IS RUNNING LOW card has been shown (TutorialFile key).</summary>
    const string FuelCardKey = "card_fuel_low";
    /// <summary>...and the THE CAR IS BADLY DAMAGED card.</summary>
    const string HealthCardKey = "card_badly_damaged";
    /// <summary>...and the A HEADLIGHT IS BROKEN card.</summary>
    const string LampCardKey = "card_headlight_broken";

    bool CardsOn => !_cardsOff && _norms.Enabled && !StakhanovRules && !InSchool;

    /// <summary>A card is up: the game is stopped under it.</summary>
    bool CardShown => _stopCard != null;

    void InitCards()
    {
        foreach (string a in CmdArgs())
            if (a == "--no-cards") _cardsOff = true;      // test: no stop cards (autodrive runs)
    }

    /// <summary>A new run (or the title): no cards waiting, the warnings armed again.</summary>
    void ResetCards()
    {
        _hud.EndNormMissCard();
        _stopQueue.Clear();
        if (_stopCard != null) _hud.StopCard = null;
        _stopCard = null;
        _cardFuelShown = _cardHealthShown = _cardLampShown = false;
    }

    static string CardKey(string kind) => kind switch
    {
        "fuel" => FuelCardKey,
        "engine" => HealthCardKey,
        "headlights" => LampCardKey,
        _ => "card_" + kind,
    };

    bool CardSeen(string kind)
    {
        string key = CardKey(kind);
        if (_tutorial.SeenKey(key)) return true;
        // Older files stored the radar explanation separately for each track.
        if (kind == "gai_chase")
            foreach (var track in Tracks)
                if (_tutorial.SeenKey("gai_chase_" + track.id))
                {
                    _tutorial.MarkKey(key);
                    return true;
                }
        return false;
    }

    void QueueCard(string kind, string title, Color color, string comment, string hint, params string[] lines)
    {
        if (!CardsOn) return;
        foreach (var pending in _stopQueue)
            if (pending.Kind == kind) return;
        _stopQueue.Enqueue(new StopCard(kind, title, lines, color, comment, hint));
        if (CardDbg) GD.Print($"card: {kind} queued at {_clock:F2} s: {title}");
    }

    /// <summary>Per frame: the next card comes up once the car is racing (not wrecked, towed or stopped by the plan).</summary>
    void CardFrame(float dt)
    {
        if (_stopCard != null)
        {
            _cardAge += dt;
            _hud.StopCardReady = _cardAge >= CardMinShow;
            if (_autodrive && _cardAge >= AutoClose) CloseCard();
            return;
        }
        if (!CardsOn) { _stopQueue.Clear(); return; }
        CardWatch();
        if (_stopQueue.Count == 0 || _state != State.Racing || FateHasCar || _countdown > -1f) return;
        // Explain even a first miss that exhausted the lap budget before the verdict proceeds.
        if (PlanHasCar && _stopQueue.Peek().Kind != "norm_missed") return;
        var next = _stopQueue.Dequeue();
        if (CardSeen(next.Kind))
        {
            var priority = next.Kind is "norm_beaten" or "norm_missed" ? Prio.Important : Prio.Critical;
            _hud.Show(next.Comment, next.Hint, 3f, next.Color, priority);
            if (CardDbg) GD.Print($"card: {next.Kind} comment at {_clock:F2} s: {next.Comment} / {next.Hint}");
            return;
        }
        _stopCard = next;
        _tutorial.MarkKey(CardKey(next.Kind));
        _cardAge = 0;
        _hud.StopCardReady = false;
        _state = State.Paused;
        _hud.Mode = HudMode.Card;
        _hud.StopCard = (_stopCard.Title, _stopCard.Lines, _stopCard.Color);
        if (next.Kind == "norm_missed") _hud.BeginNormMissCard();
        _hud.ClearAlert();
        if (CardDbg) GD.Print($"card: {_stopCard.Kind} up at {_clock:F2} s");
    }

    /// <summary>Any key, mouse or pad button (after CardMinShow) closes the card; the race goes on. True: the event was the card's.</summary>
    bool CardInput(InputEvent e)
    {
        if (_stopCard == null) return false;
        bool press = e is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true }
            or InputEventJoypadButton { Pressed: true };
        if (!press || _cardAge < CardMinShow) return true;
        CloseCard();
        return true;
    }

    void CloseCard()
    {
        if (CardDbg) GD.Print($"card: {_stopCard.Kind} closed after {_cardAge:F1} s");
        if (_stopCard.Kind == "norm_missed") _hud.EndNormMissCard();
        _stopCard = null;
        _hud.StopCard = null;
        _overlayExitFrame = Engine.GetProcessFrames();
        _state = State.Racing;
        _hud.Mode = HudMode.Race;
    }

    /// <summary>Explain the first missed norm once per player file; later misses use the live stamp.</summary>
    void FirstNormMissCard(float time, float norm, bool ranked)
    {
        if (!CardsOn || CardSeen("norm_missed")) return;
        bool valid = ranked && float.IsFinite(time) && time > 0;
        string result = valid
            ? "NORM MISSED +" + Math.Max(0, time - norm).ToString("0.00", CultureInfo.InvariantCulture) + " S"
            : "LAP NOT COUNTED";
        string left = _norms.Left <= 0 ? "NO LAPS LEFT. THE PLAN HAS FAILED."
            : _norms.Left == 1 ? "ONE LAP LEFT TO BEAT THIS NORM!"
            : $"{_norms.Left} LAPS LEFT TO BEAT THIS NORM.";
        // Put this explanation first, including when the final lap also triggered another card.
        var pending = _stopQueue.ToArray();
        _stopQueue.Clear();
        QueueCard("norm_missed", result, Hud.Red, result, left,
            $"YOUR LAP: {LapTimer.Format(time)} / NORM: {NormText(norm)}",
            valid ? "YOUR LAP MUST BE FASTER THAN THE NORM." : "ONLY A VALID LAP CAN BEAT THE NORM.",
            "EACH MISSED NORM USES ONE OF YOUR LAPS.",
            _hud.NormMissLaps, "RUN OUT OF LAPS AND THE RALLY ENDS.",
            _hud.NormMissComment);
        foreach (var card in pending)
            if (card.Kind != "norm_missed") _stopQueue.Enqueue(card);
    }

    /// <summary>Fuel and engine: a card when either first falls to 25 %; again only after a refill / a repair.</summary>
    void CardWatch()
    {
        if (_state != State.Racing) return;
        if (_tank.Enabled)
        {
            if (_tank.Fraction > 0.5f) _cardFuelShown = false;
            // Re-arm after refuelling; the career flag chooses a card or short comment.
            else if (!_cardFuelShown && _tank.Fraction <= CardFuelAt && !_tank.Empty)
            {
                _cardFuelShown = true;
                QueueCard("fuel", "FUEL IS RUNNING LOW!", Hud.Gold,
                    "FUEL LOW!", "FIND FUEL OR STOP AT THE DEPOT",
                    $"{_tank.Fraction * 100:0} % LEFT IN THE TANK",
                    _trackId=="bonus"?"SUPPLY TROOPS CARRY FUEL CANS:":"BABUSHKAS BY THE ROAD HAVE FUEL CANS:",
                    _trackId=="bonus"?"PASS CLOSE IN THE TROOPER'S LANE.":"DRIVE CLOSE PAST HER, IN HER LANE.",
                    _trackId == "siberia" ? "OR STOP AT THE DEPOT AT THE RUNWAY END." : "OR STOP AT THE DEPOT BY THE START LINE.",
                    "EMPTY TANK: TOWED, AND THE LAP IS LOST!");
            }
        }
        // Re-arm after both lamps are repaired; each new failure gives a card or comment.
        bool left = _carVis.Damage?.HeadlightWorks(true) ?? true, right = _carVis.Damage?.HeadlightWorks(false) ?? true;
        if (left && right) _cardLampShown = false;
        else if (!_cardLampShown && _dayNight.Beams > 0.5f && !FateHasCar)
        {
            _cardLampShown = true;
            QueueCard("headlights", "A HEADLIGHT IS BROKEN!", Hud.Red,
                "HEADLIGHT BROKEN!", "MECHANIC: HONK, STOP, WAIT",
                !left && !right ? "BOTH HEADLIGHTS ARE OUT. IT IS DARK, TOVARISH." : $"THE {(left ? "RIGHT" : "LEFT")} HEADLIGHT IS OUT. IT IS DARK, TOVARISH.",
                "THE ROAD AHEAD IS HARD TO SEE.",
                "THE WRENCH MARKS THE MECHANIC'S POST.",
                "WAKE HIM UP WITH THE HORN,",
                "THEN STOP NEAR HIM AND WAIT.",
                "HE PUTS NEW HEADLIGHTS IN WITH HIS HAMMER.");
        }
        if (_health.Health > 0.5f) _cardHealthShown = false;
        // Re-arm after repair, without repeating the career explanation.
        else if (!_cardHealthShown && _health.Health <= CardHealthAt && !_health.Dead)
        {
            _cardHealthShown = true;
            QueueCard("engine", "THE CAR IS BADLY DAMAGED!", Hud.Red,
                "ENGINE IN TROUBLE!", "MECHANIC: HONK, STOP, WAIT",
                $"ENGINE {Math.Max(1, MathF.Round(_health.Health * 100)):0} % - IT CAN SEIZE ANY MOMENT",
                "THE WRENCH MARKS THE MECHANIC'S POST.",
                "WAKE HIM UP WITH THE HORN,",
                "THEN STOP NEAR HIM AND WAIT.",
                "HE FIXES THE CAR IN 10 SECONDS.");
        }
    }

    /// <summary>A revenge has begun: what is coming and what to do about it.</summary>
    void CardRevenge(bool jet)
    {
        if (jet)
            QueueCard("revenge_jet", "THAT WAS THE PILOT'S GRANDMA!", Hud.Red,
                "PILOT'S REVENGE!", "MISSILES INBOUND. TURN HARD!",
                "HER GRANDSON TAKES OFF TO HUNT YOU.",
                "HIS JET FIRES TWO MISSILES AT YOU.",
                "ARROWS SHOW WHERE THEY COME FROM.",
                "THE FASTER THE BEEP, THE CLOSER: TURN HARD!",
                "IT IS OVER WHEN HIS JET IS GONE.");
        else
            QueueCard("revenge_tanks", _trackId=="bonus"?"THAT WAS THE TANK CREW'S COMRADE!":"THAT WAS THE TANK DRIVER'S GRANDMA!", Hud.Red,
                "TANKS WANT REVENGE!", "KEEP MOVING. CHANGE YOUR LINE!",
                "BOTH TANKS TURN THEIR GUNS ON YOU.",
                "THEY FIRE 10 SHELLS BETWEEN THEM.",
                "KEEP MOVING AND CHANGE YOUR LINE.",
                "A STOPPED CAR IS AN EASY TARGET!",
                "AFTER 10 SHELLS THEY GO BACK TO THE EXERCISE.");
    }
}
