"""rig: build the armature from a rig spec (JSON), place held props in the hands, store joint metadata.

  blender -b prepared.blend --python rk.py -- rig --spec rig_spec.json --out rigged.blend [--png check.png]

Spec (see reference/rig-spec.md):
{
  "armature": "Rig_Hero", "mirror_x": true, "limits": "humanoid",
  "skin_meshes": ["body"],                         # used for the inside-the-mesh joint test
  "bones": [ {"name": "Hips", "head": [x,y,z], "tail": [x,y,z], "parent": "Root",
              "bend": [0,-1,0], "deform": true, "connect": false, "prebend": 0}, ... ],
  "props": [ {"object": "axe", "bone": "Prop_Axe", "hand": "Hand_R", "grip": {...}, "hold": {...}} ]
}
Only the left (_L) and centre bones are listed when mirror_x is true; *_R bones are generated.
"""
import argparse
import json
import os

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

from . import common as C
from . import skeleton as SK

# Anatomical limits in degrees from the T-pose rest (flex, side, twist); see skeleton.py for the signs.
LIMITS = {
    "humanoid": {
        "Spine": {"flex": (-25, 40), "side": (-25, 25), "twist": (-30, 30)},
        "Chest": {"flex": (-25, 40), "side": (-25, 25), "twist": (-30, 30)},
        "UpperChest": {"flex": (-15, 25), "side": (-15, 15), "twist": (-20, 20)},
        "Neck": {"flex": (-40, 50), "side": (-35, 35), "twist": (-60, 60)},
        "Head": {"flex": (-40, 45), "side": (-30, 30), "twist": (-60, 60)},
        "Shoulder": {"flex": (-15, 20), "side": (-10, 30), "twist": (-10, 10)},
        "UpperArm": {"flex": (-60, 130), "side": (-95, 100), "twist": (-90, 90)},
        "LowerArm": {"flex": (-5, 150), "side": (-8, 8), "twist": (-90, 90)},
        "Hand": {"flex": (-70, 80), "side": (-30, 30), "twist": (-25, 25)},
        "UpperLeg": {"flex": (-30, 120), "side": (-25, 60), "twist": (-40, 40)},
        "LowerLeg": {"flex": (-3, 150), "side": (-5, 5), "twist": (-15, 15)},
        "Foot": {"flex": (-50, 30), "side": (-25, 25), "twist": (-20, 20)},
        "Toes": {"flex": (-40, 60), "side": (-5, 5), "twist": (-5, 5)},
    },
}
# chibi: same joints, the body is in the way sooner; collisions are checked separately (check_anim)
LIMITS["chibi"] = dict(LIMITS["humanoid"])
LIMITS["chibi"].update({
    "Spine": {"flex": (-15, 25), "side": (-15, 15), "twist": (-25, 25)},
    "Chest": {"flex": (-15, 25), "side": (-15, 15), "twist": (-25, 25)},
    "Head": {"flex": (-25, 30), "side": (-20, 20), "twist": (-45, 45)},
    "Neck": {"flex": (-30, 35), "side": (-25, 25), "twist": (-45, 45)},
    # short arms on a round body: below -40 they sink into it, big twists tear the armpit (found on a chibi, see
    # reference/failure-catalog.md)
    "UpperArm": {"flex": (-45, 90), "side": (-40, 100), "twist": (-60, 60)},   # ROM: 99 deg forward tears
    "UpperLeg": {"flex": (-25, 60), "side": (-15, 35), "twist": (-30, 30)},
    "LowerLeg": {"flex": (-3, 110), "side": (-5, 5), "twist": (-10, 10)},
    # feet as long as the legs barely lift in a run: the ankle folds up to 40 deg (boots stay clean in the
    # per-frame TEAR / FLIP check on that chibi); human feet stay at 30
    "Foot": {"flex": (-50, 40), "side": (-25, 25), "twist": (-20, 20)},
})


def base_name(n):
    for suf in ("_L", "_R"):
        if n.endswith(suf):
            return n[:-2]
    return n


def mirror_name(n):
    if n.endswith("_L"):
        return n[:-2] + "_R"
    if n.endswith("_R"):
        return n[:-2] + "_L"
    return n


def expand_mirror(spec):
    out = []
    for b in spec["bones"]:
        out.append(dict(b))
    if spec.get("mirror_x", True):
        names = {b["name"] for b in out}
        for b in list(out):
            if b["name"].endswith("_L") and mirror_name(b["name"]) not in names:
                m = dict(b)
                m["name"] = mirror_name(b["name"])
                m["head"] = [-b["head"][0], b["head"][1], b["head"][2]]
                m["tail"] = [-b["tail"][0], b["tail"][1], b["tail"][2]]
                if b.get("parent"):
                    m["parent"] = mirror_name(b["parent"])
                if b.get("bend"):
                    m["bend"] = [-b["bend"][0], b["bend"][1], b["bend"][2]]
                out.append(m)
    return out


def roll_from_bend(eb, bend):
    """Set the roll so that the bone's local X axis is the flex axis: rotating +X moves the tail towards bend."""
    y = (eb.tail - eb.head).normalized()
    b = Vector(bend)
    x = y.cross(b)
    if x.length < 1e-6:
        return False
    x.normalize()
    z = x.cross(y)
    eb.align_roll(z)
    return True


def inside_test(bvh, p, scale):
    """True when p is enclosed by the mesh in the 6 axis directions (works on meshes with small holes)."""
    hits = 0
    for d in ((1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)):
        loc, nor, idx, dist = bvh.ray_cast(Vector(p), Vector(d), scale * 10)
        if loc is not None:
            hits += 1
    return hits >= 5


def build(spec, rep):
    C.force_object_mode()
    name = spec.get("armature", "Rig")
    old = bpy.data.objects.get(name)
    if old is not None:
        C.remove_object(old)
    arm_data = bpy.data.armatures.new(name)
    arm_data.display_type = "OCTAHEDRAL"
    arm_ob = bpy.data.objects.new(name, arm_data)
    bpy.context.scene.collection.objects.link(arm_ob)
    arm_ob.show_in_front = True
    bones = expand_mirror(spec)
    C.select_only([arm_ob], arm_ob)
    with C.ctx(arm_ob, [arm_ob]):
        bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones
    for b in bones:
        e = eb.new(b["name"])
        e.head = b["head"]
        e.tail = b["tail"]
        e.use_deform = b.get("deform", True)
        e.inherit_scale = "FULL"
        e.use_inherit_rotation = True
    for b in bones:
        e = eb[b["name"]]
        if b.get("parent"):
            if b["parent"] not in eb:
                rep.error("PARENT", "%s: parent %s does not exist" % (b["name"], b["parent"]))
                continue
            e.parent = eb[b["parent"]]
            e.use_connect = bool(b.get("connect", False)) and (e.parent.tail - e.head).length < 1e-5
    # pre-bend the middle joint of a limb chain (knee forward, elbow back) so the bend plane is defined
    for b in bones:
        pb = float(b.get("prebend", 0))
        if pb and b.get("parent") and b.get("bend"):
            e = eb[b["name"]]
            par = e.parent
            pole = -Vector(b["bend"])  # the joint points away from where the child folds
            off = pole.normalized() * (par.length * np.sin(np.radians(pb)))
            e.head = e.head + off
            par.tail = e.head  # the parent ends where the bent joint starts
    for b in bones:
        e = eb[b["name"]]
        if b.get("bend"):
            if not roll_from_bend(e, b["bend"]):
                rep.error("BEND", "%s: bend vector is parallel to the bone" % b["name"])
        elif b.get("roll_z"):
            e.align_roll(Vector(b["roll_z"]))
        else:
            e.roll = 0.0
    with C.ctx(arm_ob, [arm_ob]):
        bpy.ops.object.mode_set(mode="OBJECT")
    # collections
    for cname in ("Deform", "Props", "Helpers"):
        if cname not in arm_data.collections:
            arm_data.collections.new(cname)
    for bone in arm_data.bones:
        cname = "Props" if bone.name.startswith("Prop_") else ("Deform" if bone.use_deform else "Helpers")
        arm_data.collections[cname].assign(bone)
    # metadata for the pose engine and the checks
    table = LIMITS.get(spec.get("limits", "humanoid"), LIMITS["humanoid"])
    meta = {}
    for b in bones:
        m = {"bend": b.get("bend"), "side": "R" if b["name"].endswith("_R") else ("L" if b["name"].endswith("_L") else "C")}
        lim = b.get("limits") or table.get(base_name(b["name"]))
        if lim:
            m["limits"] = lim
        meta[b["name"]] = m
    arm_ob["rk_meta"] = json.dumps(meta)
    arm_ob["rk_spec"] = json.dumps(spec)
    return arm_ob


def place_props(arm_ob, spec, rep):
    props = spec.get("props", [])
    if not props:
        return
    sk = SK.Skeleton(arm_ob)
    for pr in props:
        ob = bpy.data.objects.get(pr["object"])
        if ob is None:
            rep.error("PROP", "prop object %s not found" % pr["object"])
            continue
        g = pr["grip"]
        hold = pr.get("hold", {})
        hand = pr["hand"]
        sk.reset()
        for bn, ang in hold.get("pose", {}).items():
            sk.set_anat(bn, ang.get("flex", 0), ang.get("side", 0), ang.get("twist", 0))
        Mh = sk.pose[hand].copy()
        fist = float(hold.get("fist", 0.45))
        target = Mh @ Vector((0, sk.length[hand] * fist, 0)) + Vector(hold.get("offset", (0, 0, 0)))
        if isinstance(g["point"], dict):
            # centroid of the prop vertices inside a box, e.g. the part of the handle the fist closes on
            lo, hi = np.array(g["point"]["box"][0]), np.array(g["point"]["box"][1])
            co = C.world_coords(ob)
            sel = co[np.all((co >= lo) & (co <= hi), axis=1)]
            if len(sel) == 0:
                rep.error("GRIP", "%s: no vertex inside the grip box" % pr["object"])
                continue
            gp = Vector(sel.mean(0).tolist())
        else:
            gp = Vector(g["point"])
        rep.info.setdefault("grip_points", {})[pr["object"]] = C.r(gp)
        Rp = SK.frame(g["axis"], g["front"])
        Rw = SK.frame(hold["axis"], hold["front"])
        R = Rw @ Rp.transposed()
        M_pose = Matrix.Translation(target) @ R.to_4x4() @ Matrix.Translation(-gp)
        M_rest = sk.rest[hand] @ Mh.inverted() @ M_pose
        ob.data.transform(M_rest)
        ob.data.update()
        # the prop bone sits on the grip, pointing along the prop axis
        head = M_rest @ gp
        axis_w = (M_rest.to_3x3() @ Vector(g["axis"])).normalized()
        front_w = (M_rest.to_3x3() @ Vector(g["front"])).normalized()
        L = float(pr.get("bone_length", sk.length[hand]))
        C.select_only([arm_ob], arm_ob)
        with C.ctx(arm_ob, [arm_ob]):
            bpy.ops.object.mode_set(mode="EDIT")
        eb = arm_ob.data.edit_bones
        e = eb.get(pr["bone"]) or eb.new(pr["bone"])
        e.head = head
        e.tail = head + axis_w * L
        e.parent = eb[hand]
        e.use_connect = False
        e.use_deform = bool(pr.get("deform", True))
        e.align_roll(front_w.cross(axis_w))
        with C.ctx(arm_ob, [arm_ob]):
            bpy.ops.object.mode_set(mode="OBJECT")
        arm_ob.data.collections["Props"].assign(arm_ob.data.bones[pr["bone"]])
        meta = json.loads(arm_ob["rk_meta"])
        meta[pr["bone"]] = {"side": "C", "prop_of": hand, "object": pr["object"]}
        arm_ob["rk_meta"] = json.dumps(meta)
        rep.info.setdefault("props", {})[pr["object"]] = {"bone": pr["bone"], "grip_world_rest": C.r(head)}
    # static props (hat, backpack): only a bone, the mesh stays where it is
    for sp in spec.get("static_props", []):
        C.select_only([arm_ob], arm_ob)
        with C.ctx(arm_ob, [arm_ob]):
            bpy.ops.object.mode_set(mode="EDIT")
        eb = arm_ob.data.edit_bones
        e = eb.get(sp["bone"]) or eb.new(sp["bone"])
        e.head = sp["head"]
        e.tail = sp["tail"]
        e.parent = eb[sp["parent"]]
        e.use_deform = True
        e.align_roll(Vector(sp.get("roll_z", (0, -1, 0))))
        with C.ctx(arm_ob, [arm_ob]):
            bpy.ops.object.mode_set(mode="OBJECT")
        arm_ob.data.collections["Props"].assign(arm_ob.data.bones[sp["bone"]])
        meta = json.loads(arm_ob["rk_meta"])
        meta[sp["bone"]] = {"side": "C", "prop_of": sp["parent"], "object": sp.get("object")}
        arm_ob["rk_meta"] = json.dumps(meta)


def check_rig(arm_ob, spec, rep):
    bones = arm_ob.data.bones
    names = {b.name for b in bones}
    for b in bones:
        if b.length < 1e-4:
            rep.error("ZERO_BONE", "%s has zero length" % b.name)
        if b.name.endswith("_L") and mirror_name(b.name) not in names:
            rep.error("PAIR", "%s has no _R partner" % b.name)
        if " " in b.name or "." in b.name or ":" in b.name:
            rep.error("NAME", "%s: no spaces, dots or colons in bone names (QuyChuan 4.1)" % b.name)
        if b.name.endswith("_end"):
            rep.error("LEAF", "%s: no _end leaf bones" % b.name)
    if "Root" in names and bones["Root"].use_deform:
        rep.error("ROOT_DEFORM", "Root must not deform")
    # mirrored pairs must be mirror images (position and roll)
    for b in bones:
        if b.name.endswith("_L") and mirror_name(b.name) in names:
            o = bones[mirror_name(b.name)]
            mh = Vector((-b.head_local.x, b.head_local.y, b.head_local.z))
            if (mh - o.head_local).length > 1e-4:
                rep.error("MIRROR", "%s / %s heads are not mirrored" % (b.name, o.name))
            xl = b.matrix_local.to_3x3().col[0]
            xr = o.matrix_local.to_3x3().col[0]
            if (Vector((xl.x, -xl.y, -xl.z)) - xr).length > 1e-3:
                rep.warn("MIRROR_ROLL", "%s / %s rolls are not mirrored" % (b.name, o.name))
    # joints inside the skin
    meshes = [bpy.data.objects[n] for n in spec.get("skin_meshes", []) if n in bpy.data.objects]
    if meshes:
        pts = np.concatenate([C.world_coords(m) for m in meshes])
        height = float(pts[:, 2].max() - pts[:, 2].min())
        for m in meshes:
            me = m.data
            bvh = BVHTree.FromPolygons([tuple(v) for v in C.world_coords(m)], [tuple(p.vertices) for p in me.polygons])
            for b in bones:
                if not b.use_deform or b.name.startswith("Prop_"):
                    continue
                for label, p in (("head", b.head_local), ("tail", b.tail_local)):
                    if b.children and label == "tail":
                        continue
                    if not inside_test(bvh, p, height):
                        rep.warn("OUTSIDE", "%s %s %s is outside %s: bone heat and deformation will be poor"
                                 % (b.name, label, C.r(p, 3), m.name))
    rep.info["bones"] = len(bones)
    rep.info["deform_bones"] = sum(1 for b in bones if b.use_deform)


def main(argv):
    ap = argparse.ArgumentParser(prog="rig")
    ap.add_argument("--spec", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--png", default=None)
    a = ap.parse_args(argv)
    spec = C.load_json(a.spec)
    rep = C.Report("rig")
    arm = build(spec, rep)
    place_props(arm, spec, rep)
    check_rig(arm, spec, rep)
    if a.png:
        from . import review as RV
        RV.bone_sticks(arm)
        RV.sheet([lambda: None], ["front", "side"], a.png, size=720, xray=0.4)
        RV.clear_prefix()
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(a.out), copy=True)
    rep.info["saved"] = os.path.abspath(a.out)
    return rep.dump(os.path.splitext(a.out)[0] + "_rig.json")
