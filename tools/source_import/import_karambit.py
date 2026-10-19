"""Run in a fresh background Blender process; never modifies the CSS files.

blender --background --factory-startup --python import_karambit.py -- --sourceio PATH
PATH is the checkout directory containing the SourceIO package directory.
"""
import argparse
import json
import subprocess
import sys
from pathlib import Path

import bpy

parser = argparse.ArgumentParser()
parser.add_argument("--sourceio", type=Path, required=True)
parser.add_argument("--source", type=Path, default=Path(
    r"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Source\cstrike\custom\#Karambit"))
parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[2] / "assets/weapons/karambit")
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
sys.path.insert(0, str(args.sourceio))

import SourceIO
SourceIO.register()
from SourceIO.blender_bindings.models import import_model
from SourceIO.blender_bindings.models.common import put_into_collections
from SourceIO.blender_bindings.operators.import_settings_base import ModelOptions
from SourceIO.library.shared.content_manager import ContentManager
from SourceIO.library.shared.content_manager.providers.loose_files import LooseFilesContentProvider
from SourceIO.library.utils import FileBuffer
from SourceIO.library.utils.tiny_path import TinyPath

out = args.output.resolve()
out.mkdir(parents=True, exist_ok=True)
source = args.source.resolve()
mdl_path = TinyPath(str(source / "models/weapons/v_knife_t.mdl"))
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
cm = ContentManager()
cm.scan_for_content(mdl_path)
custom = LooseFilesContentProvider(TinyPath(str(source)))
cm.add_child(custom)
# CSS gameinfo uses a wildcard for custom folders; mount this mod explicitly.
cm.priority_list = [custom, *[p for p in cm.children if p is not custom]]
options = ModelOptions.default()
options.import_animations = True
options.compact_animations = False
options.import_textures = True
options.use_bvlg = False
options.bodygroup_vis_switches = False
options.scale = 0.0254
with FileBuffer(mdl_path) as buffer:
    container = import_model(mdl_path, buffer, cm, options)
put_into_collections(container, "Karambit")
rig = container.armature
rig.name = "KarambitRig"
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
assert rig and meshes, "No rig or mesh imported"

# Keep simple Principled materials, supported by the glTF exporter and Godot.
# SourceIO can retain a missing-texture placeholder after a later successful load.
materials = {m for obj in meshes for m in obj.data.materials if m}
textures = []
for mat in materials:
    for node in mat.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image and node.image.name.startswith("missing_"):
            replacement = bpy.data.images.get(node.image.name.removeprefix("missing_"))
            if replacement:
                node.image = replacement
    image_nodes = [n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image]
    assert image_nodes, f"No texture for {mat.name}"
    for node in image_nodes:
        assert not node.image.name.startswith("missing_"), f"Missing texture: {node.image.name}"
        image = node.image
        filename = image.name.replace("/", "_").replace("\\", "_") + ".png"
        image.filepath_raw = str(out / "textures" / filename)
        Path(image.filepath_raw).parent.mkdir(exist_ok=True)
        image.file_format = "PNG"
        image.save()
        textures.append({"material": mat.name, "image": image.name, "size": list(image.size)})

scene = bpy.context.scene
scene.render.fps = 30
scene.render.fps_base = 1.0
animation_report = []
descriptors = {a.name: a for a in container.mdl.anim_descs}
for action in list(bpy.data.actions):
    source_name = action.name
    descriptor = descriptors.get(source_name)
    assert descriptor is not None, f"Missing animation metadata: {source_name}"
    # SourceIO imports one key per source frame without applying the source FPS.
    ratio = scene.render.fps / descriptor.fps
    for layer in action.layers:
        for strip in layer.strips:
            for slot in action.slots:
                bag = strip.channelbag(slot)
                if bag:
                    for curve in bag.fcurves:
                        for key in curve.keyframe_points:
                            key.co.x *= ratio
                        curve.update()
    action.name = source_name.lstrip("@")
    action.use_fake_user = True
    animation_report.append({"name": action.name, "source_name": source_name,
                             "source_fps": descriptor.fps, "source_frames": descriptor.frame_count,
                             "duration": (descriptor.frame_count - 1) / descriptor.fps})

rig.animation_data_create()
idle = bpy.data.actions.get("idle") or bpy.data.actions[0]
rig.animation_data.action = idle
rig.animation_data.action_slot = idle.slots[0]
scene.frame_start = 0
scene.frame_end = int(idle.frame_range[1])
scene.frame_set(0)
bpy.context.view_layer.update()
geometry = [{"name": m.name, "vertices": len(m.data.vertices),
             "triangles": sum(len(p.vertices) - 2 for p in m.data.polygons),
             "materials": [mat.name for mat in m.data.materials]} for m in meshes]

# Blender source is excluded from Godot's automatic .blend import.
blend_dir = out / "source"
blend_dir.mkdir(exist_ok=True)
(blend_dir / ".gdignore").write_text("")
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(blend_dir / "karambit.blend"))
bpy.ops.export_scene.gltf(filepath=str(out / "karambit.glb"), export_format="GLB",
                          export_animations=True, export_animation_mode="ACTIONS",
                          export_frame_range=False, export_force_sampling=True,
                          export_skins=True, export_yup=True, export_cameras=False,
                          export_lights=False)
commit = subprocess.check_output(["git", "-C", str(args.sourceio / "SourceIO"), "rev-parse", "HEAD"], text=True).strip()
report = {"source": str(source), "mdl_version": 44, "sourceio_commit": commit,
          "meters_per_source_unit": options.scale, "bones": len(rig.data.bones),
          "geometry": geometry, "textures": textures, "animations": animation_report,
          "notes": ["Viewmodel: arms, sleeves and knife are combined in the source mesh.",
                    "Hands material/textures also depend on the installed CSS VPK archives.",
                    "Source shader lighting is approximated using glTF Principled materials.",
                    "Source game events, hit timing and gameplay logic are not glTF animations."]}
(out / "import_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("KARAMBIT_EXPORT_OK", json.dumps({"bones": report["bones"], "animations": animation_report}))
