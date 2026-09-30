"""Shared helpers for rigkit (Blender 4.2+ headless).

Conventions used everywhere in rigkit:
  * Blender world space, +Z up, the character faces -Y, the character's LEFT side is +X.
  * Bone names follow GameAnimation_QuyChuan 5.2 (Root, Hips, Spine, Chest, Neck, Head, UpperArm_L ...).
  * Reports are JSON files; every check returns {"ok": bool, "errors": [...], "warnings": [...], ...}.
"""
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Quaternion, Vector

D2R = math.pi / 180.0
R2D = 180.0 / math.pi


# --------------------------------------------------------------------------- args / io
def script_args():
    """Arguments after the first '--' on the Blender command line."""
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def load_json(path):
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def save_json(path, data):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1, ensure_ascii=False, default=_json_default)


def _json_default(o):
    if isinstance(o, (np.floating,)):
        return float(o)
    if isinstance(o, (np.integer,)):
        return int(o)
    if isinstance(o, (Vector, Quaternion)):
        return [round(float(v), 5) for v in o]
    if isinstance(o, np.ndarray):
        return o.tolist()
    raise TypeError(type(o))


def r(v, n=4):
    """Round a float or a vector for reports."""
    if isinstance(v, (int, float, np.floating)):
        return round(float(v), n)
    return [round(float(x), n) for x in v]


class Report:
    """Collects errors / warnings / info for one step and prints a compact summary."""

    def __init__(self, step):
        self.step = step
        self.errors = []
        self.warnings = []
        self.info = {}

    def error(self, code, msg, **data):
        self.errors.append(dict(code=code, msg=msg, **data))

    def warn(self, code, msg, **data):
        self.warnings.append(dict(code=code, msg=msg, **data))

    @property
    def ok(self):
        return not self.errors

    def as_dict(self):
        return {"step": self.step, "ok": self.ok, "errors": self.errors, "warnings": self.warnings,
                "info": self.info}

    def dump(self, path=None):
        if path:
            save_json(path, self.as_dict())
        tag = "OK" if self.ok else "FAIL"
        print("[rigkit:%s] %s  errors=%d warnings=%d" % (self.step, tag, len(self.errors), len(self.warnings)))
        for e in self.errors[:40]:
            print("  ERROR %s: %s" % (e["code"], e["msg"]))
        for w in self.warnings[:40]:
            print("  warn  %s: %s" % (w["code"], w["msg"]))
        return self.ok


# --------------------------------------------------------------------------- scene state
def force_object_mode():
    """A file saved in Edit Mode keeps an edit-mesh that overwrites mesh-data changes: leave every mode first."""
    for ob in list(bpy.data.objects):
        if ob.mode != "OBJECT" and ob.name in bpy.context.view_layer.objects:
            bpy.context.view_layer.objects.active = ob
            with bpy.context.temp_override(active_object=ob, object=ob):
                bpy.ops.object.mode_set(mode="OBJECT")


def select_only(objs, active=None):
    for ob in list(bpy.context.view_layer.objects):
        if ob is not None:
            ob.select_set(False)
    for ob in objs:
        ob.select_set(True)
    if active is not None:
        bpy.context.view_layer.objects.active = active


def ctx(active, selected):
    """Context override for operators that need an active object and a selection."""
    return bpy.context.temp_override(active_object=active, object=active, selected_objects=list(selected),
                                     selected_editable_objects=list(selected))


def link_to_scene(ob, like=None):
    coll = like.users_collection[0] if like is not None and like.users_collection else bpy.context.scene.collection
    coll.objects.link(ob)
    return ob


def remove_object(ob):
    data = ob.data
    bpy.data.objects.remove(ob, do_unlink=True)
    if data is not None and getattr(data, "users", 1) == 0:
        if isinstance(data, bpy.types.Mesh):
            bpy.data.meshes.remove(data)
        elif isinstance(data, bpy.types.Armature):
            bpy.data.armatures.remove(data)


# --------------------------------------------------------------------------- mesh arrays
def mesh_coords(me):
    co = np.empty(len(me.vertices) * 3, dtype=np.float64)
    me.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


def world_coords(ob, evaluated=False):
    """Vertex positions in world space; evaluated=True applies modifiers (armature deform)."""
    if evaluated:
        dg = bpy.context.evaluated_depsgraph_get()
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        co = mesh_coords(me)
        mw = np.array(ev.matrix_world)
        ev.to_mesh_clear()
    else:
        co = mesh_coords(ob.data)
        mw = np.array(ob.matrix_world)
    return co @ mw[:3, :3].T + mw[:3, 3]


def edges_array(me):
    e = np.empty(len(me.edges) * 2, dtype=np.int64)
    me.edges.foreach_get("vertices", e)
    return e.reshape(-1, 2)


def triangles_array(me):
    me.calc_loop_triangles()
    t = np.empty(len(me.loop_triangles) * 3, dtype=np.int64)
    me.loop_triangles.foreach_get("vertices", t)
    return t.reshape(-1, 3)


def islands(me):
    """Connected components over edges. Returns (labels[n_verts], n_islands)."""
    n = len(me.vertices)
    parent = np.arange(n)

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    for a, b in edges_array(me):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[ra] = rb
    roots = np.array([find(i) for i in range(n)])
    _, labels = np.unique(roots, return_inverse=True)
    return labels, int(labels.max() + 1) if n else 0


def vertex_neighbors(me):
    """Adjacency list (list of numpy arrays) from mesh edges."""
    n = len(me.vertices)
    e = edges_array(me)
    nb = [[] for _ in range(n)]
    for a, b in e:
        nb[a].append(b)
        nb[b].append(a)
    return [np.array(x, dtype=np.int64) for x in nb]


# --------------------------------------------------------------------------- weights as arrays
def read_weights(ob, group_names=None):
    """Dense weight matrix W[n_verts, n_groups] for the object's vertex groups (or the given names)."""
    names = group_names or [vg.name for vg in ob.vertex_groups]
    col = {vg.index: names.index(vg.name) for vg in ob.vertex_groups if vg.name in names}
    W = np.zeros((len(ob.data.vertices), len(names)), dtype=np.float64)
    for v in ob.data.vertices:
        for g in v.groups:
            c = col.get(g.group)
            if c is not None:
                W[v.index, c] = g.weight
    return W, names


def write_weights(ob, W, names, eps=1e-6):
    """Replace the object's vertex groups listed in `names` with the dense matrix W."""
    for nm in names:
        vg = ob.vertex_groups.get(nm)
        if vg is not None:
            ob.vertex_groups.remove(vg)
    groups = [ob.vertex_groups.new(name=nm) for nm in names]
    for j, vg in enumerate(groups):
        col = W[:, j]
        nz = np.nonzero(col > eps)[0]
        # group vertices with identical weights to cut Python overhead
        if len(nz) == 0:
            continue
        vals = np.round(col[nz], 5)
        order = np.argsort(vals)
        vals, nz = vals[order], nz[order]
        start = 0
        for i in range(1, len(vals) + 1):
            if i == len(vals) or vals[i] != vals[start]:
                vg.add(nz[start:i].tolist(), float(vals[start]), "REPLACE")
                start = i
    return groups


# --------------------------------------------------------------------------- actions (slotted in 4.4+)
def action_fcurves(action, anim_id=None):
    """All F-Curves of an action, for both the legacy API and slotted actions (Blender 4.4+)."""
    if action is None:
        return []
    try:
        fcs = list(action.fcurves)
        if fcs:
            return fcs
    except (AttributeError, RuntimeError):
        pass
    out = []
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in getattr(strip, "channelbags", []):
                out += list(bag.fcurves)
    return out


def assign_action(ob, action):
    if ob.animation_data is None:
        ob.animation_data_create()
    ob.animation_data.action = action
    # Blender 4.4+: an action needs a slot for the object
    if hasattr(ob.animation_data, "action_slot") and ob.animation_data.action_slot is None:
        slots = getattr(action, "slots", None)
        if slots is not None and len(slots):
            ob.animation_data.action_slot = slots[0]


# --------------------------------------------------------------------------- math
def swing_twist(q, axis):
    """Split rotation q into swing (perpendicular to axis) and twist (around axis). Returns (swing, twist_angle_rad)."""
    axis = Vector(axis).normalized()
    p = Vector((q.x, q.y, q.z)).project(axis)
    tw = Quaternion((q.w, p.x, p.y, p.z))
    if tw.magnitude < 1e-9:
        tw = Quaternion()
    tw.normalize()
    sw = q @ tw.inverted()
    ang = 2.0 * math.atan2(Vector((tw.x, tw.y, tw.z)).dot(axis), tw.w)
    if ang > math.pi:
        ang -= 2 * math.pi
    if ang < -math.pi:
        ang += 2 * math.pi
    return sw, ang


def signed_angle(a, b, normal):
    a = Vector(a)
    b = Vector(b)
    ang = a.angle(b, 0.0)
    if Vector(normal).dot(a.cross(b)) < 0:
        ang = -ang
    return ang


def smoothstep(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3.0 - 2.0 * t)
