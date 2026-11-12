# Changelog

What each version changed compared to the one before. The game shows this file from the
title menu (CHANGELOG); keep to the pixel font: letters, digits and . , : ! ? - + = / % " ( ) < > # * ´

## 0.89

- ATOMGRAD-4 has no fuel consumption or fuel stops. The radiation dose collected gives up to 15% extra engine power and gradually damages the engine. Washing removes the dose, the mechanic repairs the engine. The four engines and medal norms stay the same
- Atomgrad school now covers the track, radiation and repairs
- LAN on Atomgrad follows the no-fuel rule. The radiation dose and power bonus are shown for the host and joining players when damage is on, and cleared for the next race
- Lowered the surrounding ground and distant landscape on Atomgrad, Poligon and Siberia so the road sits more naturally in its surroundings. Road heights and saved record signatures stay the same
- Internal race scene refactoring and fixes to the public source build

## 0.88

- The order of the tracks changed: a medal on Kolkhoz opens ATOMGRAD-4, and a medal on ATOMGRAD-4 opens SIBERIA. A track you already have a medal on stays open
- New medal norms on several tracks and fuels (the bronze norm is the 4th, silver the 7th, gold the 10th). Kolkhoz diesel 0:55.00, 0:50.00, 0:47.00 and petrol 0:53.00, 0:48.00, 0:41.00. Siberia diesel 1:20.00, 1:14.00, 1:07.00, petrol 1:18.00, 1:12.00, 1:05.00, jet fuel silver 1:09.00 and gold 1:00.00, Sputnik silver 1:05.00 and gold 0:58.00. Atomgrad petrol 0:53.00, 0:48.00, 0:43.00. Poligon diesel 1:10.00, 1:02.00, 0:55.00. The steps in between were spaced evenly, see NORMS AND MEDALS in the pause menu
- The climate allowance for the dusk and night laps is now of the same size compared to the lap time on every track: on Kolkhoz night +2 s, dusk +1 s, sunset +0.5 s, dawn +0.75 s, and Siberia x1.33, Atomgrad x0.86 and Poligon x1.17 of these. Diesel gets the same seconds as the other fuels (it used to get 2/3.5 of the petrol seconds). The CLIMATE CHANGE ALLOWANCE alert shows the seconds that really apply
- The release build is smaller: the developers´ test code is no longer in the game package
- Removed the trailer capture options --trailer-shots, --trailer-v2 and --marketing-shot (the tour captures --drone-tour and --cinematic-tour stay)

## 0.871

- The driving school and the rally schools match the current tracks: the Kolkhoz lessons talk about the one jump near the start, the landing crest and the ford (the old ridge of rollers is gone), and the landmark tour stops at the jump and the ford
- The driving school: no bus or other road user from the rally world stands on the road in the braking and drifting lessons any more
- Siberia school: six chapters (TRACK, WINTER ROAD, FROZEN RIVER, FUEL, REPAIRS, GRANDMA). Atomgrad school: four chapters (TRACK, RADIATION, FUEL, REPAIRS). Texts on towing (the lap is void, -1 lap of the norm), the gold can (3.5x power, 20 seconds) and the depot match the game
- Main menu background: a new recorded lap, 53 seconds, with four car changes
- Tool: --import-take=DIR puts a recorded title take into the lap archive for the replay viewer and the movie export

## 0.87

- The launch card says DEVELOPMENT BUILD (was BALANCED LEARNING CURVE VERSION)
- Replays opened from the leaderboards play the right recording: the Diesel and Petrol columns had swapped places (the Diesel rows opened the Petrol laps and the other way round)
- A replay car has its own paint: it no longer takes the colour of the car that was on the title background when the replay was opened (the Petrol car looked white or olive)
- Siberia: the automatic replay camera no longer stands behind a snow bank or inside a tree, it climbs a mound or leaves the shot to a moving camera
- Atomgrad: the crawler crane jib points forwards along the tracks and no longer cuts through the sarcophagus wall, and the sarcophagus roof rests on its walls (the high end hung in the air)
- Poligon: the earth firing positions of the tank exercise are a low earth bank with rounded ends and two track marks (they were a chain of balls and sleeper-like strips)
- The mechanic marker in the world is mirrored
- Replay: F12 takes a screenshot in every camera mode, not only in the free camera
- A replay always opens in the automatic broadcast camera
- Main menu background: a recorded lap on the kolkhoz with the rally´s moving world, the car damage and the Stakhanov laps´ fuel keys. Each change of the car (diesel, petrol, jet fuel, Sputnik) brings the next hour of the day, from noon to night. The title plays it with its world and the car of each moment (the older demonstration is the fallback). Recording tool: --title-rec=DIR, then the promote script in tools/title
- The replay´s AUTO camera is a rally TV broadcast. The cuts of the lap are planned from its path and come on the action: a roadside telephoto on the outside of corners and hairpins (it pans with the car and zooms to hold its size), a head-on camera and a wide low fly-by on the straights, a camera at the lip of every jump, one on the bank of the ford, a camera beside a slide, and between them the bonnet, a close chase, a low spoiler camera, a helicopter circling the car and the top-down view. Works on every track, a seek, a pause and the movie export show the same picture
- Siberia: the ZIL that comes for a wrecked car in the labour camp stays on the road and no longer drives over the barracks, towers and wire
- The mechanic´s map symbol is the spanner alone (the sickle is gone)
- Kolkhoz: the grain delivery by the church stands on a level bench instead of the knoll´s slope, and the trailer is one rigid body (it tilted board by board and left the sacks under the bed)
- Kolkhoz: the camera at the Lenin monument is turned to face the church, and the bonnet camera of the duck stands on its tripod on the duck´s back until found
- Kolkhoz: lantern posts round the dacha´s pond light the bank at night
- The Socialist Rally and the Stakhanov laps ask for the fuel first, then the track. In the rally each fuel has a progression of its own: a track opens by a silver on the previous track with the chosen fuel (what the Diesel opened is not open on the Petrol), the Poligon by a gold with it. In the Stakhanov laps a bronze with the chosen fuel opens the next track for that fuel
- New fuel unlocks: the Petrol opens with a silver on all of the first three tracks on the Diesel, the Jet Fuel with a silver on all four tracks on the Petrol, the Sputnik with a silver on all four tracks on the Jet Fuel. The lock texts tell this
- The tractors´ wheels turn as they drive (also in the replay), and the bus wheel´s top no longer flickers through the body
- The drunks drink in a smooth movement (the arm lifts, the head tips back, the bottle turns neck up) and the bottle has the shape of a bottle
- Atomgrad: the radiation eats the engine. The stronger it is, the faster the health goes: the background costs nothing, a pass through a hot spot a few per cent, and standing in the hottest one off the road kills a new engine in half a minute. Only in the rally
- The Sputnik´s engine sounds are 30 % louder than the other cars´
- Every car trembles at idle in the tour and on the road, in the way of its engine: the Diesel´s four slow and lumpy, the Petrol´s and the Jet Fuel´s four even and fine, both at half the strength of the Sputnik´s five-cylinder
- The BONNET camera lives with the speed too: a fine shake, a slow sway, a lean into corners and a dip for braking, none of it standing and the most at a high speed (the camera shake setting turns it off)
- In a slide the CINEMATIC camera swings round hard with the car like a rubber band (a little late), so the car is easier to keep under control
- The CINEMATIC camera is a close chase camera behind the car, so you see where you are among the others and how the car slides. It follows with a soft delay (it lags in corners and on a launch), leans into corners, dips when you brake, and shakes and sways with the speed: almost still when you stand, at its most at a high speed (a slide and the ground add to it)
- The radio is on by default in the Stakhanov laps and in a LAN race, as in the rally (what you have chosen yourself is kept)
- The Jet Fuel car weighs 1000 kg (was 1050)
- Tail pipes: the Diesel has one (wider) pipe, the Jet Fuel four, two on each side of the rear. The smoke and the backfire flames come out of the same pipes, and the pipes dent, hang crooked and fall off in a hard hit from behind
- The fuel cards and the car tour give the cars in real units: torque in Nm, mass in kg, revs in rpm, and the tour´s data card also tells the gearbox (four-speed for the Diesel, five-speed for the others). No more power percentages
- The slurry revenge has a card of its own (what is coming, what to do) and a counter in the corner: SLURRY: 3 LAPS LEFT, then 2, then LAST LAP
- LAN: the secret ballot offers the tracks and fuels of the player who has opened the most of both (each player sends what he has opened when he joins, a tie goes to the host, then to the lowest slot). The ballot shows only the offered fuels, and the offer follows who is in the lobby. LAN protocol 17: every player needs the same version

## 0.863

- Engines: Petrol 200 Nm (was 175). Jet Fuel and Sputnik have 140 % of the power and 200 % on the turbo (was 175 % and 250 %)

## 0.862

- Fixed a crash: switching the fuel to Diesel from fifth gear in the middle of a run threw an error every frame (the diesel has four gears). The gearbox now drops to the highest gear it has
- Kolkhoz: one of the babushkas is the slurry tractor driver's grandmother. Run her over and the tractor leaves its fields, drives along the road and spreads slurry for three laps, then goes back to work
- The replay shows the slurry tractor's spray and its patches on the road (a patch is restored by its age when you seek)
- Slurry and the oil patches in the Poligon hall are now really slippery (far under the ice), and the film stays on the tyres for a few seconds after you have left the patch. Water washes it off faster
- The oil patches in the Poligon hall are no longer pitch black under the headlights
- The bottles of the drunks and the mechanic are held across the fist, at a right angle to the arm
- The leaderboards list Diesel first. Jet Fuel and Sputnik have the petrol car's torque again, with their 175 % power and turbo on top
- New gold norms: Kolkhoz 0:44 / 0:41 / 0:39 / 0:36, Siberia 1:07 / 1:00 / 0:58 / 0:56, Atomgrad 0:45 / 0:40 / 0:35 / 0:33, Poligon 0:57 / 0:52 / 0:49 / 0:46 (Diesel, Petrol, Jet Fuel, Sputnik), and the other norms follow in the same proportion
- Poligon: the tanks hold their fire while you are inside the base. The camp gate opens for a horn heard from 45 m, even when the car is still moving
- Atomgrad: the switchyard box no longer stands in the road

## 0.861

- The cars changed so much that the records of the earlier versions no longer compare: on the first start the ghosts, leaderboards and replays of every track are archived (renamed, not deleted) and the boards start empty
- Atomgrad: the power plant's switchyard box no longer stands in the road at the north-east corner of the plant
- Poligon: the revenge tanks hold their fire while the car is inside the base, and fire again once it is out
- Poligon: the guard opens the barrier as soon as he hears the horn, 45 m from the gate, even when the car is still moving

## 0.86

- The launch card is now BALANCED LEARNING CURVE VERSION, V. 0.86
- New engines: Diesel 200 Nm, 1050 kg. Petrol 175 Nm, 1050 kg. Jet Fuel 180 Nm with the 175 % power and the turbo on top. Sputnik 200 Nm, 900 kg, four-wheel drive
- Diesel norms: the last norm (gold) is 5 s slower on every track and the rest of the ladder follows in the same proportion. Petrol, Jet Fuel and Sputnik keep their old ratio to the next slower fuel on every norm
- LAB: switching the fuel in the middle of a run loads that fuel's own preset as the sliders, not the last fuel's values. Jet Fuel has a preset of its own
- Diesel gearbox: four close gears and a shorter final drive (top speed about 117 km/h), so the engine can climb the hills of Siberia and Poligon in third and fourth. Jet Fuel and Sputnik have their own, shorter first gears
- Smoke is drawn over water, and the diesel smoke is more see-through, so the car shows through the cloud

## 0.857

- Diesel sets the norms on every track: each better fuel (Petrol, Jet Fuel, Sputnik) is exactly 1 s tighter on every norm. Siberia Diesel runs from 1:30.00 down to 1:03.00
- Fuel use: Diesel 0.5, Petrol 0.8, Jet Fuel 1.0, Sputnik 1.2 of the tuned rate
- Every vehicle on the road smokes like the diesel car, bigger engines more: tractors, buses, lorries, snow ploughs, concrete mixers, BTRs, tanks and the BUK battery. MiGs and Hinds leave a pale kerosene haze, the MiG thicker on the afterburner. Petrol, Jet Fuel and Sputnik cars have a thin exhaust of their own, and the camp stovepipes smoke. All of it works in LAN races and LAN replays too
- The men of the Siberia prisoner column fall over when a car hits them, like the kolkhoz workers do
- The radio starts at 50 %, which is the old full volume. The title music changes at every launch and goes through all the songs. The horn is twice as loud, and tank and BTR rumble keeps growing as you get close
- A new rally run starts with LAST and the ghost cleared. A rally save made under older norms is discarded with a notice. The mechanic sickle is 40 % larger

## 0.856

- CHOOSE TRACK offers L LEADERBOARDS (controller X) for the highlighted track and mode. Rally boards follow Diesel, Petrol, Jet Fuel, Sputnik. Stakhanov always opens on Diesel and browses each fuel's top 10
- The lobby uses a compact SYNC LEADERBOARDS OFF/ON switch. Left selects OFF and Right selects ON. Consent still applies only to the current session, and synchronization remains limited to the lobby
- PARTY MEETING uses matching OFF/ON switches for visual visibility choices, softening, LAB MODE, RUMBLE and RADIO. Arrows select the state directly, Enter toggles, and either half can be clicked
- Menu cards leave an empty row between the heading and help text. Mode descriptions, Driving School, KGB ARCHIVE and LAN / INTERNET instructions are clearer and shorter, with firewall ports shown in the network menu
- The mechanic emblem has a larger spanner and smaller sickle. Red dashboard alarm beeps use half their previous signal level

## 0.855

- PARTY OFFICE groups PERSONNEL, LEADERBOARDS and RECORDINGS under KGB FILES. Multiplayer is named LAN / INTERNET
- STAKHANOV LAPS offers CHOOSE FUEL between track and time of day. The chosen engine starts the run, also after changing tracks. The card explains switching fuel while driving
- Stakhanov BEST and LAST show the current session for the selected fuel. Each track keeps a full top 10 for each fuel. Mixed laps use the highest fuel used, and old known-fuel results are preserved
- PARTY MEETING offers LEADERBOARDS on the current track. Browse fuels without changing the car, and return to the paused meeting. The compact live board shows places 1, 2, 3 and 10
- SHARE BOARDS includes all Stakhanov fuel boards and their recordings. LAN protocol 16 requires matching game builds. Older LAN replays remain supported
- Fuel cards use shorter descriptions: Diesel torque, rev limit and economy, standard Petrol and the secret SPUTNIK AWD prototype

## 0.854

- LAN SECRET BALLOT gives 60 seconds to choose the track, fuel, damage, fuel use and moving obstacles. Missing ballots say NOT VOTED. With no votes, every choice is drawn at random
- LAN offers a local choice to record races in the background. After the finish, KEEP saves a copy instead of opening a replay during the session. Recording adds no extra network traffic
- SHARE BOARDS exchanges leaderboard results and their attached recordings only between players who agree. Lobby only: the ballot waits for transfers and disk work to finish. Resync does not duplicate results
- Fixes to joined players seeing the Siberia crash site as a black slab and to rotating beacon beams on moving vehicles. Moving obstacles can be switched off for a quieter LAN race
- Single-player replays show their lap number and follow lap order across recording folders. Pinned recordings always come first, with lap order kept inside each group
- Replay EDIT / EXPORT moves from E to V and works in free camera. E still raises the camera. Camera motion and audio cleanup are more reliable
- VISUALS offers PIXEL or NATIVE rendering. NATIVE renders the 3D at the window resolution and enables CAPITALIST LUXURIES. The Godot startup splash is restored
- MP4 export is available on Windows 64-bit as well as Linux x86_64. Windows needs ffmpeg.exe with H.264 and AAC beside the game or on PATH. Vulkan and Direct3D 12 remain separate downloads
- Diesel norms on Ilyich and the bonus track are adjusted, with a limit on the climate allowance

## 0.853

- Source code is available at github.com/anders81fin/CCCP-Racing under GNU GPL version 3 or later, to the extent of rights held by the licensor
- Game content is offered under CC BY-SA 4.0 to the extent of rights held by the licensor. Music remains copyright Juha Koskikuru and is used with his permission, outside both licenses
- CREDITS names the GitHub account and source repository, explains the original player car model and its reconstruction, and identifies the Blender, procedural and AI-assisted work

## 0.852

- SPUTNIK on the STAKHANOV LAPS: key 4 (D-pad up and down on the pad) picks it when the car is open. A Stakhanov lap remembers which fuel was in use when, and the replay viewer shows the car of the moment (wing, kit, paint and voice). Laps from before this show as petrol
- Water takes the speed: the river drags the whole car by how deep the tyres stand in it, flying or not, and soaks up a landing. No more splashing through at full speed and hopping off the river bed
- Grass and dead grass resist the car more. Driving off the road onto the grass now costs real speed (fields and mud as before)
- The four auxiliary lamps of the SPUTNIK light up after dark and throw a long beam
- The exhaust pipes stand half as far out of the rear panel, and the fat pipe of the SPUTNIK is one and a half times as wide. The smoke, the puff and the glow follow
- The secret on the title menu opens every camera on every track, and it works on the Steam Deck pad too

## 0.851

- A small secret on the title menu. Old gamers know the code. The same code takes it back

## 0.85

- CAR TOUR: T (pad: Y) on the FUEL card plays the car of the highlighted fuel as a show. A black card that waits for a key, close-ups with a dry caption each, the engine revved and lifted so that you hear its voice, whistle, pops and smoke, a gauge for the revs and the boost, and a card of TECHNICAL DATA. A locked fuel can be watched but cannot start a run. Esc goes back to the card
- The wheels are in relief: a flat lip, a faceted well with a ventilation slot in every facet, a stepped hub with bolts and a dark centre hole
- SPUTNIK from behind: one fat exhaust pipe off the middle and a thin wastegate pipe that sighs when you lift off, five slits in the rear panel and a small 5 plate, the pipe glows under boost, the body trembles at idle in a beat of five (the wheels stay still) and the anti-lag crackles in fives
- The spare wheel has a wheel of its own inside the tyre, and its straps run lengthwise over the tread and are fixed to the two rack bars by the windscreen and the rear window
- The FUEL card no longer tells the colour of the car or the time windows of the norms
- The start line in the barn follows the ground and stops at the edge of the road: it no longer hangs in the air by the walls
- The key that releases the black card of a car tour, Enter included, no longer starts a run by accident

## 0.84

- SPUTNIK drives like a Group B rally car: a viscous centre lock moves the power to the axle that grips (up to 35 points), the turbo spools up faster and then hits with a kick, the engine is a five-cylinder with an uneven beat, and a lifted throttle makes the pipe crackle and pop all the way down. The lab has sliders for all of it (CENTERLOCK, TURBOSPOOL, TURBOKICK)
- SPUTNIK looks the part: no bumpers, brake cooling slats ahead of the front wheels and behind the rear ones and on the sides, louvres in the bonnet, side skirts, four lamps at the nose, an air scoop on the roof, a dark wing and no roof rack or spare. JET FUEL runs without bumpers too
- Every fuel has its own rear wing: DIESEL a low dark grey duck-tail lip, PETROL a low wing, JET FUEL a middle one and SPUTNIK a high one. The end plates carry a red star. A hard hit from behind can tear the wing off, a hard hit on the roof the roof rack with the spare
- The car of DIESEL is the olive of the duty officer UAZ. DIESEL smokes black all the time and more with the throttle: at a standstill the cloud starts at the pipes and spreads round the car
- The fender flares join the body properly. The spare wheel is redrawn: a dished wheel inside the tyre and straps fixed to the two rack bars
- Fix: the leaderboard crashed when it drew the SPUTNIK records. Old recordings of the lap play with their damage on the new car body

## 0.832

- New fourth fuel: SPUTNIK. The engine of JET FUEL on four wheels (40 % of the power to the front, 60 % to the rear), a white car, its own records and norms (the fourth block on the leaderboard). It opens with a silver on the bonus track in the rally on JET FUEL. A rally fuel only: STAKHANOV LAPS and LAN keep three fuels
- ROCKET FUEL is now called JET FUEL everywhere. Old records and settings work as before
- The car of DIESEL has the olive of the duty officer UAZ
- LAB MODE: the DRIVEFRONT slider (the share of the power that goes to the front wheels) and a SPUTNIK preset
- Fix: the leaderboard crashed when it drew the SPUTNIK records
- All physics tests pass again, with updated baselines

## 0.831

- The fuels have colours: the car of DIESEL is army matt green and the car of ROCKET FUEL is CCCP red. PETROL keeps the factory paint. The gold of OCTANE BOOSTER goes over them
- STAKHANOV LAPS: the keys 1-3 pick DIESEL, PETROL or ROCKET FUEL, with the engines of the rally, instead of 50, 100 and 200 %. The names are on the dashboard and the leaderboard
- PETROL and ROCKET FUEL open with a silver, not a bronze: PETROL with a silver on the bonus track in the rally on DIESEL, ROCKET FUEL with a silver there on PETROL. The same in the rally and in STAKHANOV LAPS

## 0.83

- Rally fuels have their own engines. DIESEL is a real diesel: a 5000 rpm redline, taller gears and a lot of torque low down, with a knocking, softer sound and a thick black cloud of soot from the pipes whenever you press the throttle. ROCKET FUEL is 75 % stronger than petrol, and an 80s turbo with a couple of seconds of lag lifts it to 150 % stronger. You hear the whistle, and when you lift at high revs the blow-off and a burst of loud detonations with flames
- PETROL is 25 % stronger in every mode. It is a closed fuel in the rally until you win a bronze on the bonus track in the rally on DIESEL. ROCKET FUEL opens with a bronze there on PETROL
- Fuel use at full throttle is lower, and diesel drinks much less than petrol. The norms of diesel and rocket fuel are adjusted to the new engines
- Lift-off crackle is more reliable: it also comes with a slow lift or the engine brake, and at mid speeds
- Dashboard warning lights for the engine, the fuel and the radiation dose, with a chime. Yellow is a warning and red an alarm
- Snowploughs have a rotating beacon with real lights, a cone of light, a beeper and a low rumble from the blade. The rescue tractor keeps to the road. No searchlight at the Siberian radar at night
- Jump landings are felt: the weight of the car shows in the landing. A body struck by the car makes a dull thump for every figure it knocks over
- Steering is more precise at speed (new defaults for the steering lock, its speed falloff and the fast steering assist)
- The first track has no fuel depot, as it has no fuel use
- Snowbanks join properly in tight bends. The sledgehammer of the mechanic swings in the right plane. A replay seek clears tyre marks, dust and mud
- Replay free camera: the microphone and the car sounds are at the lens, and the volume follows the distance
- The version card at launch comes up in 2.6 s instead of 6.5 s. The lap archive keeps the ten latest laps
- LAB MODE (SETTINGS): sliders for the driving model of the car while you play. F2 (pad Y) opens them over the race in slow motion, SETTINGS > LAB TUNING over the paused game. A lab run writes no records. COPY DIFF lists the changes
- Replay viewer camera keys: TAB and SHIFT+TAB step to the next and previous key, in the free camera too, where the camera moves to the key and you keep control. DEL removes the key at the clock. Leave the free camera with ESC or R

## 0.82

- Ilyich: the river is narrower and meanders at the road crossing, with natural variation along each bank. The ford itself is unchanged. The pond has scattered reed beds and dark meadow patches, and the river meadow plants spread along the banks
- Ilyich: the dacha balcony doors, attached fixtures and terrace foundations are repaired
- The Lada has its wheel stance widened so the tyres line up with the arch flares, and the cavity under the floor is closed. The inner faces of the wheels are finished
- The patrol UAZ of Kozlik is smaller. It is now only slightly larger than your Lada, and its collision and ground contact follow the new size. Old recordings with the larger UAZ play the patrol live at the new size instead
- Party Meeting REPLAY lists only the laps of the loaded track and the laps captured in the current run. A single session lap opens directly
- The direct FREE CAMERA action has no ellipsis in its name
- Ilyich: rolling meadow ground replaces the artificial rim at the edge of the world. The wheat follows the ground and the road blends into the meadow gently
- Ilyich: the main jump is now a ridge with a landing slope, away from the river bend. The old kicker at the river is a gentle crest. Old Ilyich lap records are archived and not compared with the new layout
- Ilyich: three stretches of the road are narrower and have worn, uneven edges. The start straight, the jump, the ford and the bus stop keep their width
- Replay FREE CAMERA: , and . step frame by frame. The whole camera works with a gamepad: left stick flies, right stick looks, LT and RT change height, L3 is fast, X adds a key, Y follows the car, R3 looks at the car and START opens MOTION and LENS. The top right shows the camera position, compass heading and pitch
- The bonnet camera sits at the front edge of the bonnet, so no bonnet is in the picture
- The title menu is simpler: the replay, radio and record lines and CHANGELOG are gone. PARTY OFFICE has CREDITS directly
- The handbrake turn is more dramatic: the nose swings round faster and the car goes nearly broadside, most of all on tarmac

## 0.81

- The replay viewer has a FREE CAMERA. Press E for EDIT and choose FREE CAMERA. Fly with W, A, S, D, Q and E. The mouse looks around by itself, and it is released when a card is open or the window loses focus. F follows the car and G aims at it. Left and Right seek five seconds, Up and Down change the playback speed, H hides the controls and F12 saves a clean PNG picture of the scene. The camera stays above the terrain
- Camera shots: press K to add a shot at the current time and TAB to review them. B opens CAMERA MOTION: tracking from the side, helicopter, crane, dolly and zoom, which can be combined. Shots, hard cuts included, are saved with the edits of a KEPT recording. The MP4 export uses the same shots and framing
- You can KEEP and rename a recording inside the viewer
- Laps that reach the leaderboard are saved automatically and open from the leaderboard list with their exact replay
- The car has its rear wing and the openings of its wheel arches repaired. Old recordings with damage still play the same
- The Hind has no passenger doors, windows or steps. The tank number markings are fixed and its dust trail is lighter

## 0.801

- Linux MP4 export keeps its renderer window hidden from startup, instead of opening a floating window over the game. This also addresses the window reported on Steam Deck. The export still uses the GPU and keeps its progress and cancellation controls in the game
- REPLAY and RECORDINGS can delete a selected KEPT or THIS SESSION lap. Press DELETE (pad X), then confirm. CANCEL is selected first. The recording and its saved edits are removed together. Other recordings, leaderboard times and exported videos stay
- The Socialist Rally leaderboard shows PETROL, DIESEL and ROCKET FUEL on one page, with the three fastest times for each fuel. Old lists are sorted before selecting the top three. STAKHANOV LAPS keeps its own ten-place leaderboard

## 0.80

- The replay viewer can make an MP4 video. Open a lap in RECORDINGS, press E for EDIT, then choose EXPORT MP4. Pick WHOLE or THE CUT, the size (1080p, 720p or 540p), the quality and whether to keep the sound. It shows its progress, and cancelling asks first. The video goes to the exports folder. Your recording is never changed
- MP4 export needs FFmpeg with libx264 and AAC on your computer. The game does not include it. Export works on Linux only for now. On Windows and macOS the game says that it is not supported
- LAN race: when the whole race is over, R opens the replay of your race in its own window, and Tab chooses the car to follow. Finishing early does not open it while others are still racing. The replay window cannot disturb the running game
- LAN MP4 export opens after the host ends the session. The replay window closes when the next lobby or race starts, and a running export is cancelled safely
- Fixed: holding the handbrake with full throttle no longer pushes the car along on locked wheels. A rear wheel that the handbrake holds stays held. If the engine is stronger than the handbrake, the wheel turns and only the extra force reaches the road, within what the tyre can grip
- Fixed: a handbrake slide that goes nearly broadside now loses speed like a real slide instead of gliding on for many seconds. Short handbrake turns and all driving without the handbrake feel the same as before
- Testing: replays are checked for leaking memory, and a failed autodrive lap still ends with code 3

## 0.79

- PARTY OFFICE has RECORDINGS. It lists the laps of this session (they live in memory and are gone when the game closes) and the laps you kept (saved on disk, there next time). Choose a lap to watch it again. ESC comes back to PARTY OFFICE on RECORDINGS
- Press K (pad Y) on a lap to KEEP it. You can type a name, or leave it empty for a default name. The whole recording is saved, and nothing is ever deleted or pushed out. KEEP on a kept lap renames it. KEEP works in the Party Meeting REPLAY list too
- A kept lap can be edited in the viewer. E (pad BACK) opens EDIT: set IN and OUT to cut the lap, add camera keys, loop, pin the lap to the top of the list, and SAVE. The cut and the keys never change the recording. The original car and world files stay exactly as they were
- Edits are saved beside the recording and tied to its exact files. If they do not match, are unreadable or come from a newer game, they are ignored and the game says so. RESET EDITS keeps the old ones as a backup
- Speed, the lap camera and the overlay choice are saved with the edits. Leaving with unsaved changes asks: save, discard or cancel. While a save is running the edit keys wait
- The edits load before the first picture, so a cut lap starts at IN. Cuts end exactly on OUT and loops return exactly to IN. Camera keys are in place before the frame that shows them
- Saving a record lap no longer stops the game for a moment at the finish line. Quitting right after a lap can wait a moment while it is written
- Testing: an autodrive run with a failed lap now says AUTODRIVE FAILED and ends with code 3 instead of waiting. The original Bonus autodrive still ends with a destroyed car, as before

## 0.782

- The Socialist Rally lap panel starts with the NORM row (NORM n/10 and its time). Laps left, TIME, BEST and LAST follow it in the same order as before, and the fuel, climate and wreck lines stay below. Other modes are unchanged
- The BEST line of the Socialist Rally is the best valid lap of this session. It starts empty at every new start or restart, and the leaderboard record stays the record. A resumed saved rally starts with an empty session best
- The medal star rests under the NORMS card, below its real bottom edge, and follows the card when it grows or shrinks
- VISUALS has three new switches: LAP INFO, NORMS INFO (Socialist Rally with norms only) and CAR INSTRUMENTS. They only hide or show the HUD. Warnings stay: engine, fuel and dose dangers, CLUTCH, refuelling and washing show as plain text lines when the instruments are hidden. The choices are remembered
- VISUALS order: COMMENTS, LAP INFO, NORMS INFO, MAP, CAR INSTRUMENTS, CAMERA, EDGES, SOFTENING, BACK
- STAKHANOV LAPS no longer ask THE LIGHT. TIME OF DAY still chooses where the day starts, and the light then always goes on lap by lap

## 0.781

- The Socialist Rally shows a NORMS card at the top right, next to the lap panel. It has the time to beat now in big numbers, the next medal row (BRONZE, SILVER or GOLD), a short highlight when a norm is beaten, and ALL TARGETS MET at the end. The times are the same as in NORMS AND MEDALS, fuel and climate included
- NORMS AND MEDALS no longer opens by itself before the start. The countdown follows the loading, and a saved rally still comes back with its progress. The table stays in the Party Meeting
- SETTINGS lines up its shortcuts in their own column, with every label starting in the same place
- The Godot logo at startup is gone

## 0.78

- PLAYER REPLAY. Every lap you complete in the Socialist Rally or the Stakhanov laps is recorded automatically, a record or not. The five newest laps are kept, and the Party Meeting has REPLAY as its first row. It shows track, mode, time, the light of the lap and KEPT for saved laps
- The replay viewer plays the lap in its own world with the camera of the game. Play and pause, seek, speeds 0.25x to 2x, single steps while paused, camera choice (found cameras and AUTO) and a switch to hide the controls. Back returns to the same Party Meeting with the race paused exactly as it was. Only CONTINUE drives on
- While a replay loads, a bar shows how far the track has come, over a still of the road where you stopped. ESC cancels
- The Party Meeting is reorganized: REPLAY, NORMS AND MEDALS, DRIVING SCHOOL (was INSTRUCTIONS), SETTINGS, CONTINUE, RESTART RACE, EXIT RACE. SETTINGS holds SOUNDS, VISUALS, RUMBLE with a pad, and KEYS
- SOUNDS starts with the radio on or off and its station, then the sound levels. VISUALS holds MAP, COMMENTS, CAMERA, EDGES and SOFTENING. The camera is no longer a luxury: CAPITALIST LUXURIES ENABLED shows for edges and softening only
- PARTY OFFICE has ABOUT THE GAME with CHANGELOG and CREDITS inside
- The Socialist Rally has no time of day question. It always starts at day, and the day then goes on lap by lap
- Loading a track shows a progress bar, an estimate from the building steps that reaches full only when the track is ready
- Tracks and the replay viewer now build in smaller steps, so the loading card keeps moving
- LAN races take up to six players. LAN protocol is now 11. All players need this build, older LAN builds cannot join

## 0.77

- LAN hosts open the lobby first. Players vote together for a track opened by the host's medals, then load it without disconnecting. Racing waits for matching loaded tracks and every driver's clutch
- Race results lead to session standings ordered by victories. Wins survive track changes, ties share places, and reconnecting from the same running game restores the player's score
- After session standings the host chooses CONTINUE RACING or END SESSION. Ending declares the session winner, or joint winners when victories are tied
- Leaving a joined LAN game asks for confirmation in the lobby, ballot, loading, grid, race and result screens. Staying is the safe default, including when closing the window
- Player collisions respond locally sooner and nearby remote cars follow their current collision positions more closely. Authoritative corrections retain the host's final physics
- Other players' broken headlights and repairs are visible in LAN. Minimap markers use the same player colours as the cars and race order panel
- LAN race results show the winner's time in the result table without repeating it below the winner's name
- The title replay has a softer red tint, halfway towards monochrome. Startup selects MULTIPLAYER, and returning from a game still selects the last played mode
- LAN protocol is now 10. All players need this build; older LAN builds cannot join

## 0.761

- LAN clients can accelerate out of automatic idle neutral after a network correction, stop and restart, and change gears normally
- JOIN GAME no longer asks for a track. The joining player automatically loads the host track and reconnects, keeping the same name and address
- LAN protocol is now 7. All players need this build; older LAN builds cannot join

## 0.76

- LAN protocol is now 6, so both players need this build. Older LAN games cannot join
- LAN races can be played on Ilyich, Siberia, Atomgrad and the bonus track. The host chooses from the tracks opened by medals, a joining player picks the track of the host, and sessions on another track are marked OTHER TRACK
- LAN starts wait for every connected driver. The grid shows WAITING FOR COMRADES and the number of clutches ready, the cars hold their grid places, and the three second GO countdown starts once all clutches are down
- LAN races show a race order panel in the upper right with the places and laps of all drivers, FIN or DNF, in place of the CCCP emblem. Results give the winner a gold banner, short confetti and the crowd cheer
- LAN day and night follow the leading car, with one lighting phase per lap for every player
- LAN fuel canisters can be caught by any player. With fuel consumption ON the can goes to the car the carrier aimed at. Tracks without fuel (Ilyich) ignore the fuel vote
- LAN clients see the mechanic and fuel depot signs, and the Ilyich village life, rowboat, duck, workers, dacha guards and the Atomgrad dosimetrist from the host
- The rally engine has a new voice that follows the revs continuously, with firing pulses, intake breath and an exhaust bark that grows under load. Idle is 1200 rpm with a rolling lope and burble, and backfire bangs are shorter and lighter
- Gear changes clunk harder under load, and clutch engagement adds a linkage click and a gearbox metal ring
- Body impacts have material voices: metal, wood, stone, snow, soft and ground contacts sound different, and the engine and road duck under a hard hit
- Stopped with the pedals released, the automatic box rests in neutral with N on the gear display, and goes into first gear on throttle
- Collision camera kicks scale with impact energy in the top-down, bonnet and other gameplay cameras. The shake option still turns them off
- Replays start the top-down camera at the recorded speed and driving distance instead of pulling in from the title camera
- New car recordings keep the original body, glass and detached-part damage, windscreen cracks, lost headlights and repairs, so replays show them
- New recordings replay the fuel depot pouring sound, fuel level and stream, the rescue tractor tow rope (including rollover pulls and tows with an empty tank), and the octane booster time, engine power, overdrive and golden paint. Older recordings do not gain this information
- New recordings show the mechanic hammer and the impact effects on lenses. GAI inspectors turn to face the road and follow passing cars on every track, and new recordings keep their radar arm, sign and flash. Older recordings fall back to live behaviour
- Snowplough snow heaps are one joined shape with no gap at the V tip, and scale with the recorded speed in replays

## 0.75

- Record lap replays now carry a world tape alongside the car: supported vehicles, people, aircraft, missiles, sounds, effects and breakables follow the recorded lap instead of running a new simulation. Seeking and looping restore their recorded state. Older or incompatible actors fall back to live playback
- Wooden fence breaks, bus fires, wrecks and escaping passengers replay with their recorded timing
- Ghost and world tape saves are paired and written in the background, reducing finish-line pauses
- Track loading shares work across frames. POLIGON 17 patrol setup and day-night material collection have shorter loading-card pauses, and the animated loading heading uses real elapsed time
- Production tools can preserve every completed lap of a driving session and film saved Ilyich laps with planned trackside, crane, bonnet and overview cameras
- Windows downloads are available separately for Vulkan and Direct3D 12

## 0.74

- ILYICH: the church and its yard stand on a raised knoll above the hairpin. Two broad rollers replace four sharp humps on the east ridge, and the homeward lane gains a flowing hollow and crest
- SIBERIA: the frozen river sits in a deeper valley with broad snowy banks, flowing road crossings and a stronger descent from the airbase. Ice and banks continue beyond both map edges for distant camera views
- ATOMGRAD-4: the red forest has pronounced hills and hollows with almost eight metres of road height variation. The town stands on a raised terrace with level streets and softer approaches
- Faded centre and edge lines and three worn pedestrian crossings mark Atomgrad's industrial and town roads. Paint follows the terrain and breaks around potholes and standing water
- The Geiger counter has a louder electrical snap and a longer, rougher crackle
- Title record replays and the fresh-install autopilot demo change cameras automatically: mostly the normal top-down view, frequent bonnet views through bends and hills, short fixed trackside passes and only occasional oblique tracking. Close traffic passes trigger bonnet highlights; pronounced hills can select a fixed panning camera or bonnet view. Starting a race restores the driving camera
- New installations play ANTTI's KOLKHOZ STAKHANOV PETROL 0:39:81 night lap behind the title menu until a personal replay is available. The demonstration does not populate player records or racing ghosts
- Snowploughs turn smoothly between road samples, with their moving collision shapes following the same heading
- Track loading is spread across frames with an animated loading title. The driving school demonstrates the clutch start at the real start line, including Ilyich's barn, and keeps the rescue demonstration and recap in the driving course
- POLIGON 17 camp puddles and ruts have more natural shapes, and its gate, mechanic's work area and roaming BTR patrols have further detail
- Changed road profiles start fresh record sets on Ilyich, Siberia and Atomgrad. Older records are archived

## 0.73

- Hinds slow down while turning onto their attack line, so the bank-limited aircraft can capture the approach instead of repeatedly circling and missing its firing window

- During the start countdown, holding the clutch prevents sliding on hills while the engine revs freely. Releasing the clutch or reaching GO releases the hill assist, in single-player and LAN
- Tanks have a continuous heavy engine rumble at idle, BTR patrols have a stronger armoured diesel voice and the bear's open-top Kozlik has its own petrol voice. Greater spatial range and stronger idle bodies make all three audible near the road. LAN preserves their engine voices and levels

- Hind turns bank into the curve: the inside wing lowers instead of the aircraft leaning outward. Approaches use separate height bands, and timed-out sorties return to their pads after finishing any active firing pass

- POLIGON 17 military highlights have heavier detail and motion: brown-olive Hind field camouflage, framed tandem cockpits, aiming chin guns, folding landing gear, banked turns and braked returns. T-72 wheels and tracks move with each side, turret traverse eases in and the gun recoils and returns
- Hind gunfire follows every visible round with a tearing report and spatial ground strikes. Tank guns have a sharp propellant crack, low pressure pulse, field echo, muzzle flash and nearby camera and controller impact. LAN clients share the weapon effects

- BONUS is now POLIGON 17. The field camp has a rebuilt perimeter and a gate beside the middle helipad. Roadside clutter is cleared, the start moves beside the FIELD TRACK sign and the GAI radar waits at the end of the western start straight
- Route reflectors and bend markers guide day and night driving. Two wider passing sections leave room for overtaking
- Detailed BTR-80 patrols travel off road. Tank crossings and gunfire alternate with Hind attack windows, advance warnings and recovery time
- Hinds fire long bursts across the whole road, starting before the crossing and ending beyond it. Direct hits and nearby explosions damage cars, including in host-authoritative LAN races
- Hinds, BTRs, tanks, BUK launchers and the loading truck have night lights. LAN clients receive the shared vehicle lights too
- Drive through a corrugated maintenance hall on the inner loop: worn concrete slabs, optional slippery oil patches, repair equipment, night work lights, joint rumble and a short hall echo. The roof fades for the driving camera and the route remains wide enough for two cars
- POLIGON 17 has a T TOUR presentation on CHOOSE TRACK: an introduction card, one continuous drone flight through the field camp, BUK battery, tank exercise and maintenance hall, a day-night-dawn transition, then a complete driving lap with track tips
- LAN and Stakhanov power choices now match the three fuels: DIESEL 50%, PETROL 100% and ROCKET FUEL 200%
- The mechanic repairs cars in LAN races. Roadside soldiers react to collisions instead of acting as rigid barriers, and Hind impact effect cleanup no longer accesses disposed objects
- LAN ballots now choose damage and fuel consumption independently of fuel power. Each majority wins; ties are drawn separately. Missing votes keep 100% power, damage ON and consumption OFF
- With LAN fuel consumption ON, the host manages every tank, depot refuelling and engine cut-out at empty. Stopping for ten seconds supplies emergency fuel; fuel levels and refuelling are shared with clients
- Mechanic repair work scales with missing engine health in single-player and LAN: about 1.7 seconds at 99%, 4.8 seconds at 50% and 7.9 seconds at 1%. Short repairs use fewer hammer blows at normal speed. Interrupted work is retained, and low frame rates no longer make the mechanic overshoot his work spot
- Menu and startup version labels retain the decimal point: V. 0.73
- The revised bonus layout starts a separate set of records. LAN protocol 4 requires all players to use the updated game

## 0.72

- BONUS: three Mi-24 Hinds replace the runway aircraft. The field base has separate helipads, a winding service road and a narrow bypass around the camp; the eastern GAI radar waits at the foot of the long descent
- BONUS: supply troops replace the roadside babushkas. The central camp, BUK crew and abandoned combine have more detail, and the combine has a cab, grain tank, unloading auger, engine grille and access ladder
- Tank shells and crashed Hinds leave detailed, terrain-following impact scars, dirt and smoke without changing the driving surface
- LAN RACE is host-authoritative: cars collide with one another and shared moving targets, while clients receive the host's world state
- OKTAN 98 is now OCTANE BOOSTER; its canister displays the selected fuel's actual power
- A missed norm uses a larger, shorter stamp message without a dark panel

## 0.71

- The bonus start line moves onto the concrete apron beside the fuel tank, and the lap runs in the opposite direction. Roadside features, the depot, speed trap, fences and runway are repositioned with the new start
- The bonus exercise ground has aircraft service equipment and a marshal, a field kitchen with a cook and soldiers, BUK crew and a shared crane truck that replenishes empty launchers. Nearby gunfire makes the soldiers duck while the cook carries on
- The bonus field kitchen, workers, abandoned combine and BUK loading truck gain more detail and visible working animations
- Inbound cannon runs have an advance radio signal and warning. Firing positions, track marks, battered targets, a rusted combine and disconnected farm wiring fill out the abandoned kolkhoz

- SUK-25 navigation lights, flashing beacons and a landing light keep the aircraft visible after dark, and cannon tracers retain their brightness at night
- Tank fire has a heavier report, stronger recoil, a muzzle pressure cloud and ground dust. Nearby shots shake the camera and controller; shell hits have a larger explosive flash and dirt cloud
- The bonus track statue is removed

- Starts now place the car close to the line and hand control over with a clutch lesson. Hold the clutch, build revs and release only on green. Without the clutch the car can move during the countdown, and a false start voids the lap. Braking works with the clutch held
- Saved SOCIALIST RALLY runs continue with their saved fuel. Starting a new run replaces the save without an extra confirmation
- Rocket fuel has individually tuned norm ladders on all four tracks
- ATOMGRAD-4 has its own planned power distribution network
- The bonus track has a rebuilt military power network, more detailed tanks and active tank patrols that cross the road. Its ruined barns and abandoned grain silos show the old farm giving way to the military training ground. The drunk tractor, intact barns and old radar are gone
- The bonus airbase operates its own detailed SUK-25 MOLE attack aircraft. Up to three can taxi out, take off, make one or two low cannon runs and leave the map. Aircraft can collide and crash
- The SUK-25 takeoff direction is set independently of the bonus lap direction
- SUK-25 exhaust plumes now visibly distort the air behind the engines
- Cannon runs cross the road away from the runway. Low passes, visible muzzle flashes and tracers, a heavy cannon report and large impact dust clouds make the attack clear. The fuel depot has moved out of the aircraft taxi route
- A detailed BUK-21 missile battery replaces the bonus radar site. Its launchers track airborne SUK-25s and fire guided missiles that can bring them down. Track shoes, suspension, hatches, lights, antennas, hydraulics and launch rails add detail. The launchers stand at different diagonal headings on the natural rolling grass terrain, without a flattened concrete pad
- The bonus track bus, both bus shelters and their waiting drunks are removed. The mechanic works beside the parked SUK-25 outside the hangar. Honk to call him over to repair the player car
- The missed-norm stamp chooses the side of the car with more space. Turning comments off keeps essential information in plain messages
- Traffic slows for other moving vehicles ahead, and the first track hen coop is solid
- The PARTY MEETING menu lesson has been removed from DRIVING SCHOOL

## 0.70

- The launch card is now MAXIMUM ATTACK! VERSION, V.070: a milestone build, still in development, with rough edges expected
- Socialist Rally norm medals have their own bronze, silver and gold ceremonies. Beating a norm can unlock the next track; Stakhanov laps unlock tracks too, and a gold rally medal opens the bonus track. The track menu celebrates each new authorization
- Missing a norm now brings a committee verdict and a stamp. The first missed norm pauses for an officer card that explains the result; the card keeps the usual officer frame and portrait. A missed norm gets a new boo
- ATOMGRAD-4 now has a DRIVING SCHOOL course: a lap tour, radiation sources and dose, the dosimetrist's limit, the wash station, fuel and repairs
- Track-specific five-speed gearboxes make use of the full box on each rally track. In a jump the engine can rev freely to the limiter when the wheels are in the air
- Rally acceleration now bites harder when the tyres find grip. Drive, cornering and braking grip have been balanced by surface; steering stays useful under hard braking, and the car responds better to full lock at speed
- The camera reacts more clearly to a hard launch and acceleration. Before the countdown the engine is held at the limiter, lifted for exhaust bangs, revved again and released
- The engine changes tone with its load, burbles and pops on overrun, and the gearbox whines. Gear changes clunk; tyres hum on hard roads and rattle on gravel; stones hit the underside and wind rises with speed
- A hard crash ducks the engine and road sounds. Slides chirp and howl, passing vehicles change pitch with distance, and the co-driver calls sharper bends through the radio
- SOUND BALANCE in PARTY MEETING controls the engine, road, surroundings, alerts and radio. The title menu has a RADIO row, and radio on/off is remembered separately for SOCIALIST RALLY and Stakhanov laps
- Controller A, START and the D-pad respond immediately in the title menu, PARTY MEETING and the pause menu. B goes back in the title menu; the Back button opens PARTY OFFICE and, pressed there again, hands in the resignation
- The mechanic now repairs engine health and body dents gradually; throttle input can interrupt the repair. CAPITALIST LUXURIES ENABLED! also blinks during a race when those graphics options are active
- DRIVING SCHOOL lessons go straight to the demo, with numbering only while a whole category plays. Sound during track tours comes from the drone's position, so nearby aircraft pass the listener
- The DRIVING SCHOOL also teaches the PARTY MEETING menu (the pause menu with the radio, the mixer and the luxuries), and its menu entry says so
- The SOCIALIST RALLY asks for the FUEL after the track: DIESEL (50% power, the tractor brigade pace), PETROL (100%, the car as it has always been) or ROCKET FUEL (200%, a golden car). The norms are set at each fuel's pace, the card shows the first and last norm and your medal, the choice is remembered, and a saved rally goes on with its own fuel. The engine drinks 0.6 times as much on diesel and 1.7 times on rocket fuel, and OKTAN 98 gives 3.5 times the fuel's power (175, 350 or 500%)
- Each fuel has its own record lap, ghost, board of honour, speed trap book and medals. PETROL keeps the old records. The BOARD OF HONOUR cycles with Left and Right through petrol, diesel, rocket fuel and Stakhanov laps. A medal on any fuel opens the next track

## 0.69

- SOCIALIST RALLY opens NORMS AND MEDALS before the countdown: current and completed norms, the lap times that beat them, and the bronze, silver and gold milestones
- After beating the first norm, EXIT RACE offers to save the unfinished rally. Choosing the track again resumes at the start line with the same norm, remaining laps, time of day and passing lap times. Saves belong to the personnel file and survive closing the game
- PARTY MEETING groups camera selection, EDGES and SOFTEN under CAPITALIST LUXURIES. Main menus blink CAPITALIST LUXURIES ENABLED! when smoothing, softening or an alternative camera is selected. Camera cards explain discoveries still hidden on the track
- Every menu-started run shows a LOADING TRACK controls card over the menu, followed by TRACK READY and PRESS ANY KEY, TOVARISH. Card headings type themselves out. The control cards use matching key icons, and PARTY MEETING includes radio song selection. Escape resumes the race
- Race-start messages stay clear of unrelated commentary. Tractors waiting in their barns no longer trigger road warnings, and their collision bodies reset with them. The first track has richer outer scenery
- DRIVING SCHOOL drops the mission lessons and bonus-track lessons. Lesson starts follow sloping ground, and the menu theme no longer overlaps the car radio after a tour
- Rivers meet the edge of the map smoothly instead of ending in a cliff. MSAA no longer draws white stripes on distant ground, and the cheering after a beaten norm is no longer lost once a track tour has been opened
- SIBERIA has a new drone tour and the SIBLAG NO.2 name. ATOMGRAD-4 has a drone film with weather timelapse and track tips. The KOLKHOZ tour includes its driving tips as captions during the lap

## 0.68

- The tour of KOLKHOZ NO.3 on CHOOSE TRACK (T) is a new one-take drone flight: the Lenin monument, the power line that lit the village, the kolkhoz by night with its tractors, workers and the duck hunt, then dawn and a lap of the track. It opens with a card telling where the name of the track comes from, and captions follow the flight
- The SOCIALIST RALLY asks the time of day to start at (DAY, SUNSET, DUSK, NIGHT, DAWN) after the track card. The day then goes on lap by lap as before, and the climate allowance follows the light
- Gameplay cameras are hidden discoveries, saved per track in the personnel file: top-down is always there, and C (pad: LB) cycles the cameras found on the track. The pond duck on KOLKHOZ NO.3 unlocks a bonnet view when driven over. The cinematic camera follows the road better on the track
- ATOMGRAD-4: a railway yard for the train, better concrete mixers and other industrial vehicles, and polluted water that looks the part. The decontamination wash has its own mark on the minimap
- A race restart restores the condition of the car, the fuel and the radiation dose. The road rescue hint is clearer and shows only off the road
- The bonus track has its own name inside the game files: its records, ghosts and personnel file data are carried over by themselves on the first start

## 0.67

- ATOMGRAD-4: the power plant is finished: the turbine hall, the three numbered reactor blocks and block 4 under its sarcophagus with a real lattice crane. The decontamination station and the twelve wrecks of the vehicle graveyard (lorry, bus, armoured carrier) are rebuilt in full detail
- The dosimetrist at the zone checkpoint is bigger, wears an orange vest, stands a little past the start line and is knocked over by the car like the other people, then gets up again
- SIBERIA: the start gate is a steel truss with flags, pennants, floodlights and a chequered line (also on the bonus track and ATOMGRAD-4), the flagpole has a waving flag, the radar station has a generator shed, antennas, a railed tower and a gate in its fence, and the timber lorry wreck has a torn-open cab and bonnet
- The snow ploughs and mixers have headlights, tail lights and work lights at night
- The first track has a finished billboard, statue and power poles, a floodlit monument square by the church, marked fuel depots and mechanic posts with sickle and spanner emblems, and the same marks on the minimap
- Medals and norm wins are celebrated without pausing the race, with stars, cheers and confetti, and missed laps are booed. The fuel and engine gauges pulse orange below half. Rally stop cards are shown once per career and short comments follow
- The launch archive appears as the banner is still coming down
- Loading is lighter: poles and hay bales share their meshes, and the game is exported with its shaders pre-compiled

## 0.66

- The title menu has three rows: SINGLE PLAYER, MULTIPLAYER and PARTY OFFICE (KGB FILES, LEADERBOARD, CHANGELOG, CREDITS, RESIGN). The changelog shows the last three versions
- Quitting is a resignation with nothing to answer: the music fades away, a gun is cocked and fired under the words, the last full stop is shot off and a stain is left in its place, and everything else goes to the dark before the game ends
- The PARTY MEETING (pause) menu: B on a controller continues the race, RESTART RACE costs nothing, EXIT RACE follows it (it was BACK TO KOLKHOZ), I opens the instructions, and there are new COMMENTS, RADIO and VOLUME rows. The camera row is last
- Stop cards say PRESS ANY KEY only when they take a key
- The first track PATH OF ILYICH is now KOLKHOZ NO.3 ILYICH´S LAMP (KOLKHOZ in the menus), RED DAWN is BONUS and the track card says CHOOSE TRACK. Records, ghosts and medals stay
- Desertions are gone: closing the game window or changing track in the middle of a rally no longer puts anything on the KGB file, and every rally starts at the first norm
- Landings feel heavier: the springs bottom out and stay down instead of bouncing the car back up like a toy, and a jump no longer spins the car sideways - the air and the landing hold its heading
- Driving off the road costs speed: grass and withered grass pull a flat-out car down to about 115 km/h and a field to about 90 (the road is 185), so cutting a corner over the grass no longer pays. The verge strip beside the road edge drags a little too
- The MiK-21 has its night lights: red and green wingtip lights, a white tail light, two flashing red beacons and a landing light on the nose gear
- The kolkhoz barn has a finished metal roof, the grain silo is weathered and the village frontages are tidier
- Headlights stay visible on the terrain at night

## 0.65

- Quitting is now a resignation. The Party has received it and asks whether it should be processed. NO keeps you in the game. YES is approved in the dark, with a last sound to remember. A controller works there too: d-pad to choose, A to confirm, B to withdraw
- PATH OF ILYICH start straight has a big weathered kolkhoz barn beside the grain silo and its long dormitory wing for the workers, with slogan banners along the road. The power line runs between the buildings and the road
- Tractors leave their barn through opening doors before they start their work in the fields
- The village bus stops for a tractor in its lane or one leaving a barn, and tractors wait at the barn door for a passing bus
- The church village has washing lines drying beside the houses nearest the road, a hen house with hens that flee a passing car, and a tethered donkey that looks up and kicks at a fast one
- Kolkhoz workers work in cycles with breaks to straighten their backs and wipe their brows. They bend their knees, follow a nearby car with their heads and shake a fist at one that passes too fast. Carriers walk between their piles and carts
- Workers, fishermen and the dacha security men fall over when hit instead of stopping the car. The dacha guards patrol: one keeps the gate and watches cars, one walks the shore beside the rowing boat and one walks the garden fence
- The rowing boat and its crew, the gulag officer and his driver match the size of the other characters. The boat rows a wider loop, the duck is twice as large and the shotgun aims at the duck without ever pointing through the rower
- Buildings between the camera and the car fade in and out smoothly, and the start barn roof no longer snaps back
- Menu typing sounds are refined and the loading cover leads more smoothly into the menus
- The camera comes a little closer to a slow or standing car and pulls back as the speed rises. Off the road it rumbles lightly with rough ground, and it dips once when the car lands from a jump
- PATH OF ILYICH has summer on the lens: poplar fluff drifting by day, dust gathering on dry roads, now and then a fly or a bug on the glass, straw from knocked hay bales and fields, and a warm flare from the low sun at dawn and dusk
- The gulag truck keeps to its own lane and waits behind other traffic. The bus, the tractors and the snow ploughs stop for it instead of driving through it
- Refuelling stops when the tank is full, and the pump falls silent. The FUEL IS RUNNING LOW card is shown once per personnel file, after that a short reminder
- THE CAR IS BADLY DAMAGED and A HEADLIGHT IS BROKEN cards are also shown once per personnel file
- A race comment stays on its side of the car and in its place for as long as it is shown
- WELCOME TO SIBERIA norms now run from 1:35 to 1:10. The bronze target is 1:26.5 and the silver target is 1:18.5
- The CCCP RACING banner glides down at an even pace

## 0.641

- People no longer stop the car like a wall. The GAI inspector, the KGB officers at the start and the men at the bus stop fall over away from the car, lie down for a moment and get up again. The car carries on, only a little slower
- The speed limit sign falls over when hit and stays down until the run restarts. A hay bale tips over and slows the car less than before
- Returning to track selection from a race types out only the selection card. The mode description above it appears at once

## 0.64

- CHOOSE THE TRACK offers full-screen scenic tours for PATH OF ILYICH and SIBERIA. Press T or the controller's Y to watch, Escape to return to the selection card, or Enter to race on the selected track. A finished tour returns to the selection card
- The minimap has a larger, frameless road outline, smoother lines and clearer car and start markers. PARTY MEETING includes MAP ON / MAP OFF, also available with M or the controller's right stick
- Race comments follow the car smoothly above or below it according to its movement, keeping the car visible
- PATH OF ILYICH has a winding river with changing widths, pools and narrows. River surfaces, banks, ice and scenery beyond the map use the same varying width. Fishermen stand beside the new banks with their floats in the water
- Hay bales can be knocked over by the car. The first session ghost no longer interrupts the race with a stop card
- Release packaging keeps old builds out of the resource scan. The macOS application reports the current release version

## 0.63

- A C64-style loading cover and tape-loading stripes slowly blend into the modern cover. The original title music starts during loading and continues into the menus. The TEST VERSION notice appears over the modern cover after a three-second hold
- KGB ARCHIVE opens at launch with five separate personnel files. Each driver keeps their own name, medals, instructions seen and speed records
- Menu headings appear letter by letter with a small cursor. Track, time-of-day and lighting choices sit below the selected mode's description. The title world has a faded red, ochre and cream poster palette that fades to full colour for driving
- CHOOSE THE TRACK is a plain selection card again. SOCIALIST RALLY starts directly after track selection. Scenic tours and track instructions remain available through DRIVING SCHOOL
- DRIVING SCHOOL is a tree of car-handling lessons and track instructions. Right opens a course, Left closes it, and Enter watches a whole course or an individual lesson
- PARTY MEETING supports Up/Down and Enter, with a visible selected row and controller navigation. INSTRUCTIONS opens the lesson tree during a race. Finishing or leaving a lesson returns to PARTY MEETING, with the original car position, lap time, fuel and damage preserved
- Press C in a run to switch between the overhead camera and a moving cinematic camera. The pause menu also offers the camera choice. The key card opens on K instead of interrupting every new run
- A procedural sky adds clouds and richer colour to day, evening and night views
- PATH OF ILYICH norms now run from 1:10 to 0:47. The bronze target is 1:00 and the silver target is 0:54
- The GAI inspector and dacha security match the larger scale of the other characters. The sleeping mechanic lies along his bench, hay bales have rolled ends and twine, the dacha veranda and balcony are refined, and the churchyard wall stays clear of the grain store corners
- Every release package includes a steam-artwork folder with portrait and wide library covers, a panoramic background, a transparent logo, PNG and ICO icons, and instructions for adding them to a Non-Steam game

## 0.62

- SOCIALIST RALLY track selection now plays a live scenic preview of the selected track. THE KOLKHOZ and WELCOME TO SIBERIA are separate from TRACK INSTRUCTIONS, which now begin with the driving lessons
- The smaller, lower video window leaves more of the CCCP RACING banner visible. Track choices sit below it without the copyright footer. Up focuses the video, Enter switches full screen, Down returns to track selection and Escape goes back through the menu
- Switching tracks shows LOADING before rebuilding the world. Escape from track selection returns to SINGLE PLAYER instead of the TEST VERSION card, including after a track change
- Scenic previews use the original instructor cards, red and gold headings, text layout and shadows. Card text is not repeated as a plain caption afterwards
- Siberia visits the valley, forestry crews, mountain radar, airbase, timber wreck, camp and mechanic. Moving workers, snowploughs, prisoners and aircraft remain active throughout the tour
- The forestry visit is shorter. The player and snowplough still meet above the radar, then the camera glides down to follow a natural MiG takeoff without interrupting it for a runway priority demonstration
- Wide, oblique winter night views show the camp searchlights and the whole airbase, hangars and runway, including a night takeoff with afterburner
- Both previews keep their music, independent car movement and smooth camera moves without camera shake. Timed camera transitions now interpolate correctly
- A tour movie capture option records one complete scenic preview with its opening music and instructor cards, without menu controls

## 0.61

- PATH OF ILYICH instructions now begin with THE KOLKHOZ: a scenic camera tour of the barns, grain silo and loading crews, party dacha and duck hunt, field workers, plough and slurry tractors, village, grain store and bus. The player car stays parked
- The kolkhoz tour plays Kevat saapuu Moskovaan from its title card. Dacha captions wait for the camera to arrive, with friendship, cooperation and mutual assistance on the pond. The music dips for the missed shotgun shot
- A wide view inside the map shows day, sunset, dusk, night and dawn, explains which laps bring them, and shows the lights coming on. DAMAGE compares two working headlights with a broken lamp and explains repairs by the mechanic
- Ilyich chapters run THE KOLKHOZ, THE MISSION, THE TRACK, DAMAGE, GAI RADAR. THE MISSION explains bronze, silver, gold and their rewards before the gulag
- SIBERIA has its own complete track tour and rally instructions: the winter road, handbrake turns in the mountain bends, snowbank touches and the hard-to-read bend at the ice exit
- Siberia introduces the 50-litre tank, driving style and fuel use, babushka cans, fuel stops, golden 98-octane boosts, the pilot babushka and her revenge, low fuel and towing to the depot when empty. The mechanic's location and call are demonstrated
- Number keys jump or rewind to the start of a lesson in every school. Driving demonstrations wait two seconds after their headings. Enter no longer advances a moving track tour; Escape offers to leave the school
- Track landmarks get camera moves and zooms while their captions are visible. Stream, field bends and grain store hairpin captions are better timed. GAI has a separate radar, militiaman speed bubble and possible patrol ambush demonstration
- The gulag officer waits while the driver refuses to get out, then fires one pistol shot into a headlamp. The driver surrenders and is escorted to the truck. The Ilyich example takes place outside the barn so the impact stays visible
- More gulag truck and rally driver detail. The officer walks around vehicles and the player, and keeps the pistol aimed at his target. The Kozlik patrol steers, brakes and reverses like a car rather than moving sideways
- The drunken dacha hunter keeps missing the giant mallard, with a loud shotgun report, recoil and splashes
- SOCIALIST RALLY always returns to SINGLE PLAYER / SOCIALIST RALLY / CHOOSE THE TRACK. A newly unlocked next track is selected; otherwise the same track stays selected
- Leaving a rally with Escape carries no penalty. The obsolete KGB desertion count and title prompts are removed. Menu descriptions and driving school technique headings are clearer. Mechanic repairs have no extra time penalty

## 0.60

- SIBERIA: a connected electricity network supplies the camp, radar and airbase. Overhead lines run south of the runway, while the airbase and runway crossings use underground cables
- A fenced radar station stands beside the eastern mountain bends, with a service drive and a place for the Kozlik patrol to stand guard
- Ten winter workers bring the landmarks to life: two men share a crosscut saw, wood carriers return empty-handed, a radar technician checks his instruments, and the camp crew chops wood and clears the steps
- The parked MiK has a mechanic, a supply carrier, a powered service cart, ladders, wheel chocks and an equipment lead. A ground crewman follows the real taxi traffic and holds his signals when aircraft wait
- Camp watchtowers have working searchlights after dark. Gate lights cover the approaches, corner lights slowly sweep the yard and fence, and the barracks cast shadows. A warm window and a small door lamp mark the wood yard
- More graphical detail on snowploughs, MiKs, hangars, barracks, radar structures, the fuel depot, log piles and the overturned truck
- The landscape continues beyond the playable map, with matching terrain, trees, extended rivers and night colours

## 0.59

- PATH OF ILYICH: a grand log dacha stands beside the pond, with a veranda, balcony, party decorations, a fenced garden and a pier. A gravel drive connects its gate to the race road, and the electricity comes from the existing grid
- Two detailed official saloons stand parked at an angle in the yard
- Two bare visitors share a rowing boat on the pond: a party official in a peaked cap and a guest in a sauna hat. An enormous mallard swims nearby
- Field workers are spread through the fields instead of lining the edges, with at least eight metres between work sites and small working clearings in the growing grain
- The dacha drive has fine earth and gravel texture, soft grass edges and a clean join to the race road

## 0.58

- SOCIALIST RALLY continues on the next track after a silver or gold, even if the car is wrecked after earning the medal. A bronze-only run ends with a reminder that the next track is open in STAKHANOV LAPS
- The gulag returns to the SINGLE PLAYER menu. SOCIALIST RALLY is its default choice, and INSTRUCTIONS, TOVARISH? defaults to YES
- PATH OF ILYICH: the church grain store has more architectural detail, boarded windows, loading crews and a red tractor with a sack-loaded trailer. A roadside stone wall guards the hairpin, with a loading entrance at the northwest corner
- The starting barn has more detail. Village houses have varied colours, windows, doors and access paths with gaps through the stone walls. The north barn and grain silo stand closer to the road, with more detail
- Growing grain mixes with harvested fields, more fields are visible beside the southern road, and more bales fill the fields. Fields stay clear of the road and houses
- Kolkhoz crews reap grain, carry sacks and load the stores. Four men fish on both sides of the river crossing, well away from the road. Workers and the church delivery tractor match the scale of the other characters and vehicles
- A green tractor pulls an ochre slurry tanker between the fields. It spreads in short bursts and closes the valve late at road crossings. The dark wet trail is nearly as slippery as ice, fading and regaining grip over 15 seconds
- The moving tractors and the rescue tractor have working headlights after dark
- The bus and bus shelter have more detail and clearer colours. A street lamp lights the stop. Connected electricity lines supply the village buildings, grain store, farm and GAI post
- The river has flowing water, stones, reeds and lily pads. Ford guides mark the shallow crossing, and stray blue triangles outside the banks are gone
- Trees have more detail. SIBERIA spruce snow sits on the broad lower boughs with irregular edges, using a lighter rendering method
- The Pyatyorka has a large spare wheel strapped to its roof rack. Unreadable rear plate lettering has been removed. The first track's visible start line matches the timing line and stays above the road
- Credits now include Blender, Claude Code, OpenAI Codex and development on Omarchy Linux

## 0.57

- The rally school of PATH OF ILYICH starts with a lap of the track and its tips: the GAI radar in the first bend, the rollers flat out, the ford on the gravel bar on the left, a clutch kick in both long bends, the handbrake in the church hairpin, a tight line up the hill to the barn
- The norms are told, not driven: 10 norms, each tighter than the last, 5 laps to beat each one. The village mechanic is introduced first: he sleeps at the bus stop
- The schools show and tell: no more tries of your own
- DRIVING SCHOOL: the clutch kick comes at the start of the bend and the slide stays in the middle of the road. The Scandinavian flick in the same bend: steer away, lift off, steer in and slide it on the gas. The handbrake turn in the church hairpin. The flick on the gas is gone, the green strip of the brake lesson is shorter
- A slide started with a flick or a clutch kick follows the nose more on the gas. Other driving is as before
- The tyres sound of the ground: stones crunch on gravel, mud squelches, grass swishes, snow squeaks, ice hisses, water sprays. The brakes screech on concrete
- SIBERIA: the norms run from 1:50 down to 1:27
- The TEST VERSION card at the start no longer shows the changelog (it is on the title menu)

## 0.56

- DRIVING SCHOOL is the first choice under SINGLE PLAYER: learn to drive on PATH OF ILYICH (the clutch start, steering, the brakes, the slides, R). The rally school of PATH OF ILYICH keeps the norms, the gulag and the damage
- The school paints the road: where to press the clutch and where to let it go, where to keep to the inside, steer away, lift off and steer in for the Scandinavian flick, where to tap the brake. A strip lights up while the car is on it
- The Scandinavian flick is taught in three steps: AWAY, LIFT OFF, IN
- Before each try in the school: 3, 2, 1, GO! The tries start on a straight
- The SOCIALIST RALLY always starts in the day. STAKHANOV LAPS: choose the time of day, and whether the light changes lap by lap or stays
- A map of the track bottom-left: the car, the ghost and the LAN comrades. M (pad: RS click) turns it on and off
- The car keeps steadier in the air: a landing on the rollers of PATH OF ILYICH no longer turns it over in the next jump
- Your name is asked when a SOCIALIST RALLY or STAKHANOV LAPS run is chosen. A top-10 lap goes on the board at once, without stopping the race

## 0.55

- The norms are beaten one at a time: however fast the lap, it beats one norm. The laps you save go on to the next norm, up to 10
- A rally bronze opens the next track for STAKHANOV LAPS. Its SOCIALIST RALLY opens with a silver
- RED DAWN, the bonus track, opens with a gold on any track
- A wreck in the SOCIALIST RALLY is the end: the engine seized or the car destroyed, the state sends no second Pyatyorka. To the gulag
- The gulag truck stops ahead of the car. An officer climbs out with a pistol, orders the driver out, hands up, and marches him round to the back of the truck. Headlamps after dark
- PATH OF ILYICH has no fuel: the tank, the cans and the depot come in from SIBERIA on
- Every track gets its own driving school with only what is new there. PATH OF ILYICH: the clutch start, steering up, across and down the screen, locked brakes against pumping, the clutch kick, both Scandinavian flicks, the handbrake in the church hairpin, a crash, the horn and the mechanic, R
- STAKHANOV LAPS have no driving school any more. HOW TO PLAY is gone from the title: the school is offered before a rally
- The slide techniques feel different from each other: the handbrake swings the tail round, a clutch kick in a bend breaks the rear loose, the Scandinavian flick swings the car like a pendulum (fully with the throttle lifted, half on the throttle)
- R puts the car back on the stretch of road it was on: no more cutting across to a nearby stretch
- A lap ends only between the posts of the line (MISSED THE LINE!). WRONG WAY! when you drive the course backwards
- On its roof the car is no longer righted by the tractor: press R, or after 5 seconds it is done by itself (the lap is void)
- The bear and his GAI chase are explained on cards, the first time on each track
- SIBERIA: the fuel depot stands at the west end of the runway, by the MiK hangars
- SOCIALIST RALLY comes first in the SINGLE PLAYER menu, with a new hint

## 0.54

- A new track: ATOMGRAD-4, the third year of the plan, in the zone round the power plant. A bronze on SIBERIA opens it
- A rainy autumn, driven the other way round: the zone checkpoint, the level crossing, the red forest, the vehicle graveyard, round the plant and block 4 under its sarcophagus, the cooling channel straight, square corners through the town round the square and its ferris wheel, the birch avenue
- The weather changes lap by lap: drizzle, rain, a downpour at sunset, a dry dusk, a thunderstorm at night, fog at dawn. A wet road has less grip, concrete the most. Standing water across the road
- Radiation: the dosimeter clicks, faster in the red forest, by block 4 and off the road. In the SOCIALIST RALLY the dose adds up on the DOSE gauge and the car glows green. Over 20 mSv the dosimetrist stops you at the line. Or wash it off at the decontamination station by the birches
- At night the hot spots glow on the ground
- A goods train over the level crossing, a lunokhod crossing the road at the plant, concrete mixers bound for the sarcophagus, Przewalski horses, a giant catfish in the cooling channel, the bear in a gas mask, the GAI on the channel straight
- RED DAWN, the bonus track, now opens with a bronze on ATOMGRAD-4
- Buildings between the camera and the car turn see-through, on every track
- The driving school has a lesson on the zone
- The computer driver waits for a train and does not overtake into oncoming traffic

## 0.53

- The GAI ambush: past the radar the bear in the Kozlik sometimes lies in wait. Speed past him in the SOCIALIST RALLY and he comes after you, siren wailing and blue lamps flashing (at most once a lap)
- Outrun him and he shakes his paw. If he gets on your tail, both cars stop: the duty officer wants your autograph, for his wife. The lost seconds are the only cost
- SIBERIA has a GAI post too, at the far end of the runway. The limit is 90
- SIBERIA: snow poles with reflectors along the snow banks. The reflectors glow faintly at night
- SIBERIA: yellow traffic cones mark the road edges over the frozen river. Knock them flying, they do no harm
- SIBERIA: the snowploughs blow a heavy air horn, not a bus horn
- SIBERIA: the MiKs roll out of the hangar door and taxi along the runway edge (they went through the hangar wall and the barracks)
- The computer driver (LAN autopilot, the title demo) drives round a snowplough, the bus or a tractor coming at it instead of getting stuck in front of a snowplough in SIBERIA

## 0.52

- RED DAWN is now the bonus track: it opens when all the other tracks are open
- The track choice always starts on the first track. A locked track is shown in black and white. Left/Right now reach every track
- A car spun round no longer rolls on tail first: its nose swings round towards where it is going, losing speed sideways. On the brakes it slides on tail first and stops
- On grass, field, mud and deep snow the tyres of a car sliding tail first or sideways dig in
- The brake no longer selects reverse while the car still slides sideways
- SIBERIA: no more invisible walls in STAKHANOV LAPS (the hidden snowploughs and the prisoner column were left on the road)
- SIBERIA: the MiKs no longer wait for you. They hold beside the runway only while a snowplough is on it or coming, and queue one behind the other
- The title shows PATH OF ILYICH until you choose another track

## 0.51

- A new track: SIBERIA in winter, the second year of the plan. A bronze on PATH OF ILYICH opens it. About 1.4 km, driven the other way round: left bends rule
- The start straight in the south valley crosses the frozen river, a fast left climbs to the switchbacks, then the airbase runway, the descent, a long slide down the frozen river, the river hairpin and the bank road through the labour camp
- Ice, packed snow and deep snow, each with its own grip: on ice the tyres only hiss and the brakes need 55 m from 80 km/h. Black ice patches on the snow road
- High snow banks along the road: soft, the car ploughs into them in a cloud of powder. Run in square and it stops, brush them and they turn you back onto the road
- Heavy snowfall, a dense snowy taiga, log piles by the road, a timber lorry on its side in the hairpin ditch, still smoking
- The airbase: hangars, the runway with markings and lights. The MiKs taxi to the end of the strip and wait while you pass, then take off
- Two snowploughs with huge V-blades, one with the race and one against it. They almost fill the road
- The labour camp: watchtowers, barracks and wire. Its mechanic sleeps by a barrack door: honk and stop in the camp and he fixes the car. A column of prisoners and two guards trudges round the lap along the road edge
- SIBERIA has its own norms: 2:00 down to 1:27 (a first guess)
- LEADERBOARD: Up/Down shows every track, the track name is on the board
- The title replay: on the replay line Left/Right shows another track. A track without a rally record replays its Stakhanov record
- The record lap keeps its time of day: the replay is lit as the lap was driven, dusk, night and headlights
- PLAN FULFILLED shows the track name
- Radio 1 is now Kadonneen jäsenkirjan jäljillä, radio 4 Kevät saapuu Moskovaan

## 0.50

- A new track: KOLKHOZ PATH OF ILYICH, the first year of the five-year plan. Sand, gravel and mud, about 1 km
- Start and finish inside a barn open at both ends. Its roof turns see-through while you are under it
- The ridge: four rollers and a jump down towards the river
- The ford: a short deep line on the inside of the bend, a gravel bar round the outside, longer but faster
- A fish pond, the mud meadow by the river, field bends, a tight hairpin round the old church (now the grain store) and a village lane of log houses
- Choose the track in STAKHANOV LAPS and in the SOCIALIST RALLY. The old kolkhoz is RED DAWN, the training ground
- Every track has its own norms. PATH OF ILYICH: 1:20 down to 0:57.50
- Medals in the rally: bronze for 4 norms, silver for 7, gold for the whole plan. The track card shows your medal
- The GAI speed trap at the end of the long straight on both tracks: the inspector measures every pass, speeding is fined (the kolkhoz pays). The fastest reading is kept for the rally and Stakhanov laps apart
- The rally, LAN races and the driving school stay on RED DAWN for now

## 0.49

- Title menu: QUIT at the end of the top row, a hint box tells what the selected mode is. SPLIT SCREEN is gone
- Messages by importance: the plan, dangers and warnings always show, news waits its turn, jokes only after a quiet spell and never in danger. TAB turns off only the jokes. Many repeated or joke comments are gone
- Messages no longer run out behind a pause or a stop card
- The brake no longer selects reverse when the car rolls backwards after a spin: it brakes
- Grass and fields slow a rolling car much more (grass coasts 30 % shorter, fields 35 %)
- Every run starts at a random time of day. A LAN race starts at the same time for every comrade
- New stop cards in the rally: A GHOST IS BORN! and A HEADLIGHT IS BROKEN! (after dark)
- The lap panel says 5 LAPS LEFT, LAST LAP! blinks, and the norm fits in the panel
- The car radio sounds half as old: wider sound, less distortion, less hiss and fewer drop-outs

## 0.48

- Stop cards in the SOCIALIST RALLY: the game stops and the liaison officer explains, until any key or button is pressed
- A card after every lap: the lap time against the norm, how many laps are left, or the new norm. Also when the fuel or the engine is down to 25 %, and when a revenge begins: what is coming and what to do
- New norms: 1:15 down to the Stakhanovite 0:52.50, 2.5 s apart (10 norms, 1:00 is norm 7). Desertions start the rally at most at norm 7
- A lap that skips norms brings their laps along: five laps for every norm climbed
- The rally ghost drives the best lap of this session. The record lap still plays on the title
- The engine drinks half as much fuel
- Day and night every 12 laps: day 1-2, sunset 3-4, dusk 5-6, night 7-10, dawn 11-12, then a new day
- Day and night in Stakhanov laps and LAN races too (no allowance there). In a LAN race the other cars have headlights
- The driving school cards stay up much longer (16 s), Enter still goes on at once
- The jets are now MiK-21s, a kolkhoz design

## 0.47

- KOLKHOZ DRIVING SCHOOL: starting a rally or Stakhanov laps asks INSTRUCTIONS, TOVARISH? Also in SINGLE PLAYER, HOW TO PLAY
- Every lesson shows it first (the autopilot drives, slow motion, an arrow points), says it in a few words with the right key or button, then YOUR TURN!
- Rally lessons: the mission and the norms, fuel (canisters, the golden one, the depot), damage (the mechanic, R), driving (braking locked and pumping, the handbrake, mud and concrete, the clutch launch, the horn)
- Stakhanov lessons: the power keys and the board
- Three misses and the lesson shows it again and moves on. Enter skips, Esc leaves. The school never touches the records or the KGB file

## 0.46

- SOCIALIST RALLY: the liaison officer hands out lap-time norms, 1:10 down to the Stakhanovite 0:48.50, five laps for each. Beat a norm and the next one follows, a fast lap can skip several. A window used up means the gulag
- Every desertion on the KGB file starts the next rally one norm higher
- Fuel: a 50 litre tank, two laps flat out are safe, the third needs a light foot. The gauge sits beside the engine
- Babushkas hand out 20 litre canisters: drive past in her lane, at any speed. Run her down and the canister bursts, honk and she dives into the ditch with it
- OKTAN 98: one golden canister at a time. 350 % power for 20 seconds, the car turns gold, the engine drinks twice as much. Such laps get a lightning bolt on the board
- The kolkhoz fuel depot by the start line: stop there and the tank fills (the clock runs)
- An empty tank: the car coasts as far as it rolls. A canister on the way starts the engine again. Stopped, the tractor tows it to the depot: the lap is void and the norm loses a lap
- The keyboard drinks less than a controller trigger, to make up for the all or nothing throttle
- Day and night in the rally: sunset from lap 4, dusk at lap 7 (headlights on), night from lap 8, dawn from lap 14. Headlights break in crashes
- At night: runway lights, lit windows, MiK afterburners, muzzle flashes, bus and Kozlik headlamps, the radar searchlight, a floodlight over the KGB
- Climate change allowance: the norm is eased by 1.0 s at sunset, 2.0 at dusk, 3.5 at night and 1.5 at dawn. Night laps get a moon on the board

## 0.45

- Game controllers: analog throttle (RT), brake (LT) and steering (left stick), A handbrake, LB clutch, Y horn, B rescue, Start pause. Faster steering on the stick
- Every menu works with a controller, names too: up and down turn the letter, A adds one, B deletes, Start confirms
- The prompts and the key card show the buttons when a controller is in use
- Rumble: crashes, potholes, landings, explosions, gravel and grass. RB in the pause menu turns it off (V on the keyboard)
- The boards show whether a lap was driven on the keyboard or with a controller. So do the LAN results
- A controller unplugged in a race pauses the game
- The title menu has two levels: SINGLE PLAYER (STAKHANOV LAPS, SOCIALIST RALLY) and MULTIPLAYER (LAN RACE, SPLIT SCREEN under construction)

## 0.44

- LAN RACE: up to 4 comrades race each other on the local network in the Stakhanov world. Host a game or join one (games on the network are found automatically, or type the IP of the host)
- The lobby shows the comrades and their ping. The host picks 3, 5 or 10 laps
- The power is chosen by a secret ballot. Most votes wins, a tie spins the lottery drum. The people always approve 99.8 %
- A 2 x 2 grid drawn by lot, one countdown for all. Name tags over the cars, arrows at the screen edge point to comrades out of sight
- The winner is announced at once, the others have 60 s to finish. Then the results and back to the lobby
- Cars pass through each other. Esc does not pause a LAN race. LAN laps never touch the records
- The firewall of the host must let UDP ports 24105 and 24106 in

## 0.43

- Trees and telegraph poles break when hit really hard (the car still stops dead). A broken pole drops its wires
- Stone walls crumble and let a fast car through, at a price in speed
- Tank shells and tanks fell trees easily
- The KGB keeps a file: a restart costs 2 laps of the plan, and deserting a plan shortens the next one
- Hills and slopes are easier to read: lower sun, stronger relief shading and shadows behind crests
- The mud stretch in the south is 40 % shorter
- Radar interference on the radio reaches half as far
- The jets are MiK-21s again

## 0.42

- Quicker steering: the front wheels reach full lock at once, and there is more of it (40 degrees, 26 at 100 km/h)
- Brake balance 70/30 instead of 80/20: less plough when braking into a bend. Pumping the brakes still steers best

## 0.41

- Revenge MiK-21 missiles are survivable: slower, clumsier, randomly aimed, a warning with arrows and a beep. Only a direct hit is fatal, near misses cost engine health
- A wrecked car or a seized engine no longer means the gulag: a new Pyatyorka in 10 seconds, the plan pays 2 or 1 laps. The gulag only comes when the plan runs out
- The horn matters: babushkas dive into the ditch (or freeze), the bus takes the ditch and may catch fire. A spare bus follows
- Tanks exercise across the road from firing positions, slower and further apart. Revenge tanks fire from their positions
- No time penalty for babushkas. The revenge babushkas are grandmothers
- The gulag truck has a canvas cover
- Power lines between the poles, birches of all sizes, spruces and pines
- Three times the potholes and puddles, irregularly spaced. Puddles blend into the road around them
- Quieter road noise under the engine
- Title: music only, louder, barely a radio. The menu sits below the replay car
- The bear in the Kozlik is the duty officer: a huge cap, a vodka bottle and hiccups
- Runway numbers no longer flicker
- A hard handbrake turn can shake off a missile's lock
- Made-up names for everything: the Pyatyorka 2105, the Pazik bus, the Kozlik jeep, MT-Agro tractors

## 0.4

- The menu: SOCIALIST RALLY and STAKHANOV LAPS
- The title screen is cleaner: key help, track name and the power line are gone
- WATCH RECORD is gone: press up to REPLAY OF THE RECORD LAP and Enter hides the menu. Enter again brings it back
- The world behind the menu is black and white. The record lap alone plays in colour
- Controls card before every start from the menu: any key and the countdown begins. K shows it again and pauses the game
- Radio: ´ next song, 0 and + volume in 11 steps, remembered between games. Number keys no longer pick stations
- Radio is on for the rally and off for Stakhanov laps (a restart keeps your choice)
- Radio keys do nothing while you type your name on the board
- Vulkan renderer (OpenGL if Vulkan is missing): a little faster
- Stakhanov laps are a clean time trial: no bus, Kozlik, tanks, tractor, drunks, babushkas or MiK-21s, no radar noise on the radio
- In Stakhanov laps the Pyatyorka takes no damage at all, not even a dent
- TAB switches the comments on the screen on and off: on for the rally, off for Stakhanov laps
- Two boards of honour: SOCIALIST RALLY and STAKHANOV LAPS, left and right switch between them
- Stakhanov laps: keys 1-9 set the power (50, 75, 100, 125, 150, 175, 200, 350 or 500 %), 100 % at the start. The old +10 % a lap plan is gone
- Every Stakhanov lap counts, at any power, and the board shows the highest power used on that lap
- 350 % and 500 % are truly brutal: taller gearing and more grip, up to 380 km/h on a long enough road
- STAKHANOV LAPS is now first on the menu
- The title screen carries a copyright line
- A TEST VERSION card with this changelog greets you at every launch: the game is not finished and changes all the time
- Old records are archived by the ministry: both boards start empty

## 0.33

- Horn on Ctrl: hold it to honk
- The kolkhoz mechanic sleeps on the bench of the first bus stop. A honk wakes him if he is within earshot
- Park a damaged Pyatyorka by the shelter and he comes over with a sledgehammer: 10 seconds of beating, then engine and bodywork are as good as new. The clock keeps running
- THE BIRCH WON now only appears when you really hit a birch
- Two new radio stations: Unknown Party Classic No. 1 and No. 2 (Censored)
- The title screen lists the horn key

## 0.32

- Training laps: power starts at 40 % and rises 10 % every lap, all the way to 200 %
- Records only at exactly 100 % power: below it you are training, above it you are cheating
- Something special happens when the plan is overfulfilled twice over
- The Pyatyorka has a dark grey underbody instead of olive paint underneath
- CHANGELOG on the title menu (you are reading it)

## 0.31

- Engine health is much more forgiving: crash, shell and MiK-21 blast damage halved
- A 12 m/s wall now costs about 10 % instead of 20 %
- Direct hits (tank shell, missile, a MiK-21 landing on you) still destroy the car

## 0.3

- The plan: 10 laps to beat the target lap time, every new #1 lap gives 10 more laps
- Plan fulfilled: success screen with a medal. Out of laps: the truck takes you to the gulag
- Engine health: crashes hurt the engine, smoke at 25 %, oil and death at 0 %
- Tank shells, missiles and crashing MiK-21s can destroy the car outright
- Revenge babushkas: the MiK-21 pilot and the tank driver have mothers, and they remember
- MiK-21 wingtip missiles with guidance
- KGB officers at the start line: the clerk logs your laps, the supervisor watches and smokes
- Guard Kozlik patrol with a peaked-cap driver and a bear
- Car radio interference near the radar station
- New jet sound that carries across the map, 3D sounds fixed (jets, tractor, bus, tanks)
- Terrain reshaped: a massif bottom-left and a ridge on the right
- Records from a differently shaped road are archived, never deleted
- Audio mix with a master limiter, player car sounds +6 dB

## 0.2

- Training laps from the title menu: power 25-50-75-100 % per lap
- Top-10 leaderboard with driver names, LEADERBOARD on the title menu
- WATCH RECORD replays the record lap without any text
- Crash damage on the Pyatyorka: dents, hanging bumpers, headlights popping out, cracked windscreen
- Breakable wooden fences
- Rescue tractor tows a flipped car back onto its wheels
- R calls the mechanic: back on the road, but the lap is void
- Clutch on Shift: free-rev, coast, dump it to kick the rear loose
- Babushka hits: she flies, sometimes into the camera lens, +1 s penalty
- Ghosts record steering, suspension, smoke, mud and backfires
- Bus stop moved to the barracks, radar next to the road with a shorter tower
- Lap counter restarts on a new run
- Linux and Windows builds

## 0.1

- First version: a time trial around the kolkhoz in a tippy Pyatyorka 2105 rally special
- Rally-Sport style handling, drifts, wheelspin, brake lock-up without ABS, backfires
- Potholes, puddles, slab joints, crests, dips and jumps
- Tyre marks, smoke, gravel, mud and water
- Drunk tractorist ploughing furrows, babushkas walking the verges, farm fences
- Highway strip airbase with a hangar and MiK-21 sorties that end in crashes
- Tank exercise: two T-72s shoot badly at each other
- Pazik bus running against the race, bus stops with drunks
- Barracks, rotting barns and an absurd radar station
- Title screen replays the record lap
- Car radio with stations and a cheap Soviet radio sound on the title music
