# Making your own tracks

A CCCP Racing track is one JSON file. You can write it by hand or let your AI agent write it, check it with the game's own
validator, and drive it in the game. A custom track is **data only**: it uses the game's buildings, trees, surfaces and road
bumps. It cannot add code, models, textures or sounds, so it cannot break the game or your files.

## Where a track lives

One folder per track in the game's user data folder, holding `track.json`:

| system | folder |
| --- | --- |
| Linux | `~/.local/share/godot/app_userdata/CCCP Racing/tracks/NAME/track.json` |
| Windows | `%APPDATA%\Godot\app_userdata\CCCP Racing\tracks\NAME\track.json` |
| macOS | `~/Library/Application Support/Godot/app_userdata/CCCP Racing/tracks/NAME/track.json` |

`NAME` is lower-case letters, digits and hyphens, 1 to 32 characters (`my-first-oval`). In the game the track is
`custom-NAME`. Two complete examples are in [examples/oval](examples/oval/track.json) (the smallest useful track) and
[examples/village](examples/village/track.json) (a village, a mud pit, a jump, a fence, a field, a footpath, a pole line,
autumn). Copy one into a new folder and change it.

## The loop: write, check, look, drive

1. **Write** `track.json` (the reference lists every field: [custom-tracks-reference.md](custom-tracks-reference.md)).
2. **Check** it. This is the step an agent must repeat until it says `RESULT: OK`:

   ```
   cccp-racing --headless -- --validate-track=NAME
   ```

   (`NAME` is the folder name in the game's `tracks/` folder, which is looked at first; a path to a folder or to a `track.json`
   also works, and so does a folder of that name in the current directory when there is no such track in the game's folder. The
   first line, `Checking ...`, names the file that was checked: make sure it is the one you meant.) It prints one line per finding, with the
   JSON path and what to change, for example `ERROR props[3].type: 'barnn' is not a prop a custom track may place (did you
   mean 'barn'?)`. It exits with 0 when the track is acceptable and 1 when it is not, and the last line, `TRACKCHECK {...}`,
   is the same result as JSON. A track that passes has been built once, so it will load. Warnings do not stop a track but
   usually mean something looks wrong (a road that crosses itself, a very long gap between two points).
3. **Look** at the map if you can render it (the check prints the road length and a signature; the in-game track tour and the
   map export are developer tools in the source repository).
4. **Drive** it: in the game choose SINGLE PLAYER > CUSTOM TRACKS. The list shows your tracks with your best lap on each
   (`ERROR` on a track that does not pass the check; select it to see why). Up and down choose (the d-pad too), Enter or A
   starts free laps at once (DIESEL, daylight: a custom track has no medals, norms or fuel), `O` opens the tracks folder in your
   file manager, Esc or B goes back. EXIT RACE brings you back to the list on the track you drove. A track that passes the check
   but still cannot be built (this should not happen) takes you back to the first track with `COULD NOT LOAD THIS TRACK` and the
   reason, and the list opens again. From the command line: `cccp-racing -- --track=custom-NAME --start --training`.

## How a track works

- **The map** is a square of `size x tileSize` metres (the examples: 160 x 2 = 320 m). Everything has `x` and `z` in metres
  from the map's corner. The road and the props must stay inside it, 14 m from the edge (props 2 m).
- **The road** is a **closed loop** through `points`, in driving order; do not repeat the first point at the end. Give each
  point a `width` (4 to 24 m; 10 to 12 is a normal rally road), a `surface` and, if you like, a `lift` (a raised point).
  The start line is at the first point and the car drives towards the second. A lap should be at least 600 m. Points 40 to
  100 m apart give a smooth road; closer than 2 m is refused. Keep the loop from crossing itself.
- **Surfaces** change the grip: `concrete` and `runway` grip most, `gravel`, `dirt`, `snow` less, `mud`, `field`, `deepsnow`
  and `ice` least, `water` and `puddle` slow the car. Use a mix: the examples change surface every few control points.
- **Road bumps**: `features` of type `crest`, `dip` or `jump` at `at` metres along the road (0 is the start line).
- **Scenery**: `props` are buildings and trees; `type`, `x`, `z`, `rot` (degrees). **A building's front faces the way
  `rot = atan2(dx, dz)` points, where (dx, dz) goes from the building to the road**, so houses face the road. Put details where the
  car passes: 6 to 20 m from the road, not on it. The list of types is in the reference. `fences` run beside the road
  (`from`, `to` in road metres, `side` left or right of the driving direction); `fields` and `paths` paint surfaces on the
  ground; `hills` and `plateaus` shape the land; `scatter` and `belts` add rocks and tree rows.
- **Season and ground**: `"season": "autumn"` colours the trees and grass; `"ground": "deepsnow"` makes a winter track.
- **Seed**: the same file always builds the same track. Records and ghost laps belong to the exact file: change a point and
  the old records no longer apply: the game renames them (`ghost_custom-NAME_..._v02.bin` and so on, in the player's data folder) and never deletes them, and the new shape starts its own.
- **Not available** (the campaign tracks use them): water, snow banks, the labour camp, radiation, the airbase, fuel and norms,
  the gearbox. Custom tracks are free laps, with no medals.

## Limits

At most 1500 props, 400 control points, 80 road bumps, 60 fences, a 512 kB file. The name has at most 24 characters from the
game's pixel font (letters, digits, space and `. , : ! ? - + = / % " ( ) < > # *`). Bigger maps and more props load slower,
especially on a Steam Deck: the check prints the build time on your machine.

## A prompt for your agent

> Write a CCCP Racing custom track as `track.json` in the folder `tracks/NAME/` of the game's user data. Read
> `custom-tracks.md` and `custom-tracks-reference.md` first and start from `examples/oval/track.json`. Make a closed road loop
> of about 1200 m with three surfaces, one jump and a village near the road. After every change run
> `cccp-racing --headless -- --validate-track=NAME` and fix everything until it prints `RESULT: OK` with no warnings.

## Sharing

A track is one file: share the folder. The other player puts it in their `tracks/` folder. LAN races use the campaign tracks only.
