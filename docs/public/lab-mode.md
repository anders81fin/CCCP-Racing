# LAB mode

Enable LAB MODE in SETTINGS before starting or restarting a run. It tunes car
parameters during play and is separate from competitive record runs. LAN races
use their normal parameters rather than LAB tuning.

In a LAB run, **F2** (controller **Y**) opens tuning over the race in slow motion.
SETTINGS > LAB TUNING opens it over a paused race. The first group includes slow
motion, RESET ALL and COPY DIFF. Other groups expose the car's parameters.

| Action | Live keyboard | Paused keyboard | Controller |
|---|---|---|---|
| Select row | PgUp / PgDn | Up / Down | D-pad up / down |
| Adjust value | Home / End | Left / Right | D-pad left / right |
| Change group | Comma / period | Comma / period | LB / RB |
| Step size or action | Insert | Enter | A |
| Reset row | Delete | Delete | X |

Changed values are saved in `lab.cfg`. COPY DIFF also writes `lab-diff.txt`
beside the settings, listing differences from defaults. Fuel presets provide
starting values for tuning. A normal run restores normal car parameters.

LAB runs do not write competitive lap records, medals or lap/world recordings.
They are not a sandbox for every setting: normal startup settings and some
other persistent game systems still operate. Audio is not slowed with the
simulation, and the car's visual model is not rebuilt as geometry sliders move.

Implementation: `src/Lab/`, `src/Race/RaceScene.Lab.cs` and `src/UI/HudLab.cs`.
After changing `CarParams`, regenerate its parameter descriptions:

```sh
python3 tools/lab/gen_car_docs.py
dotnet build
godot-mono --headless --path . res://scenes/tests/PhysicsTest.tscn -- --lab-tests
```
