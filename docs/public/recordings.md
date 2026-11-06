# Recordings and replay

Completed single-player Socialist Rally and Stakhanov laps are recorded
automatically. Recent session recordings are temporary; use **KEEP** to retain
a recording on disk. PARTY OFFICE > RECORDINGS opens the recording library.
PARTY MEETING > REPLAY lists recordings from the current run. A single available
lap opens directly; multiple laps present a selection. Current-run rows show
their lap number and follow driving order across recording folders. Pinned
recordings always appear first; the other sorting rules apply within each group.

Qualifying leaderboard laps are retained separately and can be opened from
their result row. Legacy or missing recordings are reported instead of being
replaced with a different lap.

## Watching and editing

V opens EDIT / EXPORT, including while using the free camera. E still raises
the free camera. Camera movement pauses while the edit panel owns input.

The viewer supports pause, seek, playback speed, single steps and available
cameras. KEEP can name a recording; EDIT provides IN/OUT boundaries, looping,
camera keys and saved view settings. Edits are stored beside the recording:
they do not cut or rewrite the original car and world files. An incompatible
edit sidecar is ignored with an explanation.

The free camera can place shots with tracking, helicopter, crane, dolly and
zoom motion. Export uses the saved camera framing. Check the viewer's on-screen
controls for keyboard and controller actions.

## Deleting

Delete (controller X) opens a confirmation on CANCEL. Deleting a retained lap
removes its recording folder and saved edits. Deleting a session entry releases
that archive entry. There is no undo. Other recordings, leaderboard times and
exported videos are not deleted by this action.

## MP4 export

The Linux x86_64 and Windows 64-bit viewer's EDIT menu offers EXPORT MP4 for the whole recording or its
cut. It needs FFmpeg with libx264 and AAC installed; FFmpeg is not bundled.
Export uses GPU rendering even when its renderer window is hidden. Progress
and cancellation stay in the game, and the source recording remains unchanged.
On Windows, put ffmpeg.exe and any DLLs supplied with it beside the game, or
make FFmpeg available on PATH. The renderer preserves the package graphics
backend. macOS MP4 export is not supported. Windows packages cross-build on
Linux; native Windows export and window visibility still need runtime validation.

## Recording fidelity

Replay reconstructs presentation from recorded state rather than storing a
video or the final mixed audio. Some effects are regenerated, and old recordings
can lack information added by newer versions. Legacy Stakhanov laps without a
fuel timeline show as petrol. A new model or actor integration must preserve
compatibility or report its fallback; see [world-tape integration](world-tape.md).

Main implementation areas: `src/Replay/`, `src/Export/`, `src/Race/Ghost*.cs`,
`src/Race/WorldTape*.cs` and the replay/viewer parts of `RaceScene`.
