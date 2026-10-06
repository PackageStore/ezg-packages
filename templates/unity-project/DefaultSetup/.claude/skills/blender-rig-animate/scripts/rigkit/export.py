"""export / verify_fbx: FBX for Unity (GameAnimation_QuyChuan 5.7) and a round-trip check.

  blender -b anim.blend --python rk.py -- export --out Model/Hero_Rig.fbx [--mode all|split] [--names quychuan|mixamo]
  blender -b --factory-startup --python rk.py -- verify_fbx --fbx Model/Hero_Rig.fbx --source anim.blend --out v.json

export
  * joins every mesh skinned to the armature into ONE skinned mesh (one SkinnedMeshRenderer, QuyChuan 5.3.6);
    the .blend keeps the separate objects (the join is never saved)
  * FBX Units Scale, Forward -Z / Up Y, Apply Transform OFF, Add Leaf Bones OFF, only deform bones (plus
    non-deform bones that carry deform children, e.g. Root, Shoulder), Bake Animation with Key All Bones,
    All Actions, Force Start/End Keying, Simplify 0
  * --mode split: <out> holds the rig + mesh and no animation, plus one <Subject>_<Action>.fbx per action
    (QuyChuan 4.2: one clip per file); --mode all: one file with every action as a take
  * --names mixamo: bones renamed mixamorig:* for projects whose masks / code expect Mixamo names
  * writes <out>.events.json: the AnimationEvents of every clip (Unity does not read them from the FBX)
verify_fbx
  * imports the FBX into an empty scene, compares bone names, takes, frame ranges and bone positions of every
    take with the source .blend (max deviation per take), and reports leaf bones / extra meshes
  * every imported bone is unconnected first (rest kept): the importer connects a lone child to its parent's tail
    and a connected bone ignores its location in Blender, while Unity plays the full TRS (a dropped prop read ~1 m off)
"""
import argparse
import json
import os

import bpy

from . import common as C

TO_MIXAMO = {"Hips": "Hips", "Spine": "Spine", "Chest": "Spine1", "UpperChest": "Spine2", "Neck": "Neck",
             "Head": "Head", "Shoulder": "Shoulder", "UpperArm": "Arm", "LowerArm": "ForeArm", "Hand": "Hand",
             "UpperLeg": "UpLeg", "LowerLeg": "Leg", "Foot": "Foot", "Toes": "ToeBase", "Eye": "Eye"}


def mixamo_name(n):
    side = ""
    core = n
    if n.endswith("_L"):
        side, core = "Left", n[:-2]
    elif n.endswith("_R"):
        side, core = "Right", n[:-2]
    if core in TO_MIXAMO:
        return "mixamorig:" + side + TO_MIXAMO[core]
    return n


def skinned_meshes(arm):
    return [o for o in bpy.data.objects if o.type == "MESH"
            and any(m.type == "ARMATURE" and m.object == arm for m in o.modifiers)]


def join_copies(meshes, name):
    copies = []
    for src in meshes:
        dup = src.copy()
        dup.data = src.data.copy()
        C.link_to_scene(dup, src)
        copies.append(dup)
    C.select_only(copies, copies[0])
    with C.ctx(copies[0], copies):
        bpy.ops.object.join()
    joined = copies[0]
    joined.name = name
    joined.data.name = name
    return joined


def fbx_export(path, arm, mesh, bake):
    C.select_only([arm] + ([mesh] if mesh else []), arm)
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with C.ctx(arm, [arm] + ([mesh] if mesh else [])):
        bpy.ops.export_scene.fbx(
            filepath=os.path.abspath(path), check_existing=False, use_selection=True,
            object_types={"ARMATURE", "MESH"}, global_scale=1.0, apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
            use_space_transform=True, bake_space_transform=False, use_mesh_modifiers=False,
            mesh_smooth_type="FACE", use_tspace=False, use_custom_props=False, add_leaf_bones=False,
            primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=True,
            armature_nodetype="NULL", bake_anim=bake, bake_anim_use_all_bones=True,
            bake_anim_use_nla_strips=False, bake_anim_use_all_actions=bake,
            bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
            path_mode="AUTO", embed_textures=False)


def main_export(argv):
    ap = argparse.ArgumentParser(prog="export")
    ap.add_argument("--armature", default=None)
    ap.add_argument("--out", required=True)
    ap.add_argument("--mode", default="all", choices=("all", "split"))
    ap.add_argument("--names", default="quychuan", choices=("quychuan", "mixamo"))
    ap.add_argument("--mesh-name", default=None)
    ap.add_argument("--actions", default="", help="comma list (default: every action made by rigkit anim)")
    a = ap.parse_args(argv)
    C.force_object_mode()
    rep = C.Report("export")
    arm = bpy.data.objects.get(a.armature) if a.armature else next(o for o in bpy.data.objects if o.type == "ARMATURE")
    acts = [bpy.data.actions[n] for n in a.actions.split(",") if n] or [x for x in bpy.data.actions if x.get("rk_clip")]
    # keep only the chosen actions for the export: others get no users (All Actions exports every action)
    keep = {x.name for x in acts}
    for x in list(bpy.data.actions):
        if x.name not in keep:
            bpy.data.actions.remove(x)
    meshes = skinned_meshes(arm)
    subject = a.mesh_name or os.path.splitext(os.path.basename(a.out))[0].replace("_Rig", "")
    joined = join_copies(meshes, a.mesh_name or ("Mesh_" + subject)) if len(meshes) > 1 else meshes[0]
    if a.names == "mixamo":
        for b in arm.data.bones:
            b.name = mixamo_name(b.name)  # vertex groups follow bone renames automatically
    events = {}
    for x in acts:
        meta = json.loads(x.get("rk_clip", "{}")) if x.get("rk_clip") else {}
        events[x.name] = {"frames": [int(x.frame_range[0]), int(x.frame_range[1])], "fps": meta.get("fps", 30),
                          "loop": meta.get("loop", False), "ground_speed": meta.get("ground_speed", 0.0),
                          "events": meta.get("events", [])}
    written = []
    if a.mode == "all":
        C.assign_action(arm, acts[0])
        fbx_export(a.out, arm, joined, True)
        written.append(a.out)
    else:
        if arm.animation_data:
            arm.animation_data.action = None
        fbx_export(a.out, arm, joined, False)
        written.append(a.out)
        base = os.path.dirname(os.path.abspath(a.out))
        for x in acts:
            others = [y for y in bpy.data.actions if y != x]
            saved = {y.name: y.use_fake_user for y in others}
            for y in others:  # hide the other actions from "All Actions"
                y.use_fake_user = False
            C.assign_action(arm, x)
            p = os.path.join(base, x.name + ".fbx")
            fbx_export(p, arm, joined, True)
            written.append(p)
            for y in others:
                y.use_fake_user = saved[y.name]
    C.save_json(os.path.splitext(os.path.abspath(a.out))[0] + ".events.json", events)
    rep.info.update({"written": written, "mesh": joined.name, "verts": len(joined.data.vertices),
                     "bones": len(arm.data.bones), "actions": sorted(keep), "names": a.names})
    # the .blend on disk is untouched: this process never saves
    return rep.dump(os.path.splitext(os.path.abspath(a.out))[0] + "_export.json")


def bone_positions(arm, frames):
    out = {}
    for f in frames:
        bpy.context.scene.frame_set(f)
        out[f] = {pb.name: arm.matrix_world @ pb.head for pb in arm.pose.bones}
    return out


def main_verify_fbx(argv):
    ap = argparse.ArgumentParser(prog="verify_fbx")
    ap.add_argument("--fbx", required=True)
    ap.add_argument("--source", required=True, help="the .blend the FBX came from")
    ap.add_argument("--out", required=True)
    ap.add_argument("--names", default="quychuan", choices=("quychuan", "mixamo"))
    a = ap.parse_args(argv)
    rep = C.Report("verify_fbx")
    # source bone positions per action (every 3rd frame)
    bpy.ops.wm.open_mainfile(filepath=os.path.abspath(a.source))
    src_arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    src = {}
    for x in [x for x in bpy.data.actions if x.get("rk_clip")]:
        C.assign_action(src_arm, x)
        f0, f1 = int(x.frame_range[0]), int(x.frame_range[1])
        src[x.name] = (f0, f1, bone_positions(src_arm, range(f0, f1 + 1, 3)))
    src_bones = {b.name for b in src_arm.data.bones if b.use_deform or any(c.use_deform for c in b.children_recursive)}
    # imported
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.abspath(a.fbx), automatic_bone_orientation=False, anim_offset=0.0,
                             primary_bone_axis="Y", secondary_bone_axis="X")
    arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if len(arms) != 1:
        rep.error("ARMATURES", "expected 1 armature, got %d" % len(arms))
        return rep.dump(a.out)
    arm = arms[0]
    # Blender's FBX importer connects a lone child to its parent's tail (a prop bone under a hand), and a connected
    # bone ignores its location: a dropped prop far from the hand then reads as ~1 m off. Unity has no "connect" (a
    # transform always plays its full TRS), so measure with every bone unconnected, rest kept.
    with C.ctx(arm, [arm]):
        bpy.ops.object.mode_set(mode="EDIT")
        for eb in arm.data.edit_bones:
            eb.use_connect = False
        bpy.ops.object.mode_set(mode="OBJECT")
    names = {b.name for b in arm.data.bones}
    expect = {mixamo_name(n) for n in src_bones} if a.names == "mixamo" else src_bones
    missing = sorted(expect - names)
    extra = sorted(names - expect)
    if missing:
        rep.error("BONES_MISSING", "bones missing in the FBX: %s" % missing)
    if extra:
        leaf = [n for n in extra if n.endswith("_end")]
        if leaf:
            rep.error("LEAF_BONES", "leaf bones exported (Add Leaf Bones must be off): %s" % leaf)
        other = [n for n in extra if not n.endswith("_end")]
        if other:
            rep.warn("BONES_EXTRA", "bones not in the source: %s" % other)
    if len(meshes) != 1:
        rep.warn("MESHES", "%d meshes in the FBX (one SkinnedMeshRenderer expected)" % len(meshes))
    for m in meshes:
        groups = len(m.vertex_groups)
        rep.info.setdefault("meshes", {})[m.name] = {"verts": len(m.data.vertices), "groups": groups}
    imported = {x.name: x for x in bpy.data.actions}
    rep.info["takes"] = sorted(imported)
    back = {v: k for k, v in ((n, mixamo_name(n)) for n in src_bones)} if a.names == "mixamo" else None
    for name, (f0, f1, pos) in src.items():
        take = next((t for n, t in imported.items() if n.endswith(name) or name in n), None)
        if take is None:
            rep.error("TAKE_MISSING", "take %s not found in the FBX" % name)
            continue
        C.assign_action(arm, take)
        worst = 0.0
        for f, bones in pos.items():
            bpy.context.scene.frame_set(f)
            for pb in arm.pose.bones:
                sname = back.get(pb.name, pb.name) if back else pb.name
                if sname in bones:
                    worst = max(worst, (arm.matrix_world @ pb.head - bones[sname]).length)
        rng = [int(take.frame_range[0]), int(take.frame_range[1])]
        rep.info.setdefault("deviation_m", {})[name] = round(worst, 5)
        if worst > 0.002:
            rep.error("DEVIATION", "%s: bones are up to %.3f m away from the source after the round trip" % (name, worst))
        if rng != [f0, f1]:
            rep.warn("RANGE", "%s: frame range %s in the FBX, %s in the source" % (name, rng, [f0, f1]))
    return rep.dump(a.out)
