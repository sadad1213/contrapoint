"""Complete any imported BSP with compiled collision, prop PHY and map audio.

Run with Blender --background --python-exit-code 1 --python this_file --
--sourceio PATH --game-root PATH --output assets/maps/MAP.
"""
import argparse
import importlib.util
import itertools
import json
import struct
import re
import sys
from pathlib import Path

import numpy as np
import bpy

parser = argparse.ArgumentParser()
parser.add_argument('--sourceio', type=Path, required=True)
parser.add_argument('--game-root', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
args.output = args.output.resolve()
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
from SourceIO.library.utils import FileBuffer, TinyPath
sys.path.insert(0, str(Path(__file__).resolve().parent))
from source_prop_physics import extract_physics_props

cm = ContentManager()
archives = [args.game_root / 'cstrike_pak_dir.vpk']
archives += [args.game_root.parent / 'hl2' / name for name in ('hl2_misc_dir.vpk', 'hl2_textures_dir.vpk', 'hl2_sound_misc_dir.vpk')]
providers = [VPKContentProvider(TinyPath(str(p))) for p in archives if p.exists()]
for provider in providers:
    cm.add_child(provider)
data_path = args.output / 'source/map_data.json'
data = json.loads(data_path.read_text(encoding='utf-8'))
bsp = open_bsp(TinyPath(data['source_bsp']), FileBuffer(TinyPath(data['source_bsp'])), cm, SteamAppId.COUNTER_STRIKE_SOURCE)
pak = bsp.get_lump('LUMP_PAK')
cm.add_child(pak)
cm.priority_list = [pak, *providers]

def lump(index):
    return bsp._get_lump_buffer(index, bsp.info.lumps[index]).read()

def xyz(points):
    return np.asarray(points)[:, [0, 2, 1]] * [0.0254, 0.0254, -0.0254]

sky = data['sky_room']
def is_sky(point):
    return sky.get('enabled', True) and bool(np.all(point >= np.array(sky['mins'])) and np.all(point <= np.array(sky['maxs'])))

nodes, leaves = lump(5), lump(10)
leaf_brushes = np.frombuffer(lump(17), '<u2')
brushes, sides = lump(18), lump(19)
planes = bsp.get_lump('LUMP_PLANES').planes
infos = bsp.get_lump('LUMP_TEXINFO').texture_info
texdata = bsp.get_lump('LUMP_TEXDATA').texture_data
names = bsp.get_lump('LUMP_TEXDATA_STRING_TABLE').strings
pending = [bsp.get_lump('LUMP_MODELS').models[0].head_node]
indices = set()
while pending:
    index = pending.pop()
    if index >= 0:
        pending.extend(struct.unpack_from('<ii', nodes, index*32+4))
    else:
        first, count = struct.unpack_from('<HH', leaves, (-index-1)*32+24)
        indices.update(int(i) for i in leaf_brushes[first:first+count])
world_hulls, surfaces, ladders = [], [], []
for index in sorted(indices):
    first, count, contents = struct.unpack_from('<iii', brushes, index*12)
    records = [struct.unpack_from('<Hhhh', sides, i*8) for i in range(first, first+count)]
    ladder = bool(contents & 0x20000000)
    if not contents & (1 | 2 | 8) and not ladder:
        continue
    normals = np.array([planes[p].normal for p, _, _, _ in records])
    distances = np.array([planes[p].dist for p, _, _, _ in records])
    active = [i for i, (_, _, _, bevel) in enumerate(records) if not bevel]
    points = []
    for ids in itertools.combinations(active, 3):
        matrix = normals[list(ids)]
        if abs(np.linalg.det(matrix)) < 1.e-6:
            continue
        point = np.linalg.solve(matrix, distances[list(ids)])
        if np.all(normals @ point <= distances + .05):
            points.append(point)
    if len(points) < 4 or is_sky(np.mean(points, axis=0)):
        continue
    hull = xyz(np.unique(np.round(points, 3), axis=0)).tolist()
    if ladder:
        ladders.append(hull)
    if contents & (1 | 2 | 8):
        world_hulls.append(hull)
        candidates = []
        for pi, ti, _, _ in records:
            if ti < 0:
                continue
            name = names[texdata[infos[ti].texture_data_id].name_id].lower()
            if not name.startswith('tools/'):
                candidates.append((planes[pi].normal[2], name))
        name = max(candidates)[1] if candidates else ''
        surfaces.append(data['materials'].get(name, {}).get('surface', 'concrete'))
data.update(world_hulls=world_hulls, world_surfaces=surfaces, ladder_hulls=ladders)

props = bsp.get_lump('LUMP_GAME_LUMP').game_lumps['sprp']
static = {}
for i, prop in enumerate(props.static_props):
    if not prop.solid or is_sky(prop.origin):
        continue
    static[str(i)] = dict(model=props.model_names[prop.prop_type], origin=' '.join(map(str, prop.origin)),
                         angles=' '.join(map(str, prop.rotation)), modelscale=float(prop.scaling[0]))
extract_physics_props(static, cm)
data['static_collisions'] = [dict(model=e['model'], surface=e['surface'], original_phy=e['original_phy'],
    hulls=[(np.array(h) + e['position']).tolist() for h in e['physics_hulls']]) for e in static.values()]
dynamic = {key: e for key, e in data['entities'].items() if e['classname'].startswith('prop_') and e.get('solid', '6') != '0' and not is_sky(np.fromstring(e.get('origin', '0 0 0'), sep=' '))}
extract_physics_props(dynamic, cm)
for entity in data['entities'].values():
    origin = np.fromstring(entity.get('origin', '0 0 0'), sep=' ')
    if is_sky(origin):
        entity['position'] = xyz([(origin - np.array(sky['origin'])) * sky['scale']])[0].tolist()
        entity['sky_scale'] = sky['scale']

audio = []
for entity in data['point_entities']:
    if entity['classname'] != 'ambient_generic':
        continue
    path = entity.get('message', '').lstrip('*/#@')
    buffer = cm.find_file(TinyPath('sound/' + path))
    if buffer is None:
        raise FileNotFoundError('Map audio: ' + path)
    destination = args.output / 'audio' / path
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(buffer.read())
    audio.append(path)

# Reuse the native glow renderer for maps without Inferno's detail pass.
from SourceIO.library.source1.vmt import VMT
from SourceIO.library.source1.vtf import load_texture_frames
effects, effect_textures = [], {}
for index, original in enumerate(data['point_entities']):
    if original['classname'] != 'env_sprite':
        continue
    entity = dict(original)
    entity.setdefault('hammerid', str(index))
    name = entity.get('model', '').replace('\\', '/').lower().removeprefix('materials/').removesuffix('.spr').removesuffix('.vmt')
    if name not in effect_textures:
        vmt = VMT(cm.find_file(TinyPath('materials/' + name + '.vmt')), name, cm)
        base = vmt.get_string('$basetexture', '').lower()
        frames, height, width = load_texture_frames(cm.find_file(TinyPath('materials/' + base + '.vtf')))
        if not frames:
            raise ValueError('Missing sprite texture: ' + name)
        image = bpy.data.images.new('map_sprite', width=width, height=height, alpha=True)
        image.pixels.foreach_set(np.flipud(frames[0].reshape(height, width, 4)).astype(np.float32).ravel())
        filename = 'textures/' + re.sub(r'[^a-z0-9_]', '_', base) + '_glow.png'
        image.filepath_raw = str(args.output / filename)
        image.file_format = 'PNG'
        image.save()
        bpy.data.images.remove(image)
        effect_textures[name] = dict(texture=filename, frames=1, width=width, height=height)
    effects.append(dict(entity=entity, **effect_textures[name]))
data['effects'] = effects
data['stats']['sprite_glows'] = len(effects)

for entity in data['entities'].values():
    if entity['classname'] != 'func_breakable':
        continue
    kind = int(entity.get('material', 0))
    family = 'glass/glass_sheet' if kind == 0 else 'wood/wood_box' if kind == 1 else 'metal/metal_box' if kind == 2 else 'concrete/concrete'
    path = 'physics/' + family + '_break1.wav'
    entity['break_sound'] = path
    if path not in audio:
        buffer = cm.find_file(TinyPath('sound/' + path))
        if buffer is None:
            raise FileNotFoundError('Breakable audio: ' + path)
        destination = args.output / 'audio' / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(buffer.read())
        audio.append(path)
data['stats'].update(world_collision_brushes=len(world_hulls), ladder_volumes=len(ladders),
    original_static_collisions=sum(e['original_phy'] for e in static.values()), audio_files=len(set(audio)))
data['omissions'] = ['Decals projected onto displacement surfaces', 'Directional bump lightmaps and Source cubemap reflections']
# The native builder instantiates only weapons implemented by the shared pickup system.
weapon_solids = json.loads((args.output.parents[1] / 'physics/css_weapon_solids.json').read_text(encoding='utf-8'))
weapon_counts = {}
for entity in data['point_entities']:
    classname = entity['classname']
    if classname.startswith('weapon_'):
        weapon_counts[classname] = weapon_counts.get(classname, 0) + 1
data['stats']['weapon_pickups'] = sum(count for name, count in weapon_counts.items() if name in weapon_solids)
data['unsupported_weapons'] = {name: count for name, count in weapon_counts.items() if name not in weapon_solids}
if data['unsupported_weapons']:
    data['omissions'].append('Unimplemented weapon pickups: ' + ', '.join(f'{name} ({count})' for name, count in data['unsupported_weapons'].items()))
if any(e['classname'] in ('env_fire', 'env_explosion') for e in data['point_entities']):
    data['omissions'].append('Bomb objective game rules and bomb-triggered fire/explosion effects')
if any(e['classname'] == 'point_servercommand' for e in data['point_entities']):
    data['omissions'].append('Server plugins and point_servercommand')
data_path.write_text(json.dumps(data, indent=2), encoding='utf-8')
(args.output / 'import_report.json').write_text(json.dumps({k: v for k, v in data.items() if k not in
    ('materials', 'groups', 'clip_hulls', 'displacement_ranges', 'world_hulls', 'world_surfaces', 'static_collisions', 'ladder_hulls', 'entities', 'point_entities', 'water_hulls')}, indent=2), encoding='utf-8')
print('MAP_GAMEPLAY_COMPLETE', json.dumps(data['stats']), flush=True)
