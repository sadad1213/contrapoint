"""Convert a local Source 1 MDL using a fresh background Blender process.
Use --help for required paths. SourceIO is an external dependency.
"""
import argparse
import json
import subprocess
import sys
from pathlib import Path

parser = argparse.ArgumentParser(description="Convert a local Source 1 model to GLB using background Blender and SourceIO.")
parser.add_argument("--sourceio", type=Path, required=True, help="SourceIO checkout containing __init__.py")
parser.add_argument("--content-root", type=Path, required=True, help="Root containing models/ and materials/")
parser.add_argument("--model", type=Path, required=True, help="Absolute MDL path")
parser.add_argument("--output", type=Path, required=True, help="Fresh output folder outside the game")
parser.add_argument("--game-root", type=Path, help="Optional game content root for VPK dependencies")
parser.add_argument("--name", help="Output basename, default sanitized MDL stem")
parser.add_argument("--scale", type=float, default=0.0254)
parser.add_argument("--no-animations", action="store_true")
parser.add_argument("--allow-missing-materials", action="store_true")
parser.add_argument("--overwrite", action="store_true")
parser.add_argument('--weapon-poses-only', action='store_true', help='Export shared upper-body poses for the complete CSS arsenal')
cli = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
args = parser.parse_args(cli)

import bpy
import importlib.util
import math
import re
import struct
from mathutils import Matrix

if not bpy.app.background or bpy.data.filepath:
    raise RuntimeError("Run in a fresh Blender --background --factory-startup process")
if not math.isfinite(args.scale) or args.scale <= 0:
    raise ValueError("Scale must be positive and finite")
source = args.content_root.resolve()
model_file = args.model.resolve()
repo = args.sourceio.resolve()
out = args.output.resolve()
if not model_file.is_file() or not model_file.is_relative_to(source):
    raise ValueError("Model must be an existing file inside --content-root")
for input_root in [source, *([args.game_root.resolve()] if args.game_root else [])]:
    if out.is_relative_to(input_root):
        raise ValueError("Output must be outside the input/game content root")
if out.exists() and any(out.iterdir()) and not args.overwrite:
    raise FileExistsError("Output is not empty; use a fresh folder or intentional --overwrite")
name = args.name or re.sub(r"[^A-Za-z0-9_-]+", "_", model_file.stem).strip("_")
if not name or not re.fullmatch(r"[A-Za-z0-9_-]+", name):
    raise ValueError("Output name must contain only letters, digits, underscore or hyphen")
with model_file.open("rb") as header:
    magic, mdl_version = struct.unpack("<4sI", header.read(8))
if magic != b"IDST" or mdl_version < 36:
    raise ValueError("This helper targets Source 1 IDST models, not GoldSrc or Source 2")

# Load under the package name required by SourceIO's absolute imports, even if
# the checkout folder was named SourceIO-master or another user-chosen name.
spec = importlib.util.spec_from_file_location("SourceIO", repo / "__init__.py", submodule_search_locations=[str(repo)])
if spec is None or spec.loader is None:
    raise ImportError("SourceIO checkout was not found")
SourceIO = importlib.util.module_from_spec(spec)
sys.modules["SourceIO"] = SourceIO
spec.loader.exec_module(SourceIO)
SourceIO.register()
from SourceIO.blender_bindings.models import import_model
from SourceIO.blender_bindings.models.common import put_into_collections
from SourceIO.blender_bindings.operators.import_settings_base import ModelOptions
from SourceIO.library.shared.content_manager import ContentManager
from SourceIO.library.shared.content_manager.providers.loose_files import LooseFilesContentProvider
from SourceIO.library.utils import FileBuffer
from SourceIO.library.utils.tiny_path import TinyPath

out.mkdir(parents=True, exist_ok=True)
mdl_path = TinyPath(str(model_file))
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
cm = ContentManager()
if args.game_root:
    cm.scan_for_content(TinyPath(str(args.game_root.resolve())))
cm.scan_for_content(mdl_path)
custom = LooseFilesContentProvider(TinyPath(str(source)))
cm.add_child(custom)
# CSS gameinfo uses a wildcard for custom folders; mount this mod explicitly.
cm.priority_list = [custom, *[p for p in cm.children if p is not custom]]
options = ModelOptions.default()
options.import_animations = False
options.compact_animations = False
options.import_textures = True
options.use_bvlg = False
options.bodygroup_vis_switches = False
options.scale = args.scale
with FileBuffer(mdl_path) as buffer:
    container = import_model(mdl_path, buffer, cm, options)
put_into_collections(container, name)
rig = container.armature
if rig:
    rig.name = "PlayerRig"
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
assert meshes, "No mesh imported"

# Keep simple Principled materials, supported by the glTF exporter and Godot.
# SourceIO can retain a missing-texture placeholder after a later successful load.
materials = {m for obj in meshes for m in obj.data.materials if m}
textures = []
missing = []
for mat in sorted(materials, key=lambda m: m.name):
    if mat.node_tree is None:
        missing.append({"material": mat.name, "reason": "No material node tree"})
        continue
    for node in mat.node_tree.nodes:
        if node.type == "TEX_IMAGE" and node.image and node.image.name.startswith("missing_"):
            replacement = bpy.data.images.get(node.image.name.removeprefix("missing_"))
            if replacement:
                node.image = replacement
    image_nodes = [n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image]
    if not image_nodes:
        missing.append({"material": mat.name, "reason": "No texture nodes; inspect VMT or intentional plain material"})
    for node in image_nodes:
        if node.image.name.startswith("missing_"):
            missing.append({"material": mat.name, "image": node.image.name})
            continue
        image = node.image
        filename = re.sub(r"[^A-Za-z0-9_.-]+", "_", image.name) + ".png"
        image.filepath_raw = str(out / "textures" / filename)
        Path(image.filepath_raw).parent.mkdir(exist_ok=True)
        image.file_format = "PNG"
        image.save()
        textures.append({"material": mat.name, "image": image.name, "size": list(image.size)})

if missing and not args.allow_missing_materials:
    raise RuntimeError("Unresolved materials: " + json.dumps(missing))

scene = bpy.context.scene
scene.render.fps = 30
scene.render.fps_base = 1.0
animation_report = []
# CSS player locomotion and weapon poses live in animation-only include MDLs.
from SourceIO.library.models.mdl.v44 import MdlV44
from SourceIO.library.models.mdl.load_animations import load_animations_from_mdl
from SourceIO.blender_bindings.models.import_animations import import_animations_to_armature
from types import SimpleNamespace
sys.path.insert(0, str(Path(__file__).resolve().parent))
from css_player_sequences import bake_sequences
from css_agents import animation_models
descriptors = {}
weapons = ("PISTOL", "AK", "M4", "AWP", "SCOUT", "KNIFE", "GREN")
if args.weapon_poses_only:
    weapons = ('AUG', 'ELITES', 'FAMAS', 'G3', 'GALIL', 'M249', 'M3S90', 'MAC10',
               'MP5', 'P90', 'SG550', 'SG552', 'TMP', 'UMP45', 'XM1014')
def wanted(n):
    if args.weapon_poses_only:
        return (n.startswith('@') and any(n.upper().endswith('_' + w) for w in weapons)
                and any(x in n for x in ('_Upper_', '_Reload_', '_Shoot_')) and 'staticlayer' not in n)
    return (n in ("@Idle_lower", "@Crouch_Idle_Lower", "@Jump", "@Death1")
            or n.startswith(("a_Run", "a_Walk", "a_Crouch_walk"))
            or (n.startswith("@") and any(n.endswith("_" + w) for w in weapons)
                and any(x in n for x in ("_Upper_", "_Reload_", "_Shoot_"))
                and "staticlayer" not in n)
            or n in ("@Idle_Shoot_GREN1", "@Idle_Shoot_GREN2")
            or ("_Shoot_" in n and n.upper().endswith(("_PISTOL", "_RIFLE", "_KNIFE"))))
for relative in animation_models(name):
    path = TinyPath(str(source / relative))
    with FileBuffer(path) as buffer:
        mdl = MdlV44.from_buffer(buffer)
        animations = bake_sequences(mdl, buffer, wanted)
    for animation in animations:
        # The shared rig can have accessory bones absent from an agent skin.
        animation.frames = {n: v for n, v in animation.frames.items() if n in rig.data.bones}
        descriptors[animation.name] = SimpleNamespace(fps=animation.fps, frame_count=animation.frame_count)
    import_animations_to_armature(rig, path, {relative: animations}, options.scale, False)
for action in list(bpy.data.actions):
    source_name = action.name
    descriptor = descriptors.get(source_name)
    assert descriptor is not None, f"Missing animation metadata: {source_name}"
    # SourceIO rotates animated roots by -90 degrees around Source Z, but
    # leaves unkeyed roots in Source coordinates. M4 hands use an unkeyed
    # v_weapon root, independently of the animated weapon root. Give missing
    # roots the same basis conversion without changing the bind pose.
    root_corrections = []
    if rig:
        conversion = Matrix.Rotation(-math.pi / 2, 4, "Z")
        for layer in action.layers:
            for strip in layer.strips:
                for slot in action.slots:
                    bag = strip.channelbag(slot)
                    if bag is None:
                        continue
                    for bone in rig.data.bones:
                        if bone.parent:
                            continue
                        prefix = 'pose.bones[' + json.dumps(bone.name) + ']'
                        if any(curve.data_path.startswith(prefix + ".") for curve in bag.fcurves):
                            continue
                        basis = bone.matrix_local.inverted() @ conversion @ bone.matrix_local
                        location, rotation, _ = basis.decompose()
                        for prop, values in [("location", location), ("rotation_quaternion", rotation)]:
                            for index, value in enumerate(values):
                                curve = bag.fcurves.new(prefix + "." + prop, index=index)
                                curve.keyframe_points.insert(0, value)
                        root_corrections.append(bone.name)
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
                             "unkeyed_root_corrections": root_corrections,
                             "duration": (descriptor.frame_count - 1) / descriptor.fps})

if rig and bpy.data.actions:
    rig.animation_data_create()
    idle = bpy.data.actions.get("Idle_Upper_KNIFE") or bpy.data.actions[0]
    rig.animation_data.action = idle
    rig.animation_data.action_slot = idle.slots[0]
    scene.frame_end = int(idle.frame_range[1])
scene.frame_start = 0
scene.frame_set(0)
bpy.context.view_layer.update()
geometry = [{"name": m.name, "vertices": len(m.data.vertices),
             "triangles": sum(len(p.vertices) - 2 for p in m.data.polygons),
             "materials": [mat.name for mat in m.data.materials]} for m in meshes]

# Blender source is excluded from Godot's automatic .blend import.
blend_dir = out / "source"
blend_dir.mkdir(exist_ok=True)
(blend_dir / ".gdignore").write_text("")
bpy.data.orphans_purge(do_recursive=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(blend_dir / (name + ".blend")))
bpy.ops.export_scene.gltf(filepath=str(out / (name + ".glb")), export_format="GLB",
                          export_animations=True, export_animation_mode="ACTIONS",
                          export_frame_range=False, export_force_sampling=True,
                          export_skins=True, export_yup=True, export_cameras=False,
                          export_lights=False)
try:
    commit = subprocess.check_output(["git", "-C", str(repo), "rev-parse", "HEAD"], text=True, stderr=subprocess.DEVNULL).strip()
except (OSError, subprocess.CalledProcessError):
    commit = "unknown (not a git checkout)"
report = {"status": "complete" if not missing else "incomplete_materials",
          "source": str(model_file), "content_root": str(source), "mdl_version": mdl_version,
          "sourceio_commit": commit, "blender_version": bpy.app.version_string,
          "meters_per_source_unit": options.scale, "bones": len(rig.data.bones) if rig else 0,
          "geometry": geometry, "textures": textures, "missing_materials": missing,
          "animations": animation_report, "glb": str(out / (name + ".glb")),
          "notes": ["Material lighting approximates Source using standard glTF shaders.",
                    "Gameplay logic, hit timing and sound events are not glTF animation.",
                    "Verify actual animation durations and motion in the destination engine."]}
(out / "import_report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print("SOURCE1_EXPORT_OK", json.dumps({"status": report["status"], "bones": report["bones"], "animations": animation_report}))
