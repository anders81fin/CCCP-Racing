using System;
using System.Collections.Generic;
using System.Globalization;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Race;

/// <summary>
/// Game pads: the driving is read by DriverInput (RaceScene.cs); everything else here. Pad buttons
/// become the keys each screen already knows (A Enter, B Esc, the d-pad the arrows...), chosen by
/// what is on screen (PadCtx), so every menu works with a pad. The name fields are letter wheels
/// (d-pad up/down the letter, A or right the next one, B or left delete, START confirms). In the
/// race: START pause, B rescue, X comments, BACK radio, d-pad up/down the Stakhanov
/// power; in the pause menu Y opens the key card and RB turns the rumble on or off. The prompts
/// follow the last input (keys or pad). An unplugged pad pauses the race. Rumble: Rumble.cs.
/// PADDBG prints the pads as they come and go.
/// </summary>
public partial class RaceScene
{
    /// <summary>Keys made from pad buttons carry this device id, so they do not switch the prompts to the keys.</summary>
    const int PadKeyDevice = 0x5AD;
    const string NameChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÅ0123456789- ", IpChars = "0123456789.";

    /// <summary>What the pad's buttons mean now. Any: they are raw "any key" presses (cards, the gulag).</summary>
    enum PadCtx { Race, Ask, Pause, Title, Menu, Wheel, Any, School }

    readonly DriverInput _driver = new() { Device = 0 };
    readonly Rumble _rumble = new() { Device = 0 };
    bool _padMode;            // the last input came from a pad: pad prompts and key card
    bool _lapPad;             // a pad drove during this lap
    bool _lanPad;             // ...during this LAN race
    bool _padAuto, _padLost;
    readonly Dictionary<JoyButton, Key> _padKeys = new();      // pad buttons held down as keys
    JoyButton? _padHeld;                                         // a direction held: repeats in menus and wheels
    float _padHeldFor, _padRepeat;
    int _stickX, _stickY;                                        // the left stick as a d-pad in menus
    PadCtx _padCtx;
    float _airBefore, _padUnplugAt = -1;
    float _autoRt = -1, _autoLt = -1, _autoX = -9;
    // Tests: --pad-at=T:Button[:hold], --axis-at=T:Axis:value[:hold], --pad-autodrive, --pad-unplug-at=T.
    readonly List<(float at, InputEvent ev)> _padQueue = new();

    static bool PadDbg => OS.HasEnvironment("PADDBG");

    void InitPad()
    {
        _rumble.Load(_records.Dir);
        var pads = Input.GetConnectedJoypads();
        if (pads.Count > 0) _driver.Device = _rumble.Device = pads[0];
        if (PadDbg)
        {
            GD.Print($"pads: {pads.Count} connected{(pads.Count > 0 ? $", player one drives with {pads[0]}" : "")}, rumble {(_rumble.Enabled ? "on" : "off")}");
            foreach (int d in pads) GD.Print($"pad {d}: {Input.GetJoyName(d)} ({Input.GetJoyGuid(d)})");
        }
        Input.Singleton.JoyConnectionChanged += OnJoyConnection;
        foreach (string a in CmdArgs())
        {
            if (a == "--pad-autodrive") _padAuto = _autodrive = true;      // test: the autopilot drives through the pad path (triggers, stick)
            else if (a.StartsWith("--pad-unplug-at=")) _padUnplugAt = F(a["--pad-unplug-at=".Length..]);  // test: the driving pad is pulled out at this time
            else if (a.StartsWith("--pad-at="))                            // test: press a pad button at a time, e.g. --pad-at=2.5:A or --pad-at=3:DpadDown:0.5
            {
                var t = a["--pad-at=".Length..].Split(':');
                float at = F(t[0]), hold = t.Length > 2 ? F(t[2]) : 0.1f;
                var b = ParseButton(t[1]);
                _padQueue.Add((at, new InputEventJoypadButton { ButtonIndex = b, Pressed = true, Device = 0 }));
                _padQueue.Add((at + hold, new InputEventJoypadButton { ButtonIndex = b, Pressed = false, Device = 0 }));
            }
            else if (a.StartsWith("--axis-at="))                          // test: move a pad axis at a time, e.g. --axis-at=5:RT:0.6 or --axis-at=6:LeftX:-0.5:2 (back to 0 after 2 s)
            {
                var t = a["--axis-at=".Length..].Split(':');
                float at = F(t[0]);
                var ax = ParseAxis(t[1]);
                _padQueue.Add((at, new InputEventJoypadMotion { Axis = ax, AxisValue = F(t[2]), Device = 0 }));
                if (t.Length > 3) _padQueue.Add((at + F(t[3]), new InputEventJoypadMotion { Axis = ax, AxisValue = 0, Device = 0 }));
            }
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }

    static JoyButton ParseButton(string s) => s.ToUpperInvariant() switch
    {
        "LB" => JoyButton.LeftShoulder, "RB" => JoyButton.RightShoulder, "LS" or "L3" => JoyButton.LeftStick, "RS" or "R3" => JoyButton.RightStick,
        "UP" => JoyButton.DpadUp, "DOWN" => JoyButton.DpadDown, "LEFT" => JoyButton.DpadLeft, "RIGHT" => JoyButton.DpadRight, "VIEW" => JoyButton.Back,
        _ => Enum.Parse<JoyButton>(s, true),
    };

    static JoyAxis ParseAxis(string s) => s.ToUpperInvariant() switch
    {
        "LT" => JoyAxis.TriggerLeft, "RT" => JoyAxis.TriggerRight, "LX" => JoyAxis.LeftX, "LY" => JoyAxis.LeftY,
        _ => Enum.Parse<JoyAxis>(s, true),
    };

    /// <summary>Per frame: the test queue, the context (a change lets go of the held keys), repeats, the rumble.</summary>
    void PadFrame(float dt)
    {
        for (int i = _padQueue.Count - 1; i >= 0; i--)
            if (_clock >= _padQueue[i].at)
            {
                Input.ParseInputEvent(_padQueue[i].ev);
                _padQueue.RemoveAt(i);
            }
        if (_padUnplugAt > 0 && _clock >= _padUnplugAt)
        {
            _padUnplugAt = -1;
            OnJoyConnection(_driver.Device, false);
        }
        var ctx = PadContext();
        if (ctx != _padCtx)
        {
            ReleasePadKeys();
            _padHeld = null;
            _padCtx = ctx;
        }
        if (_padHeld is JoyButton h && (_padHeldFor += dt) > 0.4f && (_padRepeat -= dt) <= 0)
        {
            _padRepeat = 0.07f;
            PadButton(h, true, echo: true);
        }
        if (!RumbleLive) _rumble.Stop(_state.ToString().ToLowerInvariant());
        else
        {
            RumbleBuzz();
            _rumble.Update();
        }
    }

    void ReleasePadKeys()
    {
        foreach (var k in _padKeys.Values) PadKey(k, false);
        _padKeys.Clear();
    }

    static void PadKey(Key key, bool pressed, bool echo = false) =>
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, Echo = echo, Device = PadKeyDevice });

    PadCtx PadContext()
    {
        if (_resignOut >= 0) return PadCtx.Any;      // the resignation is on its way: nothing to press
        if (_lanLeaveAsk) return PadCtx.Ask;
        if (_gulag || _planScreen || _showKeys || CardShown) return PadCtx.Any;
        if (_keepNameOpen || _askName || _lanPage is LanPage.Name or LanPage.Ip) return PadCtx.Wheel;
        if (_recordingsOpen) return PadCtx.Menu;
        if (_lanPage != LanPage.None || _watching || _showBoard || _showLog || _showFiles || _showLessons) return PadCtx.Menu;
        if (_state == State.Title) return PadCtx.Title;
        if (_state == State.Paused) return PadCtx.Pause;
        if (InSchool) return PadCtx.School;
        if (InLanRace && _lanLeaveAsk) return PadCtx.Ask;
        return PadCtx.Race;
    }

    /// <summary>First look at every raw event: keys and pads switch the prompts; pad events become keys. True: taken.</summary>
    bool PadRawInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventKey { Pressed: true } k when k.Device != PadKeyDevice:
                _padMode = false;
                return false;
            case InputEventJoypadMotion m:
                PadSeen(m.Device, Math.Abs(m.AxisValue) > 0.5f);
                if (m.Axis is JoyAxis.LeftX or JoyAxis.LeftY && m.Device == _driver.Device) StickAsDpad(m);
                return true;
            case InputEventJoypadButton b:
                PadSeen(b.Device, b.Pressed);
                return b.Device == _driver.Device && PadButton(b.ButtonIndex, b.Pressed, false);
        }
        return false;
    }

    /// <summary>A pad did something: it is the driver's pad from now on if it was pressed (and it is back if it was unplugged).</summary>
    void PadSeen(int device, bool active)
    {
        if (device == _driver.Device) _driver.Gone = false;
        if (!active) return;
        if (device != _driver.Device)
        {
            ReleasePadKeys();
            _rumble.Stop("another pad");
            _driver.Device = _rumble.Device = device;
            _driver.Gone = false;
            if (PadDbg) GD.Print($"pad {device} ({Input.GetJoyName(device)}) drives now");
        }
        _padMode = true;
    }

    /// <summary>In menus and the wheels the left stick is a d-pad (on past 0.6, off under 0.4).</summary>
    void StickAsDpad(InputEventJoypadMotion m)
    {
        bool x = m.Axis == JoyAxis.LeftX;
        int was = x ? _stickX : _stickY;
        int now = m.AxisValue > 0.6f ? 1 : m.AxisValue < -0.6f ? -1 : Math.Abs(m.AxisValue) < 0.4f ? 0 : was;
        if (now == was) return;
        if (x) _stickX = now; else _stickY = now;
        JoyButton Dir(int s) => x ? (s < 0 ? JoyButton.DpadLeft : JoyButton.DpadRight) : (s < 0 ? JoyButton.DpadUp : JoyButton.DpadDown);
        if (was != 0) PadButton(Dir(was), false, false);
        if (now != 0 && (PadContext() is not (PadCtx.Race or PadCtx.Ask or PadCtx.Any) || _lanLeaveAsk)) PadButton(Dir(now), true, false);
    }

    /// <summary>A pad button in the current context: a key, a direct action, or (false) a raw "any key" press.</summary>
    bool PadButton(JoyButton b, bool pressed, bool echo)
    {
        if (!pressed)
        {
            if (_padHeld == b) _padHeld = null;
            if (_padKeys.Remove(b, out Key up)) PadKey(up, false);
            return true;
        }
        var ctx = PadContext();
        bool dpad = b is JoyButton.DpadUp or JoyButton.DpadDown or JoyButton.DpadLeft or JoyButton.DpadRight;
        if (!echo && dpad && (ctx is PadCtx.Title or PadCtx.Menu or PadCtx.Wheel or PadCtx.Pause || ctx == PadCtx.Race && _labOpen))
        {
            _padHeld = ctx == PadCtx.Wheel && b is JoyButton.DpadLeft or JoyButton.DpadRight ? null : b;
            _padHeldFor = 0;
            _padRepeat = 0;
        }
        Key key = Key.None;
        switch (ctx)
        {
            case PadCtx.Any:
                return false;
            case PadCtx.Wheel:
                key = PadWheel(b);
                break;
            case PadCtx.Race:
                if (LabPadKey(b, true) is var labKey && labKey != Key.None) { key = labKey; break; }      // RaceScene.Lab.cs
                if (_labOpen) return true;      // (the panel is open over the race: the other buttons do nothing)
                if (b is JoyButton.DpadUp or JoyButton.DpadDown && !echo) PadPower(b == JoyButton.DpadUp ? 1 : -1);
                key = b switch
                {
                    JoyButton.Start => Key.Escape, JoyButton.B => Key.R, JoyButton.X => Key.Tab,
                    JoyButton.Back => Key.Backspace, JoyButton.RightStick => Key.M, _ => Key.None,
                };
                break;
            case PadCtx.School:
                if (b is JoyButton.DpadUp or JoyButton.DpadDown && !echo) PadPower(b == JoyButton.DpadUp ? 1 : -1);
                key = SchoolPadKey(b);
                break;
            case PadCtx.Ask:
                key = b switch { JoyButton.A => Key.Enter, JoyButton.B or JoyButton.Start => Key.Escape, _ => _lanLeaveAsk ? DpadKey(b) : Key.None };
                break;
            case PadCtx.Pause:
                if (_labOpen) { key = LabPadKey(b, false); break; }      // RaceScene.Lab.cs
                if (_hud.RallyExitOpen || _hud.SettingsOpen || _hud.VisualOpen || _hud.MixerOpen || _hud.ReplayListOpen)
                {
                    key = b switch { JoyButton.A => Key.Enter, JoyButton.B or JoyButton.Start or JoyButton.Back => Key.Escape, JoyButton.X when _hud.ReplayListOpen => Key.Delete, JoyButton.Y when _hud.SettingsOpen && !_hud.VisualOpen && !_hud.MixerOpen || b == JoyButton.Y && _hud.ReplayListOpen => Key.K, _ => DpadKey(b) };
                    break;
                }
                if (_hud.PauseNormsOpen)
                {
                    key = b is JoyButton.A or JoyButton.B or JoyButton.Start or JoyButton.Back ? Key.Escape : Key.None;
                    break;
                }
                if (b == JoyButton.RightShoulder) { ToggleRumble(); return true; }
                if (b == JoyButton.LeftShoulder) { key = Key.C; break; }     // the camera
                if (b == JoyButton.B) { PauseChooseId(PauseRowId.Continue); return true; }       // back to the race, whatever row is selected
                // A and Start choose the selected row straight away (not through a key press that the next frame has to notice).
                if (b is JoyButton.A or JoyButton.Start && !echo) { if (PadDbg) GD.Print($"pad: {b} in the pause menu chooses row {_hud.PauseSelection} at {_clock:F2} s"); PauseChoose(_hud.PauseSelection); return true; }
                key = b switch
                {
                    JoyButton.A or JoyButton.Start => Key.Enter, JoyButton.X => Key.R, JoyButton.Y => Key.K,
                    JoyButton.RightStick => Key.M,      // the map on/off, as in the race
                    JoyButton.LeftStick => Key.Tab,     // the comments on/off
                    JoyButton.Back => Key.Escape, _ => DpadKey(b),
                };
                break;
            case PadCtx.Title:
                if (TitleDirect)
                {
                    // A, Start and the d-pad act at once (the same code as the keys), not through a key press the next frame has to notice.
                    if (b is JoyButton.A or JoyButton.Start && !echo) { if (PadDbg) GD.Print($"pad: {b} on the title at {_clock:F2} s, menu {_menu}, parent {_menuParent}"); TitleStart(); return true; }
                    if (b == JoyButton.DpadLeft) { TitleSide(-1); return true; }
                    if (b == JoyButton.DpadRight) { TitleSide(1); return true; }
                }
                key = b == JoyButton.A || b == JoyButton.Start ? Key.Enter : b == JoyButton.Y ? Key.T : DpadKey(b);   // Y: a track's tour
                if (b == JoyButton.Back && _card == TitleCard.None && !echo) { PadBackButtonOnTitle(); return true; }
                if (b == JoyButton.B && _menuParent != null) key = Key.Escape;       // back to the top row (there Esc would quit the game)
                break;
            case PadCtx.Menu:
                key = b switch { JoyButton.A or JoyButton.Start => Key.Enter, JoyButton.B => Key.Escape, JoyButton.X when _recordingsOpen => Key.Delete, JoyButton.Y when _recordingsOpen => Key.K, _ => DpadKey(b) };
                if (key == Key.None && _recordingsOpen) return true;                            // RECORDINGS: other buttons do nothing
                if (key == Key.None && _lanPage == LanPage.Message) key = Key.Enter;          // "any key" there wants a key
                else if (key == Key.None && _lanPage == LanPage.None) return false;           // any other button closes the board or the changelog
                break;
        }
        if (key == Key.None) return true;
        if (!echo)
        {
            if (_padKeys.Remove(b, out Key old)) PadKey(old, false);
            _padKeys[b] = key;
        }
        PadKey(key, true, echo);
        return true;
    }

    /// <summary>
    /// The pad's Back button (the small one left of the big centre button) on the title menu: PARTY OFFICE opens, and in
    /// PARTY OFFICE it hands in the resignation. B is always just back (Esc) one level.
    /// </summary>
    void PadBackButtonOnTitle()
    {
        if (_menuParent == MenuItem.Office) { AskResignation(); return; }
        OpenMenu(MenuItem.Office, MenuItem.Files);
    }

    static Key DpadKey(JoyButton b) => b switch
    {
        JoyButton.DpadUp => Key.Up, JoyButton.DpadDown => Key.Down, JoyButton.DpadLeft => Key.Left, JoyButton.DpadRight => Key.Right, _ => Key.None,
    };

    /// <summary>
    /// The name entry and the LAN name / IP fields: up/down turn the last letter (an empty field gets
    /// its first), A or right adds a letter (a copy of the last), B or left deletes, START is Enter.
    /// B in an empty LAN field goes back (Esc). Returns the key to press, if any.
    /// </summary>
    Key PadWheel(JoyButton b)
    {
        bool keep = _keepNameOpen, entry = _askName, ip = !entry && !keep && _lanPage == LanPage.Ip;
        string chars = ip ? IpChars : NameChars;
        int max = ip ? 15 : keep ? KeepNameMax : Leaderboard.MaxName;
        string t = keep ? _keepName : entry ? _entryName : _lanText;
        switch (b)
        {
            case JoyButton.DpadUp or JoyButton.DpadDown:
                int step = b == JoyButton.DpadUp ? 1 : -1;
                if (t.Length == 0) t = chars[0].ToString();
                else
                {
                    int i = chars.IndexOf(t[^1]);
                    t = t[..^1] + chars[i < 0 ? 0 : (i + step + chars.Length) % chars.Length];
                }
                break;
            case JoyButton.A or JoyButton.DpadRight:
                if (t.Length < max) t += t.Length > 0 ? t[^1] : chars[0];
                break;
            case JoyButton.B or JoyButton.DpadLeft:
                if (t.Length > 0) t = t[..^1];
                else if (b == JoyButton.B) return Key.Escape;     // an empty field: back (the menu, the LAN page)
                break;
            case JoyButton.Start:
                return Key.Enter;
        }
        if (keep) _keepName = t;
        else if (entry) _entryName = t;
        else _lanText = t;
        return Key.None;
    }

    /// <summary>D-pad up/down in Stakhanov laps: the power one step up or down (the same path as keys 1-4).</summary>
    void PadPower(int step)
    {
        if (_state is not (State.Countdown or State.Racing)) return;
        if (InLanRace) { _hud.ShowAlways("THE PEOPLE HAVE DECIDED", $"{_power}% FOR EVERY COMRADE", 2f, Hud.Red); return; }
        if (!StakhanovRules) return;
        int at = 1;
        for (int k = 1; k <= Training.LapsCount; k++) if (Training.PercentForKey(k) <= _power) at = k;
        SetPower(Training.PercentForKey(Math.Clamp(at + step, 1, Training.LapsCount)));
    }

    void ToggleRumble()
    {
        _rumble.SetEnabled(!_rumble.Enabled);
        if (_rumble.Enabled) _rumble.Kick(0.5f, 0.3f, 0.25f, "rumble on");
        _hud.ShowAlways(_rumble.Enabled ? "RUMBLE ON" : "RUMBLE OFF", null, 1.5f, _rumble.Enabled ? Hud.Gold : Hud.Paper);
    }

    /// <summary>A pad plugged in drives at once if player one has none; the driving pad pulled out mid-race pauses the game.</summary>
    void OnJoyConnection(long device, bool connected)
    {
        int d = (int)device;
        if (PadDbg) GD.Print(connected ? $"pad {d} connected: {Input.GetJoyName(d)} ({Input.GetJoyGuid(d)})" : $"pad {d} disconnected");
        if (connected)
        {
            if (d == _driver.Device) _driver.Gone = false;
            else if (_driver.Gone || !Input.GetConnectedJoypads().Contains(_driver.Device))
            {
                _driver.Device = _rumble.Device = d;
                _driver.Gone = false;
            }
            return;
        }
        if (d != _driver.Device) return;
        ReleasePadKeys();
        _padHeld = null;
        _stickX = _stickY = 0;
        _rumble.Stop("pad unplugged");
        _driver.Gone = true;
        bool used = _padMode || _lapPad;
        var pads = Input.GetConnectedJoypads();
        if (pads.Count > 0) { _driver.Device = _rumble.Device = pads[0]; _driver.Gone = false; }
        if (!used || _state is not (State.Countdown or State.Racing) || _showKeys || CardShown) return;
        if (InLanRace) _hud.Alert("CONTROLLER DISCONNECTED", "THE RACE GOES ON", 3.5f, Hud.Red);     // a LAN race never stops
        else
        {
            _state = State.Paused;
            _hud.Mode = HudMode.Paused;
            _hud.PauseSelection = PauseStartRow;
            _padLost = true;
        }
    }

    /// <summary>
    /// --pad-autodrive: the autopilot decides, its throttle, brake and steering go back into the
    /// pad as trigger and stick positions (through the inverse of the dead zones and the curve), and
    /// the car is then driven from the pad like by a player (a frame later, as a real pad would be).
    /// </summary>
    void PadAutoDrive(float dt)
    {
        int gear = _car.Gear;
        float thr0 = _car.ThrottleTarget, brk0 = _car.Brake, steer0 = _car.SteerIn, hb0 = _car.Handbrake;
        _ap.Drive(_car, dt);
        bool rev = _car.Gear < 0;
        float rt = rev ? _car.Brake : _car.ThrottleTarget, lt = rev ? _car.ThrottleTarget : _car.Brake, x = _car.SteerIn;
        _car.Gear = gear;
        _car.ThrottleTarget = thr0;
        _car.Brake = brk0;
        _car.SteerIn = steer0;
        _car.Handbrake = hb0;
        Axis(JoyAxis.TriggerRight, DriverInput.PedalRaw(rt), ref _autoRt);
        Axis(JoyAxis.TriggerLeft, DriverInput.PedalRaw(lt), ref _autoLt);
        Axis(JoyAxis.LeftX, DriverInput.SteeringRaw(x), ref _autoX);

        static void Axis(JoyAxis a, float v, ref float last)
        {
            if (Math.Abs(v - last) < 1e-4f) return;
            last = v;
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = a, AxisValue = v, Device = 0 });
        }
    }

    /// <summary>The car is driven and felt: rumble only in a run, never on the title replay or under a card.</summary>
    bool RumbleLive => _state is State.Countdown or State.Racing or State.Failed or State.Finished
                       && !_gulag && !_planScreen && !_showKeys && !Replaying;

    void Rumb(float weak, float strong, float duration, string cause)
    {
        if (RumbleLive) _rumble.Kick(weak, strong, duration, cause);
    }

    /// <summary>The light continuous buzz: loose ground under the wheels (with speed), the rev limiter.</summary>
    void RumbleBuzz()
    {
        float weak = 0;
        string cause = "";
        if (_state == State.Racing && !_rescue.Active)
        {
            float rough = 0;
            int contacts = 0;
            foreach (var w in _car.Wheels)
            {
                if (!w.Contact) continue;
                contacts++;
                rough += w.Surface switch
                {
                    Track.SurfaceType.Concrete or Track.SurfaceType.Runway or Track.SurfaceType.Pothole or Track.SurfaceType.Puddle or Track.SurfaceType.Ice => 0f,
                    Track.SurfaceType.Gravel or Track.SurfaceType.Dirt or Track.SurfaceType.Shallow or Track.SurfaceType.Water or Track.SurfaceType.Snow => 0.5f,
                    _ => 1f,                                // grass, field, mud, deep snow: rougher
                };
            }
            float k = contacts > 0 ? rough / contacts * Math.Clamp(_car.Speed / 25f, 0f, 1f) : 0f;
            if (k > 0.02f) { weak = 0.05f + 0.17f * k; cause = rough / Math.Max(1, contacts) > 0.5f ? "grass" : "gravel"; }
        }
        if (_state is State.Racing or State.Countdown && _car.Limiter && weak < 0.1f) { weak = 0.1f; cause = "limiter"; }
        _rumble.Buzz(weak, 0f, cause);
    }

    /// <summary>After the physics tick: a landing after a jump.</summary>
    void RumbleLanding()
    {
        if (_airBefore > 0.3f && _car.AirTime == 0f)
        {
            float k = Math.Clamp((_airBefore - 0.3f) / 1f, 0f, 1f);
            Rumb(0.45f + 0.5f * k, 0.3f + 0.7f * k, 0.15f + 0.3f * k, $"landing after {_airBefore:F2} s");
        }
    }
}
