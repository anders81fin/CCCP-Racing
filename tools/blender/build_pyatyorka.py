"""
CCCP Racing - Pyatyorka 2105 rally special, low-poly.

Built from the original design screenshots: olive boxy saloon, flared arches, roof rack,
huge rear wing with red-star end plates, chrome bumpers with black corner caps,
twin exhausts, steel wheels.

Run:  blender --background --python tools/blender/build_pyatyorka.py -- <out_dir> [<preview_dir>]
Writes pyatyorka2105.blend, pyatyorka2105.glb (+ preview renders).

Blender: Z up, car faces +Y (glTF/Godot: -Z forward), +X = right.
Origin = the physics centre of gravity, 0.64 m above the ground, so the wheels line up with
the suspension in src/Car/CarParams.cs (wheel centres at y=+1.25/-1.20, x=±0.889 m in game units).
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0] if argv else ".")
PREVIEW = os.path.abspath(argv[1]) if len(argv) > 1 else None
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

# The body is authored at real Pyatyorka 2105 size ("design space", ground at z=0) and then scaled to
# the chunkier proportions of the original CCCP Racing design: shorter, wider, taller.
SX, SY, SZ = 1.11, 0.94, 1.10
CG_HEIGHT = 0.64          # physics CG above ground at rest; model is shifted down by this
WHEEL_R = 0.34            # final size (wheels are not scaled)
WHEEL_Z = 0.31            # design space; ×SZ ≈ WHEEL_R so tyres touch the ground
FRONT_Y, REAR_Y = 1.25, -1.20   # design space; ×SY in game units
HALF_TRACK = 0.889 / SX       # preview only: tyre outer face flush with flare, same as CarParams
ARCH_R = 0.44             # design space, big enough for the larger wheels


# ------------------------------------------------------------------ materials

def srgb(c):
    return tuple(((v / 255.0 + 0.055) / 1.055) ** 2.4 if v / 255.0 > 0.04045 else v / 255.0 / 12.92 for v in c)


def material(name, rgb255, metallic=0.0, roughness=0.8):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    col = (*srgb(rgb255), 1.0)
    bsdf.inputs["Base Color"].default_value = col
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    m.diffuse_color = col
    return m


M = {
    "paint": material("Paint_Olive", (190, 188, 70), roughness=0.55),
    "paint_dark": material("Paint_Olive_Dark", (150, 148, 54), roughness=0.6),
    "glass": material("Glass", (150, 160, 180), roughness=0.2),
    "seal": material("Rubber_Seal", (58, 60, 64)),
    "black": material("Black_Plastic", (34, 34, 36)),
    "chrome": material("Chrome", (215, 216, 220), metallic=0.6, roughness=0.3),
    "head": material("Headlight", (236, 236, 228), roughness=0.3),
    "amber": material("Amber", (226, 170, 40), roughness=0.4),
    "red": material("Soviet_Red", (196, 38, 26), roughness=0.5),
    "white": material("Wing_White", (226, 224, 216), roughness=0.6),
    "tyre": material("Tyre", (44, 44, 62), roughness=0.9),
    "steel": material("Steel_Wheel", (200, 200, 196), metallic=0.3, roughness=0.5),
    "grille": material("Grille", (70, 72, 78)),
    "under": material("Underbody", (62, 62, 64), roughness=0.9),
}


# ------------------------------------------------------------------ mesh helpers

class Builder:
    """Collects polygons with materials into one mesh object."""

    def __init__(self, name):
        self.name = name
        self.verts, self.faces, self.mats = [], [], []
        self.mat_list = []

    def mat_index(self, m):
        if m not in self.mat_list:
            self.mat_list.append(m)
        return self.mat_list.index(m)

    def poly(self, pts, m, outward=None):
        """Adds one polygon; if outward is given the winding is fixed to face it."""
        pts = [Vector(p) for p in pts]
        if outward is not None:
            n = (pts[1] - pts[0]).cross(pts[2] - pts[0])
            if n.dot(Vector(outward)) < 0:
                pts.reverse()
        base = len(self.verts)
        self.verts.extend(pts)
        self.faces.append(tuple(range(base, base + len(pts))))
        self.mats.append(self.mat_index(m))

    def box(self, lo, hi, m):
        (x0, y0, z0), (x1, y1, z1) = lo, hi
        c = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
             (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        for idx, n in (((0, 1, 2, 3), (0, 0, -1)), ((4, 5, 6, 7), (0, 0, 1)),
                       ((0, 1, 5, 4), (0, -1, 0)), ((1, 2, 6, 5), (1, 0, 0)),
                       ((2, 3, 7, 6), (0, 1, 0)), ((3, 0, 4, 7), (-1, 0, 0))):
            self.poly([c[i] for i in idx], m, n)

    def prism(self, bottom, top, m, cap_bottom=True):
        """Solid between two 4-point loops (same ordering)."""
        centre = sum((Vector(p) for p in bottom + top), Vector()) / 8
        n = len(bottom)
        for i in range(n):
            j = (i + 1) % n
            q = [bottom[i], bottom[j], top[j], top[i]]
            out = sum((Vector(p) for p in q), Vector()) / 4 - centre
            self.poly(q, m, out)
        self.poly(top, m, (0, 0, 1))
        if cap_bottom:
            self.poly(bottom, m, (0, 0, -1))

    def cylinder_x(self, cx, cy, cz, r, x0, x1, m, seg=12, cap_m=None, cap_end=True):
        ring0 = [(x0, cy + r * math.cos(a), cz + r * math.sin(a)) for a in (2 * math.pi * k / seg for k in range(seg))]
        ring1 = [(x1, y, z) for _, y, z in ring0]
        for k in range(seg):
            j = (k + 1) % seg
            q = [ring0[k], ring0[j], ring1[j], ring1[k]]
            mid = Vector(((q[0][1] + q[1][1]) / 2 - cy, 0, 0))
            out = Vector((0, (q[0][1] + q[1][1]) / 2 - cy, (q[0][2] + q[1][2]) / 2 - cz))
            self.poly(q, m, out)
        self.poly(ring0, cap_m or m, (-1 if x0 < x1 else 1, 0, 0))
        if cap_end:
            self.poly(ring1, cap_m or m, (1 if x0 < x1 else -1, 0, 0))

    def cylinder_y(self, cx, cz, r, y0, y1, m, seg=8):
        ring0 = [(cx + r * math.cos(a), y0, cz + r * math.sin(a)) for a in (2 * math.pi * k / seg for k in range(seg))]
        ring1 = [(x, y1, z) for x, _, z in ring0]
        for k in range(seg):
            j = (k + 1) % seg
            q = [ring0[k], ring0[j], ring1[j], ring1[k]]
            out = Vector(((q[0][0] + q[1][0]) / 2 - cx, 0, (q[0][2] + q[1][2]) / 2 - cz))
            self.poly(q, m, out)
        self.poly(ring1, m, (0, 1 if y1 > y0 else -1, 0))

    def build(self, shift_z=-CG_HEIGHT, scale=(SX, SY, SZ)):
        me = bpy.data.meshes.new(self.name)
        sx, sy, sz = scale
        me.from_pydata([(v.x * sx, v.y * sy, v.z * sz + shift_z) for v in self.verts], [], self.faces)
        for m in self.mat_list:
            me.materials.append(m)
        for poly, mi in zip(me.polygons, self.mats):
            poly.material_index = mi
            poly.use_smooth = False
        me.update()
        ob = bpy.data.objects.new(self.name, me)
        scene.collection.objects.link(ob)
        return ob


def bilerp(p00, p10, p01, p11, u, v):
    a = p00.lerp(p10, u)
    b = p01.lerp(p11, u)
    return a.lerp(b, v)


def face_quad(b, corners, u0, u1, v0, v1, offset, m, outward):
    """Sub-rectangle (in face UV) of a quad face, pushed out along its normal."""
    p00, p10, p01, p11 = (Vector(c) for c in corners)
    n = Vector(outward).normalized()
    pts = [bilerp(p00, p10, p01, p11, u, v) + n * offset for u, v in ((u0, v0), (u1, v0), (u1, v1), (u0, v1))]
    b.poly(pts, m, outward)


def face_normal(p00, p10, p01, hint):
    n = (Vector(p10) - Vector(p00)).cross(Vector(p01) - Vector(p00)).normalized()
    return n if n.dot(Vector(hint)) > 0 else -n


# ------------------------------------------------------------------ body

body = Builder("Body")

HX = 0.81                       # half width of the hull
Y_FRONT, Y_REAR = 2.07, -2.06
Z_SILL, Z_BELT = 0.20, 0.84
CH = 0.05                       # chamfer on the hull's top edges


def arch_points(y_c, r=ARCH_R):
    """Arch outline from the rear foot, over the top, to the front foot (y, z)."""
    return [(y_c + r * math.cos(math.radians(a)), WHEEL_Z + r * math.sin(math.radians(a)))
            for a in (188, 150, 120, 90, 60, 30, -8)]


# Side profile (y, z) of the hull with the wheel arches cut out of the lower edge. Extruded
# across the width; the top edge gets a chamfer.
Z_TOP = Z_BELT - CH
YF_TOP, YR_TOP = Y_FRONT - 0.04, Y_REAR + 0.05
profile = [(Y_FRONT, Z_SILL), (YF_TOP, Z_TOP), (YR_TOP, Z_TOP), (Y_REAR, Z_SILL)]
for yc in (REAR_Y, FRONT_Y):
    arch = arch_points(yc)
    profile.append((arch[0][0], Z_SILL))
    profile.extend(arch)
    profile.append((arch[-1][0], Z_SILL))

for s in (-1, 1):
    body.poly([(s * HX, y, z) for y, z in profile], M["paint"], (s, 0, 0))

centre_yz = Vector((0, (Z_SILL + Z_BELT) / 2))
n = len(profile)
for i in range(n):
    (y0, z0), (y1, z1) = profile[i], profile[(i + 1) % n]
    if i == 1:
        # Top edge: chamfer strips + top of the hull.
        body.poly([(HX, y0, Z_TOP), (HX, y1, Z_TOP), (HX - CH, y1, Z_BELT), (HX - CH, y0, Z_BELT)], M["paint"], (1, 0, 1))
        body.poly([(-HX, y0, Z_TOP), (-HX, y1, Z_TOP), (-HX + CH, y1, Z_BELT), (-HX + CH, y0, Z_BELT)], M["paint"], (-1, 0, 1))
        body.poly([(HX - CH, y0, Z_BELT), (HX - CH, y1, Z_BELT), (-HX + CH, y1, Z_BELT), (-HX + CH, y0, Z_BELT)], M["paint"], (0, 0, 1))
        continue
    ey, ez = y1 - y0, z1 - z0
    normal = Vector((-ez, ey))                # one of the two perpendiculars in (y, z)
    in_arch = i >= 4 and not (abs(z0 - Z_SILL) < 1e-4 and abs(z1 - Z_SILL) < 1e-4)
    if in_arch:
        # Arch lining faces the wheel centre.
        yc = FRONT_Y if y0 > 0 else REAR_Y
        towards = Vector((yc - (y0 + y1) / 2, WHEEL_Z - (z0 + z1) / 2))
        if normal.dot(towards) < 0:
            normal = -normal
        m = M["black"]
    else:
        mid = Vector(((y0 + y1) / 2, (z0 + z1) / 2))
        if normal.dot(mid - centre_yz) < 0:
            normal = -normal
        # The floor between the arches is dark grey underbody, not paint.
        m = M["under"] if abs(z0 - Z_SILL) < 1e-4 and abs(z1 - Z_SILL) < 1e-4 else M["paint"]
    body.poly([(HX, y0, z0), (HX, y1, z1), (-HX, y1, z1), (-HX, y0, z0)], m, (0, normal.x, normal.y))

# Closed inner wheel tubs: the old transverse arch lining left a tunnel through
# the car, visible from low free-camera angles. Keep steering clearance at front.
for yc in (FRONT_Y, REAR_Y):
    inner_x = 0.35 if yc == FRONT_Y else 0.48
    arch = arch_points(yc)
    tub = [(arch[0][0], 0.14)] + arch + [(arch[-1][0], 0.14)]
    for side in (-1, 1):
        body.poly([(side * inner_x, y, z) for y, z in tub], M["black"], (side, 0, 0))

# Boxed rocker/underfloor edges join both wheel wells without filling the
# designed ground clearance or intruding into tyre/suspension travel.
for side in (-1, 1):
    xa, xb = sorted((side * (HX - 0.12), side * (HX + 0.008)))
    body.box((xa, REAR_Y + ARCH_R + 0.01, Z_SILL - 0.005),
             (xb, FRONT_Y - ARCH_R - 0.01, Z_SILL + 0.08), M["paint_dark"])

# Close the strips between the raked nose/tail tops and the chamfered top.
for yt in (YF_TOP, YR_TOP):
    sgn = 1 if yt > 0 else -1
    body.poly([(-HX, yt, Z_TOP), (HX, yt, Z_TOP), (HX - CH, yt, Z_BELT), (-HX + CH, yt, Z_BELT)], M["paint"], (0, sgn, 0))


def flare(y_c, side):
    """Boxy, faceted wheel-arch flare standing proud of the hull (the 'levennetyt lokasuojat')."""
    r_in, r_out = ARCH_R, ARCH_R + 0.09
    x_in, x_out = side * (HX - 0.02), side * (HX + 0.09)
    angles = [math.radians(a) for a in (-8, 30, 60, 90, 120, 150, 188)]
    inner = [(y_c + r_in * math.cos(a), WHEEL_Z + r_in * math.sin(a)) for a in angles]
    outer = [(y_c + r_out * math.cos(a), WHEEL_Z + r_out * math.sin(a)) for a in angles]
    # Where the slope meets the hull's side wall: the outer arc pushed out 15 %, but never above the hull's top edge (it used to float over the bonnet and the wing).
    slope = [(o[0] + (o[0] - y_c) * 0.15, min(o[1] + (o[1] - WHEEL_Z) * 0.15, Z_TOP - 0.005)) for o in outer]
    for k in range(len(angles) - 1):
        a, bq = k, k + 1
        # outer face of the flare lip
        body.poly([(x_out, outer[a][0], outer[a][1]), (x_out, outer[bq][0], outer[bq][1]),
                   (x_out, inner[bq][0], inner[bq][1]), (x_out, inner[a][0], inner[a][1])], M["paint"], (side, 0, 0))
        # top slope from hull to lip
        body.poly([(x_in, slope[a][0], slope[a][1]), (x_in, slope[bq][0], slope[bq][1]),
                   (x_out, outer[bq][0], outer[bq][1]), (x_out, outer[a][0], outer[a][1])],
                  M["paint_dark"], (side * 0.4, outer[a][0] - y_c, outer[a][1] - WHEEL_Z))
        # arch lining (dark inside of the wheel well)
        body.poly([(x_in - side * 0.3, inner[a][0], inner[a][1]), (x_in - side * 0.3, inner[bq][0], inner[bq][1]),
                   (x_out, inner[bq][0], inner[bq][1]), (x_out, inner[a][0], inner[a][1])],
                  M["black"], (0, y_c - inner[a][0], WHEEL_Z - inner[a][1]))
    # Close both feet of the flare, so that no gap shows between the lip, the slope and the hull.
    for e in (0, len(angles) - 1):
        sgn = 1 if outer[e][0] > y_c else -1
        body.poly([(x_in, slope[e][0], slope[e][1]), (x_out, outer[e][0], outer[e][1]),
                   (x_out, inner[e][0], inner[e][1]), (x_in, inner[e][0], inner[e][1])], M["paint_dark"], (0, sgn, 0))


for yc in (FRONT_Y, REAR_Y):
    for s in (-1, 1):
        flare(yc, s)

# Cabin / greenhouse: tapered box.
cab_bottom = [(-0.76, -1.12, Z_BELT), (0.76, -1.12, Z_BELT), (0.76, 0.88, Z_BELT), (-0.76, 0.88, Z_BELT)]
cab_top = [(-0.66, -0.62, 1.40), (0.66, -0.62, 1.40), (0.66, 0.30, 1.40), (-0.66, 0.30, 1.40)]
body.prism(cab_bottom, cab_top, M["paint"], cap_bottom=False)

# Windows: dark seal frame, then glass, on each greenhouse face.
V = Vector
faces = {
    "windscreen": ((-0.76, 0.88, Z_BELT), (0.76, 0.88, Z_BELT), (-0.66, 0.30, 1.40), (0.66, 0.30, 1.40), (0, 1, 0.6)),
    "rear": ((0.76, -1.12, Z_BELT), (-0.76, -1.12, Z_BELT), (0.66, -0.62, 1.40), (-0.66, -0.62, 1.40), (0, -1, 0.6)),
}
for name, (p00, p10, p01, p11, hint) in faces.items():
    nrm = face_normal(p00, p10, p01, hint)
    face_quad(body, (p00, p10, p01, p11), 0.05, 0.95, 0.08, 0.93, 0.004, M["seal"], nrm)
    face_quad(body, (p00, p10, p01, p11), 0.08, 0.92, 0.14, 0.88, 0.008, M["glass"], nrm)

for s in (-1, 1):
    p00 = (s * 0.76, -1.12, Z_BELT)
    p10 = (s * 0.76, 0.88, Z_BELT)
    p01 = (s * 0.66, -0.62, 1.40)
    p11 = (s * 0.66, 0.30, 1.40)
    nrm = face_normal(p00, p10, p01, (s, 0, 0))
    face_quad(body, (p00, p10, p01, p11), 0.16, 0.97, 0.07, 0.93, 0.004, M["seal"], nrm)
    face_quad(body, (p00, p10, p01, p11), 0.21, 0.52, 0.13, 0.88, 0.008, M["glass"], nrm)   # rear door
    face_quad(body, (p00, p10, p01, p11), 0.57, 0.93, 0.13, 0.88, 0.008, M["glass"], nrm)   # front door

# Shut lines / trims: bonnet and boot edges as thin dark strips.
body.box((-0.70, 1.00, Z_BELT - 0.004), (0.70, 1.03, Z_BELT + 0.004), M["paint_dark"])
body.box((-0.70, -1.28, Z_BELT - 0.004), (0.70, -1.25, Z_BELT + 0.004), M["paint_dark"])

# Door handles, side indicators, mirrors.
for s in (-1, 1):
    for y in (0.45, -0.45):
        body.box((s * HX - 0.01 if s > 0 else -HX - 0.02, y - 0.10, 0.74), (HX + 0.02 if s > 0 else -HX + 0.01, y + 0.02, 0.79), M["black"])
    body.box((s * HX - 0.01 if s > 0 else -HX - 0.015, 1.62, 0.70), (HX + 0.015 if s > 0 else -HX + 0.01, 1.72, 0.75), M["amber"])
    x0, x1 = (0.74, 0.90) if s > 0 else (-0.90, -0.74)
    body.box((x0, 0.74, 0.88), (x1, 0.84, 1.00), M["black"])

# Front: grille with slats and a red star, headlights (white + amber), chrome bumper.
yf = Y_FRONT + 0.002
body.box((-0.40, yf - 0.01, 0.48), (0.40, yf + 0.01, 0.72), M["grille"])
for z in (0.53, 0.60, 0.67):
    body.box((-0.38, yf + 0.01, z - 0.012), (0.38, yf + 0.025, z + 0.012), M["seal"])
star = []
for k in range(10):
    a = math.pi / 2 + k * math.pi / 5
    r = 0.07 if k % 2 == 0 else 0.03
    star.append((r * math.cos(a), yf + 0.03, 0.60 + r * math.sin(a)))
body.poly(star, M["red"], (0, 1, 0))
for s in (-1, 1):
    xa, xb = sorted((s * 0.42, s * 0.64))
    body.box((xa, yf - 0.01, 0.50), (xb, yf + 0.02, 0.71), M["head"])
    xa, xb = sorted((s * 0.64, s * 0.77))
    body.box((xa, yf - 0.01, 0.50), (xb, yf + 0.02, 0.71), M["amber"])


def bumper(y_face, sign):
    y0, y1 = sorted((y_face, y_face + sign * 0.09))
    body.box((-0.80, y0, 0.30), (0.80, y1, 0.41), M["chrome"])
    body.box((-0.78, y0, 0.25), (0.78, y1 - (0.02 if sign > 0 else -0.02), 0.30), M["black"])
    for s in (-1, 1):
        xa, xb = sorted((s * 0.70, s * 0.86))
        ya, yb = sorted((y_face - sign * 0.10, y_face + sign * 0.11))
        body.box((xa, ya, 0.24), (xb, yb, 0.43), M["black"])


bumper(Y_FRONT, 1)
bumper(Y_REAR, -1)

# Rear: tail lights (red / amber / white), CCCP plate, twin exhaust.
yr = Y_REAR - 0.002
for s in (-1, 1):
    for (a, bx, m) in ((0.44, 0.52, M["head"]), (0.52, 0.62, M["amber"]), (0.62, 0.78, M["red"])):
        xa, xb = sorted((s * a, s * bx))
        body.box((xa, yr - 0.02, 0.50), (xb, yr + 0.01, 0.70), m)
body.box((-0.24, yr - 0.02, 0.50), (0.24, yr + 0.01, 0.64), M["red"])

# The twin pipes have materials of their own (the same colours as Chrome and Black_Plastic): CarDamage splits them out as a part the SPUTNIK does not wear (it has one fat pipe, see its kit).
m_pipe_c = material("Pipe_Chrome", (215, 216, 220), metallic=0.6, roughness=0.3)
m_pipe_d = material("Pipe_Dark", (34, 34, 36))
for x in (0.30, 0.42):
    body.cylinder_y(x, 0.22, 0.045, -1.95, Y_REAR - 0.11, m_pipe_c, seg=8)      # (the pipes stand 0.11 out of the rear panel: half of what they did)
    body.cylinder_y(x, 0.22, 0.030, -1.95, Y_REAR - 0.115, m_pipe_d, seg=8)

# Roof rack: two cross bars on legs and two side rails.
for y in (0.16, -0.48):
    body.box((-0.74, y - 0.025, 1.47), (0.74, y + 0.025, 1.52), M["seal"])
    for s in (-1, 1):
        xa, xb = sorted((s * 0.60, s * 0.66))
        body.box((xa, y - 0.02, 1.40), (xb, y + 0.02, 1.48), M["seal"])
for s in (-1, 1):
    xa, xb = sorted((s * 0.58, s * 0.62))
    body.box((xa, -0.56, 1.52), (xb, 0.26, 1.555), M["seal"])

# A full-size spare tyre lies flat across both rack crossbars. The torus is
# authored in the horizontal plane (Blender Z-up), with its steel face upward.
spare_cx, spare_cy, spare_cz = 0.0, -0.16, 1.674
major_r, tube_r = 0.3875, 0.11875  # 25% larger diameter than the previous spare
major_segments, tube_segments = 20, 8
spare_rings = []
for i in range(major_segments):
    theta = 2 * math.pi * i / major_segments
    ring = []
    for j in range(tube_segments):
        phi = 2 * math.pi * j / tube_segments
        radial = major_r + tube_r * math.cos(phi)
        ring.append((spare_cx + radial * math.cos(theta),
                     spare_cy + radial * math.sin(theta),
                     spare_cz + tube_r * math.sin(phi)))
    spare_rings.append(ring)
for i in range(major_segments):
    ni = (i + 1) % major_segments
    for j in range(tube_segments):
        nj = (j + 1) % tube_segments
        body.poly([spare_rings[i][j], spare_rings[ni][j],
                   spare_rings[ni][nj], spare_rings[i][nj]], M["tyre"])

# The wheel of the spare: a dished pressed-steel disc that sits INSIDE the tyre (its rim touches the tyre's inner edge, its centre is recessed), with a hub and five bolts.
def disc(radius, z, n=12):
    return [(spare_cx + radius * math.cos(2 * math.pi * k / n), spare_cy + radius * math.sin(2 * math.pi * k / n), z) for k in range(n)]
rim_lip = disc(major_r - tube_r + 0.004, spare_cz + 0.012)      # at the tyre's inner edge
rim_step = disc(0.225, spare_cz - 0.022)
rim_core = disc(0.11, spare_cz - 0.036)
for ring_a, ring_b in ((rim_lip, rim_step), (rim_step, rim_core)):
    for k in range(12):
        j = (k + 1) % 12
        body.poly([ring_a[k], ring_a[j], ring_b[j], ring_b[k]], M["steel"], (0, 0, 1))
body.poly(rim_core, M["steel"], (0, 0, 1))
hub = disc(0.0875, spare_cz - 0.022, 8)
body.poly(hub, M["seal"], (0, 0, 1))
for k in range(5):
    a0 = 2 * math.pi * k / 5
    bx, by = spare_cx + 0.16 * math.cos(a0), spare_cy + 0.16 * math.sin(a0)
    body.poly([(bx + 0.022 * math.cos(a0 + t), by + 0.022 * math.sin(a0 + t), spare_cz - 0.0265) for t in (0, 1.5708, 3.1416, 4.7124)], M["seal"], (0, 0, 1))

# Two dark canvas straps run lengthwise (the way the car drives) over the tread of the spare, each fixed to both crossbars (the ones by the windscreen and by the rear window): from the front bar
# up the outer wall of the tyre, over the tread, following its curve, and down again to the rear bar. They keep to the tyre (x offset 0.31, clear of the wheel).
rr = tube_r + 0.006
STRAP_X = 0.31
phi_top = math.acos(max(-1.0, min(1.0, (STRAP_X - major_r) / rr)))          # where the strap's plane leaves the tyre's inner wall: its middle
for sx in (-STRAP_X, STRAP_X):
    for side, bar_y in ((1, 0.16), (-1, -0.48)):
        pts = [(bar_y, 1.525)]      # at the bar (its top)
        for deg in range(-70, int(math.degrees(phi_top)), 12):
            phi = math.radians(deg)
            rho = major_r + rr * math.cos(phi)
            if rho <= STRAP_X + 0.005:
                continue
            pts.append((spare_cy + side * math.sqrt(rho * rho - STRAP_X * STRAP_X), spare_cz + rr * math.sin(phi)))
        pts.append((spare_cy, spare_cz + rr * math.sin(phi_top)))
        for (y0, z0), (y1, z1) in zip(pts, pts[1:]):
            body.poly([(spare_cx + sx - 0.026, y0, z0), (spare_cx + sx + 0.026, y0, z0),
                       (spare_cx + sx + 0.026, y1, z1), (spare_cx + sx - 0.026, y1, z1)], M["black"], (0, 0, 1))
    # a small buckle on the strap at the top of the tread
    ztop = spare_cz + rr * math.sin(phi_top)
    body.box((spare_cx + sx - 0.032, spare_cy - 0.035, ztop), (spare_cx + sx + 0.032, spare_cy + 0.035, ztop + 0.014), M["steel"])

# Rear wing, in three heights (the fuels: PETROL low, JET FUEL middle, SPUTNIK high; DIESEL has none). CarDamage splits each variant out of the body by its materials
# (Wing_<Height>, Wing_Emblem_<Height>) as a loose part that a hard rear hit can tear off, and shows the one the fuel wants. All three stand in the same place.
WING_TOPS = (("Low", 1.10), ("Mid", 1.22), ("High", 1.34))
for variant, top in WING_TOPS:
    # the SPUTNIK's high wing is dark like its skirts, scoop and slats (Kit_Dark); the red star stays
    m_white = material("Wing_" + variant, (30, 30, 32) if variant == "High" else (226, 224, 216), roughness=0.7 if variant == "High" else 0.6)
    m_red = material("Wing_Emblem_" + variant, (196, 38, 26), roughness=0.5)
    k = (top - Z_BELT) / (1.22 - Z_BELT)                 # the rakes are those of the middle wing (top 1.22), scaled to the height
    rear_at = lambda z: (YR_TOP + 0.02) + (-2.13 - (YR_TOP + 0.02)) * (z - Z_BELT) / (1.22 - Z_BELT)
    front_at = lambda z: -1.45 + (-1.76 + 1.45) * (z - Z_BELT) / (1.22 - Z_BELT)
    for s in (-1, 1):
        x0, x1 = sorted((s * 0.68, s * 0.72))
        # Upright plates: the rear edge rakes backwards going up, its foot inside the boot's rear edge (YR_TOP); the front edge is raked too.
        plate_b = [(x0, YR_TOP + 0.02, Z_BELT), (x1, YR_TOP + 0.02, Z_BELT), (x1, -1.45, Z_BELT), (x0, -1.45, Z_BELT)]
        plate_t = [(x0, rear_at(top), top), (x1, rear_at(top), top), (x1, front_at(top), top), (x0, front_at(top), top)]
        body.prism(plate_b, plate_t, m_white)
        # A red star on the outer face, in the middle of the plate and the largest that fits inside its raked edges with a margin.
        xo = s * 0.72 + s * 0.004
        cz = (Z_BELT + top) / 2
        cy = (rear_at(cz) + front_at(cz)) / 2
        for R in (0.14 - 0.005 * n for n in range(0, 20)):
            star = [(cy + (R if k % 2 == 0 else 0.4 * R) * math.cos(math.pi / 2 + k * math.pi / 5),
                     cz + (R if k % 2 == 0 else 0.4 * R) * math.sin(math.pi / 2 + k * math.pi / 5)) for k in range(10)]
            if all(rear_at(z) + 0.02 <= y <= front_at(z) - 0.02 and Z_BELT + 0.02 <= z <= top - 0.02 for y, z in star):
                break
        else:
            raise SystemExit("the star does not fit the plate " + variant)
        print("wing", variant, "star radius", round(R, 3), "centre", round(cy, 3), round(cz, 3))
        body.poly([(xo, y, z) for y, z in star], m_red, (s, 0, 0))

    # The blade (0.04 thick) ends at the INNER faces of the plates, a little below their top; its rear tip and its front stay inside the raked edges of the plates.
    zb = top - 0.055
    rb, fb = rear_at(zb + 0.04) + 0.04, front_at(zb + 0.04) - 0.012
    blade_lo = [(-0.68, rb, zb), (0.68, rb, zb), (0.68, fb, zb), (-0.68, fb, zb)]
    blade_hi = [(x, y, z + 0.04) for x, y, z in blade_lo]
    body.prism(blade_lo, blade_hi, m_white)

# The DIESEL's rear: no wing, a low dark grey duck-tail lip on the edge of the boot lid, its rear edge a few cm past the boot's edge (a loose part like the wings: Wing_Duck).
m_duck = material("Wing_Duck", (62, 62, 66), roughness=0.7)
duck_bottom = [(-0.77, -1.78, Z_BELT), (0.77, -1.78, Z_BELT), (0.77, -2.05, Z_BELT - 0.04), (-0.77, -2.05, Z_BELT - 0.04)]
duck_top = [(-0.77, -1.80, Z_BELT + 0.022), (0.77, -1.80, Z_BELT + 0.022), (0.77, -2.05, Z_BELT + 0.055), (-0.77, -2.05, Z_BELT + 0.055)]
body.prism(duck_bottom, duck_top, m_duck)

# The SPUTNIK's kit (Kit_Dark, Kit_Lamp): brake-cooling slats behind the front arches and ahead of the rear ones, two brake ducts in the nose (the bumper is off),
# louvres in the bonnet, side skirts, four auxiliary lamps on a bar at the nose and an air scoop on the roof. CarDamage splits all of it out as one part that only the SPUTNIK shows.
m_dark = material("Kit_Dark", (28, 28, 30), roughness=0.7)
m_lamp = material("Kit_Lamp", (250, 244, 205), roughness=0.3)
for s in (-1, 1):
    xa, xb = sorted((s * HX, s * (HX + 0.012)))
    for y in (0.36, 0.46, 0.56):                       # behind the front arch
        body.box((xa, y - 0.022, 0.36), (xb, y + 0.022, 0.62), m_dark)
    for y in (-0.52, -0.42, -0.32):                    # ahead of the rear arch
        body.box((xa, y - 0.022, 0.36), (xb, y + 0.022, 0.62), m_dark)
    for y in (1.905, 1.955, -1.905, -1.955):           # two ahead of the front arch and two behind the rear one: clear of the flare's foot (1.86) and 8 cm from the end of the car
        body.box((xa, y - 0.018, 0.36), (xb, y + 0.018, 0.62), m_dark)
    # side skirt between the arches
    xs, xt = sorted((s * (HX + 0.003), s * (HX + 0.07)))
    body.box((xs, -0.74, 0.15), (xt, 0.79, 0.30), m_dark)
    # brake duct in the nose, where the bumper was
    xd0, xd1 = sorted((s * 0.42, s * 0.72))
    body.box((xd0, Y_FRONT - 0.015, 0.24), (xd1, Y_FRONT + 0.012, 0.38), m_dark)
    # bonnet louvres
    xl0, xl1 = sorted((s * 0.16, s * 0.52))
    for y in (1.05, 1.20, 1.35, 1.50):
        body.box((xl0, y - 0.03, Z_BELT), (xl1, y + 0.03, Z_BELT + 0.012), m_dark)
body.box((-0.56, 1.86, Z_BELT), (0.56, 1.98, Z_BELT + 0.035), m_dark)       # the lamp bar
for x in (-0.43, -0.15, 0.15, 0.43):
    body.cylinder_y(x, Z_BELT + 0.11, 0.075, 1.84, 1.985, m_dark, seg=8)
    body.cylinder_y(x, Z_BELT + 0.11, 0.062, 1.985, 1.995, m_lamp, seg=8)
scoop_bottom = [(-0.22, 0.10, 1.40), (0.22, 0.10, 1.40), (0.22, -0.34, 1.40), (-0.22, -0.34, 1.40)]
scoop_top = [(-0.20, 0.10, 1.53), (0.20, 0.10, 1.53), (0.20, -0.34, 1.42), (-0.20, -0.34, 1.42)]
body.prism(scoop_bottom, scoop_top, m_dark)

# The SPUTNIK is a five: the odd number shows from behind. One fat oval-ish pipe off the middle (the twin pipes of the others stay home), a thin wastegate pipe beside it that gives a puff when
# the throttle is lifted, five heat-extraction slits on the other side and a small "5" plate in the middle of the rear panel (as read by a viewer behind the car, whose right is the car's +x).
m_kit_chrome = material("Kit_Chrome", (215, 216, 220), metallic=0.6, roughness=0.3)
m_kit_red = material("Kit_Red", (196, 38, 26), roughness=0.5)
body.cylinder_y(0.36, 0.22, 0.1125, -1.95, Y_REAR - 0.13, m_kit_chrome, seg=8)      # (the fat pipe: 1.5 times as wide and standing 0.13 out, half as far)
body.cylinder_y(0.36, 0.22, 0.084, Y_REAR - 0.125, Y_REAR - 0.136, m_dark, seg=8)
body.cylinder_y(0.54, 0.31, 0.022, -2.0, Y_REAR - 0.08, m_kit_chrome, seg=6)
body.cylinder_y(0.54, 0.31, 0.014, Y_REAR - 0.075, Y_REAR - 0.086, m_dark, seg=6)
for x in (-0.60, -0.50, -0.40, -0.30, -0.20):
    body.box((x - 0.016, -2.07, 0.26), (x + 0.016, -2.035, 0.42), m_dark)
body.box((-0.07, -2.07, 0.26), (0.07, -2.035, 0.42), m_dark)
for (x0, x1, z0, z1) in ((-0.035, 0.035, 0.384, 0.40), (-0.035, -0.019, 0.34, 0.40), (-0.035, 0.035, 0.332, 0.348), (0.019, 0.035, 0.28, 0.34), (-0.035, 0.035, 0.28, 0.296)):
    body.box((x0, -2.0735, z0), (x1, -2.066, z1), m_kit_red)

body_ob = body.build()

# Keep the red rear plate plain: lettering is not visible at gameplay distance.

# Merge coincident vertices per material region is not needed for flat shading; keep faces hard.

# ------------------------------------------------------------------ wheel (one mesh, reused ×4 in game)

wheel = Builder("Wheel")
W = 0.22
wheel.cylinder_x(0, 0, 0, WHEEL_R, -W / 2, W / 2, M["tyre"], seg=12, cap_m=M["tyre"], cap_end=False)      # the outer sidewall is a ring (below), open to the wheel in the middle
# The outer (+X) face of the steel wheel in relief, after the owner's drawing: a flat lip round the edge, a faceted well sloping down from it with a ventilation hole in every
# facet, a flat floor, then a stepped hub boss that rises again, with four bolt recesses and a dark centre hole. Flat shading lights every facet differently.
RIM_N = 12      # the tyre's 12 sides: the sidewall ring joins the lip one to one
RIM_ROT = 0.0


def ring(r, x, rot=RIM_ROT):
    return [(x, r * math.cos(rot + 2 * math.pi * k / RIM_N), r * math.sin(rot + 2 * math.pi * k / RIM_N)) for k in range(RIM_N)]


xo = W / 2
tyre_edge = ring(WHEEL_R, xo)
lip_out, lip_in = ring(0.245, xo), ring(0.215, xo + 0.008)
well_top, well_floor = ring(0.195, xo - 0.012), ring(0.155, xo - 0.07)
floor_in, boss_foot = ring(0.135, xo - 0.07), ring(0.12, xo - 0.03)
boss_top = ring(0.10, xo - 0.012)
bolt_ring_r, hub_hole = 0.066, ring(0.034, xo - 0.012)


def band(ra, rb, m=None):
    for k in range(RIM_N):
        j = (k + 1) % RIM_N
        wheel.poly([ra[k], ra[j], rb[j], rb[k]], m or M["steel"], (1, 0, 0))


band(tyre_edge, lip_out, M["tyre"])       # the tyre's outer sidewall (dark rubber, not steel)
band(lip_out, lip_in)          # the flat lip
band(lip_in, well_top)         # the step in
band(well_top, well_floor)     # the faceted well (the ventilation holes go on these)
band(well_floor, floor_in)     # the floor
band(floor_in, boss_foot)      # the boss rises
band(boss_foot, boss_top)      # its chamfer
# Ventilation holes: a dark slot set into every facet of the well.
for k in range(RIM_N):
    j = (k + 1) % RIM_N
    quad = [well_top[k], well_top[j], well_floor[j], well_floor[k]]
    c = Vector((sum(q[0] for q in quad), sum(q[1] for q in quad), sum(q[2] for q in quad))) / 4
    slot = [tuple(c + (Vector(q) - c) * 0.58 + Vector((0.003, 0, 0))) for q in quad]
    wheel.poly(slot, M["black"], (1, 0, 0))
# Boss top: flat, with four bolt recesses and the dark centre hole.
boss_face = [(xo - 0.012, boss_top[k][1], boss_top[k][2]) for k in range(RIM_N)]
wheel.poly(boss_face, M["steel"], (1, 0, 0))
for k in range(4):
    a0 = math.pi / 4 + k * math.pi / 2
    by, bz = bolt_ring_r * math.cos(a0), bolt_ring_r * math.sin(a0)
    wheel.poly([(xo - 0.009, by + 0.014 * math.cos(t), bz + 0.014 * math.sin(t)) for t in [2 * math.pi * m / 6 for m in range(6)]], M["black"], (1, 0, 0))
wheel.poly([(xo - 0.009, p[1], p[2]) for p in hub_hole], M["seal"], (1, 0, 0))
# Inner steel face, ventilation holes and hub: visible under jumps/free-camera views.
rim_inner = [(-W / 2 - 0.004, 0.23 * math.cos(2 * math.pi * j / 12),
              0.23 * math.sin(2 * math.pi * j / 12)) for j in range(12)]
wheel.poly(rim_inner, M["steel"], (-1, 0, 0))
for k in range(6):
    a = 2 * math.pi * k / 6 + 0.3
    cy, cz = 0.15 * math.cos(a), 0.15 * math.sin(a)
    hole = [(-W / 2 - 0.008, cy + 0.032 * math.cos(2 * math.pi * j / 6),
             cz + 0.032 * math.sin(2 * math.pi * j / 6)) for j in range(6)]
    wheel.poly(hole, M["black"], (-1, 0, 0))
inner_hub = [(-W / 2 - 0.012, 0.075 * math.cos(2 * math.pi * j / 8),
              0.075 * math.sin(2 * math.pi * j / 8)) for j in range(8)]
wheel.poly(inner_hub, M["seal"], (-1, 0, 0))
wheel_ob = wheel.build(shift_z=0, scale=(1, 1, 1))      # wheel origin = axle centre

# Preview-only wheel instances (not exported) so renders show the full car.
previews = []
for (y, s) in ((FRONT_Y, 1), (FRONT_Y, -1), (REAR_Y, 1), (REAR_Y, -1)):
    inst = bpy.data.objects.new(f"PreviewWheel_{'F' if y > 0 else 'R'}{'R' if s > 0 else 'L'}", wheel_ob.data)
    inst.location = (s * HALF_TRACK * SX, y * SY, WHEEL_Z * SZ - CG_HEIGHT)
    inst.rotation_euler = (0, 0, 0 if s > 0 else math.pi)
    scene.collection.objects.link(inst)
    previews.append(inst)

# ------------------------------------------------------------------ save + export

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "pyatyorka2105.blend"))

bpy.ops.object.select_all(action="DESELECT")
body_ob.select_set(True)
wheel_ob.select_set(True)
bpy.ops.export_scene.gltf(
    filepath=os.path.join(OUT, "pyatyorka2105.glb"),
    export_format="GLB",
    use_selection=True,
    export_apply=True,
    export_yup=True,
)
print("Exported", os.path.join(OUT, "pyatyorka2105.glb"))
print(f"CarParams: WheelRadius={WHEEL_R} FrontZ={-FRONT_Y * SY:.3f} RearZ={-REAR_Y * SY:.3f} HalfTrack={HALF_TRACK * SX:.3f} "
      f"wheel centre y at rest={WHEEL_Z * SZ - CG_HEIGHT:.3f}")

# ------------------------------------------------------------------ previews

if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    wheel_ob.hide_render = True      # the axle-centred master wheel sits inside the car
    try:
        scene.render.engine = "BLENDER_WORKBENCH"
    except TypeError as e:
        print("engine:", e)
    shading = scene.display.shading
    shading.light = "STUDIO"
    shading.color_type = "MATERIAL"
    shading.show_object_outline = True
    scene.render.resolution_x, scene.render.resolution_y = 900, 560
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("W")
    scene.world = world

    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def shoot(name, loc, target=(0, 0, 0.05)):
        cam.location = loc
        d = Vector(target) - Vector(loc)
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(PREVIEW, f"pyatyorka_{name}.png")
        bpy.ops.render.render(write_still=True)

    shoot("rear34", (4.5, -6.0, 3.0))
    shoot("front34", (-4.5, 6.0, 2.2))
    shoot("side", (-9.0, 0.0, 0.8))
    shoot("low_left", (-8.0, 0.0, -0.20), target=(0, 0, -0.15))
    shoot("low_right", (8.0, 0.0, -0.20), target=(0, 0, -0.15))
    shoot("rear_under", (4.0, -7.0, -0.30), target=(0, -1.2, -0.25))
    shoot("wing_close", (-2.9, -4.3, 1.8), target=(0, -1.8, 0.9))
    # Game camera: 53° down, fixed; car seen at a random heading.
    shoot("game", (0.0, -8.0, 10.5))
    print("Previews in", PREVIEW)
