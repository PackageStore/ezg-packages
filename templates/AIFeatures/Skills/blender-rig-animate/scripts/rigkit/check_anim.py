"""check_anim: frame-by-frame gate on clips (or on static poses from a poses file).

  blender -b anim.blend --python rk.py -- check_anim --out anim_check.json [--actions A,B] [--png-dir dir]
  blender -b skinned.blend --python rk.py -- check_anim --poses poses.py --names READY,WINDUP --out p.json --png p.png

Errors:
  LIMIT              a joint leaves its anatomical range (bone, axis, value, frames)
  GROUND             the skin goes below the ground (> 1 cm)
  FOOT_SLIDE         a foot moves while it should be planted (declared contact or detected contact)
  PROP_PENETRATION   a rigid prop / accessory goes > 1.5 cm into the body (hand holding it excluded)
  LIMB_PENETRATION   an arm or leg goes > 2 cm into the torso or the other leg
  TEAR / FLIP        the skin tears or turns inside out on this frame (same metrics as check_weights)
  LOOP_SEAM          a loop does not end on its first pose
  POP                a bone jumps (> 20 deg in one frame and > 3x faster than the frames around it)
  ROOT_MOTION        Root moves (clips are in place: QuyChuan 7.2)
  SCALE_CHILDREN     non-uniform scale on a bone that has children (QuyChuan 5.2 rule 6)
  DROP_FAR           a dropped prop comes to rest > 2 heights from the body (it leaves the character's cell)
  HUB_START/HUB_END  an action clip starts / ends far from the hub pose (QuyChuan A-22: frame 0 of the idle of the
                     clip's set, i.e. the *_Idle clip whose name prefix the clip shares, or --hub): bones off by > 20 deg
                     on average or one bone > 90 deg; the transition jumps. Hit clips may start off the hub (hub_start)
Warnings: FOOT_FLOAT (declared contact but the sole is > 1.5 cm above ground), LOOP_VELOCITY,
  DROP_FAR (> 0.6 height from the body: readable, but check it against the spacing of units in the game),
  HUB_START/HUB_END above 10 deg on average or 45 deg on one bone,
  LOW_AMPLITUDE (upper-body bones turn < 8 deg on average in the idle, < 20 deg in an action clip: it reads stiff).
The hub and amplitude numbers are calibrated on clips the team judged (see HUB_WARN): the ExplosiveLLC pack passes
without a warning, a set of code-generated chibi clips the team rejected does not.
Tolerances are fractions of the character height (see GROUND_TOL ...), the cm above are for a 1.46 m character.
"""
import argparse
import json
import math
import os
import re

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

from . import common as C
from . import check_weights as CW
from . import pose as P
from . import skeleton as SK

# tolerances as a fraction of the character height (1.46 m chibi -> ~1 cm ground, ~1.5 cm prop)
GROUND_TOL = 0.007
PROP_TOL = 0.01
LIMB_TOL = 0.014
SLIDE_TOL = 0.008
DROP_WARN, DROP_ERR = 0.6, 2.0   # distance from a dropped prop to the body at the end of the clip, x height
# First / last pose of an action clip vs the hub pose, (mean over bones, worst bone) in degrees. Measured with the
# same metric by tests/measure_clips.py: ExplosiveLLC attacks meet their idle within 2.5 / 15 (Unarmed) and 9.2 / 42.5
# (2Hand-Sword), hits 0-1 / 0-6;
# a code-generated chibi attack the team rejected as stiff and jumpy was off by 35 / 134.
HUB_WARN, HUB_ERR = (10.0, 45.0), (20.0, 90.0)
# Mean rotation range of the upper-body bones, degrees: ExplosiveLLC idles 15-16, attacks 42-86, hits 41-64; the
# rejected code-generated chibi idles ~5 (5.6 on the chibi in reference/failure-catalog.md).
AMP_IDLE, AMP_ACTION = 8.0, 20.0
LEG_WORDS = ("UpperLeg", "LowerLeg", "UpLeg", "Foot", "Toe")


def is_leg(name):
    """Leg bones in QuyChuan and Mixamo names (legs are IK: their range says little about the acting)."""
    s = name.split(":")[-1]
    return any(w in s for w in LEG_WORDS) or s.endswith("Leg")


def _ang(q0, q1):
    return math.degrees(2.0 * math.acos(min(1.0, abs(q0.dot(q1)))))


def local_rotations(sk, rot_hist):
    """Armature-space rotations per frame -> parent-relative rotations (Root and props left out)."""
    out = []
    for fr in rot_hist:
        d = {}
        for n in sk.order:
            if n == "Root" or n.startswith("Prop_"):
                continue
            p = sk.parent[n]
            d[n] = (fr[p].inverted() @ fr[n]) if p else fr[n]
        out.append(d)
    return out


def rotation_ranges(frames):
    """Largest angle between any two frames, per bone (every 2nd frame)."""
    rng = {}
    for n in frames[0]:
        qs = [fr[n] for fr in frames[::2]]
        rng[n] = max(_ang(a, b) for a in qs for b in qs)
    return rng


HUB_RE = re.compile(r"_Idle(_\d+)?$")


def is_hub(name):
    """The base idle of a set, <Subject>[_<Bo>]_Idle[_01] (QuyChuan 4.2, 4.3). Idle breaks (_Idle_Break_01) and
    _Idle_Static are not hubs: they start and end on one."""
    return bool(HUB_RE.search(name))


def hub_of(name, idles):
    """The idle a clip starts from and returns to: the one with the longest name prefix in common
    (Hero_Sword2H_Atk_01 -> Hero_Sword2H_Idle rather than Hero_Idle)."""
    best, best_len = None, -1
    for h in idles:
        pre = h[:HUB_RE.search(h).start() + 1] if HUB_RE.search(h) else ""
        if name.startswith(pre) and len(pre) > best_len:
            best, best_len = h, len(pre)
    return best


def hub_checks(sc, rep, hub=None):
    """Action clips start and end on the hub pose; the idle and the actions move enough to read."""
    mo = sc.motion
    idles = [h for h in ([hub] if hub else [n for n in mo if is_hub(n)]) if h in mo]
    if not idles:
        if any(not m["loop"] for m in mo.values()):
            rep.warn("HUB_MISSING", "no idle clip (<Subject>[_<Set>]_Idle) to compare the action clips with: pass --hub")
        return
    for name, m in mo.items():
        up = [v for n, v in m["range"].items() if not is_leg(n)]
        mean, top = (sum(up) / len(up), max(up)) if up else (0.0, 0.0)
        rep.info.setdefault("motion", {})[name] = {"upper_range_mean_deg": C.r(mean, 1), "upper_range_max_deg": C.r(top, 1)}
        if name in idles:
            if mean < AMP_IDLE:
                rep.warn("LOW_AMPLITUDE", "%s: upper-body bones turn %.1f deg on average (max %.0f); idles the team "
                         "liked: 15-16" % (name, mean, top), clip=name)
            continue
        if m["loop"]:
            continue  # locomotion loops blend in at another phase of the cycle
        if mean < AMP_ACTION:
            rep.warn("LOW_AMPLITUDE", "%s: upper-body bones turn %.1f deg on average (max %.0f); actions the team "
                     "liked: 42-86" % (name, mean, top), clip=name)
        own = hub if hub else hub_of(name, idles)
        if own is None:
            continue
        rep.info["motion"][name]["hub"] = own
        ref = mo[own]["first"]
        ends = (([("HUB_START", "first", m["first"])] if m["hub_start"] else [])
                + ([("HUB_END", "last", m["last"])] if m["hub_end"] else []))
        for code, which, pose in ends:
            ds = [(_ang(pose[n], ref[n]), n) for n in ref if n in pose]
            if not ds:
                continue
            avg = sum(d for d, _ in ds) / len(ds)
            worst, bone = max(ds)
            key = "hub_%s" % ("start" if which == "first" else "end")
            rep.info["motion"][name][key] = {"mean_deg": C.r(avg, 1), "max_deg": C.r(worst, 1), "bone": bone}
            msg = ("%s: %s pose is %.1f deg away from the hub (%s frame 0) on average, %s %.0f deg"
                   % (name, which, avg, own, bone, worst))
            if avg > HUB_ERR[0] or worst > HUB_ERR[1]:
                rep.error(code, msg, clip=name, bone=bone)
            elif avg > HUB_WARN[0] or worst > HUB_WARN[1]:
                rep.warn(code, msg, clip=name, bone=bone)


class Scene:
    """Everything the per-frame checks need, computed once."""

    def __init__(self, arm):
        self.arm = arm
        self.meshes = CW.armature_meshes(arm)
        self.soft, self.rigid = [], []
        for ob in self.meshes:
            W, names = C.read_weights(ob)
            if CW.is_rigid(ob, W):
                self.rigid.append((ob, names[int(np.argmax(W[0]))]))
            else:
                self.soft.append((ob, W, names))
        self.topo = {ob.name: CW.MeshTopo(ob, arm) for ob, _, _ in self.soft}
        # regions of the skin: which triangles belong to which limb / torso (for penetration tests)
        self.regions = {}
        for ob, W, names in self.soft:
            from .skin import limb_of
            tris = C.triangles_array(ob.data)
            groups = {}
            for j, n in enumerate(names):
                if n not in arm.data.bones or not arm.data.bones[n].use_deform:
                    continue
                lb = limb_of(n)
                key = "%s_%s" % lb if lb else "torso"
                groups.setdefault(key, []).append(j)
            keys = list(groups)
            S = np.stack([W[:, groups[k]].sum(1) for k in keys], 1)
            dom = np.argmax(S, 1)
            strong = S.max(1) > 0.85  # ignore the blend bands: they are meant to touch
            tri_reg = {}
            for i, k in enumerate(keys):
                sel = np.all((dom[tris] == i) & strong[tris], axis=1)
                tri_reg[k] = tris[sel]
            self.regions[ob.name] = tri_reg
        rest_min = min((C.world_coords(ob)[:, 2].min() for ob, _, _ in self.soft), default=0.0)
        self.ground = min(rest_min, 0.0)
        self.height = max((float(np.ptp(C.world_coords(ob)[:, 2])) for ob, _, _ in self.soft), default=1.0)
        self.tol = {k: v * self.height for k, v in (("ground", GROUND_TOL), ("prop", PROP_TOL), ("limb", LIMB_TOL),
                                                     ("slide", SLIDE_TOL))}
        # what already touches in the rest pose (thighs under a tunic, a helmet on the head) is by design
        pos = arm.data.pose_position
        arm.data.pose_position = "REST"
        bpy.context.view_layer.update()
        self.baseline = penetrations(self, self.evaluated())
        arm.data.pose_position = pos
        bpy.context.view_layer.update()

        self.motion = {}  # per clip: first / last parent-relative rotations and ranges (hub_checks)

    def evaluated(self):
        return {ob.name: C.world_coords(ob, evaluated=True) for ob in self.meshes}


def bvh(co, tris):
    if len(tris) == 0:
        return None
    return BVHTree.FromPolygons([tuple(p) for p in co], [tuple(t) for t in tris])


def tri_areas(co, tris):
    return 0.5 * np.linalg.norm(np.cross(co[tris[:, 1]] - co[tris[:, 0]], co[tris[:, 2]] - co[tris[:, 0]]), axis=1)


def overlap_depth(m_co, m_tris, t_bvh, max_d):
    """How far a moving part goes into a target surface.
    Only the vertices of the triangles that really cross the target are measured (the nearest-face test is
    meaningless far from the crossing, e.g. next to the openings of a partial region). Returns
    (depth in metres, fraction of the moving part's area that crosses)."""
    m_bvh = bvh(m_co, m_tris)
    if m_bvh is None or t_bvh is None:
        return 0.0, 0.0
    pairs = m_bvh.overlap(t_bvh)
    if not pairs:
        return 0.0, 0.0
    tri_ids = np.unique(np.array([pq[0] for pq in pairs], dtype=np.int64))
    verts = np.unique(m_tris[tri_ids].ravel())
    worst, where = 0.0, None
    for v in verts:
        loc, nor, idx, d = t_bvh.find_nearest(Vector(m_co[v]), max_d)
        if loc is not None and (Vector(m_co[v]) - loc).dot(nor) < 0 and d > worst:
            worst, where = d, m_co[v]
    A = tri_areas(m_co, m_tris)
    frac = float(A[tri_ids].sum() / max(A.sum(), 1e-12))
    if where is None:
        where = m_co[verts[0]]
    OVERLAP_AT[0] = C.r(where, 3)
    return worst, frac


OVERLAP_AT = [None]


def penetration_depth(points, target_bvh, max_d):
    """Kept for callers outside this module: nearest-face depth of loose points (use overlap_depth instead)."""
    worst = 0.0
    for p in points:
        loc, nor, idx, d = target_bvh.find_nearest(Vector(p), max_d)
        if loc is not None and (Vector(p) - loc).dot(nor) < 0:
            worst = max(worst, d)
    return worst


def penetrations(sc, ev):
    """{('prop', name) | ('limb', region): (depth m, crossing area fraction)} for one evaluated frame."""
    arm = sc.arm
    from .skin import limb_of
    res = {}
    md = 0.08 * sc.height
    for ob, _, _ in sc.soft:
        co = ev[ob.name]
        reg = sc.regions[ob.name]
        torso = bvh(co, reg.get("torso", []))
        for pob, pbone in sc.rigid:
            holder = arm.data.bones[pbone].parent.name if arm.data.bones[pbone].parent else None
            excl = set()
            if holder:
                lb = limb_of(holder)
                if lb:
                    excl.add("%s_%s" % lb)  # the hand (and forearm) holding the prop may touch it
            tris = [reg[k] for k in reg if k not in excl and len(reg[k])]
            if not tris:
                continue
            target = bvh(co, np.concatenate(tris))
            res[("prop", pob.name)] = overlap_depth(ev[pob.name], C.triangles_array(pob.data), target, md) + \
                (OVERLAP_AT[0],)
        for k, tri in reg.items():
            if k == "torso" or torso is None or not len(tri):
                continue
            res[("limb", k)] = overlap_depth(co, tri, torso, md) + (OVERLAP_AT[0],)
    # props against each other (axe through shield, shield through helmet)
    for i in range(len(sc.rigid)):
        for j in range(i + 1, len(sc.rigid)):
            a_ob, b_ob = sc.rigid[i][0], sc.rigid[j][0]
            B = bvh(ev[b_ob.name], C.triangles_array(b_ob.data))
            A = bvh(ev[a_ob.name], C.triangles_array(a_ob.data))
            d1, f1 = overlap_depth(ev[a_ob.name], C.triangles_array(a_ob.data), B, md)
            w1 = OVERLAP_AT[0]
            d2, f2 = overlap_depth(ev[b_ob.name], C.triangles_array(b_ob.data), A, md)
            res[("prop", "%s/%s" % (a_ob.name, b_ob.name))] = (max(d1, d2), max(f1, f2), w1 if d1 >= d2 else OVERLAP_AT[0])
    return res


def frame_checks(sc, f, ev, sk, out):
    # limits
    for bone, axis, val, lim in P.limit_violations(sk, tol=2.0):
        out["limits"].setdefault((bone, axis), []).append((f, val, lim))
    # ground
    for ob, _, _ in sc.soft:
        i = int(np.argmin(ev[ob.name][:, 2]))
        z = ev[ob.name][i, 2]
        if z < sc.ground - sc.tol["ground"]:
            out["ground"].append((f, ob.name, round(float(sc.ground - z), 3), C.r(ev[ob.name][i], 3)))
    # skin deformation
    for ob, _, _ in sc.soft:
        spike, collapse, flip = CW.deform_metrics(sc.topo[ob.name], ev[ob.name])
        big = sc.topo[ob.name].big.sum()
        if (spike > CW.SPIKE_ERR).mean() > CW.SPIKE_FRAC:
            out["tear"].append((f, ob.name, round(float(spike.max()), 2)))
        if flip.sum() / max(big, 1) > CW.FLIP_MAX:
            fl = np.nonzero(flip)[0]
            where = C.r(sc.topo[ob.name].rest[sc.topo[ob.name].tris[fl]].mean(1).mean(0), 3)
            out["flip"].append((f, ob.name, round(float(flip.sum() / max(big, 1)), 4), where))
    # penetration beyond what the rest pose already has
    for (kind, what), (depth, frac, where) in penetrations(sc, ev).items():
        d0, f0 = sc.baseline.get((kind, what), (0.0, 0.0, None))[:2]
        extra = depth - d0
        if extra > sc.tol[kind] or (frac - f0) > 0.04:
            out[kind].append((f, what, round(extra, 3), where))


def feet_points(sc):
    """Per foot bone: vertex ids of the skin that mostly follow it (for contact and slide)."""
    out = {}
    for ob, W, names in sc.soft:
        for j, n in enumerate(names):
            if n.split(":")[-1].startswith(("Foot", "LeftFoot", "RightFoot")) or n.endswith("Foot"):
                ids = np.nonzero(W[:, j] > 0.6)[0]
                if len(ids):
                    out[n] = (ob.name, ids)
    return out


def check_action(sc, act, rep, png_dir=None, frames_png=None):
    arm = sc.arm
    C.assign_action(arm, act)
    meta = json.loads(act.get("rk_clip", "{}")) if act.get("rk_clip") else {}
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    loop = bool(meta.get("loop", act.use_cyclic))
    fps = int(meta.get("fps", bpy.context.scene.render.fps))
    gs = float(meta.get("ground_speed", 0.0))
    contacts = meta.get("contacts", {})
    sk = SK.Skeleton(arm)
    out = {k: [] for k in ("ground", "tear", "flip", "prop", "limb")}
    out["limits"] = {}
    feet = feet_points(sc)
    foot_track = {n: [] for n in feet}
    rot_hist = []
    pos_hist = []
    root_moves = False
    scale_bad = set()
    scn = bpy.context.scene
    rom = meta.get("kind") == "rom"
    ev_last = None
    for f in range(f0, f1 + 1):
        scn.frame_set(f)
        sk.read(arm)
        ev = sc.evaluated()
        frame_checks(sc, f, ev, sk, out)
        if rom:  # a ROM tests the skin: props, ground and feet are not part of it
            out["ground"] = []
            out["prop"] = []
        for n, (obn, ids) in feet.items():
            pts = ev[obn][ids]
            foot_track[n].append((f, pts.mean(0), pts[:, 2].min()))
        if f == f1:
            ev_last = ev
        rot_hist.append({n: arm.pose.bones[n].matrix.to_quaternion() for n in sk.order})
        pos_hist.append({n: arm.pose.bones[n].tail.copy() for n in sk.order})
        rb = arm.pose.bones.get("Root")
        if rb is not None and (rb.location.length > 1e-5 or rb.rotation_quaternion.angle > 1e-4):
            root_moves = True
        for pb in arm.pose.bones:
            s = pb.scale
            if pb.bone.children and (abs(s.x - s.y) > 1e-3 or abs(s.y - s.z) > 1e-3):
                scale_bad.add(pb.name)
    name = act.name
    # ---- report
    for (bone, axis), hits in out["limits"].items():
        worst = max(hits, key=lambda h: abs(h[1]))
        rep.error("LIMIT", "%s: %s %s = %.0f deg (limit %s..%s) on %d frames, worst at f%d"
                  % (name, bone, axis, worst[1], worst[2][0], worst[2][1], len(hits), worst[0]),
                  clip=name, bone=bone, frames=[h[0] for h in hits])
    for key, code, unit in (("ground", "GROUND", "m below ground"), ("prop", "PROP_PENETRATION", "m inside the body"),
                            ("limb", "LIMB_PENETRATION", "m inside the torso"), ("tear", "TEAR", "spike"),
                            ("flip", "FLIP", "flipped fraction")):
        if out[key]:
            by = {}
            for h in out[key]:
                by.setdefault(h[1], []).append(h)
            for what, hits in by.items():
                worst = max(hits, key=lambda h: h[2])
                at = (" at %s" % (worst[3],)) if len(worst) > 3 and worst[3] is not None else ""
                rep.error(code, "%s: %s %.3f %s on %d frames (worst f%d%s)" % (name, what, worst[2], unit, len(hits),
                                                                               worst[0], at),
                          clip=name, frames=[h[0] for h in hits])
    # feet: declared contacts (or detected ones) must not slide
    fwd = Vector((0, -1, 0))
    for n, tr in ({} if rom else foot_track).items():
        spans = contacts.get(n)
        if spans is None:
            # detect: sole within 1 cm of the ground, at least 3 frames in a row
            on = [z <= sc.ground + sc.tol["ground"] for _, _, z in tr]
            spans, s0 = [], None
            for i, v in enumerate(on + [False]):
                if v and s0 is None:
                    s0 = i
                if not v and s0 is not None:
                    if i - s0 >= 3:
                        spans.append((tr[s0][0], tr[i - 1][0]))
                    s0 = None
        for a, b in spans:
            seg = [t for t in tr if a <= t[0] <= b]
            if len(seg) < 2:
                continue
            p0 = seg[0][1]
            worst = 0.0
            for fr, p, z in seg:
                expect = p0 - fwd * gs * (fr - seg[0][0]) / fps  # planted foot slides back at the ground speed
                dev = (Vector(p) - Vector(expect)).xy.length
                worst = max(worst, dev)
                if z > sc.ground + 1.5 * sc.tol["ground"] and contacts.get(n):
                    rep.warn("FOOT_FLOAT", "%s: %s should touch the ground at f%d but is %.3f m up" % (name, n, fr, z),
                             clip=name)
                    break
            if worst > sc.tol["slide"]:
                rep.error("FOOT_SLIDE", "%s: %s slides %.3f m during contact f%d-f%d" % (name, n, worst, a, b),
                          clip=name, foot=n)
    # pops
    speeds = {n: [] for n in sk.order}
    for i in range(1, len(rot_hist)):
        for n in sk.order:
            q0, q1 = rot_hist[i - 1][n], rot_hist[i][n]
            d = min(1.0, abs(q0.dot(q1)))
            speeds[n].append(math.degrees(2.0 * math.acos(d)))  # q and -q are the same rotation
    for n, sp in speeds.items():
        if len(sp) < 3:
            continue
        for i in range(len(sp)):
            prev = sp[i - 1] if i > 0 else (sp[-1] if loop else sp[i])
            nxt = sp[i + 1] if i + 1 < len(sp) else (sp[0] if loop else sp[i])
            # a pop is a velocity spike: much faster than the frames around it (a fast swing is not a pop)
            if sp[i] > 20.0 and sp[i] > 3.0 * max(prev, 1.0) and sp[i] > 3.0 * max(nxt, 1.0):
                rep.error("POP", "%s: %s jumps %.0f deg between f%d and f%d" % (name, n, sp[i], f0 + i, f0 + i + 1),
                          clip=name, bone=n)
                break
    # loop seam
    if loop and len(pos_hist) > 2:
        d = max((pos_hist[0][n] - pos_hist[-1][n]).length for n in sk.order)
        if d > 0.002:
            rep.error("LOOP_SEAM", "%s: last frame is %.3f m away from the first" % (name, d), clip=name)
        v0 = {n: pos_hist[1][n] - pos_hist[0][n] for n in sk.order}
        v1 = {n: pos_hist[-1][n] - pos_hist[-2][n] for n in sk.order}
        dv = max((v0[n] - v1[n]).length for n in sk.order)
        typ = max(np.median([v0[n].length for n in sk.order]), 1e-4)
        if dv > max(4 * typ, 0.01):
            rep.warn("LOOP_VELOCITY", "%s: speed changes at the loop seam (%.3f m/frame)" % (name, dv), clip=name)
    # props that fall off must land near the body, not across the board
    drops = meta.get("drops", []) if sc.soft and ev_last is not None else []
    if drops:
        body = np.concatenate([ev_last[ob.name] for ob, _, _ in sc.soft])
        kd = KDTree(len(body))
        for i, v in enumerate(body):
            kd.insert(v, i)
        kd.balance()
    for d in drops:
        pts = [ev_last[pob.name] for pob, pbone in sc.rigid if pbone == d["bone"]]
        if not pts:
            continue
        p = np.concatenate(pts)
        dist = min(kd.find(v)[2] for v in p[:: max(1, len(p) // 400)])
        where = C.r(p.mean(0).tolist(), 2)
        if dist > DROP_ERR * sc.height:
            rep.error("DROP_FAR", "%s: %s lands %.2f m from the body (at %s)" % (name, d["bone"], dist, where),
                      clip=name, bone=d["bone"])
        elif dist > DROP_WARN * sc.height:
            rep.warn("DROP_FAR", "%s: %s lands %.2f m from the body (at %s)" % (name, d["bone"], dist, where),
                     clip=name, bone=d["bone"])
        rep.info.setdefault("drops", {})["%s/%s" % (name, d["bone"])] = {"distance": C.r(dist, 3), "at": where}
    if root_moves:
        rep.error("ROOT_MOTION", "%s: Root moves; clips are in place, code moves the character" % name, clip=name)
    for n in sorted(scale_bad):
        rep.error("SCALE_CHILDREN", "%s: %s has non-uniform scale and children (they get sheared)" % (name, n), clip=name)
    rep.info.setdefault("clips", {})[name] = {"frames": [f0, f1], "loop": loop, "ground_speed": gs,
                                             "events": meta.get("events", [])}
    if not rom and rot_hist:
        loc = local_rotations(sk, rot_hist)
        sc.motion[name] = {"first": loc[0], "last": loc[-1], "range": rotation_ranges(loc), "loop": loop,
                           "hub_end": bool(meta.get("hub_end", True)),
                           "hub_start": bool(meta.get("hub_start", "_Hit" not in name))}


def main(argv):
    ap = argparse.ArgumentParser(prog="check_anim")
    ap.add_argument("--armature", default=None)
    ap.add_argument("--actions", default="")
    ap.add_argument("--poses", default=None, help="python file defining POSES = {name: pose dict}")
    ap.add_argument("--names", default="")
    ap.add_argument("--png", default=None)
    ap.add_argument("--views", default="front34,side,game")
    ap.add_argument("--out", required=True)
    ap.add_argument("--hub", default=None, help="action whose first frame is the hub pose for every clip "
                    "(default: per clip, the *_Idle clip of its set)")
    a = ap.parse_args(argv)
    C.force_object_mode()
    arm = bpy.data.objects.get(a.armature) if a.armature else next(o for o in bpy.data.objects if o.type == "ARMATURE")
    rep = C.Report("check_anim")
    sc = Scene(arm)
    if a.poses:
        import importlib.util
        import sys
        here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        if here not in sys.path:
            sys.path.insert(0, here)
        spec = importlib.util.spec_from_file_location("rk_poses", a.poses)
        mod = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(mod)
        names = [n for n in a.names.split(",") if n] or list(mod.POSES)
        sk = SK.Skeleton(arm)
        if arm.animation_data:
            arm.animation_data.action = None
        setters = []
        for nm in names:
            notes = []
            P.apply(sk, P.solve_goals(sk, mod.POSES[nm], notes), notes)
            sk.apply(arm)
            bpy.context.view_layer.update()
            out = {k: [] for k in ("ground", "tear", "flip", "prop", "limb")}
            out["limits"] = {}
            frame_checks(sc, 0, sc.evaluated(), sk, out)
            for (bone, axis), hits in out["limits"].items():
                rep.error("LIMIT", "pose %s: %s %s = %.0f (limit %s..%s)" % (nm, bone, axis, hits[0][1], hits[0][2][0],
                                                                           hits[0][2][1]), pose=nm)
            for key, code in (("ground", "GROUND"), ("prop", "PROP_PENETRATION"), ("limb", "LIMB_PENETRATION"),
                              ("tear", "TEAR"), ("flip", "FLIP")):
                for h in out[key]:
                    at = (" at %s (rest position)" % (h[3],)) if len(h) > 3 and key == "flip" else (
                        (" at %s" % (h[3],)) if len(h) > 3 else "")
                    rep.error(code, "pose %s: %s %.3f%s" % (nm, h[1], h[2], at), pose=nm)
            for n in notes:
                rep.warn(n[0], "pose %s: %s reach %.2f" % (nm, n[1], n[2]), pose=nm)
            st = sk.copy_state()

            def setter(st=st):
                sk.set_state(st)
                sk.apply(arm)
            setters.append(setter)
        if a.png:
            from . import review as RV
            RV.sheet(setters, a.views.split(","), a.png, size=300, color_type="TEXTURE")
        return rep.dump(a.out)
    acts = [bpy.data.actions[n] for n in a.actions.split(",") if n] or [x for x in bpy.data.actions if x.get("rk_clip")]
    hubs = [a.hub] if a.hub else [x.name for x in bpy.data.actions if is_hub(x.name) and x.get("rk_clip")]
    have = {x.name for x in acts}
    acts = [bpy.data.actions[h] for h in hubs if h not in have] + acts  # the hub clips are needed to compare against
    for act in acts:
        check_action(sc, act, rep)
    hub_checks(sc, rep, a.hub)
    return rep.dump(a.out)
