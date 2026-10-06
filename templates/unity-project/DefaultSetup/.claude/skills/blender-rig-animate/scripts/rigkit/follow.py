"""follow: follow-through springs for clips (QuyChuan 2 row 5, 7.6.6, 7.6.9).

A part hangs on a damped spring that the keyed motion drags around: a mass on a moving base, damped on the relative
speed. At constant speed it rides the keys (offset 0, no lag); when the keys stop dead it carries on with the speed it
had, overshoots and settles in a few shrinking wobbles. Clip.drag is a fixed delay that never overshoots: when the body
stops, the part stops one frame later. Use a spring where the part should roll out past a dead stop (a blade after a
chop, a head after a landing, a weapon that floats).

Clip API (anim.py):
  clip.spring_channel(target, gain=1.0, hz=2.8, zeta=0.3, room=None, windows=None, fade=4)
      A spring on channels of the keys, before the bake: target = a bone name (its flex, side and twist), a channel
      tuple such as ("Head", "flex"), or a list of them. It follows the keyed channel itself (a nod that stops dead).
      An anatomical channel stays softly inside the joint limits of the rig; `room` = (lo, hi) bounds the offset of a
      channel without limits (a virtual control such as a squash amount).
  clip.spring(bone, kind="turn", gain=1.0, hz=2.8, zeta=0.3, max_deg=None, windows=None, fade=4, grip=None)
      A spring on the orientation of `bone` in armature space, run in the bake after the pose and the IK (before drag
      and drop), so the part also carries on when the body under it stops.
      kind "turn": driven by the bone's own orientation (a blade carries on past a dead stop).
      kind "grip": a pendulum about the head of `bone`, driven by the world motion of the head of `grip`: the weapon
      lags the hops and landings of the body too, not only its turns (spring("UpperArm_R", "grip", grip="Hand_R")).
      Springs run in the order they were added, each on the pose the previous one left (grip on the arm, then turn
      on the hand). Not on Hips or legs: it runs after the leg IK and would lift or sink planted feet.

Why a held or floating object gets ONE spring on the orientation of the bone that carries it, not one per joint: an
arm solved by IK spreads a turn over redundant joints that cancel each other, and a spring per joint breaks that (a
blade swung 95 deg on a take-off where it hardly turns). The result is written back as anatomical angles: the change is
read from the angles BEFORE and AFTER the turn and wrapped to +-180 (the keyed triple may be another triple for the same
orientation, and "after - keyed" then is a big turn out of nothing), soft-limited to the joint limits and softly capped
at max_deg (a 2-frame chop sweeps ~180 deg; a free hand would carry the blade on another ~100 deg, out of the wrist
limits, and clamping each axis breaks the orientation).

gain 1: the part keeps its full speed after a dead stop, like a free joint. hz 2.8: one wobble ~11 frames at 30 fps, the
first overshoot peaks ~3 frames after the stop. zeta 0.3: about two visible wobbles, settled within ~0.5 s; successive
peaks shrink by exp(-pi zeta / sqrt(1 - zeta^2)) = 0.37. Give farther parts a lower hz (looser, slower): a stop rolls
out along the body (chest -> head, hand -> weapon).
Loops: the spring runs LOOP_CYCLES cycles and keeps the last (periodic, no seam). Other clips: windows = [(start, end[,
fade[, gain]]), ...]: the spring starts from rest on `start` (a dead stop ON that frame still kicks) and its offset fades
to 0 over `fade` frames before `end`. Default: the whole clip, faded before the end unless the clip ends somewhere else
(hub_end=False, Die). The first and last frames stay on the keys (hub pose, QuyChuan A-22); a window that ends before a
contact keeps the strike on its keys, one that starts on the contact lets the stop roll out.
"""
import math

from mathutils import Quaternion, Vector

HZ = 2.8
ZETA = 0.3
SUBSTEPS = 8          # semi-implicit Euler steps per frame
LOOP_CYCLES = 3       # loops: run this many cycles, keep the last
ANAT = ("flex", "side", "twist")
KINDS = ("turn", "grip")


def smooth(x):
    x = min(1.0, max(0.0, x))
    return x * x * (3.0 - 2.0 * x)


def soft_limit(off, y, lim):
    """The offset `off` on a value `y`, eased so y + off never leaves lim = (lo, hi): off saturates on the room left,
    room x (1 - e^(-|off| / room)), slope 1 at 0. The keyed value itself is never touched, so a spring that runs into a
    limit slows down instead of hitting a wall (no pop, no LIMIT)."""
    room = (lim[1] - y) if off > 0.0 else (y - lim[0])
    if room <= 0.0:
        return 0.0
    return math.copysign(room * (1.0 - math.exp(-abs(off) / room)), off)


def soft_cap(v, most):
    """Rotation vector v (radians) with its angle softly capped at `most` degrees (slope 1 at 0)."""
    ang = math.degrees(v.length)
    if most is None or ang < 1e-9:
        return v
    return v * (most * (1.0 - math.exp(-ang / most)) / ang)


# --------------------------------------------------------------------------- integrators
def spring(ys, fps, hz=HZ, zeta=ZETA, start=0, loop=False):
    """Offsets of a spring-hung part from the keyed values ys (one per frame; a loop gives one value per frame of the
    cycle, frame n == frame 0). Between frames the base moves at a constant speed; on every frame the change of base
    speed kicks the part (it keeps its own speed), then the free damped oscillator o'' = -w^2 o - 2 zeta w o' runs
    SUBSTEPS semi-implicit Euler steps. Nothing before `start` (state 0 there)."""
    n = len(ys)
    if n < 2:
        return [0.0] * n
    w = 2.0 * math.pi * hz
    h = 1.0 / (fps * SUBSTEPS)
    span = n if loop else n - 1
    total = span * (LOOP_CYCLES if loop else 1)
    off = [0.0] * n
    o = u = 0.0
    v_prev = None
    for i in range(total):
        f = i % span if loop else i
        if not loop and f < start:
            continue
        b = (f + 1) % n if loop else f + 1
        v = (ys[b] - ys[f]) * fps
        if v_prev is None:
            v_prev = (ys[f] - ys[f - 1]) * fps if (loop or f > 0) else v   # a stop ON the start frame still kicks
        u -= v - v_prev
        v_prev = v
        for _ in range(SUBSTEPS):
            u += (-w * w * o - 2.0 * zeta * w * u) * h
            o += u * h
        if not loop or i >= total - span:
            off[b] = o
    return off


def spring_rot(samples, spin, fps, hz=HZ, zeta=ZETA, start=0, loop=False):
    """spring() for a turning part: spin(samples, a, b) = the angular velocity (armature space, rad/s) the part needs
    from frame a to frame b; the offsets are rotation vectors (radians) applied on the left of the keyed orientation.
    Small-angle superposition, as Clip.drag."""
    n = len(samples)
    off = [Vector((0.0, 0.0, 0.0)) for _ in range(n)]
    if n < 2:
        return off
    w = 2.0 * math.pi * hz
    h = 1.0 / (fps * SUBSTEPS)
    span = n if loop else n - 1
    total = span * (LOOP_CYCLES if loop else 1)
    o = Vector((0.0, 0.0, 0.0))
    u = Vector((0.0, 0.0, 0.0))
    v_prev = None
    for i in range(total):
        f = i % span if loop else i
        if not loop and f < start:
            continue
        b = (f + 1) % n if loop else f + 1
        v = spin(samples, f, b)
        if v_prev is None:
            v_prev = spin(samples, (f - 1) % n, f) if (loop or f > 0) else v
        u -= v - v_prev
        v_prev = v
        for _ in range(SUBSTEPS):
            u += (-w * w * o - 2.0 * zeta * w * u) * h
            o += u * h
        if not loop or i >= total - span:
            off[b] = o.copy()
    return off


def rot_log(q):
    """Rotation vector (radians) of a quaternion, the short way round."""
    q = q.normalized()
    if q.w < 0.0:
        q.negate()
    axis, ang = q.to_axis_angle()
    return Vector(axis) * ang


def rot_exp(v):
    ang = v.length
    return Quaternion() if ang < 1e-9 else Quaternion(v / ang, ang)


def turn_spin(fps):
    """Angular velocity of an orientation (samples: quaternions)."""
    return lambda qs, a, b: rot_log(qs[b] @ qs[a].inverted()) * fps


def grip_spin(fps):
    """Angular velocity about a pivot that the world motion of a grip point stands for (samples: (pivot, grip)):
    (r x v) / |r|^2, r = pivot -> grip, v = grip velocity. A pivot that stops dead leaves the grip moving: the part
    swings on."""
    def spin(sg, a, b):
        r = sg[a][1] - sg[a][0]
        return r.cross((sg[b][1] - sg[a][1]) * fps) / max(r.length_squared, 1e-6)
    return spin


# --------------------------------------------------------------------------- windows
def norm_windows(windows, frames, fade):
    """[(start, end, fade, gain)]; default: the whole clip."""
    out = []
    for w in (windows or [(0, frames)]):
        w = tuple(w)
        if len(w) < 2:
            raise ValueError("a spring window is (start, end[, fade[, gain]]), got %r" % (w,))
        out.append((int(w[0]), int(w[1]), float(w[2]) if len(w) > 2 else float(fade),
                    float(w[3]) if len(w) > 3 else 1.0))
    return out


def windowed(samples, run, zero, frames, loop, hub_end, windows, gain):
    """gain x the spring offsets of samples: one periodic run for a loop, else one run per window, faded before its end
    (not when the window reaches the last frame of a clip that ends elsewhere, hub_end=False). zero() = a 0 offset."""
    if loop:
        return [o * gain for o in run(samples, 0, True)]
    total = [zero() for _ in samples]
    for w0, w1, w_fade, w_gain in windows:
        w0 = max(w0, 0)
        w1 = min(w1, len(samples) - 1)
        if w1 <= w0:
            continue
        off = run(samples[:w1 + 1], w0, False)
        for f in range(w0, w1 + 1):
            env = w_gain
            if w_fade > 0 and (w1 < frames or hub_end):
                env *= smooth((w1 - f) / w_fade)
            total[f] = total[f] + off[f] * (gain * env)
    return total


# --------------------------------------------------------------------------- channel springs (before the bake)
def channel_targets(target):
    """[(bone, field[, index])] of a spring_channel target: a bone name = its three anatomical channels; a tuple whose
    second item is a field name = one channel; a list = several."""
    if isinstance(target, str):
        return [(target, a) for a in ANAT]
    if isinstance(target, tuple) and len(target) >= 2 and isinstance(target[0], str) and isinstance(target[1], str):
        return [tuple(target)]
    out = []
    for t in target:
        out += channel_targets(t)
    return out


def channel_offsets(clip, tracks, sk):
    """clip.chan_springs on the keyed tracks (IK goals already solved to angles). Returns ({channel: [offset per frame
    0..N]}, {channel: (lim, relative)}, {channel: largest |offset|}, [channels no key moves]); Clip.pose_at adds the
    offsets, soft-limited: an anatomical channel against its joint limits (absolute), another one against its `room`
    (relative to the key). A channel only a wave moves has no track: the spring follows keys, not waves."""
    N = clip.frames
    n = N if clip.loop else N + 1
    offs, lims, info, unused = {}, {}, {}, []
    for sp in clip.chan_springs:
        if sp["gain"] <= 0.0:
            continue
        wins = norm_windows(sp["windows"], N, sp["fade"])

        def run(s, st, lp, hz=sp["hz"], zeta=sp["zeta"]):
            return spring(s, clip.fps, hz, zeta, st, lp)

        for c in sp["channels"]:
            if c not in tracks:
                unused.append(c)
                continue
            ys = [tracks[c].value(f) for f in range(n)]
            o = windowed(ys, run, float, N, clip.loop, clip.hub_end, wins, sp["gain"])
            if clip.loop:
                o = o + [o[0]]
            prev = offs.get(c)
            offs[c] = o if prev is None else [a + b for a, b in zip(prev, o)]
            lim = (sk.meta.get(c[0], {}).get("limits") or {}).get(c[1]) if len(c) == 2 and c[1] in ANAT else None
            if lim:
                lims[c] = (tuple(lim), False)
            elif sp["room"] is not None:
                lims[c] = (tuple(sp["room"]), True)
    for c, o in offs.items():
        info["%s.%s" % (c[0], ".".join(str(x) for x in c[1:]))] = round(max(abs(x) for x in o), 3)
    return offs, lims, info, unused


def add_channel_offsets(ch, f, offs, lims):
    """Add the spring offsets of frame f to the flat channels ch (in place), soft-limited (channel_offsets)."""
    for c, o in offs.items():
        if f >= len(o) or not o[f]:
            continue
        y = ch.get(c, 0.0)
        lim, rel = lims.get(c, (None, False))
        if lim is None:
            ch[c] = y + o[f]
        else:
            ch[c] = y + soft_limit(o[f], 0.0 if rel else y, lim)
    return ch


# --------------------------------------------------------------------------- rotation springs (in the bake)
def rotation_pass(clip, sk, states, world_hist):
    """clip.springs on the baked frames, in the order they were added. states / world_hist (one per frame 0..N, as
    anim.bake keeps them) are updated in place. Returns ({bone: largest spring angle applied, degrees}, [bones not in
    the rig])."""
    N = clip.frames
    n = N if clip.loop else N + 1
    info, unused = {}, []
    for sp in clip.springs:
        bone = sp["bone"]
        if bone not in sk.rest:
            unused.append(bone)
            continue
        if sp["gain"] <= 0.0:
            continue
        if sp["kind"] == "grip":
            if sp["grip"] not in sk.rest:
                raise ValueError("%s: spring(%s, 'grip'): grip bone %r not in the rig" % (clip.name, bone, sp["grip"]))
            samples = [(world_hist[f][bone].translation.copy(), world_hist[f][sp["grip"]].translation.copy())
                       for f in range(n)]
            spin = grip_spin(clip.fps)
        else:
            samples = [world_hist[f][bone].to_3x3().normalized().to_quaternion() for f in range(n)]
            spin = turn_spin(clip.fps)

        def run(s, st, lp, hz=sp["hz"], zeta=sp["zeta"], spin=spin):
            return spring_rot(s, spin, clip.fps, hz, zeta, st, lp)

        offs = windowed(samples, run, lambda: Vector((0.0, 0.0, 0.0)), N, clip.loop, clip.hub_end,
                        norm_windows(sp["windows"], N, sp["fade"]), sp["gain"])
        if clip.loop:
            offs = offs + [offs[0]]
        lim = sk.meta.get(bone, {}).get("limits") or {}
        worst = 0.0
        for f in range(N + 1):
            turn = soft_cap(offs[f], sp["max_deg"])
            if turn.length < 1e-7:
                continue
            sk.set_state(states[f])
            keyed = sk.pose[bone].to_3x3().normalized().to_quaternion()
            before = sk.get_anat(bone)
            sk.set_world_rot(bone, (rot_exp(turn) @ keyed).to_matrix())
            after = sk.get_anat(bone)
            new = []
            for a, v0, v in zip(ANAT, before, after):
                d = (v - v0 + 180.0) % 360.0 - 180.0
                new.append(v0 + (soft_limit(d, v0, lim[a]) if a in lim else d))
            sk.set_anat(bone, *new)
            states[f] = sk.copy_state()
            world_hist[f] = {m: sk.pose[m].copy() for m in sk.order}
            worst = max(worst, math.degrees(turn.length))
        info[bone] = round(worst, 2)
    return info, unused
