"""pose: describe a pose with anatomical angles and IK targets, apply it to a Skeleton, check joint limits.

A pose is a dict. Every entry is optional; what is not given stays at rest.
  "Hips":       {"loc": [dx, dy, dz], "flex": deg, "side": deg, "twist": deg}   loc = armature-space offset
  "<Bone>":     {"flex": deg, "side": deg, "twist": deg}                          FK, anatomical (skeleton.py)
  "leg_L":      {"offset": [dx, dy, dz], "yaw": deg, "roll": deg, "pivot": "ankle"|"toe"|"heel",
                 "knee": [dx, dy, dz]}
      Legs are ALWAYS solved with IK unless "fk": true. offset = where the ankle goes relative to its rest
      position (0,0,0 = foot planted where it stands in the rest pose). The foot stays flat on the ground unless
      roll is given (positive = toes up). The knee points where the foot points (+ optional "knee" nudge).
  "arm_L":      {"target": [x, y, z] | "offset": [dx, dy, dz], "elbow": [dx, dy, dz]}  optional arm IK (wrist)
Angles are degrees. Signs: flex > 0 = natural flexion; side > 0 = away from the body (arms up, legs apart)
or leaning to the character's right for spine/head; see skeleton.py.
"""
import math

from mathutils import Matrix, Vector

from . import common as C
from . import skeleton as SK

LEG_CHAINS = {"L": ("UpperLeg_L", "LowerLeg_L", "Foot_L", "Toes_L"), "R": ("UpperLeg_R", "LowerLeg_R", "Foot_R", "Toes_R")}
ARM_CHAINS = {"L": ("UpperArm_L", "LowerArm_L", "Hand_L"), "R": ("UpperArm_R", "LowerArm_R", "Hand_R")}
ANAT_KEYS = ("flex", "side", "twist")


def is_bone_entry(k, sk):
    return k in sk.rest


def apply(sk, pose, report=None):
    """Apply a pose dict to the skeleton (sk.reset() first). Returns sk."""
    sk.reset()
    # 1. hips and every FK entry, parents before children (the skeleton order)
    for n in sk.order:
        e = pose.get(n)
        if not e:
            continue
        if any(k in e for k in ANAT_KEYS):
            sk.rot[n] = SK.anat_quat(e.get("flex", 0.0), e.get("side", 0.0), e.get("twist", 0.0), SK.side_sign(n))
        if "loc" in e:
            sk.fk()
            sk.set_loc(n, e["loc"])
        if "scale" in e:
            sk.scl[n] = Vector(e["scale"])
    sk.fk()
    # 2. arm IK (optional). "reach": [azimuth, elevation, extension] around the shoulder, in the frame of the
    #    parent of the upper arm (it turns with the chest): azimuth 0 = forward, +90 = out to the side, -90 = across
    #    the body, 180 = back; elevation +90 = up; extension = wrist distance / arm length. Interpolating these
    #    angles moves the hand on an arc around the shoulder (arcs principle) instead of a straight line.
    for side, (up, lo, hand) in ARM_CHAINS.items():
        e = pose.get("arm_" + side)
        if not e or up not in sk.rest:
            continue
        H = sk.head(up)
        sx = 1.0 if side == "L" else -1.0
        par = sk.parent[up]
        R_frame = (sk.pose[par].to_3x3() @ sk.rest[par].to_3x3().inverted()) if par else Matrix.Identity(3)
        L_arm = (sk.head(lo) - H).length + sk.length[lo]
        if "reach" in e:
            az, el, ext = e["reach"]
            az, el = az * C.D2R, el * C.D2R
            d = Vector((sx * math.sin(az) * math.cos(el), -math.cos(az) * math.cos(el), math.sin(el)))
            T = H + R_frame @ d * (ext * L_arm)
        elif "target" in e:
            T = Vector(e["target"])
        else:
            rest_wrist = sk.rest[lo] @ Vector((0, sk.length[lo], 0))
            T = rest_wrist + Vector(e.get("offset", (0, 0, 0)))
        ev = Vector(e.get("elbow", (0.5, 0.45, -0.75)))  # out, back, down (left-side convention)
        pole = (H + T) * 0.5 + R_frame @ Vector((sx * ev.x, ev.y, ev.z)).normalized() * sk.length[up]
        sk.ik2(up, lo, T, pole, report, "arm_" + side)
        if hand in (pose or {}):
            h = pose[hand]
            sk.rot[hand] = SK.anat_quat(h.get("flex", 0), h.get("side", 0), h.get("twist", 0), SK.side_sign(hand))
            sk.fk()
    # 2b. aim a hand-held prop: {"Prop_Axe": {"aim": [x, y, z], "front": [x, y, z]}} = the prop's axis (grip ->
    #     head) and its front (blade edge / shield face), in armature space. The hand turns to make it so; the
    #     part of the turn that is a twist beyond the wrist limit goes to the forearm (like a twist bone).
    for n in sk.order:
        e = pose.get(n)
        if not e or "aim" not in e:
            continue
        hand = sk.parent[n]
        fore = sk.parent[hand] if hand else None
        if hand is None:
            continue
        front = Vector(e.get("front", sk.axis(n, 0)))
        aim = Vector(e["aim"]).normalized()
        R_p = SK.frame(aim, front)
        R_rel = sk.rest_rel[n].to_3x3()
        R_h = R_p @ R_rel.inverted()
        # People turn the forearm (pronation / supination) so the wrist stays nearly straight: search the forearm
        # twist that points the prop best with the wrist as posed, then let the wrist take only the rest.
        flim = sk.meta.get(fore, {}).get("limits", {}).get("twist") if fore else None
        if fore and flim:
            ff, fs, ft0 = sk.get_anat(fore)
            hq = sk.rot[hand].copy()
            best, best_err = ft0, None
            for tw in range(int(flim[0]), int(flim[1]) + 1, 4):
                sk.set_anat(fore, ff, fs, tw)
                sk.rot[hand] = hq
                sk.fk()
                R_now = sk.pose[hand].to_3x3() @ R_rel
                err = R_now.col[1].angle(aim, 3.14) + 0.35 * R_now.col[0].angle(R_p.col[0], 3.14)
                if best_err is None or err < best_err:
                    best, best_err = tw, err
            sk.set_anat(fore, ff, fs, best)
        sk.set_world_rot(hand, R_h)
    # 3. legs: IK with the foot flat unless told otherwise; "fk": 0..1 blends towards the FK angles of the
    #    UpperLeg / LowerLeg / Foot entries (animate it to hand a falling body over from IK to FK without a pop)
    for side, (up, lo, foot, _toes) in LEG_CHAINS.items():
        if up not in sk.rest or lo not in sk.rest:
            continue
        e = pose.get("leg_" + side, {})
        w = float(e.get("fk", 0.0)) if not isinstance(e.get("fk"), bool) else (1.0 if e["fk"] else 0.0)
        if w >= 0.999:
            continue
        chain = [b for b in (up, lo, foot) if b in sk.rest]
        fk_q = {b: sk.rot[b].copy() for b in chain}
        solve_leg(sk, side, e, report)
        if w > 1e-4:
            for b in chain:
                q = sk.rot[b]
                if q.dot(fk_q[b]) < 0:
                    q = -q
                sk.rot[b] = q.slerp(fk_q[b], w)
            sk.fk()
    return sk


def _limit_cost(sk, bones):
    """0 inside the limits, grows with the degrees outside them."""
    c = 0.0
    for b in bones:
        lim = sk.meta.get(b, {}).get("limits")
        if not lim:
            continue
        for k, v in zip(ANAT_KEYS, sk.get_anat(b)):
            if k in lim:
                lo, hi = lim[k]
                if v < lo:
                    c += (lo - v)
                elif v > hi:
                    c += (v - hi)
    return c


def solve_arm_goal(sk, side, e, aim=None, report=None):
    """Arm IK that respects the joints: the wrist goes to the reach target and a held prop points along `aim`.
    Searches the elbow swivel (rotation of the elbow around the shoulder-wrist line) and the forearm twist,
    keeping the one with no limit violation, the straightest wrist and the most natural elbow (down / out).
    Returns the anatomical FK angles of UpperArm / LowerArm / Hand so clips can interpolate them (arcs)."""
    up, lo, hand = ARM_CHAINS[side]
    sx = 1.0 if side == "L" else -1.0
    par = sk.parent[up]
    R_frame = (sk.pose[par].to_3x3() @ sk.rest[par].to_3x3().inverted()) if par else Matrix.Identity(3)
    H = sk.head(up)
    L1 = (sk.head(lo) - H).length
    L2 = sk.length[lo]
    az, el, ext = e["reach"]
    az_r, el_r = az * C.D2R, el * C.D2R
    d = Vector((sx * math.sin(az_r) * math.cos(el_r), -math.cos(az_r) * math.cos(el_r), math.sin(el_r)))
    T = H + R_frame @ d * (ext * (L1 + L2))
    u = (T - H).normalized()
    pref = R_frame @ Vector((sx * 0.45, 0.35, -0.8)).normalized()  # a relaxed elbow points down, a bit out and back
    if "elbow" in e:
        ev = Vector(e["elbow"])
        pref = R_frame @ Vector((sx * ev.x, ev.y, ev.z)).normalized()
    r1 = pref - u * pref.dot(u)
    if r1.length < 1e-6:
        r1 = u.orthogonal()
    r1.normalize()
    r2 = u.cross(r1)
    hand_fk = e.get("hand", {})
    q_hand = SK.anat_quat(hand_fk.get("flex", 0), hand_fk.get("side", 0), hand_fk.get("twist", 0), SK.side_sign(hand))
    flim = sk.meta.get(lo, {}).get("limits", {}).get("twist", (-90, 90))
    R_p = R_rel = None
    if aim is not None:
        R_p = SK.frame(aim["aim"], aim.get("front", (0, 0, 1)))
        R_rel = sk.rest_rel[aim["_bone"]].to_3x3()
    best = None
    state0 = sk.copy_state()
    for psi_deg in range(-180, 180, 12):
        psi = psi_deg * C.D2R
        pole = H + u * (0.5 * (L1 + L2)) + (r1 * math.cos(psi) + r2 * math.sin(psi)) * L1
        sk.set_state(state0)
        sk.ik2(up, lo, T, pole)
        elbow_dir = (sk.head(lo) - H) - u * (sk.head(lo) - H).dot(u)
        nat = (1.0 - elbow_dir.normalized().dot(r1)) if elbow_dir.length > 1e-6 else 1.0
        ff, fs, ft0 = sk.get_anat(lo)
        twists = range(int(flim[0]), int(flim[1]) + 1, 10) if aim is not None else [ft0]
        for tw in twists:
            sk.set_anat(lo, ff, fs, tw)
            if aim is not None:
                sk.set_world_rot(hand, R_p @ R_rel.inverted())
            else:
                sk.rot[hand] = q_hand
                sk.fk()
            hf, hs, _ = sk.get_anat(hand)
            ut = sk.get_anat(up)[2]
            cost = (10.0 * _limit_cost(sk, (up, lo, hand)) + (hf * hf + hs * hs) / 1600.0 + (ut / 60.0) ** 2 * 1.0
                    + (tw / 90.0) ** 2 * 0.1 + 0.6 * nat)
            if best is None or cost < best[0]:
                best = (cost, psi_deg, tw, sk.copy_state())
    sk.set_state(best[3])
    if report is not None and _limit_cost(sk, (up, lo, hand)) > 0.5:
        report.append(("ARM_LIMIT", "arm_" + side, round(_limit_cost(sk, (up, lo, hand)), 1)))
    out = {}
    for b in (up, lo, hand):
        f_, s_, t_ = sk.get_anat(b)
        out[b] = {"flex": round(f_, 2), "side": round(s_, 2), "twist": round(t_, 2)}
    return out


def solve_goals(sk, pose, report=None):
    """Turn IK goals of a key pose ("arm_X" with "reach", "Prop_*" with "aim") into FK angles.
    Clips call this once per key, then interpolate the FK angles: the hands travel on arcs and nothing pops
    between frames because the solver never picks a different solution mid-motion. Legs stay IK (planted)."""
    goals = {k: v for k, v in pose.items() if k.startswith("arm_") and isinstance(v, dict) and "reach" in v}
    aims = {k: v for k, v in pose.items() if k.startswith("Prop_") and isinstance(v, dict) and "aim" in v}
    if not goals and not aims:
        return pose
    out = {k: v for k, v in pose.items() if k not in goals}
    apply(sk, {k: v for k, v in pose.items() if not k.startswith("arm_") and k not in aims}, report)
    for k, e in goals.items():
        side = k[-1]
        hand = ARM_CHAINS[side][2]
        aim = None
        for pn, pe in aims.items():
            if sk.parent.get(pn) == hand:
                aim = dict(pe)
                aim["_bone"] = pn
        fk = solve_arm_goal(sk, side, e, aim, report)
        for b, ang in fk.items():
            out[b] = ang
        for pn in list(aims):
            if sk.parent.get(pn) == hand:
                out[pn] = {kk: vv for kk, vv in aims[pn].items() if kk not in ("aim", "front")}
                if not out[pn]:
                    del out[pn]
    return out


def solve_leg(sk, side, e, report=None):
    up, lo, foot, toes = LEG_CHAINS[side]
    has_foot = foot in sk.rest
    ankle_rest = sk.rest[lo] @ Vector((0, sk.length[lo], 0))
    off = Vector(e.get("offset", (0, 0, 0)))
    yaw = e.get("yaw", 0.0) * C.D2R
    roll = e.get("roll", 0.0) * C.D2R
    Rz = Matrix.Rotation(yaw, 3, "Z")
    foot_rest3 = sk.rest[foot].to_3x3() if has_foot else Matrix.Identity(3)
    # foot orientation in the pose: rest orientation turned by yaw, then rolled about the foot's flex axis
    flex_axis = (Rz @ foot_rest3).col[0].normalized() if has_foot else Vector((1, 0, 0))
    Rroll = Matrix.Rotation(roll, 3, flex_axis)
    foot_R = Rroll @ Rz @ foot_rest3
    ankle = ankle_rest + off
    # pivot of the roll: -1 heel, 0 ankle, +1 toe (numeric so clips interpolate it; strings still accepted)
    pv = e.get("pivot", 0.0)
    pv = {"heel": -1.0, "ankle": 0.0, "toe": 1.0}.get(pv, pv) if isinstance(pv, str) else float(pv)
    if has_foot and roll != 0.0 and abs(pv) > 1e-4:
        L = sk.length[foot]
        sole = getattr(sk, "sole", {}).get(foot)
        if sole:
            # pivots on the real sole: the front-most and back-most of its lowest points (rest, world space)
            R0 = sk.rest[foot].to_3x3()
            world = [ankle_rest + R0 @ p for p in sole]
            fwd0 = (R0.col[1] * Vector((1, 1, 0))).normalized()
            toe_rest = max(world, key=lambda q: q.dot(fwd0))
            heel_rest = min(world, key=lambda q: q.dot(fwd0))
        else:
            toe_rest = sk.rest[foot] @ Vector((0, L, 0))
            heel_rest = ankle_rest + (ankle_rest - toe_rest) * 0.35
            heel_rest.z = min(heel_rest.z, toe_rest.z)
        piv_rest = ankle_rest.lerp(toe_rest, pv) if pv > 0 else ankle_rest.lerp(heel_rest, -pv)
        piv = Rz @ (piv_rest - ankle_rest) + ankle_rest + off
        ankle = piv + Rroll @ Rz @ (ankle_rest - piv_rest)
    # knee points along the foot direction (+ nudge)
    fwd = (foot_R.col[1] if has_foot else Vector((0, -1, 0))).copy()
    fwd.z = 0
    if fwd.length < 1e-6:
        fwd = Vector((0, -1, 0))
    fwd.normalize()
    H = sk.head(up)
    pole = (H + ankle) * 0.5 + fwd * (sk.length[up] + sk.length[lo]) + Vector(e.get("knee", (0, 0, 0)))
    sk.ik2(up, lo, ankle, pole, report, "leg_" + side)
    if has_foot:
        # "follow" 0..1: 0 = foot flat on the ground (world), 1 = foot hangs from the shin at its rest angle
        # (a lifted foot relaxes; keeping it flat under a folded knee over-bends the ankle)
        w = float(e.get("follow", 0.0))
        if w > 1e-4:
            q_flat = foot_R.to_quaternion()
            q_hang = (sk.pose[lo].to_3x3() @ sk.rest_rel[foot].to_3x3()).to_quaternion()
            if q_flat.dot(q_hang) < 0:
                q_hang = -q_hang
            foot_R = q_flat.slerp(q_hang, min(w, 1.0)).to_matrix()
        # ground clearance: a hanging foot under a folded knee points its toes into the floor (short legs do
        # this at 3-4 cm of lift); pitch it up around the ankle just enough to keep toes and heel above z = 0
        if w > 1e-4 or off.z > 0.002:
            A = sk.tail(lo)
            L = sk.length[foot]
            ax = foot_R.col[0].normalized()
            sole = getattr(sk, "sole", {}).get(foot)  # real sole points of the mesh, in the foot's rest frame
            if sole:
                R0 = sk.rest[foot].to_3x3()
                pts = [R0 @ p for p in sole]           # rest orientation, relative to the ankle
            def lowest(Rt):
                if sole:
                    Rd = Rt @ R0.inverted()
                    return min((A + Rd @ p).z for p in pts)
                return min((A + Rt.col[1] * L).z, (A - Rt.col[1] * (0.35 * L)).z)

            # only when the sole really goes into the floor; smallest pitch either way, 36 deg at most
            if lowest(foot_R) < -0.002:
                best = None
                for step in range(1, 13):
                    for sgn in (1.0, -1.0):
                        Rt = Matrix.Rotation(math.radians(3.0 * step * sgn), 3, ax) @ foot_R
                        lw = lowest(Rt)
                        if lw >= -0.002:
                            best = Rt
                            break
                        if best is None or lw > lowest(best):
                            best = Rt if best is None else best
                    if best is not None and lowest(best) >= -0.002:
                        break
                if best is not None and lowest(best) > lowest(foot_R):
                    foot_R = best
        sk.set_world_rot(foot, foot_R)
        if toes in sk.rest and "toes" in e:
            sk.set_anat(toes, e["toes"], 0, 0)
        # A flat foot under a knee that travels far forward over-bends the ankle. People lift the heel; that
        # helps long legs but makes SHORT legs worse (the ankle rises, the knee folds more, the shin tilts more:
        # measured on a chibi). So try a few heel lifts and keep the least bad one. Only when the pose did not
        # ask for its own roll.
        lim = sk.meta.get(foot, {}).get("limits", {}).get("flex")
        on_ground = off.z < 0.005 and float(e.get("follow", 0.0)) < 0.05
        if lim and on_ground and e.get("auto_heel", True) and not e.get("roll") and not e.get("_heel_pass"):
            flex = sk.get_anat(foot)[0]
            if flex > lim[1] - 2.0:
                best = (_limit_cost(sk, (up, lo, foot)), sk.copy_state())
                for r in (-6.0, -12.0, -18.0, -26.0):
                    e2 = dict(e)
                    e2.update({"roll": r, "pivot": "toe", "_heel_pass": True})
                    solve_leg(sk, side, e2, report)
                    c = _limit_cost(sk, (up, lo, foot))
                    if c < best[0] - 0.5:
                        best = (c, sk.copy_state())
                sk.set_state(best[1])


def limit_violations(sk, tol=1.0):
    """[(bone, axis, value, (lo, hi))] for every bone whose local rotation leaves its anatomical limits."""
    out = []
    for n in sk.order:
        lim = sk.meta.get(n, {}).get("limits")
        if not lim:
            continue
        vals = dict(zip(ANAT_KEYS, sk.get_anat(n)))
        for k in ANAT_KEYS:
            if k not in lim:
                continue
            lo, hi = lim[k]
            v = vals[k]
            if v < lo - tol or v > hi + tol:
                out.append((n, k, round(v, 1), (lo, hi)))
    return out


def merge(base, over):
    """Deep merge of two pose dicts (over wins)."""
    out = {}
    for k in set(base) | set(over):
        a, b = base.get(k), over.get(k)
        if isinstance(a, dict) and isinstance(b, dict):
            out[k] = merge(a, b)
        else:
            out[k] = b if b is not None else a
    return out


def mirror(pose):
    """Left <-> right mirrored pose (side/twist signs are handled by the mirrored bone frames)."""
    def swap(k):
        for a, b in (("_L", "_R"), ("_R", "_L")):
            if k.endswith(a):
                return k[: -len(a)] + b
        return k
    out = {}
    for k, v in pose.items():
        nk = swap(k)
        if isinstance(v, dict):
            v = dict(v)
            if "offset" in v:
                v["offset"] = [-v["offset"][0], v["offset"][1], v["offset"][2]]
            if "loc" in v:
                v["loc"] = [-v["loc"][0], v["loc"][1], v["loc"][2]]
            if nk in ("Hips", "Spine", "Chest", "UpperChest", "Neck", "Head") or not (k.endswith("_L") or k.endswith("_R")):
                for key in ("side", "twist", "yaw"):
                    if key in v:
                        v[key] = -v[key]
            elif "yaw" in v:
                v["yaw"] = -v["yaw"]
        out[nk] = v
    return out
