# LAN multiplayer

Up to six players race on Ilyich, Siberia, Atomgrad or the bonus track. All
participants need a compatible game build and LAN protocol; the current code
uses protocol 11. The host's campaign unlocks determine the available tracks.
Joining players do not need their own medals for the selected track.

One player hosts a lobby. Others join through local discovery or the host's
IP address. The host needs UDP **24105** for game traffic and **24106** for
discovery. This is a local-network mode; it does not provide matchmaking,
Internet relay or host migration.

Players vote on the race settings, then load the same selected track while
remaining connected. The host waits for matching track signatures and for all
drivers to hold the clutch. A shared countdown starts the round. Session
standings count victories across rounds; only the host continues or ends the
session. Leaving the host ends the hosted session.

## Authority and world state

The host simulates cars and world AI, validates checkpoints and determines race
results. Clients send control intent, predict their own car and reconcile to
the host. Authoritative correction may be visible during network stalls.
World actors, damage presentation and shared lighting use the networking
integration; cosmetic particles are not exact network physics trajectories.

LAN results are separate from single-player records. A post-race replay can
follow different drivers. MP4 export becomes available after the host ends the
session, so it cannot interfere with a continuing race.

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
