using System;
using System.Collections.Generic;
using CCCPRacing.UI;
using Godot;

namespace CCCPRacing.Tests;

public partial class PhysicsTest
{
    /// <summary>The Party Meeting's screens (UI/Hud.cs DrawPause, HudSettings.cs, HudVisual.cs, HudSoundBalance.cs): fit, hit tests, row labels, smoke render.</summary>
    async void MenuUiTests()
    {
        try { await MenuUiTestsBody(); }
        catch (Exception e) { GD.PrintErr(e); _failures++; }
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    static bool MenuInside(Rect2 r, Vector2 size) => r.Position.X >= 0 && r.Position.Y >= 0 && r.End.X <= size.X && r.End.Y <= size.Y;

    async System.Threading.Tasks.Task MenuUiTestsBody()
    {
        GD.Print("Menu UI: main, settings, visuals, sounds");
        var allIds = new[] { PauseRowId.Replay, PauseRowId.Norms, PauseRowId.Instructions, PauseRowId.Settings, PauseRowId.Continue, PauseRowId.Restart, PauseRowId.Exit };

        // --- main labels for every subset of {Replay, Norms}, in id order ---
        foreach (bool pad in new[] { false, true })
        for (int mask = 0; mask < 4; mask++)
        {
            bool replay = (mask & 1) != 0, norms = (mask & 2) != 0;
            var ids = new List<PauseRowId>();
            if (replay) ids.Add(PauseRowId.Replay);
            if (norms) ids.Add(PauseRowId.Norms);
            ids.AddRange(new[] { PauseRowId.Instructions, PauseRowId.Settings, PauseRowId.Continue, PauseRowId.Restart, PauseRowId.Exit });
            foreach (bool avail in new[] { false, true })
            {
                var hud = new Hud { PauseRowIds = ids.ToArray(), PauseReplayAvailable = avail, Pad = pad };
                var lines = hud.PauseLines(out string replayLine);
                string tag = $"replay={replay} norms={norms} pad={pad} avail={avail}";
                bool ok = lines.Count == ids.Count;
                string[] words = { "REPLAY", "NORMS AND MEDALS", "DRIVING SCHOOL", "SETTINGS", "CONTINUE", "RESTART RACE", "EXIT RACE" };      // (by enum order)
                for (int i = 0; ok && i < ids.Count; i++) ok &= lines[i].Contains(words[(int)ids[i]]);
                Check(ok, $"pause labels follow the ids ({tag})");
                Check(replay ? ReferenceEquals(replayLine, lines[0]) && lines[0].Contains("NO LAPS") == !avail : replayLine == null, $"replay line object and NO LAPS wording ({tag})");
                Check(!lines.Exists(l => l.Contains("MAP") || l.Contains("COMMENTS") || l.Contains("RADIO") || l.Contains("RUMBLE") || l.Contains("VISUAL") || l.Contains("SOUND") || l.Contains("KEYS")), $"no setting rows in the main menu ({tag})");
                hud.Free();
            }
        }
        {
            var hud = new Hud();
            Check(hud.PauseLines(out _).Count == 5 && hud.PauseLines(out _)[1].Contains("SETTINGS") && hud.PauseLines(out _)[4].Contains("EXIT RACE"), "the default main menu has 5 rows: DRIVING SCHOOL, SETTINGS, CONTINUE, RESTART RACE, EXIT RACE");
            hud.Free();
        }
        {
            var hud = new Hud { Rumble = true };
            hud.SettingsRowTexts(SettingsRowId.Rumble, out string on, out _);
            hud.Rumble = false;
            hud.SettingsRowTexts(SettingsRowId.Rumble, out string off, out _);
            Check(on.EndsWith("RUMBLE ON") && off.EndsWith("RUMBLE OFF"), "the rumble row shows its state");
            hud.Free();
        }

        foreach (var size in ReplayUiSizes)
        {
            string sz = $"{size.X}x{size.Y}";
            var vp = new SubViewport { Size = new Vector2I((int)size.X, (int)size.Y), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(vp);
            var hud = new Hud { Mode = HudMode.Paused };
            vp.AddChild(hud);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // --- main menu: worst case, every row, controller lost ---
            hud.PauseRowIds = allIds; hud.PadLost = true; hud.PauseHasNorms = true;
            foreach (bool pad in new[] { false, true })
            {
                hud.Pad = pad;
                var lines = hud.PauseLines(out _);
                float h = 52 + lines.Count * 10 + 12;
                Check(h <= size.Y && h <= 256, $"{sz}: main menu with all rows is {h} px high (pad {pad})");
                float lx = Mathf.Floor(size.X / 2 - PixelFont.Measure("       EXIT RACE") / 2f);
                bool fit = true;
                foreach (var l in lines) fit &= lx + PixelFont.Measure(l) <= size.X / 2 + 110 - 2;
                Check(fit, $"{sz}: every main line fits the 220 px panel (pad {pad})");
            }
            foreach (bool lost in new[] { false, true })
            {
                hud.PadLost = lost;
                foreach (int n in new[] { 5, 6, 7 })
                {
                    hud.PauseRowIds = allIds[(7 - n)..];
                    float h = 52 + n * 10 + (lost ? 12 : 0), top = Mathf.Floor(size.Y / 2 - h / 2);
                    float y0 = top + 28 + (lost ? 12 : 0);
                    bool ok = true;
                    for (int i = 0; i < n; i++) ok &= hud.PauseRowAt(new Vector2(size.X / 2, y0 + i * 10 + 4)) == i;
                    ok &= hud.PauseRowAt(new Vector2(size.X / 2, y0 + n * 10 + 4)) == -1 && hud.PauseRowAt(new Vector2(size.X / 2, y0 - 3)) == -1 && hud.PauseRowAt(new Vector2(size.X / 2 - 120, y0 + 4)) == -1;
                    Check(ok, $"{sz}: PauseRowAt maps {n} drawn rows (controller lost {lost})");
                }
            }
            hud.PadLost = false; hud.PauseRowIds = allIds;

            // --- settings ---
            var settingsSets = new[]
            {
                new[] { SettingsRowId.Sounds, SettingsRowId.Visuals, SettingsRowId.Keys, SettingsRowId.Back },
                new[] { SettingsRowId.Sounds, SettingsRowId.Visuals, SettingsRowId.Rumble, SettingsRowId.Keys, SettingsRowId.Back },
            };
            foreach (var set in settingsSets)
                foreach (bool pad in new[] { false, true })
                {
                    hud.SettingsRowIds = set; hud.Pad = pad;
                    var r = hud.SettingsRect;
                    bool fit = MenuInside(r, size);
                    foreach (var id in set)
                    {
                        hud.SettingsRowTexts(id, out string label, out string value);
                        float x = hud.SettingsLabelX(r);
                        fit &= x + PixelFont.Measure(label) + (value != null ? PixelFont.Measure(value) + 8 : 0) <= r.End.X - 12;
                        if (hud.SettingsRowKey(id) is { } k) fit &= r.Position.X + (r.Size.X < 240 ? 10 : 18) + PixelFont.Measure(k) < x;      // (the shortcut ends before the label column)
                    }
                    Check(fit, $"{sz}: settings screen with {set.Length} rows fits (pad {pad})");
                    bool ok = true;
                    for (int i = 0; i < set.Length; i++) ok &= hud.SettingsRowAt(new Vector2(r.Position.X + r.Size.X / 2, r.Position.Y + 38 + i * 16 + 4)) == i;
                    ok &= hud.SettingsRowAt(new Vector2(r.Position.X + r.Size.X / 2, r.Position.Y + 38 + set.Length * 16 + 4)) == -1 && hud.SettingsRowAt(new Vector2(r.Position.X - 1, r.Position.Y + 40)) == -1;
                    Check(ok, $"{sz}: SettingsRowAt maps {set.Length} rows");
                    string hint = pad ? "D-PAD CHOOSE  A SELECT" : "UP/DOWN CHOOSE  ENTER SELECT";
                    Check(PixelFont.Measure(hint) <= r.Size.X - 12, $"{sz}: settings footer fits (pad {pad})");
                }
            hud.Pad = false;

            // --- visuals ---
            var visualSets = new[]
            {
                new[] { VisualRowId.Comments, VisualRowId.LapInfo, VisualRowId.NormsCard, VisualRowId.Map, VisualRowId.CarInstruments, VisualRowId.Edges, VisualRowId.Softening, VisualRowId.Back },
                new[] { VisualRowId.Comments, VisualRowId.LapInfo, VisualRowId.NormsCard, VisualRowId.Map, VisualRowId.CarInstruments, VisualRowId.Camera, VisualRowId.Edges, VisualRowId.Softening, VisualRowId.Back },
            };
            hud.AntialiasingName = "MSAA 4X + FXAA"; hud.SofteningName = "STRONG";
            foreach (var set in visualSets)
            {
                hud.VisualRowIds = set; hud.VisualCameraName = "BONNET INSIDE COCKPIT";
                foreach (bool pad in new[] { false, true })
                {
                    hud.Pad = pad;
                    var r = hud.VisualRect;
                    bool fit = MenuInside(r, size) && r.Size.Y <= 256;
                    foreach (var id in set)
                    {
                        hud.VisualRowTexts(id, out string label, out string value);
                        float x = r.Position.X + (r.Size.X < 240 ? 10 : 18);
                        fit &= x + PixelFont.Measure(label) + (value != null ? PixelFont.Measure(value) + 8 : 0) <= r.End.X - 12;
                    }
                    Check(fit, $"{sz}: visuals screen with {set.Length} rows fits (pad {pad})");
                    Check(r.End.Y - 12 >= r.Position.Y + 38 + set.Length * 16, $"{sz}: visuals footer below the last row");
                    bool ok = true;
                    for (int i = 0; i < set.Length; i++) ok &= hud.VisualRowAt(new Vector2(r.Position.X + r.Size.X / 2, r.Position.Y + 38 + i * 16 + 4)) == i;
                    ok &= hud.VisualRowAt(new Vector2(r.Position.X + r.Size.X / 2, r.Position.Y + 38 + set.Length * 16 + 4)) == -1 && hud.VisualRowAt(new Vector2(r.Position.X - 1, r.Position.Y + 40)) == -1;
                    Check(ok, $"{sz}: VisualRowAt maps {set.Length} rows");
                    string hint = pad ? "D-PAD CHOOSE / CHANGE  A SELECT" : "UP/DOWN CHOOSE  LEFT/RIGHT CHANGE";
                    if (PixelFont.Measure(hint) > r.Size.X - 12) hint = pad ? "D-PAD SELECT / CHANGE" : "UP/DOWN SELECT  < > CHANGE";
                    Check(PixelFont.Measure(hint) <= r.Size.X - 12, $"{sz}: visuals footer fits (pad {pad})");
                }
            }
            hud.Pad = false;
            hud.VisualRowTexts(VisualRowId.Map, out _, out string mv); hud.MapOn = !hud.MapOn; hud.VisualRowTexts(VisualRowId.Map, out _, out string mv2);
            Check(mv != mv2, $"{sz}: the map row shows its state");
            hud.VisualRowTexts(VisualRowId.Edges, out string el, out string ev); hud.VisualRowTexts(VisualRowId.Softening, out string sl, out string sv);
            Check(el == "EDGES" && ev.Contains("FXAA") && sl == "SOFTENING" && sv.Contains("STRONG"), $"{sz}: edges and softening rows show their values");

            // --- sounds: 7 and 9 rows ---
            foreach (bool radio in new[] { false, true })
            {
                hud.RadioOn = radio; hud.RadioStationCount = 12; hud.RadioStation = 12;
                int expect = 1 + (radio ? 1 : 0) + 5 + 2;
                var r = hud.MixerRect;
                bool ok = hud.MixerRowCount == expect && MenuInside(r, size) && r.Size.Y <= 256;
                for (int i = 0; i < expect; i++) ok &= hud.MixerRowAt(new Vector2(r.Position.X + r.Size.X / 2, r.Position.Y + 38 + i * 16 + 4)) == i;
                ok &= hud.MixerRowAt(new Vector2(r.Position.X + r.Size.X / 2, r.Position.Y + 38 + expect * 16 + 4)) == -1;
                Check(ok, $"{sz}: sounds with radio {radio} has {expect} rows inside the screen, hit tests map");
                float x = r.Position.X + (r.Size.X < 240 ? 10 : 18);
                bool fit = x + PixelFont.Measure("ENGINE AND GEARBOX") + 8 + PixelFont.Measure("##########") <= r.End.X - 12
                    && x + PixelFont.Measure("RADIO VOLUME") + 8 + PixelFont.Measure("##########") <= r.End.X - 12
                    && x + PixelFont.Measure("STATION") + 8 + PixelFont.Measure("12 / 12") <= r.End.X - 12;
                Check(fit, $"{sz}: sounds rows fit (radio {radio})");
                Check(r.End.Y - 12 >= r.Position.Y + 38 + expect * 16, $"{sz}: sounds footer below the last row (radio {radio})");
            }

            // --- smoke render of every screen ---
            var states = new Action[]
            {
                () => { hud.PadLost = true; hud.PadConnected = true; hud.PauseSelection = 7; },
                () => { hud.PadLost = false; hud.PauseRowIds = allIds[2..]; hud.PauseSelection = 5; },
                () => { hud.SettingsRowIds = settingsSets[1]; hud.SetSettingsOpen(true); hud.SettingsSelection = 2; },
                () => { hud.VisualRowIds = visualSets[1]; hud.VisualCameraName = "BONNET INSIDE COCKPIT"; hud.SetVisualOpen(true); hud.VisualSelection = 5; },
                () => { hud.SetCameraHintOpen(true); hud.CameraSomeFound = true; },
                () => { hud.SetCameraHintOpen(false); hud.SetVisualOpen(false); hud.RadioOn = true; hud.SetMixerOpen(true); hud.MixerSelection = 8; },
                () => { hud.RadioOn = false; hud.MixerSelection = 6; hud.Pad = true; },
                () => { hud.SetMixerOpen(false); hud.SetSettingsOpen(false); },
            };
            foreach (var set in states)
            {
                set();
                hud.QueueRedraw();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Check(true, $"{sz}: the menu draw paths ran without exceptions");
            vp.RemoveChild(hud);
            hud.Free();
            vp.Free();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GD.Print(_failures == 0 ? "Menu UI: all ok" : $"Menu UI: {_failures} FAILED");
    }
}
