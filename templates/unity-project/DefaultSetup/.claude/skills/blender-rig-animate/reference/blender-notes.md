# Blender 4.x khi chạy headless: những chỗ hay vấp

Kiểm trên Blender 4.5.14 LTS (máy này). Manual PDF là bản 2.77: đối chiếu ở đây khi tên đã đổi.

| Chủ đề | Blender 4.x | Ghi chú |
|---|---|---|
| Chạy | `blender -b file.blend --factory-startup --python-exit-code 1 --python rk.py -- <bước>` | `--factory-startup` bỏ add-on của người dùng (add-on Tripo làm lỗi khi mở file); `--python-exit-code 1` để lỗi Python ra exit code |
| File lưu lúc đang Edit Mode | edit-mesh ghi đè mọi thay đổi mesh | `common.force_object_mode()` trước mọi bước |
| Lưu | `bpy.ops.wm.save_as_mainfile(filepath=..., copy=True)`, `preferences.filepaths.save_version = 0` | `copy=True` không đổi file đang mở; không sinh `.blend1` |
| Operator cần context | `with bpy.context.temp_override(active_object=..., selected_objects=[...])` | `common.ctx()`; `parent_set(type="ARMATURE_AUTO")` chạy được ở `-b` |
| Bone heat | chỉ xương `use_deform`; bật tắt tạm deform để loại xương đạo cụ | báo "failed to find solution" khi mesh hở, mảnh chồng: rigkit tự rơi về proxy voxel |
| Bone layers → Bone Collections | `arm.data.collections.new("Deform")`, `collection.assign(bone)` | màu theo collection thay Bone Groups |
| Slotted actions (4.4+) | `action.fcurves` có thể rỗng; F-Curve nằm trong `action.layers[].strips[].channelbags[]` | `common.action_fcurves()`, `common.assign_action()` (gán slot) |
| Keyframe | `pose_bone.keyframe_insert("rotation_quaternion", frame=f, group=name)` | key đủ loc / rot / scale cho mọi xương mỗi clip |
| Quaternion liên tục | `q` và `-q` cùng một phép xoay | đổi dấu khi `dot(q_trước, q) < 0` trước khi key; đo vận tốc góc bằng `2 acos(|dot|)` |
| Evaluated mesh | `ob.evaluated_get(depsgraph).to_mesh()` rồi `to_mesh_clear()` | `common.world_coords(ob, evaluated=True)` |
| Voxel remesh | modifier `REMESH` mode `VOXEL`, `bpy.data.meshes.new_from_object(ob.evaluated_get(dg))` | lấp lỗ trước (`bmesh.ops.holes_fill`) |
| Render duyệt | Workbench, `view_settings.view_transform = "Standard"` | AgX làm màu xỉn; Armature không render, rigkit vẽ que xương bằng mesh tạm |
| FBX xuất | `export_scene.fbx(apply_scale_options="FBX_SCALE_UNITS", add_leaf_bones=False, use_armature_deform_only=True, bake_anim_use_all_actions=True, ...)` | xương không deform nhưng có con deform (Root, Shoulder) vẫn được xuất |
| FBX nhập để kiểm | `import_scene.fbx(anim_offset=0.0)` | mặc định dời +1 frame; tên take thành `Armature|Armature|Action` |
| numpy | có sẵn trong Python của Blender | không có scipy, PIL: ghép ảnh bằng `bpy.data.images` + numpy |
| PDF, tài liệu | Python của Blender cài được gói thuần Python vào thư mục riêng: `python.exe -m pip install --target <dir> pypdf` | không ghi vào bản cài Blender |
