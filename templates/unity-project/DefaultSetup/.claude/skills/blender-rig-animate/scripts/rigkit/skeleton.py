"""Pure-Python FK / IK on an armature's rest data. No depsgraph, no constraints: deterministic and fast.

Bone frame convention (set by rig.py through the roll): every bone's local X axis is its FLEX axis, chosen so
that a positive rotation about X bends the joint the natural way ("bend" vector in the rig spec):
    Spine/Chest/Neck/Head, UpperArm, LowerArm, UpperLeg   bend forward (-Y)
    LowerLeg                                               bend backward (+Y)
    Foot, Toes, Shoulder                                   bend up (+Z)
Anatomical rotation of a bone = (flex, side, twist) in degrees, relative to the rest pose:
    flex  about local X  (positive = flexion: knee folds, elbow folds, spine leans forward, arm swings forward)
    side  about local Z  (positive = abduction: arm/leg away from the body; spine/head lean to the character's right)
    twist about local Y  (along the bone)
Right-side bones use mirrored frames, so side and twist are negated for *_R: the same numbers give a mirrored pose.
"""
import json
import math

from mathutils import Matrix, Quaternion, Vector

from . import common as C


def frame(y, x_hint):
    """Orthonormal 3x3 with Y along y and X as close as possible to x_hint (columns = axes)."""
    y = Vector(y).normalized()
    x = Vector(x_hint) - y * Vector(x_hint).dot(y)
    if x.length < 1e-8:
        x = y.orthogonal()
    x.normalize()
    z = x.cross(y)
    return Matrix((x, y, z)).transposed()


def side_sign(name):
    return -1.0 if name.endswith("_R") or name.endswith(".R") else 1.0


def anat_quat(flex=0.0, side=0.0, twist=0.0, sign=1.0):
    """Local rotation from anatomical angles (degrees). Swing (flex about X, side about Z) after twist about Y."""
    s = side * sign
    t = twist * sign
    sw = Vector((flex, 0.0, s)) * C.D2R
    ang = sw.length
    q_sw = Quaternion(sw.normalized(), ang) if ang > 1e-9 else Quaternion()
    q_tw = Quaternion((0, 1, 0), t * C.D2R)
    return q_sw @ q_tw


def quat_anat(q, sign=1.0):
    """Inverse of anat_quat: (flex, side, twist) in degrees."""
    sw, tw = C.swing_twist(q, (0, 1, 0))
    if sw.w < 0:
        sw = -sw
    ang = 2.0 * math.acos(max(-1.0, min(1.0, sw.w)))
    axis = Vector((sw.x, sw.y, sw.z))
    if axis.length > 1e-9 and ang > 1e-9:
        axis = axis.normalized() * ang
    else:
        axis = Vector((0, 0, 0))
    return axis.x * C.R2D, axis.z * C.R2D * sign, tw * C.R2D * sign


class Skeleton:
    def __init__(self, arm_ob):
        self.ob = arm_ob
        bones = arm_ob.data.bones
        order, seen = [], set()

        def visit(b):
            if b.name in seen:
                return
            if b.parent is not None:
                visit(b.parent)
            seen.add(b.name)
            order.append(b.name)

        for b in bones:
            visit(b)
        self.order = order
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in bones}
        self.rest = {b.name: b.matrix_local.copy() for b in bones}
        self.length = {b.name: b.length for b in bones}
        self.rest_rel = {}
        for n in order:
            p = self.parent[n]
            self.rest_rel[n] = (self.rest[p].inverted() @ self.rest[n]) if p else self.rest[n].copy()
        meta = arm_ob.get("rk_meta")
        self.meta = json.loads(meta) if isinstance(meta, str) else {}
        self.reset()

    # ------------------------------------------------------------------ state
    def reset(self):
        self.loc = {n: Vector((0, 0, 0)) for n in self.order}
        self.rot = {n: Quaternion() for n in self.order}
        self.scl = {n: Vector((1, 1, 1)) for n in self.order}
        self.pose = {}
        self.fk()

    def copy_state(self):
        return ({n: v.copy() for n, v in self.loc.items()}, {n: q.copy() for n, q in self.rot.items()},
                {n: v.copy() for n, v in self.scl.items()})

    def set_state(self, st):
        self.loc, self.rot, self.scl = ({n: v.copy() for n, v in st[0].items()}, {n: q.copy() for n, q in st[1].items()},
                                        {n: v.copy() for n, v in st[2].items()})
        self.fk()

    def basis(self, n):
        return Matrix.LocRotScale(self.loc[n], self.rot[n], self.scl[n])

    def fk(self):
        for n in self.order:
            p = self.parent[n]
            m = self.rest_rel[n] @ self.basis(n)
            self.pose[n] = (self.pose[p] @ m) if p else m
        return self.pose

    # ------------------------------------------------------------------ queries (armature space)
    def head(self, n):
        return self.pose[n].translation.copy()

    def tail(self, n):
        return self.pose[n] @ Vector((0, self.length[n], 0))

    def point(self, n, local):
        return self.pose[n] @ Vector(local)

    def axis(self, n, i):
        return self.pose[n].to_3x3().col[i].normalized()

    def rest_axis(self, n, i):
        return self.rest[n].to_3x3().col[i].normalized()

    # ------------------------------------------------------------------ setters
    def set_world_rot(self, n, R3):
        """Give bone n the armature-space orientation R3 (3x3), keeping its head where the parent puts it."""
        p = self.parent[n]
        parent_pose = self.pose[p] if p else Matrix.Identity(4)
        cur = parent_pose @ self.rest_rel[n] @ Matrix.Translation(self.loc[n])
        target = Matrix.LocRotScale(cur.translation, R3.to_quaternion(), Vector((1, 1, 1)))
        b = (parent_pose @ self.rest_rel[n]).inverted() @ target
        _, q, _ = b.decompose()
        self.rot[n] = q.normalized()
        self.fk()

    def set_anat(self, n, flex=0.0, side=0.0, twist=0.0):
        self.rot[n] = anat_quat(flex, side, twist, side_sign(n))
        self.fk()

    def get_anat(self, n):
        return quat_anat(self.rot[n], side_sign(n))

    def set_loc(self, n, world_offset):
        """Translate bone n by a world (armature) offset from its rest position relative to the parent."""
        p = self.parent[n]
        parent_pose = self.pose[p] if p else Matrix.Identity(4)
        m = parent_pose @ self.rest_rel[n]
        local = m.to_3x3().inverted() @ Vector(world_offset)
        self.loc[n] = local
        self.fk()

    def aim(self, n, direction, x_hint=None):
        """Point bone n along direction (armature space); x_hint keeps the flex axis (default: current X)."""
        xh = Vector(x_hint) if x_hint is not None else self.axis(n, 0)
        R = frame(direction, xh) @ frame(self.rest_axis(n, 1), self.rest_axis(n, 0)).transposed()
        self.set_world_rot(n, R @ self.rest[n].to_3x3())

    # ------------------------------------------------------------------ two-bone IK
    def ik2(self, upper, lower, target, pole, report=None, tag=""):
        """Place the tail of `lower` at target with the middle joint pointing towards pole (armature space).
        Bone lengths never change. Returns the reach ratio d / (L1 + L2) (1 = fully straight)."""
        self.fk()
        H = self.head(upper)
        L1 = (self.head(lower) - H).length
        L2 = self.length[lower]
        T = Vector(target)
        d_vec = T - H
        d = d_vec.length
        lo_d, hi_d = abs(L1 - L2) + 1e-6, (L1 + L2) * 0.9995
        reach = d / (L1 + L2)
        if d > hi_d or d < lo_d:
            if report is not None and d > (L1 + L2) * 1.02:
                report.append(("IK_REACH", tag, round(reach, 3)))
            d = min(max(d, lo_d), hi_d)
        u = d_vec.normalized() if d_vec.length > 1e-9 else Vector((0, 0, -1))
        w = Vector(pole) - H
        w = w - u * w.dot(u)
        if w.length < 1e-8:
            w = self.axis(lower, 2) if self.meta.get(lower, {}).get("bend") is None else Vector(self.meta[lower]["bend"])
            w = w - u * w.dot(u)
        w.normalize()
        a = (L1 * L1 - L2 * L2 + d * d) / (2 * d)
        h = math.sqrt(max(L1 * L1 - a * a, 0.0))
        K = H + u * a + w * h
        Tn = H + u * d
        n1 = w.cross(u).normalized()           # flex axis of the lower bone in the pose
        n0 = self.rest_axis(lower, 0)          # flex axis of the lower bone at rest
        for bone, y_new in ((upper, K - H), (lower, Tn - K)):
            R = frame(y_new, n1) @ frame(self.rest_axis(bone, 1), n0).transposed()
            self.set_world_rot(bone, R @ self.rest[bone].to_3x3())
        return reach

    # ------------------------------------------------------------------ write to Blender
    def apply(self, arm_ob=None):
        ob = arm_ob or self.ob
        for n in self.order:
            pb = ob.pose.bones[n]
            pb.rotation_mode = "QUATERNION"
            pb.location = self.loc[n]
            pb.rotation_quaternion = self.rot[n]
            pb.scale = self.scl[n]

    def read(self, arm_ob=None):
        ob = arm_ob or self.ob
        for n in self.order:
            pb = ob.pose.bones[n]
            self.loc[n] = pb.location.copy()
            self.rot[n] = (pb.rotation_quaternion.copy() if pb.rotation_mode == "QUATERNION"
                           else pb.rotation_euler.to_quaternion())
            self.scl[n] = pb.scale.copy()
        self.fk()
