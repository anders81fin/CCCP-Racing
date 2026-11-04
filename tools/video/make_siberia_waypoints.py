#!/usr/bin/env python3
"""Write tools/video/drone_siberia_waypoints.json: the SIBLAG NO.2 film's flight.

Camp by day (the volunteers march out of the gate), a U-turn round the sawyers by the road, the start gate, the
start straight, up to the ridge past the radar, then the airbase at night (a MiK overtakes the drone), the west
road, past the camp (searchlights) and in to the start gate from the north. Arcs keep the turn rate below ~18 deg/s.
"""
import json
import math
from pathlib import Path

R_U = 50
CAMP = (121, 216)


def arc(center, R, h0, h1, n, ccw, agl=14):
    pts = []
    for k in range(n + 1):
        hh = h0 + (h1 - h0) * k / n
        ang = math.radians(hh + 90 if ccw else hh - 90)
        pts.append([round(center[0] + R * math.cos(ang), 1), round(center[1] + R * math.sin(ang), 1), agl])
    return pts


W = []
# 1. the camp by day, west along its road (the banner towards us), the volunteers set off at the far gate
W += [{"name": "camp_day", "eye": [182, 248, 19], "ahead": [36, 6, -4]},
      {"eye": [152, 230, 18], "ahead": [36, 6, -4]},
      {"eye": [124, 214, 17], "ahead": [36, 6, -4]},
      {"name": "gate2", "eye": [94, 201, 17], "ahead": [34, 6, -4]}]
# 2. the U-turn round the sawyers and the column: one circle, centre (75,247), left turn from west-north-west to east
U = (75, 247)
circ = arc(U, R_U, 202, 0, 9, True, 16)[1:]
for i, p in enumerate(circ):
    p[2] = [16, 16, 15, 15, 15, 14, 14, 14, 14][i]
    W.append({"eye": p, "ahead": [32, 5, -4 if i < 6 else -2]})
W[len(W) - 5]["name"] = "uturn"
W[-1]["name"] = "start_gate"
# 3. the start straight, then round the corner over the sawyers by the road (metre 200, east of the corner), up to the radar
W += [{"eye": [112, 297, 17], "ahead": [36, 5, -4]},
      {"eye": [152, 296, 17], "ahead": [36, 5, -6]},
      {"name": "straight", "eye": [196, 292, 17], "ahead": [36, 5, -2]},
      {"eye": [250, 288, 17], "ahead": [36, 5, 4]}]
for i, p in enumerate(arc((250, 236), 52, 0, -90, 4, True, 16)[1:]):
    w = {"eye": p, "ahead": [34, 5, 6]}
    if i == 1:
        w["name"] = "sawyers"
    W.append(w)
# 4. past the radar (to the east of the drone)
W += [{"eye": [298, 210, 18], "ahead": [36, 6, 2]},
      {"eye": [272, 190, 18], "ahead": [38, 6, 4]},
      {"eye": [244, 174, 17], "ahead": [38, 6, 6]},
      {"name": "radar", "eye": [230, 150, 17], "ahead": [38, 6, 8]},
      {"eye": [236, 130, 19], "ahead": [38, 6, 2]},
      {"eye": [246, 116, 22], "ahead": [38, 6, 0]},
      {"name": "north", "eye": [258, 96, 24], "ahead": [38, 6, 0]}]
# 5. round the airbase's north-east corner and west along the runway (the MiK overtakes the drone here)
for i, p in enumerate(arc((190, 96), 68, 270, 180, 5, True, 14)[1:]):
    p[2] = [24, 22, 19, 16, 14][i]
    W.append({"eye": p, "ahead": [40, 4, -8]})
W[-1]["name"] = "airbase"
W += [{"name": "runway", "eye": [150, 30, 14], "ahead": [40, 4, -10]},
      {"name": "hangars", "eye": [112, 32, 14], "ahead": [40, 4, -10]}]
for p in arc((100, 88), 56, 180, 90, 4, True, 14):
    W.append({"eye": p, "ahead": [38, 5, 0]})
W[-1]["name"] = "west"
# 6. down the west road, past the camp by night (the lamps), in to the start gate from the north
W += [{"eye": [44, 124, 16], "ahead": [40, 5, -4]},
      {"eye": [46, 160, 18], "target": [121, 216, 5]},
      {"name": "camp_in", "eye": [54, 196, 19], "target": [121, 216, 5]},
      {"eye": [60, 228, 19], "target": [121, 216, 5]},
      {"name": "lap_start", "eye": [68, 258, 17], "ahead": [32, 4, 0]},
      {"eye": [78, 284, 15], "ahead": [32, 4, 0]},
      {"eye": [96, 300, 15], "ahead": [34, 4, 0]},
      {"name": "tail", "eye": [130, 306, 15], "ahead": [34, 4, 0]}]

spec = {"speed": 14.0, "smooth_eye_m": 26, "smooth_target_m": 32,
        "map_boxes": {"AIRBASE APRON": [88, 6, 242, 30], "CAMP (road 1172-1243)": [78, 200, 155, 250]},
        "clock": [[0, 1.2], ["start_gate", 1.5], ["sawyers", 1.8], ["radar", 2.6], ["north", 3.4], ["airbase", 5.2], ["runway", 6.5],
                  ["camp_in", 7.0], ["lap_start", 11.8], [600, 11.8]],
        "waypoints": W}
out = Path(__file__).with_name('drone_siberia_waypoints.json')
out.write_text(json.dumps(spec, indent=1))
print(out, len(W), 'waypoints')
