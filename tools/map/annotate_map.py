#!/usr/bin/env python3
"""
Turns the MapExport render + data dump into two planning maps:

  bonus_map.png     top-down game render with grid, control points, distance markers,
                      crests/dips/jump, start and scale
  bonus_height.png  elevation colours with 1 m / 5 m contours and the road on top

Coordinates on the grid are the same metres as tracks/bonus.json (x to the right, z down;
-z is "up the screen" in the game).

Usage: python3 tools/map/annotate_map.py [docs/map] [track id, default bonus]

A track's "plan" block (things not built in the game yet: river, ford, barn, depot, houses,
labels) is drawn on top from tracks/<id>.json.
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

DIR = sys.argv[1] if len(sys.argv) > 1 else "docs/map"
TRACK = sys.argv[2] if len(sys.argv) > 2 else "bonus"
FONT = "/usr/share/fonts/liberation/LiberationSans-Bold.ttf"
FONT_R = "/usr/share/fonts/liberation/LiberationSans-Regular.ttf"


def font(size, bold=True):
    try:
        return ImageFont.truetype(FONT if bold else FONT_R, size)
    except OSError:
        return ImageFont.load_default()


data = json.load(open(os.path.join(DIR, f"{TRACK}_data.json")))


def load_track_json(path):
    """tracks/*.json allows // comments: strip them (not inside strings) before parsing."""
    out, in_str, i, src = [], False, 0, open(path).read()
    while i < len(src):
        c = src[i]
        if c == '"' and (i == 0 or src[i - 1] != "\\"):
            in_str = not in_str
        if not in_str and src.startswith("//", i):
            i = src.find("\n", i)
            if i < 0:
                break
            continue
        out.append(c)
        i += 1
    return json.loads("".join(out))


PLAN = load_track_json(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "tracks", f"{TRACK}.json")).get("plan")
W = data["worldSize"]
centre = np.array(data["centre"])          # x, z, road height, half width, surface
n_c = len(centre)


def text(d, xy, s, f, fill=(255, 255, 255), stroke=(0, 0, 0), anchor="mm", sw=None):
    d.text(xy, s, font=f, fill=fill, stroke_width=sw if sw is not None else max(2, f.size // 9),
           stroke_fill=stroke, anchor=anchor)


def centre_dir(i):
    a = centre[(i + 2) % n_c, :2]
    b = centre[(i - 2) % n_c, :2]
    v = a - b
    return v / (np.linalg.norm(v) + 1e-9)


def annotate(img, ppm, margin, title, subtitle):
    """Common overlay: grid, road markers, control points, features, start, legend."""
    W_px = int(W * ppm)
    canvas = Image.new("RGB", (W_px + 2 * margin, W_px + 2 * margin + int(margin * 1.2)), (24, 22, 22))
    canvas.paste(img.convert("RGB"), (margin, margin))
    over = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(over)

    def P(x, z):
        return (margin + x * ppm, margin + z * ppm)

    f_small = font(max(11, int(ppm * 1.3)))
    f_mid = font(max(14, int(ppm * 1.9)))
    f_big = font(max(18, int(ppm * 2.8)))
    f_title = font(max(24, int(ppm * 4.2)))

    # Grid: 20 m lines, labelled every 40 m on all four sides.
    for m in range(0, int(W) + 1, 20):
        strong = m % 40 == 0
        col = (255, 255, 255, 70 if strong else 35)
        d.line([P(m, 0), P(m, W)], fill=col, width=2 if strong else 1)
        d.line([P(0, m), P(W, m)], fill=col, width=2 if strong else 1)
        if strong:
            text(d, (P(m, 0)[0], margin * 0.5), f"x {m}", f_small)
            text(d, (P(m, W)[0], margin + W_px + margin * 0.5), f"x {m}", f_small)
            text(d, (margin * 0.5, P(0, m)[1]), f"z {m}", f_small)
            text(d, (margin + W_px + margin * 0.5, P(0, m)[1]), f"z {m}", f_small)

    # Distance markers along the road every 50 m (metres from the start line = "at" in features).
    for i in range(0, n_c, 50):
        x, z = centre[i, 0], centre[i, 1]
        dv = centre_dir(i)
        side = np.array([-dv[1], dv[0]])
        off = centre[i, 3] + 4.5
        lx, lz = x + side[0] * off, z + side[1] * off
        r = ppm * 0.9
        d.ellipse([P(x, z)[0] - r, P(x, z)[1] - r, P(x, z)[0] + r, P(x, z)[1] + r], fill=(255, 255, 255, 230),
                  outline=(0, 0, 0, 255), width=2)
        d.line([P(x, z), P(lx, lz)], fill=(255, 255, 255, 160), width=2)
        text(d, P(lx, lz), f"{i} m", f_small, fill=(255, 255, 255))

    # Features.
    icon = {"crest": "▲", "dip": "▼", "jump": "⤴"}
    col = {"crest": (255, 190, 60), "dip": (90, 200, 255), "jump": (255, 80, 60)}
    for ft in data["features"]:
        i = int(ft["at"]) % n_c
        x, z = centre[i, 0], centre[i, 1]
        dv = centre_dir(i)
        side = np.array([-dv[1], dv[0]])
        off = -(centre[i, 3] + 7)
        lx, lz = x + side[0] * off, z + side[1] * off
        # Mark the extent along the road.
        half = int(ft["length"] / 2) if ft["type"] != "jump" else 0
        seg = [(centre[(i + k) % n_c, 0], centre[(i + k) % n_c, 1]) for k in
               (range(-half, half + 1, 2) if half else range(-int(ft["length"]), 1, 2))]
        c = col.get(ft["type"], (255, 255, 255))
        d.line([P(a, b) for a, b in seg], fill=c + (230,), width=max(3, int(ppm * 0.8)))
        d.line([P(x, z), P(lx, lz)], fill=c + (200,), width=2)
        name = {"crest": "harjanne", "dip": "notko", "jump": "hyppyri"}.get(ft["type"], ft["type"])
        label = f"{name} {ft['height']:.2f} m @{int(ft['at'])}"
        text(d, P(lx, lz), label, f_small, fill=c)

    # The plan: what the game does not build yet (river, ford, barn, depot, houses, labels).
    if PLAN:
        if "pond" in PLAN:
            q = PLAN["pond"]
            d.ellipse([P(q["x"] - q["r"], q["z"] - q["r"]), P(q["x"] + q["r"], q["z"] + q["r"])], fill=(60, 130, 230, 210))
        if "river" in PLAN:
            rw = PLAN.get("riverWidth", 10)
            d.line([P(x, z) for x, z in PLAN["river"]], fill=(60, 130, 230, 200), width=int(rw * ppm), joint="curve")
        for hx, hz in PLAN.get("houses", []):
            d.rectangle([P(hx - 4, hz - 3), P(hx + 4, hz + 3)], fill=(150, 60, 40, 230), outline=(255, 220, 160, 255), width=2)
        if "barn" in PLAN:
            b = PLAN["barn"]
            a = math.radians(b.get("rot", 0))
            corners = []
            for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                lx, lz = sx * b["w"] / 2, sz * b["d"] / 2
                corners.append(P(b["x"] + lx * math.cos(a) - lz * math.sin(a), b["z"] + lx * math.sin(a) + lz * math.cos(a)))
            d.polygon(corners, outline=(255, 215, 80, 255), width=max(3, int(ppm * 0.6)))
            text(d, P(b["x"], b["z"] - b["d"] / 2 - 5), b.get("label", "BARN"), f_small, fill=(255, 215, 80))
        if "church" in PLAN:
            q = PLAN["church"]
            d.rectangle([P(q["x"] - 5, q["z"] - 8), P(q["x"] + 5, q["z"] + 8)], fill=(235, 225, 200, 240), outline=(255, 215, 80, 255), width=2)
            r = ppm * 2.2
            d.ellipse([P(q["x"], q["z"] - 8)[0] - r, P(q["x"], q["z"] - 8)[1] - r, P(q["x"], q["z"] - 8)[0] + r, P(q["x"], q["z"] - 8)[1] + r], fill=(60, 160, 90, 255))
            text(d, P(q["x"], q["z"] + 13), q.get("label", "CHURCH"), f_small, fill=(255, 235, 180))
        for key, colr in (("ford", (120, 200, 255)), ("depot", (255, 120, 60))):
            if key in PLAN:
                q = PLAN[key]
                r = ppm * 3
                d.ellipse([P(q["x"], q["z"])[0] - r, P(q["x"], q["z"])[1] - r, P(q["x"], q["z"])[0] + r, P(q["x"], q["z"])[1] + r],
                          outline=colr + (255,), width=max(3, int(ppm * 0.5)))
                text(d, P(q["x"], q["z"] + 8), q.get("label", key.upper()), f_small, fill=colr)
        for lb in PLAN.get("labels", []):
            text(d, P(lb["x"], lb["z"]), lb["text"], f_mid, fill=(255, 255, 255), anchor="lm")

    # Control points (tracks/<id>.json "points").
    for k, p in enumerate(data["points"]):
        x, z = p["x"], p["z"]
        r = ppm * 1.8
        d.ellipse([P(x, z)[0] - r, P(x, z)[1] - r, P(x, z)[0] + r, P(x, z)[1] + r],
                  fill=(200, 30, 30, 235), outline=(255, 220, 90, 255), width=3)
        text(d, P(x, z), f"P{k}", f_small, fill=(255, 235, 150), sw=2)

    # Start line and direction of travel.
    x, z = centre[0, 0], centre[0, 1]
    dv = centre_dir(0)
    side = np.array([-dv[1], dv[0]])
    hw = centre[0, 3] + 1.5
    d.line([P(x - side[0] * hw, z - side[1] * hw), P(x + side[0] * hw, z + side[1] * hw)], fill=(255, 255, 255, 255), width=int(ppm))
    ax, az = x + dv[0] * 18, z + dv[1] * 18
    d.line([P(x, z), P(ax, az)], fill=(255, 255, 255, 255), width=max(3, int(ppm * 0.7)))
    ang = math.atan2(dv[1], dv[0])
    for s in (-1, 1):
        hx = ax - 5 * math.cos(ang + s * 0.5)
        hz = az - 5 * math.sin(ang + s * 0.5)
        d.line([P(ax, az), P(hx, hz)], fill=(255, 255, 255, 255), width=max(3, int(ppm * 0.7)))
    text(d, P(x - side[0] * (hw + 6), z - side[1] * (hw + 6)), "LÄHTÖ / MAALI", f_mid, fill=(255, 255, 255))

    canvas = Image.alpha_composite(canvas.convert("RGBA"), over)
    d = ImageDraw.Draw(canvas)

    # Title, scale bar and legend under the map.
    y0 = margin + W_px + margin * 1.05
    text(d, (margin, y0), title, f_title, anchor="lm", fill=(255, 215, 80))
    text(d, (margin, y0 + f_title.size * 1.05), subtitle, f_small, anchor="lm", fill=(220, 220, 220))
    sx = margin + W_px - 50 * ppm
    d.rectangle([sx, y0 - 6, sx + 50 * ppm, y0 + 6], fill=(255, 255, 255))
    for k in range(0, 51, 10):
        d.line([sx + k * ppm, y0 - 12, sx + k * ppm, y0 + 12], fill=(255, 255, 255), width=2)
    text(d, (sx + 25 * ppm, y0 + 26), "50 m", f_mid)
    return canvas


# ---------------------------------------------------------------- 1. game render map

render = Image.open(os.path.join(DIR, f"{TRACK}_render.png"))
ppm1 = data["pxPerMetre"]
m1 = annotate(render, ppm1, margin=110, title=f"{data['name']} – kartta",
              subtitle=f"Ruudukko 20 m, koordinaatit = tracks/{TRACK}.json (x oikealle, z alas; pelissä -z on ruudun ylälaita). "
                       "P = ohjauspisteet, valkoiset pallot = etäisyys lähdöstä tietä pitkin (= featuresin 'at').")
m1.convert("RGB").save(os.path.join(DIR, f"{TRACK}_map.png"), optimize=True)

# ---------------------------------------------------------------- 2. elevation map

nv = data["n"] + 1
H = np.array(data["heights"], dtype=np.float32).reshape(nv, nv)   # [z][x]
ppm2 = 5
size = int(W * ppm2)
# Bilinear upsample of the vertex grid.
gx = np.linspace(0, nv - 1, size)
x0 = np.floor(gx).astype(int).clip(0, nv - 2)
fx = gx - x0
rows = H[:, x0] * (1 - fx) + H[:, x0 + 1] * fx
Z = rows[x0, :] * (1 - fx)[:, None] + rows[x0 + 1, :] * fx[:, None]

lo, hi = np.percentile(Z, 1), np.percentile(Z, 99.5)
t = np.clip((Z - lo) / (hi - lo), 0, 1)
# Terrain colour ramp: low green -> yellow -> brown -> pale.
stops = np.array([[0.00, 40, 90, 60], [0.30, 90, 150, 70], [0.55, 200, 190, 90],
                  [0.78, 160, 110, 60], [1.00, 235, 225, 210]], dtype=np.float32)
rgb = np.zeros(Z.shape + (3,), np.float32)
for c in range(3):
    rgb[..., c] = np.interp(t, stops[:, 0], stops[:, c + 1])
# Hill shading so the shapes read.
gy, gxx = np.gradient(Z, 1.0 / ppm2)
shade = np.clip(1.0 + (-gxx * 0.45 - gy * 0.55) * 0.35, 0.6, 1.3)
rgb *= shade[..., None]
# Contours: 1 m thin, 5 m bold.
for step, dark in ((1.0, 0.72), (5.0, 0.35)):
    q = np.floor(Z / step)
    edge = (np.abs(np.diff(q, axis=0, prepend=q[:1])) + np.abs(np.diff(q, axis=1, prepend=q[:, :1]))) > 0
    rgb[edge] *= dark
elev = Image.fromarray(np.clip(rgb, 0, 255).astype(np.uint8))

# Road on top: outline + surface colour.
d = ImageDraw.Draw(elev, "RGBA")
surf_col = {"Concrete": (170, 170, 165, 200), "Gravel": (215, 190, 150, 200), "Mud": (110, 75, 45, 210),
            "Dirt": (180, 140, 95, 200), "Ice": (120, 190, 235, 220), "Snow": (240, 242, 248, 210)}
names = data["surfaces"]
pts = [(c[0] * ppm2, c[1] * ppm2) for c in centre] + [(centre[0][0] * ppm2, centre[0][1] * ppm2)]
d.line(pts, fill=(20, 20, 20, 255), width=int(10 * ppm2), joint="curve")
# One continuous stroke per run of the same surface (short segments leave seams).
run = [0]
for i in range(1, n_c + 1):
    if i == n_c or centre[i][4] != centre[run[0]][4]:
        idx = run + [i % n_c]
        col = surf_col.get(names[int(centre[run[0]][4])], (200, 200, 200, 255))
        d.line([(centre[k][0] * ppm2, centre[k][1] * ppm2) for k in idx], fill=col[:3] + (255,),
               width=int(8 * ppm2), joint="curve")
        run = [i % n_c]
    else:
        run.append(i)
# Road height labels every 100 m.
fl = font(13)
for i in range(0, n_c, 100):
    text(d, (centre[i, 0] * ppm2, centre[i, 1] * ppm2), f"{centre[i, 2]:+.1f} m", fl, fill=(255, 255, 255))

m2 = annotate(elev, ppm2, margin=80, title=f"{data['name']} – korkeuskartta",
              subtitle=f"Korkeuskäyrät 1 m (ohut) ja 5 m (paksu). Maasto {lo:+.1f} … {hi:+.1f} m. "
                       "Tien väri = pinta (betoni / sora / muta / hiekkatie), luku = tien korkeus.")
# Colour bar.
d2 = ImageDraw.Draw(m2)
bx, by, bw, bh = m2.width - 80 - 420, m2.height - 60, 420, 18
for k in range(bw):
    tt = k / (bw - 1)
    c = [int(np.interp(tt, stops[:, 0], stops[:, j + 1])) for j in range(3)]
    d2.line([bx + k, by, bx + k, by + bh], fill=tuple(c))
text(d2, (bx, by + bh + 14), f"{lo:+.0f} m", font(14), anchor="lm")
text(d2, (bx + bw, by + bh + 14), f"{hi:+.0f} m", font(14), anchor="rm")
m2.convert("RGB").save(os.path.join(DIR, f"{TRACK}_height.png"), optimize=True)

print("Wrote", os.path.join(DIR, f"{TRACK}_map.png"), "and", os.path.join(DIR, f"{TRACK}_height.png"))
