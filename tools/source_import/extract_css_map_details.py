"""Extract original PHY collision hulls, effects and water cubemaps for Inferno.

Run in background Blender with the same --sourceio/--game-root/--output arguments.
"""
import argparse
import importlib.util
import itertools
import json
import re
import hashlib
import subprocess
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Euler

parser = argparse.ArgumentParser()
parser.add_argument('--sourceio', type=Path, required=True)
parser.add_argument('--game-root', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--refresh-models', action='store_true')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
spec = importlib.util.spec_from_file_location('SourceIO', args.sourceio / '__init__.py', submodule_search_locations=[str(args.sourceio)])
module = importlib.util.module_from_spec(spec)
sys.modules['SourceIO'] = module
spec.loader.exec_module(module)
module.register()
from SourceIO.library.shared.content_manager import ContentManager
from SourceIO.library.shared.content_manager.providers.vpk_provider import VPKContentProvider
from SourceIO.library.shared.app_id import SteamAppId
from SourceIO.library.source1.bsp.bsp_file import open_bsp
from SourceIO.library.source1.bsp.lumps import *
from SourceIO.library.models.mdl.v44 import MdlV44
from SourceIO.library.models.phy.phy import Phy
from SourceIO.library.utils import FileBuffer, TinyPath
from SourceIO.library.utils.math_utilities import vector_transform_v
from SourceIO.library.source1.vmt import VMT
from SourceIO.library.utils.pylib.vtf import load_vtf_texture
from SourceIO.library.source1.vtf import load_texture_frames

cm = ContentManager()
cm.add_child(VPKContentProvider(TinyPath(str(args.game_root / 'cstrike_pak_dir.vpk')), SteamAppId.COUNTER_STRIKE_SOURCE))
for archive in ('hl2_misc_dir.vpk', 'hl2_textures_dir.vpk', 'hl2_sound_misc_dir.vpk'):
    cm.add_child(VPKContentProvider(TinyPath(str(args.game_root.parent / 'hl2' / archive))))
data_path = args.output / 'source/map_data.json'
data = json.loads(data_path.read_text())
if data['map'] != 'de_inferno':
    raise ValueError('This detail pass includes Inferno-specific models and soundscape scripts')
bsp = open_bsp(TinyPath(data['source_bsp']), FileBuffer(TinyPath(data['source_bsp'])), cm, SteamAppId.COUNTER_STRIKE_SOURCE)
cm.add_child(bsp.get_lump('LUMP_PAK'))
# Keep the map-specific models reproducible without relying on a temporary
# extraction directory. Each GLB is converted in a fresh Blender process.
extra_models = []
content_root = args.output.resolve() / 'source/css'
for name, folder in (('goldfish', 'fish'), ('elevatordoor', 'gate')):
    source_files = []
    for extension in ('mdl', 'vvd', 'dx90.vtx'):
        relative = 'models/props/de_inferno/' + name + '.' + extension
        buffer = cm.find_file(TinyPath(relative))
        if buffer is None:
            raise FileNotFoundError(relative)
        destination = content_root / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        payload = buffer.read()
        destination.write_bytes(payload)
        source_files.append({'path': relative, 'sha256': hashlib.sha256(payload).hexdigest()})
    output = args.output.resolve() / folder
    if args.refresh_models or not (output / (name + '.glb')).exists():
        result = subprocess.run([bpy.app.binary_path, '--background', '--factory-startup', '--python-exit-code', '1',
            '--python', str(Path(__file__).with_name('import_source1_model.py').resolve()), '--',
            '--sourceio', str(args.sourceio.resolve()), '--content-root', str(content_root),
            '--model', str(content_root / ('models/props/de_inferno/' + name + '.mdl')),
            '--game-root', str(args.game_root.resolve()), '--output', str(output), '--name', name, '--overwrite'],
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        if result.returncode:
            raise RuntimeError(result.stdout[-6000:])
    extra_models.append({'name': name, 'scene': folder + '/' + name + '.glb', 'source_files': source_files})
data['extra_models'] = extra_models
props = bsp.get_lump('LUMP_GAME_LUMP').game_lumps['sprp']
cache = {}

def physics(path):
    path = path.lower()
    if path in cache:
        return cache[path]
    mdl = MdlV44.from_buffer(cm.find_file(TinyPath(path)))
    phy_buffer = cm.find_file(TinyPath(path).with_suffix('.phy'))
    original = phy_buffer is not None
    hulls, mass, surface = [], 10.0, 'concrete'
    if phy_buffer:
        phy = Phy.from_buffer(phy_buffer)
        match = re.search(r'"mass"\s+"([\d.]+)"', phy.kv)
        if match:
            mass = float(match[1])
        match = re.search(r'"surfaceprop"\s+"([^"\n]+)"', phy.kv)
        if match:
            surface = match[1]
        def collect(node):
            leaves = [node.convex_leaf] if node.convex_leaf else []
            if node.left_node:
                leaves += collect(node.left_node)
            if node.right_node:
                leaves += collect(node.right_node)
            return leaves
        for solid in phy.solids:
            leaves = collect(solid.collision_model.root_tree)
            count = max(i for leaf in leaves for i in leaf.unique_vertices) + 1
            vertices = solid.collision_model.get_vertex_data(phy_buffer, solid.collision_model.root_tree.convex_leaf, count) / 0.0254
            # IVP stores (x,-z,y). SourceIO swaps Y/Z but leaves the upward axis
            # inverted; restore Source Z before applying the model's bind pose.
            vertices[:, 2] *= -1
            for leaf in leaves:
                if leaf.has_children:
                    continue
                points = vertices[sorted(leaf.unique_vertices)].copy()
                if mdl.bones and leaf.bone_id > 0:
                    points = vector_transform_v(points, mdl.bones[leaf.bone_id - 1].pose_to_bone.copy())
                if len(points) >= 4:
                    hulls.append(points)
    if not hulls:
        mins, maxs = mdl.header.hull_min, mdl.header.hull_max
        hulls = [np.array(list(itertools.product(*zip(mins, maxs))))]
    cache[path] = (hulls, mass, surface, original)
    return cache[path]

def transform_hulls(hulls, angles, origin, scaling=(1, 1, 1)):
    pitch, yaw, roll = np.radians(angles)
    rotation = np.array(Euler((roll, pitch, yaw), 'XYZ').to_matrix())
    result = []
    for hull in hulls:
        points = (hull * scaling) @ rotation.T + origin
        result.append((points[:, [0, 2, 1]] * [0.0254, 0.0254, -0.0254]).tolist())
    return result

static = []
sky_min, sky_max = np.array(data['sky_room']['mins']), np.array(data['sky_room']['maxs'])
for prop in props.static_props:
    if not prop.solid or np.all(prop.origin >= sky_min) and np.all(prop.origin <= sky_max):
        continue
    path = props.model_names[prop.prop_type]
    hulls, mass, surface, original = physics(path)
    static.append({'model': path, 'surface': surface, 'original_phy': original,
                   'hulls': transform_hulls(hulls, prop.rotation, prop.origin, prop.scaling)})
for entity in data['entities'].values():
    if entity.get('model') == 'models/props/de_inferno/elevatordoor.mdl':
        entity['animated_scene'] = 'gate/elevatordoor.glb'
    if not entity['classname'].startswith('prop_physics'):
        continue
    hulls, mass, surface, original = physics(entity['model'])
    entity['mass'] = mass
    entity['surface'] = surface
    entity['original_phy'] = original
    angles = np.fromstring(entity.get('angles', '0 0 0'), sep=' ')
    entity['physics_hulls'] = transform_hulls(hulls, angles, (0, 0, 0))
data['static_collisions'] = static
data['stats']['original_static_collisions'] = sum(p['original_phy'] for p in static)
data['stats']['original_dynamic_collisions'] = sum(e.get('original_phy', False) for e in data['entities'].values())

# Collision comes from compiled brush volumes, including invisible/nodraw sides.
# Displacement brushes are traced separately by Source; keep their triangle meshes.
def lump_bytes(index):
    return bsp._get_lump_buffer(index, bsp.info.lumps[index]).read()

import struct
nodes, leaves = lump_bytes(5), lump_bytes(10)
leaf_brushes = np.frombuffer(lump_bytes(17), '<u2')
brushes, sides = lump_bytes(18), lump_bytes(19)
planes = bsp.get_lump('LUMP_PLANES').planes
pending = [bsp.get_lump('LUMP_MODELS').models[0].head_node]
world_indices = set()
while pending:
    index = pending.pop()
    if index >= 0:
        pending.extend(struct.unpack_from('<ii', nodes, index*32+4))
    else:
        first, count = struct.unpack_from('<HH', leaves, (-index-1)*32+24)
        world_indices.update(int(i) for i in leaf_brushes[first:first+count])
world_hulls, world_surfaces = [], []
texture_infos = bsp.get_lump('LUMP_TEXINFO').texture_info
texture_data = bsp.get_lump('LUMP_TEXDATA').texture_data
texture_names = bsp.get_lump('LUMP_TEXDATA_STRING_TABLE').strings
for brush_id in sorted(world_indices):
    first, count, contents = struct.unpack_from('<iii', brushes, brush_id*12)
    if not contents & (1 | 2 | 8):
        continue
    records = [struct.unpack_from('<Hhhh', sides, i*8) for i in range(first, first+count)]
    # This CSS BSP clears brush-side dispinfo to zero; displacement-only
    # brushes have contents=0 and were already excluded above.
    normals = np.array([planes[pi].normal for pi, _, _, _ in records])
    distances = np.array([planes[pi].dist for pi, _, _, _ in records])
    active = [i for i, (_, _, _, bevel) in enumerate(records) if not bevel]
    points = []
    for ids in itertools.combinations(active, 3):
        matrix = normals[list(ids)]
        if abs(np.linalg.det(matrix)) < 1.e-6:
            continue
        point = np.linalg.solve(matrix, distances[list(ids)])
        if np.all(normals @ point <= distances + .05):
            points.append(point)
    if len(points) < 4:
        continue
    points = np.unique(np.round(points, 3), axis=0)
    center = points.mean(axis=0)
    if np.all(center >= sky_min) and np.all(center <= sky_max):
        continue
    world_hulls.append((points[:, [0, 2, 1]] * [.0254, .0254, -.0254]).tolist())
    candidates = []
    for pi, texture_id, _, _ in records:
        if texture_id < 0:
            continue
        name = texture_names[texture_data[texture_infos[texture_id].texture_data_id].name_id].lower()
        if not name.startswith('tools/'):
            candidates.append((planes[pi].normal[2], name))
    name = max(candidates)[1] if candidates else ''
    world_surfaces.append(data['materials'].get(name, {}).get('surface', 'concrete'))
data['world_hulls'] = world_hulls
data['world_surfaces'] = world_surfaces
data['stats']['world_collision_brushes'] = len(world_hulls)
print('WORLD_COLLISION_BRUSHES', len(world_hulls), flush=True)

effects = []
effect_textures = {}
for entity in data['point_entities']:
    if entity['classname'] not in ('env_sprite', 'env_beam'):
        continue
    name = entity.get('texture', entity.get('model', '')).removesuffix('.spr').removesuffix('.vmt').lower()
    if name not in effect_textures:
        vmt = VMT(cm.find_file(TinyPath('materials/' + name + '.vmt')), name, cm)
        base = vmt.get_string('$basetexture', '')
        frames, h, w = load_texture_frames(cm.find_file(TinyPath('materials/' + base + '.vtf')))
        if not frames:
            raise ValueError('Missing effect texture: ' + name)
        strip = np.concatenate(frames, axis=1)
        image = bpy.data.images.new('effect', width=w*len(frames), height=h, alpha=True)
        image.pixels.foreach_set(np.flipud(strip).astype(np.float32).ravel())
        filename = 'textures/' + re.sub(r'[^a-z0-9_]', '_', base.lower()) + '_animation.png'
        image.filepath_raw = str(args.output / filename)
        image.file_format = 'PNG'
        image.save()
        bpy.data.images.remove(image)
        effect_textures[name] = {'texture': filename, 'frames': len(frames), 'width': w, 'height': h}
    effects.append({'entity': entity, **effect_textures[name]})
data['effects'] = effects
data['stats']['sprite_glows'] = sum(e['entity']['classname'] == 'env_sprite' for e in effects)
data['stats']['smoke_beams'] = sum(e['entity']['classname'] == 'env_beam' for e in effects)

# Water uses the BSP's own six-face cubemap rather than a flat invented reflection.
for name, material in data['materials'].items():
    if material['shader'] != 'water':
        continue
    vmt = VMT(cm.find_file(TinyPath('materials/' + name + '.vmt')), name, cm)
    material['above_water'] = bool(vmt.get_int('$abovewater', 1))
    proxy = vmt.get('proxies', {})
    animated = proxy.get('animatedtexture', {}) if proxy else {}
    material['normal_fps'] = float(animated.get('animatedtextureframerate', 16))
    if max(material['fog_color']) > 1:
        material['fog_color'] = [v / 255 for v in material['fog_color']]
    envmap = vmt.get_string('$envmap', '')
    if envmap and envmap != 'env_cubemap':
        buffer = cm.find_file(TinyPath('materials/' + envmap + '.vtf'))
        if buffer:
            contents = buffer.read()
            material['cubemap_faces'] = []
            cube_faces = []
            for face in range(6):
                pixels, w, h, floating = load_vtf_texture(contents, 0, face, 0)
                pixels = np.frombuffer(pixels, np.float32 if floating else np.uint8).reshape(h, w, 4)
                if not floating:
                    pixels = pixels.astype(np.float32) / 255
                cube_faces.append(pixels)
                image = bpy.data.images.new('cube', width=w, height=h, alpha=True, float_buffer=False)
                image.pixels.foreach_set(np.flipud(pixels).ravel())
                filename = 'textures/' + re.sub(r'[^a-z0-9_]', '_', envmap.lower()) + '_' + str(face) + '.png'
                image.filepath_raw = str(args.output / filename)
                image.file_format = 'PNG'
                image.save()
                bpy.data.images.remove(image)
                material['cubemap_faces'].append(filename)
            strip = np.concatenate(cube_faces, axis=1)
            h, w = strip.shape[:2]
            image = bpy.data.images.new('reflection_strip', width=w, height=h, alpha=True)
            image.pixels.foreach_set(np.flipud(strip).ravel())
            filename = 'textures/' + re.sub(r'[^a-z0-9_]', '_', envmap.lower()) + '_strip.png'
            image.filepath_raw = str(args.output / filename)
            image.file_format = 'PNG'
            image.save()
            bpy.data.images.remove(image)
            material['reflection_texture'] = filename
sys.path.insert(0, str(Path(__file__).parent))
from import_css_audio import keyvalues
soundscapes = {}
audio_files = set()
for script in ('soundscapes_general.txt', 'soundscapes_inferno.txt'):
    buf = cm.find_file(TinyPath('scripts/' + script))
    if not buf:
        continue
    text = buf.read().decode('cp1252', errors='replace')
    for name, pairs in keyvalues(text):
        if isinstance(pairs, list):
            soundscapes[name] = pairs
selected = {}
def select_soundscape(name):
    name = name.lower()
    if name in selected:
        return
    if name not in soundscapes:
        raise KeyError('Unknown soundscape: ' + name)
    selected[name] = soundscapes[name]
    def visit(pairs):
        for key, value in pairs:
            if isinstance(value, list):
                visit(value)
            elif key == 'wave':
                audio_files.add(value.lstrip('*#@><^)}$!?').replace('\\', '/').lower())
            elif key == 'name' and value.lower() in soundscapes:
                select_soundscape(value)
    visit(soundscapes[name])
for entity in data['point_entities']:
    if entity['classname'] == 'env_soundscape':
        select_soundscape(entity['soundscape'])
data['soundscapes'] = selected
data['stats']['soundscape_audio_files'] = len(audio_files)
for path in ('physics/glass/glass_sheet_break1.wav', 'physics/glass/glass_sheet_break2.wav'):
    audio_files.add(path)
world_sounds = {}
for script in ('game_sounds_world.txt', 'game_sounds_ambient_generic.txt'):
    buffer = cm.find_file(TinyPath('scripts/' + script))
    if buffer:
        world_sounds.update(dict(keyvalues(buffer.read().decode('cp1252', errors='replace'))))
for point in data['point_entities']:
    if point['classname'] != 'ambient_generic' or not point.get('sourceentityname'):
        continue
    definition = world_sounds.get(point.get('message', '').lower(), [])
    waves = [v for k, v in definition if k == 'wave']
    for key, value in definition:
        if key == 'rndwave':
            waves.extend(v for k, v in value if k == 'wave')
    if not waves:
        continue
    path = waves[0].lstrip('*#@><^)}$!?').replace('\\', '/').lower()
    audio_files.add(path)
    for entity in data['entities'].values():
        if entity.get('targetname') == point['sourceentityname']:
            entity['animation_sound'] = path
for path in sorted(audio_files):
    buffer = cm.find_file(TinyPath('sound/' + path))
    if buffer is None:
        raise FileNotFoundError('Missing soundscape audio: ' + path)
    destination = args.output / 'audio' / path
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(buffer.read())
data['audio_files'] = sorted(audio_files)
data['stats']['audio_files'] = len(audio_files)
data['omissions'] = [
    'Godot physics approximates Source VPhysics; ragdoll constraints use pin joints',
    'Bomb/buy volumes are preserved; round economy, planting and explosions are not implemented by the game',
    'Source DSP effects, full entity I/O and particle systems are not emulated',
    'Decals on displacement surfaces and directional bump lightmaps are not imported',
    'Source material shading, water effects, fish and rope motion are approximations',
]
data_path.write_text(json.dumps(data, indent=2))
(args.output / 'import_report.json').write_text(json.dumps({k: v for k, v in data.items()
    if k not in ('materials', 'groups', 'clip_hulls', 'world_hulls', 'world_surfaces', 'displacement_ranges', 'static_collisions', 'entities', 'point_entities', 'soundscapes')}, indent=2))
print('MAP_DETAILS_COMPLETE', json.dumps(data['stats']), flush=True)
