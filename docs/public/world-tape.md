# World-tape integration

A car recording stores the driving state. A paired world tape records supported
moving actors, parts, sound controls and events around that lap. Playback poses
recorded content at a supplied clock; it does not rerun the original physics
or guarantee deterministic reproduction of the whole world.

## Source contracts

Start with `src/Race/WorldTape.Types.cs`, `WorldTapeIndex.cs`,
`WorldTapeRecorder.cs`, `WorldTapeCodec.cs` and `WorldTapePlayer.cs`. The compiled
interfaces and codec are the reference for exact formats and ranges.

Current pose sampling is 15 Hz (every second 30 Hz car sample); sound controls
are sampled at 10 Hz. Full keyframes are spaced five seconds apart. A pairing
fingerprint binds a world tape to its reference car recording.

## Adding or changing content

1. Register the actor root and meaningful moving parts with stable identities.
   Keep actor and part names stable across recording and playback.
2. Register continuously rotating parts with their real, unwrapped angles.
   Sparse pose samples cannot recover a rotor's number of revolutions.
3. Register relevant sound controls. The final waveform is regenerated during
   playback; it is not stored as a mixed audio recording.
4. For spawned objects and effects, represent creation, removal and lifetime.
   Seek and loop must reconstruct the proper state and age, including effects
   that began before the target clock.
5. Suppress live simulation for tape-driven content and restore the normal
   lifecycle when leaving playback.
6. Check compatibility when changing hierarchy, rest transforms or playback
   meaning. Update actor `tape_ver` when needed. An incompatible actor must
   fall back safely and report the mismatch instead of silently misplaying it.

Static scenery that never moves, changes or breaks does not need registration.
Registration alone does not establish a complete integration: exercise actual
production recording, save/load, playback, seek, loop and cleanup.

## Car damage and effects

The car recording has a separate damage extension for original deformation and
part states. Its model signature and compatibility mappings protect replay on
changed car meshes. Detached parts play from poses rather than live damage
physics. Some particles are rebuilt from emission state and age; exact original
per-frame particle paths are not necessarily retained.

Do not infer unrecorded historical state from a newer implementation. A legacy
recording can only reproduce the information it contains, plus explicitly
reported approximations. Review the existing production path before extending
fuel, towing, power state, breakables or actor effects.

Focused checks include `--tape-tests`, `--damage-tape-tests` and production-path
scene tests in `src/Tests/`. Use isolated generated fixtures. Keep original
recordings unchanged, and report any remaining live or unrecorded content.
