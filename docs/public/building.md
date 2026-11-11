# Building from source

Use Godot **4.7.2 .NET** and the **.NET 10 SDK**. The standard Godot build without
.NET support cannot run the C# project. Commands here call the Godot .NET
executable `godot-mono`; substitute its name on your system if necessary.

From the repository root:

```sh
dotnet build
godot-mono --headless --path . --import
godot-mono --path . --windowed
```

On Linux, `./play.sh --windowed` runs those steps. You can also open
`project.godot` in the Godot .NET editor and build/run it there.

## Source assets

Models, tracks and synthesized game sounds are included. Juha Koskikuru's music
is separately licensed and is omitted. A source build has no bundled radio music
stations; attempts to load omitted music may produce a missing-resource message.
Do not copy the official music into a redistributed source tree without permission.

The player car is loaded from `assets/cars/pyatyorka2105.glb`. Its procedural
Blender builder is `tools/blender/build_pyatyorka.py`. To regenerate the model:

```sh
blender --background --python tools/blender/build_pyatyorka.py -- assets/cars
```

The Linux x86_64 MP4 renderer uses an embedded native window helper. Its source,
build command and platform limits are documented in
[`native/export-window/README.md`](../../native/export-window/README.md).

## Checks

```sh
godot-mono --headless --path . res://scenes/tests/PhysicsTest.tscn
```

Run `dotnet build` first. Individual tests have additional switches in
`src/Tests/PhysicsTest.cs`. Headless checks do not verify GPU rendering, audio
output or physical controller behavior.

The checks need the ordinary (development) build. A release build, the
`ExportRelease` configuration that Godot uses for exports, leaves out the
test code (`src/Tests` and the test partials of `RaceScene` that
`CCCPRacing.csproj` lists), so exported packages are smaller and carry no test
entry points. To build it directly, name the project file, because the
solution has no such configuration:
`dotnet build CCCPRacing.csproj -c ExportRelease`.

## Packages

Install the matching Godot .NET export templates. The package script accepts
`linux`, `windows`, `windows-d3d12` and `macos`; no arguments builds all four:

```sh
sh tools/package.sh
```

The script uses GPU export and may open an editor window. Outputs are under
`build/`. Keep the executable, PCK and runtime folder together. Windows Vulkan
and Direct3D 12 are separate packages of the same game version.

Packages include LICENSE, COPYING, CC-BY-SA-4.0.txt, CREDITS.md and third-party
notices. When distributing binaries under the GPL, make the matching source
available and state where to obtain it. The official source repository is
https://github.com/anders81fin/CCCP-Racing.
