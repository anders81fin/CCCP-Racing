# CCCP Racing

Rally-Sport-inspired time trial on a Soviet kolkhoz. Power: ACCORDING TO PLAN. Suspension: NYET.

Source code: [anders81fin/CCCP-Racing](https://github.com/anders81fin/CCCP-Racing).
Source for version **0.871**: [v0.871](https://github.com/anders81fin/CCCP-Racing/tree/v0.871).
GitHub account: [anders81fin](https://github.com/anders81fin).

Source code is offered under **GPL-3.0-or-later** and game models, images, track
data, texts and sounds under **CC BY-SA 4.0**, to the extent of rights held by
Antti Korsumäki. Where required by the content license, credit Antti Korsumäki /
CCCP Racing and indicate changes. Both licenses allow commercial use under
their terms. Copyright remains with the respective rights holders.

Development and content creation involved substantial generative AI assistance,
including Claude Code and OpenAI Codex. License grants apply only to rights
held by the licensor; they do not create copyright in otherwise unprotected
AI-generated material or restrict uses that need no permission under law.

**Music is excluded from both licenses.** Music copyright remains with Juha
Koskikuru and it is used with his permission. No additional rights to his music
are granted by this change. Do not assume permission to include it in a public
source archive, fork or other project; obtain permission or omit/replace it.
See [LICENSE](LICENSE), [GNU GPL](COPYING), [CC BY-SA 4.0](CC-BY-SA-4.0.txt)
and [CREDITS.md](CREDITS.md).

When distributing compiled versions under the GPL, provide the complete
corresponding source and build scripts in accordance with GPL section 6. For
downloads, offer the matching source alongside the binaries with clear access
instructions. A source release must not contain private credentials or local
user data, and music requires its own distribution permission. This checkout's
public repository starts with a source snapshot, without the private development
history that contains the music. Music recordings and their import metadata are
omitted from the public repository and source archives. The original development
checkout retains them for the official builds.

To build from source, install Godot **4.7.2 .NET** and the **.NET 10 SDK**. The
commands below use `godot-mono`; if your Godot .NET executable has another name,
adjust it (and `play.sh`) accordingly. Run `./play.sh --windowed`, or open
`project.godot` in Godot .NET and build/run the project. The public source works
without the excluded music; the radio has no music stations and missing music
loads may be reported. Models, tracks and synthesized game sounds are included.

User and developer documentation: [docs/public/README.md](docs/public/README.md).
Internal development notes are excluded from the public source. Reviewed source
archives are made with `tools/source/export_public.py` using the explicit file
list `public-source-files.txt`. Do not push the original development history
to the public repository.

    ./play.sh                 # fullscreen, low-res pixels scaled to fill the screen
    ./play.sh --windowed      # in a window

Game controllers (Xbox layout, any SDL pad; Steam Controller / Steam Deck through Steam Input): RT throttle,
LT brake (and reverse), left stick or D-pad steer, A handbrake, LB clutch, Y horn, B rescue, X comments, Start pause,
Back radio, D-pad up/down power (Stakhanov). Rumble on/off: RB in the pause menu.

Controls: arrows/WASD drive, Space handbrake, Shift clutch, Ctrl horn, R rescue (voids the lap), Esc pause, Enter start,
Alt+Enter / F11 fullscreen toggle, F1 engineer's view (wheel loads, roll, resolution).
Horn: hold Ctrl. It wakes the mechanic asleep on the first bus stop's bench (within ~60 m). Park by the
shelter with a damaged car and he comes over and beats it with a sledgehammer for 10 seconds (the race
clock keeps running): the engine's health climbs evenly meanwhile and the bodywork is straightened blow by blow, as new
at the end. Pressing the throttle breaks the repair off at any point and the engine keeps the health it got so far.
PARTY MEETING settings use compact OFF/ON switches for boolean choices: Left selects OFF, Right selects ON, and Enter toggles. You can also click either half of a switch. Camera, rendering, antialiasing, stations and volume levels keep their named selections.

Car radio: Backspace on/off; choose the SONG row in PARTY MEETING and use left/right to change stations. Stations are the mp3 files in
`assets/music/radio/` (sorted by file name; the file name is shown as the song title).

Title menu: SINGLE PLAYER (DRIVING SCHOOL, SOCIALIST RALLY, STAKHANOV LAPS), MULTIPLAYER
(LAN / INTERNET), PARTY OFFICE (KGB FILES: PERSONNEL, LEADERBOARDS with the 10 fastest laps and their drivers,
RECORDINGS; CREDITS; RESIGN); a hint
box explains the selected mode, Down selects the replay of the record lap. The name is asked when a rally or Stakhanov laps are
chosen on the title; a top-10 lap goes on the board under it at once, the race going on. The boards are stored in
`user://leaderboard_<track>*.cfg`. In-race messages go by importance (docs/comments.csv):
TAB (X on a pad) turns off only the jokes.

LAN RACE (title menu): up to 6 players race on the local network with the rally's moving
vehicles and hazards. Cars collide with one another and the world; damage affects engine power,
and a wreck respawns after ten seconds. The host simulates all cars and actors and validates laps;
clients predict their own steering and reconcile with the host. One hosts, the others join from the list
of games found on the network or by typing the host's IP. Players vote for the track
from those opened by the host's medals, plus power, damage and fuel. Ties use a lottery.
The connection stays intact while all machines load the selected track. Race results lead
to session standings by victories; the host chooses another race or ends the session
with a shared champion screen. The host's firewall must allow UDP 24105
(game) and 24106 (discovery beacon); on Linux with ufw: `sudo ufw allow 24105:24106/udp`.
LAN laps never touch the ghost or the leaderboards. All players need the same LAN protocol and track.
Technical details and repeatable checks: [LAN documentation](docs/public/networking.md).

Tracks: KOLKHOZ NO.3 ILYICH'S LAMP (tracks/ilyich.json; the barn start, the ridge and jump, the ford,
the church hairpin), SIBLAG NO.2 FIVE YEARS IN FOUR (SIBERIA in the menus; tracks/siberia.json, opened by a medal on Ilyich's Lamp: winter,
driven anticlockwise, ice, packed and deep snow, soft snow banks, the frozen river, the airbase
with its MiKs, the labour camp, snowploughs), ATOMGRAD-4 (tracks/atomgrad.json, opened by a medal
on Siberia: the zone round a power plant in a rainy autumn, weather changing lap by lap, radiation
dose and the decontamination wash, the train, the lunokhod, concrete mixers) and BONUS
(tracks/bonus.json), opened by a gold on any track. A rally bronze opens
the next track for STAKHANOV LAPS, its SOCIALIST RALLY takes a silver. The track card in STAKHANOV LAPS and
the rally chooses, opening on the first track, a locked one in black and white (LAN votes among the host's available tracks). The DRIVING SCHOOL (SINGLE PLAYER) teaches car control on Ilyich's Lamp: the clutch start,
steering, brakes, the slide techniques, R. Each track has its own rally school, offered before a
rally there, with only what is new on it (Ilyich's Lamp and the bonus track so far); Stakhanov laps have none. The title's LEADERBOARD and record-lap replay cover every track. Medals per
track in user://campaign.cfg (bronze 4 norms, silver 7, gold the plan; the norms are beaten one a lap,
the laps saved go on to the next norm, at most 10); the GAI speed trap's
records in user://speedtrap.cfg. In the rally the Kozlik's bear may wait in ambush past the radar
and chase a speeder (once a lap); caught, both stop and he asks for an autograph.

The plan (SOCIALIST RALLY): the liaison officer's lap-time norms, per track (bonus track 1:15 down to
0:52.50, Ilyich's Lamp 1:20 down to 0:57.50, Siberia 2:00 down to 1:27, Atomgrad-4 1:07 down to 0:48.50), five laps for each (a fast lap can skip several and brings their laps along; at night the climate change allowance eases the
norm). Stop cards after every lap, at 25 % fuel or engine and when a revenge begins explain what
to do. The rally ghost drives the session's best lap. Day and night come round every 12 laps in
every mode. Fuel: a 50 l tank, babushkas'
20 l canisters (a rare golden OCTANE BOOSTER canister gives 3.5 times the fuel's power for 20 s), the depot by the start line.
The rally's FUEL is chosen after the track: DIESEL (a tractor engine of its own: 200 Nm low and wide, a 1050 kg car, a 5000 rpm
redline with four close gears, a duller knocking voice; drinks 0.6 times as much), PETROL (200 Nm, a 1050 kg car, a 7000 rpm redline, five gears, as the game has always been) or
JET FUEL (280 Nm and an 80s turbo that lifts it to 400 Nm, a 1000 kg CCCP red car; drinks 1.7 times as much) or SPUTNIK (the jet fuel's engine
on all four wheels, 40 % of the force to the front and 60 % to the rear, 900 kg, a white car; drinks 1.9 times as much). PETROL opens with a silver on the
bonus track in the rally on DIESEL, JET FUEL with a silver there on PETROL and SPUTNIK with a silver there on JET FUEL. The norms are set at each fuel's pace
(the ladder times 1.02 for diesel, 0.97 for jet fuel and 0.95 for the Sputnik, per track in `tracks/ID.json` "norms"), and each fuel has its own record
lap, ghost, board and medals (a medal on any fuel opens the next track). Note that the top speed is about the same on all three (155-159 km/h):
the fuels differ in how the car pulls out of corners (the diesel with torque from the bottom, the jet fuel with power).
STAKHANOV LAPS ask CHOOSE FUEL after the track and before TIME OF DAY: the same four fuels and locks (no medals, no tour, not saved);
keys 1-4 (a pad: D-pad up/down) still switch the fuel while driving, as the card says.
On CHOOSE TRACK, L (pad: X) opens only the highlighted track's boards for the chosen mode.
Rally shows its four fuel boards; Stakhanov uses Left/Right to browse fuel top tens. Esc (pad: B) returns to the same track card.
T (pad: Y) still opens the track tour.
BEST and LAST show the current session's times for the fuel currently selected; starting or restarting a run clears them.
Each track keeps a separate Stakhanov top 10 for each fuel. A mixed-fuel lap belongs to the highest fuel used during that lap.
The small board at the top right shows places 1, 2, 3 and 10. PARTY MEETING > LEADERBOARDS opens the current track and fuel;
Left/Right browses the other fuels without changing the car or track, and Esc (pad: B) returns to the paused meeting.
Existing known-fuel results are retained; unknown legacy results stay unclassified. The first upgraded save preserves the original
Stakhanov board as `leaderboard_<track>_stakhanov.cfg.before-fuel-top10`. LAN sharing includes all fuel boards in the lobby.
T (a pad: Y) on the rally's FUEL card plays the car of the highlighted fuel as a CAR TOUR (RaceScene.CarTour.cs, the scripts in CarTourScript.cs): a black card, close-ups with a dry caption each, the engine revved and lifted so that its voice, whistle, pops and smoke are heard, and a card of its data. A locked fuel's tour can be watched but cannot start a run; nothing is raced or recorded. Test hooks: `--car-tour=diesel|petrol|rocket|sputnik` (with `--car-tour-nocard`, `--car-tour-at=SECONDS` and `--car-tour-check`).
Beat the last norm and you move on (the next track is under construction); run out of laps, kill the engine
(it has health: crashes hurt it, smoke at 25 %, oil at 0 %) or get the car destroyed
(tank shell, missile, a crashing MiK-21) and the truck takes you to the gulag. Training laps
are forgiving. Two of the babushkas are somebody's mother — you'll find out.

Changing the road's shape archives the old record and leaderboard as `*_v02` files (never
deleted); `--data-dir=PATH` points the game at another data folder for tests.

Physics tests (headless):

    dotnet build && godot-mono --headless --path . res://scenes/tests/PhysicsTest.tscn

Tweak the car in `src/Car/CarParams.cs`, the track in `tracks/bonus.json`.
Best lap ghost is stored in `user://ghost_<track>.bin` (poses plus steering, pedals, revs,
springs and tyre state and the time of day, so the replay and the ghost show the same effects; old pose-only
files still load and have that state reconstructed).

Upside down? A MT-Agro comes and tows you back onto your wheels; the wait is the penalty
(R still puts you back on the road at once). Wooden fences break, stone walls do not.

Release builds (export templates 4.7.2.stable.mono in ~/.local/share/godot/export_templates):

    godot-mono --headless --path . --export-release "Linux"   build/linux/CCCPRacing.x86_64
    godot-mono --headless --path . --export-release "Windows" build/windows/CCCPRacing.exe
    godot-mono --headless --path . --export-release "macOS"   build/macos/CCCPRacing.zip

Each build is the executable plus CCCPRacing.pck and a data_* folder (.NET runtime); ship the whole folder.
`tools/package.sh [linux] [windows] [windows-d3d12] [macos]` exports and zips them for release, with LICENSE, COPYING, CC-BY-SA-4.0.txt, CREDITS.md and
the third-party licenses (Godot, .NET: MIT requires the texts to travel with the game) next to the game.
The default exports all four packages under `build/releases/VERSION/`. Windows Vulkan and Direct3D 12
are separate downloads, published to itch.io channels `windows` and `windows-d3d12` with the same version.
The `steam-artwork` folder travels with every platform package and includes library covers, a hero
background, a transparent logo, PNG/ICO icons and instructions for a Non-Steam shortcut.

Top-down is always available. Alternative gameplay cameras are hidden discoveries, saved per track
in the active personnel file. C (pad: LB) cycles only the cameras found on the current track.
Ilyich's pond duck unlocks the bonnet view when driven over; other camera finds are off the racing line.
