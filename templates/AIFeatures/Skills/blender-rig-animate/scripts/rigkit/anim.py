"""anim: build clips from key poses, bake every frame through the pose solver, key the deform bones.

  blender -b skinned.blend --python rk.py -- anim --clips clips.py --out anim.blend [--only Idle,Attack]

clips.py is plain Python that imports rigkit.anim and defines CLIPS = [Clip(...), ...]:

    from rigkit.anim import Clip
    from rigkit.pose import merge
    READY = {"Hips": {"loc": [0, 0, -0.01]}, "UpperArm_R": {"side": -55, "flex": 20}, ...}
    idle = Clip("Hero_Idle", frames=60, loop=True, base=READY)
    idle.key(0, {"Hips": {"loc": [0, 0, -0.012]}})
    idle.key(30, {"Hips": {"loc": [0, 0, 0.0]}, "Chest": {"flex": -2}})
    idle.drag("Prop_Hat", parent="Head", delay=2, amount=0.6)
    CLIPS = [idle]

Keys hold a FULL pose (base + overrides). Between keys every channel is interpolated with clamped
Catmull-Rom tangents (like Blender's Auto Clamped handles: it flows through the key, never overshoots).
Per-key ease (the video's handle types, as code):
    "auto"    flow through (default)            "stop"    slow in and slow out (tangent 0)
    "impact"  arrive fast, stop dead (contact)  "burst"   leave fast (player action, release of anticipation)
    "linear"  straight segment after this key   "hold"    step: keep the value until the next key
Loops: the pose of frame 0 is reused at the last frame and tangents wrap around, so the cycle has no seam.
Every frame is baked (FK + IK) and keyed LINEAR; the solver keeps feet planted and bone lengths fixed.
"""
import argparse
import importlib.util
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

from . import common as C
from . import pose as P
from . import skeleton as SK

EASES = ("auto", "stop", "impact", "burst", "linear", "hold")


# --------------------------------------------------------------------------- channels
def flatten(pose, prefix=()):
    out = {}
    for k, v in pose.items():
        if isinstance(v, dict):
            out.update(flatten(v, prefix + (k,)))
        elif isinstance(v, (list, tuple)) and v and all(isinstance(x, (int, float)) for x in v):
            for i, x in enumerate(v):
                out[prefix + (k, i)] = float(x)
        elif isinstance(v, (int, float)) and not isinstance(v, bool):
            out[prefix + (k,)] = float(v)
    return out


def unflatten(ch, template):
    """Rebuild a pose dict: numeric leaves from ch, everything else (strings, flags) from template."""
    def build(t, prefix):
        if isinstance(t, dict):
            return {k: build(v, prefix + (k,)) for k, v in t.items()}
        if isinstance(t, (list, tuple)) and t and all(isinstance(x, (int, float)) for x in t):
            return [ch.get(prefix + (i,), float(t[i])) for i in range(len(t))]
        if isinstance(t, (int, float)) and not isinstance(t, bool):
            return ch.get(prefix, float(t))
        return t
    return build(template, ())


def _ensure_path(template, target):
    """Make sure a (bone, field[, index]) channel exists in the pose template (so a wave can drive it)."""
    bone, field = target[0], target[1]
    if bone in template and isinstance(template[bone], dict) and field in template[bone]:
        return template
    t = dict(template)
    e = dict(t.get(bone, {}))
    e[field] = [0.0, 0.0, 0.0] if len(target) > 2 else 0.0
    t[bone] = e
    return t


# --------------------------------------------------------------------------- interpolation
def hermite(p0, p1, m0, m1, t, dt):
    t2, t3 = t * t, t * t * t
    return ((2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * dt * m0 + (-2 * t3 + 3 * t2) * p1
            + (t3 - t2) * dt * m1)


class Track:
    """One scalar channel: keys (frame, value, ease) -> value at any frame."""

    def __init__(self, keys, length, loop):
        self.keys = sorted(keys, key=lambda k: k[0])
        self.length = length
        self.loop = loop
        self._tangents()

    def _tangents(self):
        ks = self.keys
        n = len(ks)
        self.m_in = [0.0] * n
        self.m_out = [0.0] * n
        if n < 2:
            return
        L = self.length

        def nb(i, d):
            j = i + d
            if self.loop:
                shift = 0.0
                if j < 0:
                    j, shift = n - 1 + j, -L  # key n-1 is the duplicate of key 0 in loops
                elif j > n - 1:
                    j, shift = j - (n - 1), L
                return ks[j][0] + shift, ks[j][1]
            j = min(max(j, 0), n - 1)
            return ks[j][0], ks[j][1]

        for i in range(n):
            t, v, ease = ks[i]
            tp, vp = nb(i, -1)
            tn, vn = nb(i, +1)
            if not self.loop and (i == 0 or i == n - 1):
                m = 0.0
            else:
                dl = (v - vp) / max(t - tp, 1e-6)
                dr = (vn - v) / max(tn - t, 1e-6)
                if dl * dr <= 0:
                    m = 0.0  # local extremum: clamp (no overshoot, no wobble)
                else:
                    m = (vn - vp) / max(tn - tp, 1e-6)
                    lim = 3.0 * min(abs(dl), abs(dr))
                    m = max(-lim, min(lim, m))
            m_in = m_out = m
            if ease == "stop":
                m_in = m_out = 0.0
            elif ease == "impact":
                dl = (v - vp) / max(t - tp, 1e-6)
                m_in, m_out = 2.0 * dl, 0.0
            elif ease == "burst":
                dr = (vn - v) / max(tn - t, 1e-6)
                m_out = 2.0 * dr
            self.m_in[i], self.m_out[i] = m_in, m_out
        # linear / hold segments: the tangents of the segment become the secant (or step)
        for i in range(n - 1):
            ease = ks[i][2]
            if ease == "linear":
                s = (ks[i + 1][1] - ks[i][1]) / max(ks[i + 1][0] - ks[i][0], 1e-6)
                self.m_out[i] = s
                self.m_in[i + 1] = s
        if self.loop:
            self.m_in[0] = self.m_in[n - 1]
            self.m_out[n - 1] = self.m_out[0]

    def value(self, f):
        ks = self.keys
        if len(ks) == 1:
            return ks[0][1]
        if self.loop:
            f = f % self.length if f != self.length else self.length
        if f <= ks[0][0]:
            return ks[0][1]
        if f >= ks[-1][0]:
            return ks[-1][1]
        for i in range(len(ks) - 1):
            t0, v0, e0 = ks[i]
            t1, v1, _ = ks[i + 1]
            if t0 <= f <= t1:
                if e0 == "hold":
                    return v0 if f < t1 else v1
                dt = t1 - t0
                return hermite(v0, v1, self.m_out[i], self.m_in[i + 1], (f - t0) / dt, dt)
        return ks[-1][1]


# --------------------------------------------------------------------------- clip
class Clip:
    def __init__(self, name, frames, loop=False, base=None, fps=30, ground_speed=0.0, hub_end=True, hub_start=None):
        """check_anim compares the first and last frame with the hub pose (frame 0 of the idle of the clip's set,
        QuyChuan A-22). hub_end=False for a clip that ends somewhere else on purpose (Die); hub_start=False for one
        whose first frame is meant to jump (a hit reaction, 7.5). hub_start defaults to False for names with "_Hit"."""
        self.name = name
        self.hub_end = hub_end
        self.hub_start = ("_Hit" not in name) if hub_start is None else bool(hub_start)
        self.frames = int(frames)
        self.loop = loop
        self.base = base or {}
        self.fps = fps
        self.ground_speed = ground_speed  # m/s, for in-place locomotion (planted feet slide back at this speed)
        self.keys = []
        self.drags = []
        self.events = []
        self.contacts = {}
        self.drops = []
        self.notes = []
        self.waves = []

    def key(self, frame, pose=None, ease="auto"):
        if ease not in EASES:
            raise ValueError("ease must be one of %s" % (EASES,))
        self.keys.append((int(frame), P.merge(self.base, pose or {}), ease))
        return self

    def wave(self, target, amplitude, period=None, phase=0.0, frames=None):
        """Additive sine layer on one channel, e.g. wave(("Hips", "loc", 2), 0.004) or wave(("Chest", "twist"), -5).
        period defaults to the clip length (seamless in loops); frames=(f0, f1) fades it in/out (moving holds)."""
        self.waves.append((tuple(target), float(amplitude), float(period or self.frames), float(phase), frames))
        return self

    def drag(self, bone, parent=None, delay=2, amount=0.6):
        """Overlap: bone keeps part of its parent's orientation from `delay` frames ago (hat, weapon tip, ears)."""
        self.drags.append((bone, parent, int(delay), float(amount)))
        return self

    def event(self, frame, function, param=None):
        """AnimationEvent for Unity (QuyChuan 7.4 list: AE_Footstep, AE_Sfx, AE_Vfx, AE_Shake, AE_Signal)."""
        self.events.append({"frame": int(frame), "function": function, "param": param})
        return self

    def contact(self, foot, f0, f1):
        """Frames where a foot must stay on the ground (checked by check_anim)."""
        self.contacts.setdefault(foot, []).append((int(f0), int(f1)))
        return self

    def drop(self, bone, frame, velocity=(0, 0, 0), spin=(0, 0, 0), restitution=0.25, friction=0.6, mesh=None,
             inherit=1.0):
        """From `frame` on, the prop bone leaves its parent and falls under gravity onto the ground (z=0).
        It starts with `inherit` x the velocity it had on the release frame plus `velocity` (m/s). Release on an
        `impact` key and the hand may be moving 5-10 m/s: lower `inherit` or release a frame after the dead stop,
        check_anim reports DROP_FAR when the prop lands far from the body."""
        self.drops.append({"bone": bone, "frame": int(frame), "velocity": Vector(velocity), "spin": Vector(spin),
                           "restitution": restitution, "friction": friction, "mesh": mesh, "inherit": float(inherit)})
        return self

    # ------------------------------------------------------------------ evaluation
    def tracks(self):
        keys = sorted(self.keys, key=lambda k: k[0])
        if not keys:
            keys = [(0, dict(self.base), "auto")]
        if self.loop:
            first = keys[0]
            if first[0] != 0:
                raise ValueError("%s: a loop needs a key at frame 0" % self.name)
            keys = [k for k in keys if k[0] < self.frames] + [(self.frames, first[1], first[2])]
        chans = {}
        for f, pose, ease in keys:
            for ch, v in flatten(pose).items():
                chans.setdefault(ch, []).append((f, v, ease))
        base_flat = flatten(self.base)
        out = {}
        for ch, ks in chans.items():
            present = {k[0] for k in ks}
            for f, pose, ease in keys:  # a channel missing from a key keeps the base value there
                if f not in present:
                    ks.append((f, base_flat.get(ch, 0.0), ease))
            out[ch] = Track(ks, self.frames, self.loop)
        template = {}
        for _, pose, _ in keys:
            template = P.merge(template, pose)
        return out, template

    def pose_at(self, f, tracks, template):
        ch = {c: t.value(f) for c, t in tracks.items()}
        for target, amp, period, phase, win in self.waves:
            w = 1.0
            if win:
                f0, f1 = win
                if f < f0 or f > f1:
                    continue
                span = max(f1 - f0, 1)
                w = math.sin(math.pi * (f - f0) / span)  # fades in and out inside the window
            ch[target] = ch.get(target, 0.0) + w * amp * math.sin(2 * math.pi * (f / period + phase))
            template = _ensure_path(template, target)
        return unflatten(ch, template)


# --------------------------------------------------------------------------- baking
def key_bones(arm, sk, frame, names, last_q, with_loc, with_scale):
    for n in names:
        pb = arm.pose.bones[n]
        pb.rotation_mode = "QUATERNION"
        q = sk.rot[n].copy()
        prev = last_q.get(n)
        if prev is not None and prev.dot(q) < 0:
            q = -q
        last_q[n] = q
        pb.rotation_quaternion = q
        pb.keyframe_insert("rotation_quaternion", frame=frame, group=n)
        if n in with_loc:
            pb.location = sk.loc[n]
            pb.keyframe_insert("location", frame=frame, group=n)
        if n in with_scale:
            pb.scale = sk.scl[n]
            pb.keyframe_insert("scale", frame=frame, group=n)


def prop_points(arm, bone, mesh_name):
    """Vertices of the prop mesh in the prop bone's rest space (to find its lowest point when dropped)."""
    ob = bpy.data.objects.get(mesh_name) if mesh_name else None
    if ob is None:
        for o in bpy.data.objects:
            if o.type == "MESH" and bone in o.vertex_groups and len(o.vertex_groups) == 1:
                ob = o
                break
    if ob is None:
        return [Vector((0, 0, 0))]
    inv = arm.data.bones[bone].matrix_local.inverted()
    co = C.world_coords(ob)
    step = max(1, len(co) // 200)
    return [inv @ Vector(p) for p in co[::step]]


def simulate_drop(sk, d, frames, fps, world_hist, pts):
    """Ballistic fall with ground bounce. Returns {frame: 4x4 armature-space matrix} for the prop bone."""
    g = Vector((0, 0, -9.81))
    f0 = d["frame"]
    M = world_hist[f0][d["bone"]].copy()
    if f0 > 0:
        v_hand = (world_hist[f0][d["bone"]].translation - world_hist[f0 - 1][d["bone"]].translation) * fps
    else:
        v_hand = Vector((0, 0, 0))
    v = v_hand * d.get("inherit", 1.0) + d["velocity"]
    w = d["spin"].copy()
    pos = M.translation.copy()
    R = M.to_quaternion()
    out = {}
    dt = 1.0 / fps
    resting = False
    for f in range(f0, frames + 1):
        out[f] = Matrix.LocRotScale(pos, R, Vector((1, 1, 1)))
        if resting:
            continue
        v += g * dt
        pos += v * dt
        if w.length > 1e-6:
            R = Quaternion(w.normalized(), w.length * dt) @ R
        Rm = R.to_matrix()
        low = min((Rm @ p + pos).z for p in pts)
        if low < 0:
            pos.z -= low
            if v.z < 0:
                v.z = -v.z * d["restitution"]
                v.x *= d["friction"]
                v.y *= d["friction"]
                w *= 0.5
            if abs(v.z) < 0.25 and v.xy.length < 0.2:
                resting = True
                # settle flat: keep the current orientation, sit on the ground
    return out


def sole_points(arm, sk):
    """Lowest skin vertices that follow each foot bone, in that bone's rest frame relative to its head: the
    ground-clearance test uses the real sole, not the bone line (the sole of a boot is cm below the bone)."""
    import numpy as np
    out = {}
    for foot in ("Foot_L", "Foot_R"):
        if foot not in sk.rest:
            continue
        pts = []
        for ob in bpy.data.objects:
            if ob.type != "MESH" or foot not in ob.vertex_groups:
                continue
            if not any(m.type == "ARMATURE" and m.object == arm for m in ob.modifiers):
                continue
            W, _ = C.read_weights(ob, [foot])
            co = C.world_coords(ob)
            ids = np.nonzero(W[:, 0] > 0.6)[0]
            if len(ids):
                pts.append(co[ids])
        if not pts:
            continue
        P_ = np.concatenate(pts)
        low = P_[P_[:, 2] <= np.percentile(P_[:, 2], 25)]
        step = max(1, len(low) // 60)
        head = sk.rest[foot].translation
        R0i = sk.rest[foot].to_3x3().inverted()
        out[foot] = [R0i @ (Vector(p) - head) for p in low[::step]]
    return out


def bake(arm, clip, rep):
    sk = SK.Skeleton(arm)
    sk.sole = sole_points(arm, sk)
    notes = []
    # IK goals of the keys (arm reach, prop aim) are solved ONCE per key into FK angles; frames in between
    # interpolate those angles, so hands move on arcs and the solver can never jump to another solution
    clip.keys = [(f, P.solve_goals(sk, pose, notes), ease) for f, pose, ease in clip.keys]
    for n in notes:
        if n[0] == "ARM_LIMIT":
            rep.warn("ARM_LIMIT", "%s: a key asks %s for a pose outside its joint limits (%.0f deg over)"
                     % (clip.name, n[1], n[2]), clip=clip.name)
    tracks, template = clip.tracks()
    # every bone but Root: non-deform bones with deform children (Shoulder) are exported as transforms too
    names = [n for n in sk.order if n != "Root"]
    ik_notes = []
    states = []
    world_hist = []
    for f in range(clip.frames + 1):
        pose = clip.pose_at(f, tracks, template)
        # in-place locomotion: nothing to do here, the gait helper already slides the planted foot back
        P.apply(sk, pose, ik_notes)
        states.append(sk.copy_state())
        world_hist.append({n: sk.pose[n].copy() for n in sk.order})
    # overlap / drag: second pass, using the history of the parent's orientation
    if clip.drags:
        for f in range(clip.frames + 1):
            sk.set_state(states[f])
            for bone, parent, delay, amount in clip.drags:
                if bone not in sk.rest:
                    continue
                par = parent or sk.parent[bone]
                fp = f - delay
                if clip.loop:
                    fp %= clip.frames
                else:
                    fp = max(fp, 0)
                R_now = world_hist[f][par].to_3x3()
                R_past = world_hist[fp][par].to_3x3()
                delta = (R_past @ R_now.inverted()).to_quaternion()
                part = Quaternion().slerp(delta, amount)
                R_b = sk.pose[bone].to_3x3()
                sk.set_world_rot(bone, part.to_matrix() @ R_b)
            states[f] = sk.copy_state()
            world_hist[f] = {n: sk.pose[n].copy() for n in sk.order}
    # props that fall off
    for d in clip.drops:
        pts = prop_points(arm, d["bone"], d.get("mesh"))
        track = simulate_drop(sk, d, clip.frames, clip.fps, world_hist, pts)
        for f, M in track.items():
            sk.set_state(states[f])
            par = sk.parent[d["bone"]]
            parent_pose = sk.pose[par] if par else Matrix.Identity(4)
            basis = (parent_pose @ sk.rest_rel[d["bone"]]).inverted() @ M
            loc, q, _ = basis.decompose()
            sk.loc[d["bone"]] = loc
            sk.rot[d["bone"]] = q
            sk.fk()
            states[f] = sk.copy_state()
    # key
    act = bpy.data.actions.get(clip.name)
    if act is not None:
        bpy.data.actions.remove(act)
    act = bpy.data.actions.new(clip.name)
    act.use_fake_user = True
    C.assign_action(arm, act)
    # Key location, rotation AND scale of every bone in every clip. A channel without keys keeps whatever the
    # previous clip left there (Blender) or depends on Write Defaults (Unity): the hat of the death clip stayed on
    # the ground in the idle. Constant curves cost nothing after Unity's Remove Constant Scale Curves / compression.
    with_loc = set(names)
    with_scale = set(names)
    last_q = {}
    for f, st in enumerate(states):
        sk.set_state(st)
        key_bones(arm, sk, f, names, last_q, with_loc, with_scale)
    for fc in C.action_fcurves(act):
        for kp in fc.keyframe_points:
            kp.interpolation = "LINEAR"
    act.use_frame_range = True
    act.frame_start = 0
    act.frame_end = clip.frames
    act.use_cyclic = clip.loop
    for m in list(act.pose_markers):
        act.pose_markers.remove(m)
    for ev in clip.events:
        mk = act.pose_markers.new("%s:%s" % (ev["function"], ev["param"]))
        mk.frame = ev["frame"]
    meta = {"frames": clip.frames, "fps": clip.fps, "loop": clip.loop, "events": clip.events,
            "contacts": clip.contacts, "ground_speed": clip.ground_speed, "notes": clip.notes,
            "kind": getattr(clip, "kind", "clip"), "drops": [{"bone": d["bone"], "frame": d["frame"]} for d in clip.drops],
            "hub_end": getattr(clip, "hub_end", True), "hub_start": getattr(clip, "hub_start", True)}
    act["rk_clip"] = __import__("json").dumps(meta)
    reach = [n for n in ik_notes if n[0] == "IK_REACH"]
    if reach:
        rep.warn("IK_REACH", "%s: %d frames ask a limb to reach farther than it can (%s)" % (clip.name, len(reach),
                 sorted({r[1] for r in reach})), clip=clip.name)
    rep.info.setdefault("clips", {})[clip.name] = {"frames": clip.frames, "loop": clip.loop, "keys": len(clip.keys),
                                                  "bones_keyed": len(names), "events": len(clip.events)}
    return act


# --------------------------------------------------------------------------- gait helper
def gait(clip, stride, lift, duty=0.5, forward=(0, -1, 0), phase_r=0.5, heel_roll=12.0, toe_roll=18.0,
         bob=0.0, bob_phase=0.0, sway=0.0, hips_twist=0.0, arm_swing=0.0, arm_bones=None, keys_per_cycle=16,
         follow_max=0.85):
    """Fill an in-place walk / run cycle. The planted foot slides back at the ground speed
    (clip.ground_speed = stride / contact time), the swing foot travels forward on an arc.
      stride  distance the foot slides back while on the ground (m); a leg of length L standing with the hip
              at height h reaches only sqrt(L^2 - h^2) in front of / behind the hip: measure it first
      lift    swing height of the ankle (m)
      duty    fraction of the cycle a foot is on the ground (walk 0.6, jog 0.45, run 0.35)
      follow_max  how much the lifted foot hangs from the shin (0.85 human; ~0.3 for chibi feet as long as
              the leg, which otherwise point their toes into the floor)
    Hips bob twice per cycle; arms swing opposite to the legs. Adds contact spans for check_anim."""
    N = clip.frames
    fwd = Vector(forward).normalized()
    contact_frames = duty * N
    clip.ground_speed = stride / (contact_frames / clip.fps)
    clip.notes.append("ground speed %.2f m/s (stride %.2f m, contact %.1f frames)" % (clip.ground_speed, stride,
                                                                                    contact_frames))
    feet = {"L": 0.0, "R": phase_r}
    for side, ph in feet.items():
        c0 = int(round(ph * N)) % N
        c1 = int(round(ph * N + contact_frames)) % N
        if c0 < c1:
            clip.contact("Foot_" + side, c0, c1)
        else:
            clip.contact("Foot_" + side, c0, N)
            clip.contact("Foot_" + side, 0, c1)
    for i in range(keys_per_cycle):
        f = int(round(i * N / keys_per_cycle))
        t = f / N
        pose = {}
        for side, ph in feet.items():
            u = (t - ph) % 1.0  # 0 = heel strike
            # every quantity below is continuous over the whole cycle (no snap at contact or lift-off)
            if u < duty:
                s = u / duty  # stance: front (+stride/2) -> back (-stride/2)
                along = stride * (0.5 - s)
                up = 0.0
                if s < 0.15:      # heel strike: toes still up, rolling onto the sole around the heel
                    roll, pivot = heel_roll * (1 - s / 0.15), -1.0
                elif s > 0.8:     # push off: heel rises, the foot pivots on the toes
                    roll, pivot = -toe_roll * (s - 0.8) / 0.2, 1.0
                else:
                    roll, pivot = 0.0, 0.0
            else:
                s = (u - duty) / (1 - duty)  # swing: back -> front along an arc
                along = stride * (-0.5 + (0.5 - 0.5 * math.cos(math.pi * s)))
                up = lift * math.sin(math.pi * s)
                a = C.smoothstep(s / 0.4)
                b = C.smoothstep((s - 0.6) / 0.4)
                roll = -toe_roll * (1 - a) + heel_roll * b     # toes relax after lift-off, come up before contact
                pivot = 1.0 * (1 - a) - 1.0 * b
            follow = 0.0 if u < duty else follow_max * C.smoothstep(s / 0.3) * (1 - C.smoothstep((s - 0.65) / 0.35))
            off = fwd * along + Vector((0, 0, up))
            pose["leg_" + side] = {"offset": [off.x, off.y, off.z], "roll": roll, "pivot": pivot, "follow": follow}
        if bob:
            hz = -bob * math.cos(4 * math.pi * (t - bob_phase))
            pose.setdefault("Hips", {})["loc"] = list(P.merge({}, clip.base).get("Hips", {}).get("loc", [0, 0, 0]))
            pose["Hips"]["loc"][2] += hz
        if sway:
            pose.setdefault("Hips", {})["side"] = sway * math.sin(2 * math.pi * t)
        if hips_twist:
            pose.setdefault("Hips", {})["twist"] = hips_twist * math.sin(2 * math.pi * t)
        if arm_swing and arm_bones:
            for side, bone in arm_bones.items():
                ph = feet["R" if side == "L" else "L"]
                sw = arm_swing * math.cos(2 * math.pi * (t - ph))
                base = P.merge({}, clip.base).get(bone, {})
                pose[bone] = dict(base)
                pose[bone]["flex"] = base.get("flex", 0.0) + sw
        clip.key(f, pose)
    return clip


# --------------------------------------------------------------------------- step
def load_clips(path):
    here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    if here not in sys.path:
        sys.path.insert(0, here)
    spec = importlib.util.spec_from_file_location("rk_clips", path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod.CLIPS


def main(argv):
    ap = argparse.ArgumentParser(prog="anim")
    ap.add_argument("--clips", required=True)
    ap.add_argument("--armature", default=None)
    ap.add_argument("--only", default="")
    ap.add_argument("--out", required=True)
    a = ap.parse_args(argv)
    C.force_object_mode()
    arm = bpy.data.objects.get(a.armature) if a.armature else next(o for o in bpy.data.objects if o.type == "ARMATURE")
    rep = C.Report("anim")
    clips = load_clips(a.clips)
    only = {x for x in a.only.split(",") if x}
    sc = bpy.context.scene
    sc.render.fps = clips[0].fps if clips else 30
    for clip in clips:
        if only and clip.name not in only:
            continue
        bake(arm, clip, rep)
    first = next((bpy.data.actions.get(c.name) for c in clips if bpy.data.actions.get(c.name)), None)
    if first is not None:
        C.assign_action(arm, first)
        sc.frame_start, sc.frame_end = 0, int(first.frame_range[1])
    sc.frame_set(0)
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a.out), copy=True)
    rep.info["saved"] = os.path.abspath(a.out)
    return rep.dump(os.path.splitext(a.out)[0] + "_anim.json")
