#!/usr/bin/env python3
"""Write tools/video/drone_atomgrad_waypoints.json: the ATOMGRAD-4 film's flight.

The zone checkpoint by day (drizzle), the level crossing and the train, the red forest at dusk, the vehicle graveyard
and the lunokhod in the storm, the plant (sarcophagus, reactors, the billboard) in lightning, the cooling channel, the
town and its ferris wheel, the decontamination wash in the dawn fog, in to the start gate from the north. The light
(and with it the rain, the storm and the fog, which follow DayNight's clock) runs day -> dusk -> night -> dawn.
Arcs keep the turn rate below ~18 deg/s.
"""
import json
import math
from pathlib import Path


def arc(center, R, h0, h1, n, agl, ccw=True):
    """Points on a circle round `center` from angle h0 to h1 degrees (z points down: 90 deg is south)."""
    pts = []
    for k in range(n + 1):
        a = math.radians(h0 + (h1 - h0) * k / n)
        a_agl = agl[k] if isinstance(agl, list) else agl
        pts.append([round(center[0] + R * math.cos(a), 1), round(center[1] + R * math.sin(a), 1), a_agl])
    return pts


W = []


def look(name, eye, target):
    """A waypoint of the eye that keeps `target` (x, z, agl) abeam: the look-at point rides ahead of the eye along the
    path, offset sideways to the target's side, so the camera turns with the path instead of snapping to the feature."""
    w = {"eye": eye, "feature": target}
    if name:
        w["name"] = name
    W.append(w)


def resolve():
    """Turn the features into 'ahead' look-at specs, with the path's direction from the neighbouring eyes."""
    for i, w in enumerate(W):
        if "feature" not in w:
            continue
        a, b = W[max(i - 1, 0)]["eye"], W[min(i + 1, len(W) - 1)]["eye"]
        dx, dz = b[0] - a[0], b[1] - a[1]
        n = math.hypot(dx, dz) or 1
        dx, dz = dx / n, dz / n
        fx, fz, fa = w.pop("feature")
        vx, vz = fx - w["eye"][0], fz - w["eye"][1]
        lateral = max(-34, min(34, vx * -dz + vz * dx))      # the baker's "right" is (-dz, dx)
        w["ahead"] = [38, fa, round(lateral, 1)]


# 1. the zone checkpoint (the booth, the barrier, the ZONA sign) in the drizzle, along the south road
look("checkpoint", [24, 304, 14], [62, 297, 4])
look(None, [54, 314, 13], [64, 298, 4])
look(None, [90, 314, 14], [100, 297, 5])
# 2. the level crossing and the train
look("crossing", [124, 314, 15], [135, 286, 3])
look(None, [160, 312, 16], [136, 270, 3])
look(None, [198, 306, 17], [216, 272, 4])
look("forest_in", [234, 292, 19], [242, 252, 6])
# 3. round the red forest (centre 240,240) at dusk, to the south and east of it
for i, p in enumerate(arc((240, 240), 50, 85, -25, 5, 20)[1:]):
    look("forest" if i == 2 else None, p, [240, 240, 7])
# 4. the vehicle graveyard (wrecks at x 299-307, z 140-196), the lunokhod crossing at (268-297, 123-133)
look(None, [272, 206, 20], [303, 188, 3])
look("graveyard", [274, 178, 20], [303, 168, 3])
# 5. the plant: round the sarcophagus (233,118) and the stack, along the reactor row, past the billboard
for i, p in enumerate(arc((233, 118), 48, 30, -105, 7, [22, 23, 25, 27, 28, 27, 26, 26])):
    look({1: "robot", 4: "sarcophagus"}.get(i), p, [282, 128, 2] if i < 2 else [233, 118, 14])
look("turbine", [206, 68, 26], [205, 92, 8])
look("reactors", [172, 68, 24], [176, 118, 10])
look(None, [146, 90, 17], [150, 70, 6])
look("billboard", [134, 92, 15], [150, 70, 6])
# 6. the cooling channel (z 38, x 20-180), west along it
look(None, [118, 80, 15], [104, 44, 0])
look("channel", [96, 50, 13], [44, 40, 0])
look(None, [64, 50, 13], [20, 38, 0])
# 7. the town: the blocks, then round the ferris wheel (86,150)
look(None, [30, 86, 18], [44, 112, 6])
for i, p in enumerate(arc((86, 150), 40, 190, 80, 5, 20)):
    look("ferris" if i == 2 else None, p, [86, 150, 12])
# 8. the decontamination wash (68,231) in the fog, then in to the start gate from the north
look(None, [92, 200, 18], [70, 222, 4])
look("wash", [92, 232, 16], [68, 231, 3])
look(None, [80, 256, 16], [68, 231, 3])
W += [{"name": "lap_start", "eye": [76, 280, 16], "ahead": [32, 4, 0]},
      {"eye": [104, 292, 15], "ahead": [32, 4, 0]},
      {"eye": [138, 294, 15], "ahead": [34, 4, 0]},
      {"name": "tail", "eye": [174, 292, 15], "ahead": [34, 4, 0]}]

resolve()
spec = {"speed": 10.0, "smooth_eye_m": 24, "smooth_target_m": 60,
        "map_boxes": {},
        "clock": [[0, 0.7], ["checkpoint", 0.9], ["crossing", 1.6], ["forest_in", 3.4], ["forest", 4.4],
                  ["graveyard", 5.0], ["robot", 5.2], ["sarcophagus", 5.5], ["reactors", 5.8], ["billboard", 6.1],
                  ["channel", 6.5], ["ferris", 7.2], ["wash", 10.2], ["lap_start", 11.8], [900, 11.8]],
        "waypoints": W}
out = Path(__file__).with_name('drone_atomgrad_waypoints.json')
out.write_text(json.dumps(spec, indent=1))
print(out, len(W), 'waypoints')
