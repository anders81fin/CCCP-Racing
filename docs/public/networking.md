# LAN multiplayer

Up to six players race on Ilyich, Siberia, Atomgrad or the bonus track. All
participants need the same game version and LAN protocol: the join handshake
compares the full version string plus the protocol (currently 17), so a host and
a client of different releases are refused with VERSION MISMATCH. Every player sends what the campaign has opened (tracks and
fuels) with the hello, and the SECRET BALLOT offers the tracks and fuels of the
player with the most opened of both (a tie goes to the lowest slot, the host
first). Joining players do not need their own medals for the selected track.

One player hosts a lobby. Others join through local discovery or the host's
IP address. The host needs UDP **24105** for game traffic and **24106** for
discovery. This is a local-network mode; it does not provide matchmaking,
Internet relay or host migration.

The SECRET BALLOT allows 60 seconds to choose the track, fuel, damage, fuel
consumption and moving obstacles. Unsubmitted choices are NOT VOTED; when
nobody votes, the settings are chosen randomly. Players then load the selected track while
remaining connected. The host waits for matching track signatures and for all
drivers to hold the clutch. A shared countdown starts the round. Session
standings count victories across rounds; only the host continues or ends the
session. Leaving the host ends the hosted session.

## Track rules: fuel and radiation

Ilyich and Atomgrad-4 have no fuel: there are no tanks, depot or canisters, and
the lobby offers the fuel and fuel-consumption votes only when at least one
offered track has fuel. On Atomgrad-4 the collected radiation dose adds up to
15 percent engine power and slowly wears the engine; the decontamination wash
lowers the dose. The host simulates each driver's dose and the damage vote
applies to this wear; the power reaches clients through the car state, and the
client's dose gauge and RAD hint follow it. The dosimetrist stop is a rally-only
mechanic and does not occur in LAN, and the LAN replay hides the HUD.

## Authority and world state

The host simulates cars and world AI, validates checkpoints and determines race
results. Clients send control intent, predict their own car and reconcile to
the host. Authoritative correction may be visible during network stalls.
World actors, damage presentation and shared lighting use the networking
integration; cosmetic particles are not exact network physics trajectories.

LAN race results remain separate from single-player records. RECORD LAN RACES?
asks whether this machine should record in the background; it is a local
setting and adds no recording traffic to the network. After the finish, KEEP
retains a copy of the completed race instead of opening a viewer during the
session. Offline playback and MP4 export are available after the host ends it.

## Synchronizing single-player leaderboards

The lobby's SYNC LEADERBOARDS switch starts OFF in every session.
Up/Down selects LAPS or the switch; Left/Right sets OFF/ON (controller: D-pad).
ON shares board times, names and attached recordings with other participants
who also choose ON, and adds their results locally. Clicking OFF/ON works too.
R RESYNC / CANCEL (controller X) starts another sync or cancels ongoing work.
B (controller Y) also opens the consent question or turns sharing off.
OFF withdraws consent; results already merged remain on disk.

Only the lobby before the ballot permits synchronization. The host waits for
all transfers and disk workers before starting the ballot. A host choosing NO
can relay between two consenting clients without importing their records.
Existing local recording files are never overwritten; incoming pairs are
verified and missing recordings are fetched once. Incompatible track records
are skipped. Backups precede changes to existing board files.

## Source entry points

- `src/Net/LanSession.cs`: protocol, transport and session state.
- `src/Net/LanDiscovery.cs`: local discovery.
- `src/Race/RaceScene.Lan.cs`: game integration.

Authority checks and a local multi-process smoke test:

```sh
dotnet build
godot-mono --headless --path . res://scenes/tests/PhysicsTest.tscn -- --lan-authority-tests
python3 tools/net/lan_smoke.py
```

The process test uses isolated test data and local UDP. Passing it does not
establish compatibility with a different machine, router or firewall setup.
