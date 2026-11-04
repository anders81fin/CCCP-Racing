# Background export window helper

Godot's headless display uses dummy rendering, and minimize/no-focus requests do
not hide its GPU window consistently on Hyprland or Gamescope. The Linux x86_64
renderer child therefore uses an isolated GNU loader audit helper to suppress
X11 window mapping from the start. It intercepts only `XMapWindow`, `XMapRaised`
and `XMapSubwindows`, including Godot's `dlsym` bindings. Rendering, the movie
writer and audio remain active. The main game, FFmpeg, desktop configuration and
other applications never receive this helper.

`linux-x86_64.so` is embedded in the managed assembly, so packaged games do not
need an external helper installation or a compiler. Export extracts it into the
job-owned directory and sets `LD_AUDIT` only on that child. The native loader
callback writes `window-hidden.ready`; export rejects frames without that proof.
Cancellation/failure/success use the existing child-kill and owned-folder cleanup.

Rebuild with `sh native/export-window/build.sh` on Linux x86_64. Only glibc
`GLIBC_2.2.5` symbols are required (`readelf -V linux-x86_64.so`); there is no
build-machine-specific libc dependency. The checked-in binary is 14 KB, built
without timestamps/build IDs. Verify reproducibility by hashing before/after a
rebuild. No additional CPU architectures or non-glibc Linux platforms are claimed.

The native GPU smoke and full export checks are documented in
`docs/replay-mp4-export-review.md`. Steam Deck hardware still needs its own native
verification; absence of an X11 mapping is independent of compositor minimize
behaviour, but is not a claim of a completed Deck test.

API references: [GNU loader auditing](https://man7.org/linux/man-pages/man7/rtld-audit.7.html)
and [Godot command-line options](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html).
