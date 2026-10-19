"""Import a local CSS BSP with original face lightmaps in background Blender.

Run with Blender --background --factory-startup --python this_file -- --help.
The game installation is only read. Geometry is built into Godot resources by
build_css_map.gd after this extraction step.
"""
import argparse
import hashlib
import importlib.util
import json
import math
import re
import sys
import struct
import itertools
from collections import defaultdict
from pathlib import Path
from mathutils import Euler, Vector
from mathutils.kdtree import KDTree

import bpy
import numpy as np

parser = argparse.ArgumentParser()
parser.add_argument('--sourceio', type=Path, required=True)
parser.add_argument('--game-root', type=Path, required=True)
parser.add_argument('--map', default='de_dust2')
parser.add_argument('--bsp', type=Path, help='External BSP; game installation supplies shared assets only')
parser.add_argument('--decals-only', action='store_true', help='Refresh decals while preserving existing geometry, entities and detail-pass metadata')
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--probe', action='store_true')
parser.add_argument('--interactive', action='store_true', help='Preserve brush entities, physics props and water volumes')
parser.add_argument('--physics-props', action='store_true', help='Preserve physics props without changing brush entity behavior')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
repo = args.sourceio.resolve()
game = args.game_root.resolve()
out = args.output.resolve()
previous = json.loads((out / 'source/map_data.json').read_text(encoding='utf-8')) if args.decals_only else None
if out.is_relative_to(game):
    raise ValueError('Output must be outside the game installation')
spec = importlib.util.spec_from_file_location('SourceIO', repo / '__init__.py', submodule_search_locations=[str(repo)])
module = importlib.util.module_from_spec(spec)
sys.modules['SourceIO'] = module
spec.loader.exec_module(module)
module.register()
from SourceIO.library.shared.content_manager import ContentManager
from SourceIO.library.shared.content_manager.providers.vpk_provider import VPKContentProvider
from SourceIO.library.shared.content_manager.providers.loose_files import LooseFilesContentProvider
from SourceIO.library.shared.app_id import SteamAppId
from SourceIO.library.source1.bsp.bsp_file import open_bsp
from SourceIO.library.source1.bsp.lumps import *
from SourceIO.library.utils import FileBuffer, TinyPath
from SourceIO.library.source1.vmt import VMT
from SourceIO.library.source1.vtf import load_texture

cm = ContentManager()
# VPK paths are lowercase; some shipped MDL search paths retain uppercase.
original_check = cm.check
cm.check = lambda path: original_check(TinyPath(str(path).replace('\\', '/').lower()))
cm.add_child(VPKContentProvider(TinyPath(str(game / 'cstrike_pak_dir.vpk')), SteamAppId.COUNTER_STRIKE_SOURCE))
hl2 = game.parent / 'hl2' / 'hl2_misc_dir.vpk'
if hl2.exists():
    cm.add_child(VPKContentProvider(TinyPath(str(hl2))))
    cm.add_child(VPKContentProvider(TinyPath(str(hl2.parent / 'hl2_textures_dir.vpk'))))
map_path = args.bsp.resolve() if args.bsp else game / 'maps' / (args.map + '.bsp')
# Map archives commonly ship a cstrike/maps BSP beside cstrike/materials.
# Mount that archive read-only, ahead of stock assets and behind its BSP pak.
loose_root = map_path.parent.parent
loose = None
if args.bsp and (loose_root / 'materials').is_dir():
    loose = LooseFilesContentProvider(TinyPath(str(loose_root)), SteamAppId.COUNTER_STRIKE_SOURCE)
    cm.add_child(loose)
    cm.priority_list = [loose, *[child for child in cm.children if child is not loose]]
if previous and hashlib.sha256(map_path.read_bytes()).hexdigest() != previous['source_sha256']:
    raise ValueError('Decal refresh requires the exact BSP used by the original import')
bsp = open_bsp(TinyPath(str(map_path)), FileBuffer(TinyPath(str(map_path))), cm, SteamAppId.COUNTER_STRIKE_SOURCE)
pak = bsp.get_lump('LUMP_PAK')
if pak:
    cm.add_child(pak)
    # BSP-embedded assets override installed files, including custom VMT patches.
    cm.priority_list = [pak, *([loose] if loose else []), *[child for child in cm.children if child is not pak and child is not loose]]
faces = bsp.get_lump('LUMP_FACES').faces
verts = bsp.get_lump('LUMP_VERTICES').vertices
edges = bsp.get_lump('LUMP_EDGES').edges
surfedges = bsp.get_lump('LUMP_SURFEDGES').surf_edges
infos = bsp.get_lump('LUMP_TEXINFO').texture_info
texdata = bsp.get_lump('LUMP_TEXDATA').texture_data
strings = bsp.get_lump('LUMP_TEXDATA_STRING_TABLE').strings
entities = bsp.get_lump('LUMP_ENTITIES').entities
models = bsp.get_lump('LUMP_MODELS').models
disps = bsp.get_lump('LUMP_DISPINFO')
static_props = bsp.get_lump('LUMP_GAME_LUMP').game_lumps.get('sprp')
print('MAP_PROBE', json.dumps({
    'version': bsp.info.version, 'faces': len(faces), 'vertices': len(verts),
    'materials': strings, 'displacements': len(disps.infos) if disps else 0,
    'props': len(static_props.static_props), 'prop_models': static_props.model_names,
    'entities': {c: sum(e.get('classname') == c for e in entities) for c in sorted({e.get('classname', '') for e in entities})},
    'world': next(e for e in entities if e['classname'] == 'worldspawn'),
    'lumps': {str(i): [l.size, l.version] for i, l in enumerate(bsp.info.lumps) if l.size},
    'pak_files': list(pak._cache)[:30],
}, default=str), flush=True)
if args.probe:
    sys.exit(0)

out.mkdir(parents=True, exist_ok=True)
(out / 'source').mkdir(exist_ok=True)
(out / 'source' / '.gdignore').touch()
(out / 'textures').mkdir(exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
SCALE = 0.0254
ATLAS = 2048
sky_entity = next((e for e in entities if e['classname'] == 'sky_camera'), None)
sky_origin = np.fromstring(sky_entity['origin'], sep=' ') if sky_entity else np.zeros(3)
sky_scale = float(sky_entity['scale']) if sky_entity else 1.0

node_raw, leaf_raw = bsp._get_lump_buffer(5, bsp.info.lumps[5]).read(), bsp._get_lump_buffer(10, bsp.info.lumps[10]).read()
leaf_stride = 56 if bsp.info.version[0] < 20 and bsp.info.lumps[10].version == 0 else 32
plane_raw = bsp._get_lump_buffer(1, bsp.info.lumps[1]).read()

def leaf_at(point, index=0):
    while index >= 0:
        pi, front, back = struct.unpack_from('<iii', node_raw, index * 32)
        *normal, distance = struct.unpack_from('<4f', plane_raw, pi * 20)
        index = front if np.dot(point, normal) >= distance else back
    return -index - 1

sky_area = -1
sky_min, sky_max = np.zeros(3), np.zeros(3)
if sky_entity:
    sky_area = struct.unpack_from('<H', leaf_raw, leaf_at(sky_origin) * leaf_stride + 6)[0] & 511
    sky_bounds = np.array([struct.unpack_from('<6h', leaf_raw, i + 8) for i in range(0, len(leaf_raw), leaf_stride)
                           if struct.unpack_from('<H', leaf_raw, i + 6)[0] & 511 == sky_area])
    sky_min, sky_max = sky_bounds[:, :3].min(axis=0), sky_bounds[:, 3:].max(axis=0)


def lump_bytes(index):
    return bsp._get_lump_buffer(index, bsp.info.lumps[index]).read()


def rgbexp(raw):
    a = np.asarray(raw, dtype=np.uint8).reshape(-1, 4)
    return a[:, :3].astype(np.float32) * np.exp2(a[:, 3].view(np.int8).astype(np.float32))[:, None] / 255.0


def godot_xyz(points, sky=False):
    p = np.asarray(points, dtype=np.float32).reshape(-1, 3).copy()
    if sky:
        p = (p - sky_origin) * sky_scale
    return p[:, [0, 2, 1]] * np.array([SCALE, SCALE, -SCALE])


def sky_region(p):
    # Sky rooms occupy a separate BSP area; Inferno's is below the world.
    if sky_entity is None:
        return False
    center = np.mean(np.asarray(p).reshape(-1, 3), axis=0)
    return bool(np.all(center >= sky_min) and np.all(center <= sky_max))


def save_image(name, pixels, hdr=False):
    h, w = pixels.shape[:2]
    img = bpy.data.images.new(name, width=w, height=h, alpha=True, float_buffer=hdr)
    img.colorspace_settings.name = 'Linear Rec.709' if hdr else 'sRGB'
    # Blender images start at the bottom; our arrays follow Source/Godot top-down UVs.
    img.pixels.foreach_set(np.flipud(pixels).astype(np.float32).ravel())
    img.filepath_raw = str(out / (name + ('.exr' if hdr else '.png')))
    img.file_format = 'OPEN_EXR' if hdr else 'PNG'
    if hdr:
        settings = bpy.context.scene.render.image_settings
        settings.file_format = 'OPEN_EXR'
        settings.color_mode = 'RGB'
        settings.color_depth = '16'
        settings.exr_codec = 'ZIP'
        img.save_render(img.filepath_raw, scene=bpy.context.scene)
    else:
        img.save()
    bpy.data.images.remove(img)
    return name + ('.exr' if hdr else '.png')


texture_cache = {}
materials = dict(previous['materials']) if previous else {}
missing = []


def texture(path):
    path = path.replace('\\', '/').lower().removeprefix('materials/').removesuffix('.vtf')
    if path in texture_cache:
        return texture_cache[path]
    buf = cm.find_file(TinyPath('materials/' + path + '.vtf'))
    if buf is None:
        raise FileNotFoundError('Missing VTF: ' + path)
    rgba, h, w = load_texture(buf)
    if rgba is None:
        raise ValueError('Cannot decode VTF: ' + path)
    name = 'textures/' + re.sub(r'[^a-z0-9_-]', '_', path)
    filename = save_image(name, rgba.reshape(h, w, 4))
    texture_cache[path] = filename
    return filename


def material(name):
    name = name.lower().replace('\\', '/').removeprefix('materials/').removesuffix('.vmt')
    if name in materials:
        return name
    path = TinyPath('materials/' + name + '.vmt')
    buf = cm.find_file(path)
    if buf is None:
        # Some custom maps ship the upper water VMT but omit its named bottom
        # material. Reuse that authored water rather than inventing a texture.
        for candidate in strings if not name.startswith('decals/') else []:
            candidate = candidate.lower().replace('\\', '/')
            if candidate == name:
                continue
            parent_buf = cm.find_file(TinyPath('materials/' + candidate + '.vmt'))
            if parent_buf is None:
                continue
            parent = VMT(parent_buf, candidate, cm)
            if parent.shader.lower() == 'water' and parent.get_string('$bottommaterial', '').lower() == name:
                parent_name = material(candidate)
                materials[name] = dict(materials[parent_name], nocull=1)
                missing.append({'material': name, 'reused_authored_water': parent_name})
                print('WATER_BOTTOM_REUSED', name, parent_name, flush=True)
                return name
        raise FileNotFoundError('Missing VMT: ' + name)
    vmt = VMT(buf, str(path), cm)
    base = vmt.get_string('$basetexture', '') or vmt.get_string('$hdrbasetexture', '')
    base2 = vmt.get_string('$basetexture2', '')
    color, _ = vmt.get_vector('$color', (1, 1, 1))
    if max(color) > 2:
        color = [c / 255 for c in color]
    materials[name] = {
        'texture': texture(base) if base else '',
        'texture2': texture(base2) if base2 else '',
        'shader': vmt.shader,
        'alpha_test': vmt.get_int('$alphatest'),
        'alpha_cutoff': vmt.get_float('$alphatestreference', 0.5),
        'transparent': vmt.get_int('$translucent'),
        'nocull': vmt.get_int('$nocull'),
        'decal': vmt.get_int('$decal') or vmt.shader in ('decalmodulate',),
        'color': list(color),
        'decal_scale': vmt.get_float('$decalscale', 1.0),
        'surface': vmt.get_string('$surfaceprop', 'concrete'),
        'alpha': vmt.get_float('$alpha', 1.0),
        'unlit': vmt.shader.lower() == 'unlitgeneric',
    }
    if vmt.shader.lower() == 'water':
        from SourceIO.library.source1.vtf import load_texture_frames
        normal_path = vmt.get_string('$normalmap', 'dev/water_normal')
        frames, h, w = load_texture_frames(cm.find_file(TinyPath('materials/' + normal_path + '.vtf')))
        if not frames:
            raise ValueError('Missing water animation: ' + normal_path)
        # A horizontal strip keeps the original VTF animation without hundreds of samplers.
        strip = np.concatenate(frames, axis=1)
        entry = materials[name]
        entry['normal_frames'] = len(frames)
        entry['normal_texture'] = save_image('textures/water_normal_frames', strip)
        entry['fog_color'] = list(vmt.get_vector('$fogcolor', (0.2, 0.3, 0.25))[0])
        if max(entry['fog_color']) > 1:
            entry['fog_color'] = [c / 255.0 for c in entry['fog_color']]
        entry['refract_tint'] = list(vmt.get_vector('$refracttint', (1, 1, 1))[0])
        entry['reflect_tint'] = list(vmt.get_vector('$reflecttint', (1, 1, 1))[0])
        entry['fog_start'] = vmt.get_float('$fogstart', 0) * SCALE
        entry['fog_end'] = vmt.get_float('$fogend', 100) * SCALE
        entry['refract_amount'] = vmt.get_float('$refractamount', 0.1)
        entry['reflect_amount'] = vmt.get_float('$reflectamount', 0.8)
        entry['normal_fps'] = 30
        (out / 'source' / 'water_material.txt').write_text(str(vmt.data), encoding='utf-8')
    return name


# Preserve the HDR face samples and pack them with duplicated edge texels.
# Face offsets refer to HDR lighting when using HDR faces (lump 58).
hdr_faces = bsp.info.lumps[58]
if hdr_faces.size:
    from SourceIO.library.source1.bsp.datatypes.face import Face
    buf = bsp._get_lump_buffer(58, hdr_faces)
    faces = []
    while buf:
        faces.append(Face.from_buffer(buf, hdr_faces.version, bsp))
lighting = rgbexp(np.frombuffer(lump_bytes(53 if hdr_faces.size else 8), np.uint8))
atlas = np.zeros((ATLAS, ATLAS, 4), np.float32)
atlas[:, :, 3] = 1
atlas[0:4, 0:4, :3] = 1
rects = {}
patches = {}
x, y, row_h = 4, 0, 4
for fi, face in sorted(enumerate(faces), key=lambda item: -item[1].lightmap_height):
    if face.light_offset < 0 or face.tex_info_id < 0:
        continue
    w, h = face.lightmap_width + 1, face.lightmap_height + 1
    if w <= 0 or h <= 0:
        continue
    if x + w + 2 > ATLAS:
        x, y, row_h = 0, y + row_h, 0
    if y + h + 2 > ATLAS:
        raise RuntimeError('Lightmap atlas is full')
    offset = face.light_offset // 4
    patch = lighting[offset:offset + w * h].reshape(h, w, 3)
    atlas[y:y+h+2, x:x+w+2, :3] = np.pad(patch, ((1, 1), (1, 1), (0, 0)), mode='edge')
    rects[fi] = (x + 1, y + 1, w, h)
    patches[fi] = patch
    x += w + 2
    row_h = max(row_h, h + 2)
atlas_height = 1 << (y + row_h - 1).bit_length()
save_image('lightmap_hdr', atlas[:atlas_height], hdr=True)
LM_SIZE = np.array([ATLAS, atlas_height])
print('LIGHTMAP_ATLAS', len(rects), 'faces, height', y + row_h, flush=True)

# Original ambient cube probes supply baked lighting to props. CSS does not
# store prop VHV vertex colors in this map; use the compiled leaf probes instead.
leaf_raw = lump_bytes(10)
leaf_count = len(leaf_raw) // leaf_stride
index_raw = lump_bytes(51 if hdr_faces.size else 52)
ambient_raw = lump_bytes(55 if hdr_faces.size else 56)
sample_raw = np.frombuffer(ambient_raw, np.uint8).reshape(-1, 28) if index_raw else None
if leaf_stride == 56:
    ambient_raw = b''.join(leaf_raw[i+30:i+54] for i in range(0, len(leaf_raw), leaf_stride))
if not index_raw and len(ambient_raw) != leaf_count * 24:
    raise ValueError('Unsupported legacy leaf ambient cube layout')
probe_points, probe_cubes = [], []
for li in range(leaf_count):
    mins = np.array(struct.unpack_from('<3h', leaf_raw, li * leaf_stride + 8))
    maxs = np.array(struct.unpack_from('<3h', leaf_raw, li * leaf_stride + 14))
    # Older CSS BSPs store one six-direction RGBExp32 cube per leaf,
    # without the newer sample index or quantized sample position.
    if not index_raw:
        probe_points.append((mins + maxs) * 0.5)
        probe_cubes.append(rgbexp(np.frombuffer(ambient_raw[li*24:(li+1)*24], np.uint8)).reshape(6, 3) * 255.0)
        continue
    count, first = struct.unpack_from('<HH', index_raw, li * 4)
    for sample in sample_raw[first:first+count]:
        probe_points.append(mins + (maxs - mins) * sample[24:27] / 255)
        # Ambient cubes store normalized linear vectors; unlike surface luxels,
        # engine ColorRGBExp32ToVector multiplies TexLightToLinear by 255.
        probe_cubes.append(rgbexp(sample[:24]).reshape(6, 3) * 255.0)
probe_cubes = np.asarray(probe_cubes)
tree = KDTree(len(probe_points))
for i, p in enumerate(probe_points):
    tree.insert(p, i)
tree.balance()
print('AMBIENT_PROBES', len(probe_points), flush=True)


def ambient(points, normals):
    result = np.empty((len(points), 3), np.float32)
    for i, (p, n) in enumerate(zip(points, normals)):
        nearest = tree.find_n(p, 4)
        weights = np.array([1.0 / max(d, 8)**2 for _, _, d in nearest])
        cube = sum(probe_cubes[j] * weight for (_, j, _), weight in zip(nearest, weights)) / weights.sum()
        result[i] = sum(cube[axis * 2 + (int(n[axis] < 0))] * n[axis]**2 for axis in range(3))
    return result


groups = defaultdict(list)
displacement_ranges = []
face_points = {}
planes = bsp.get_lump('LUMP_PLANES').planes
disp_verts = bsp.get_lump('LUMP_DISP_VERTS')
face_model = {}
model_entities = {int(e['model'][1:]): e for e in entities if e.get('model', '').startswith('*')}
for mi, model in enumerate(models):
    for fi in range(model.first_face, model.first_face + model.face_count):
        face_model[fi] = mi


def add_triangles(name, points, normals, uv, uv2, colors, triangles, mode='lightmap', collision=True, sky=False, owner=''):
    p = godot_xyz(points, sky)
    n = np.asarray(normals)[:, [0, 2, 1]] * [1, 1, -1]
    triangles = np.asarray(triangles, dtype=np.int32).copy()
    # Godot front faces are clockwise. Test each triangle against its normals.
    cross = np.cross(p[triangles[:, 1]] - p[triangles[:, 0]], p[triangles[:, 2]] - p[triangles[:, 0]])
    flip = np.einsum('ij,ij->i', cross, n[triangles[:, 0]]) > 0
    triangles[flip] = triangles[flip][:, [0, 2, 1]]
    ids = triangles.ravel()
    chunk = tuple(int(a) for a in np.floor(np.mean(p, axis=0)[[0, 2]] / 16))
    key = (name, mode, bool(collision and not sky), *chunk, owner)
    values = np.concatenate([p, n, uv, uv2, colors], axis=1).astype('<f4')
    first = sum(len(a) for a in groups[key])
    groups[key].append(values[ids])
    return key, first, len(ids)


def face_uv(fi, points):
    face = faces[fi]
    info = infos[face.tex_info_id]
    td = texdata[info.texture_data_id]
    tv, lv = np.asarray(info.texture_vectors), np.asarray(info.lightmap_vectors)
    uv = (points @ tv[:, :3].T + tv[:, 3]) / [max(td.view_width, 1), max(td.view_height, 1)]
    if fi in rects:
        rx, ry, w, h = rects[fi]
        luxels = points @ lv[:, :3].T + lv[:, 3] - face.lightmap_texture_mins_in_luxels
        uv2 = (luxels + [rx + 0.5, ry + 0.5]) / LM_SIZE
    else:
        uv2 = np.tile(1.5 / LM_SIZE, (len(points), 1))
    return uv, uv2


world_faces = 0
for fi, face in enumerate(faces):
    mi = face_model.get(fi, 0)
    entity = model_entities.get(mi, {})
    brush_classes = ('func_brush', 'func_illusionary', 'func_lod', 'func_door', 'func_door_rotating', 'func_rotating', 'func_breakable', 'func_breakable_surf', 'func_button', 'func_tanktrain', 'func_physbox', 'func_physbox_multiplayer') if args.interactive else ('func_brush',)
    if mi and entity.get('classname') not in brush_classes:
        continue
    if face.tex_info_id < 0:
        continue
    info = infos[face.tex_info_id]
    td = texdata[info.texture_data_id]
    name = strings[td.name_id].lower()
    if int(info.flags) & (0x4 | 0x80 | 0x100 | 0x200) or name.startswith('tools/'):
        continue
    se = surfedges[face.first_edge:face.first_edge + face.edge_count]
    points = np.array([verts[edges[abs(e), 0 if e >= 0 else 1]] for e in se])
    if len(points) < 3:
        continue
    face_points[fi] = points.copy()
    # Compiled face plane indices already reference the oriented plane pair.
    normal = np.array(planes[face.plane_index].normal)
    colors = np.ones((len(points), 4), np.float32)
    triangles = [[0, i, i+1] for i in range(1, len(points)-1)]
    uv_points = points
    if face.disp_info_id >= 0:
        disp = disps.infos[face.disp_info_id]
        start = np.argmin(np.linalg.norm(points - disp.start_position, axis=1))
        corners = np.roll(points, -start, axis=0)
        size = (1 << disp.power) + 1
        uv_points = np.array([(corners[0]*(1-v)+corners[1]*v)*(1-u) + (corners[3]*(1-v)+corners[2]*v)*u
                              for v in np.linspace(0, 1, size) for u in np.linspace(0, 1, size)])
        dv = disp_verts.vertices[disp.disp_vert_start:disp.disp_vert_start+size*size]
        points = uv_points + dv['position'] * dv['dist']
        colors = np.ones((len(points), 4), np.float32)
        colors[:, 3] = dv['alpha'].ravel() / 255
        triangles = []
        for row in range(size-1):
            for col in range(size-1):
                a = row * size + col
                triangles.extend([[a, a+1, a+size], [a+1, a+size+1, a+size]] if a & 1 else [[a, a+size+1, a+size], [a, a+1, a+size+1]])
    normals = np.tile(normal, (len(points), 1))
    uv, uv2 = face_uv(fi, uv_points)
    if face.disp_info_id >= 0 and fi in rects:
        # Source CCoreDispSurface::CalcLuxelCoords assigns the lightmap to the
        # displacement's start-oriented grid, not its planar texture vectors.
        # Corner 0 = (0,0), 1 = (0,height), 2 = (width,height), 3 = (width,0).
        # The compiled extents also include the displacement's extra luxel.
        rx, ry, w, h = rects[fi]
        grid_u, grid_v = np.meshgrid(np.linspace(0, 1, size), np.linspace(0, 1, size))
        luxels = np.column_stack([grid_u.ravel() * face.lightmap_width,
                                  grid_v.ravel() * face.lightmap_height])
        uv2 = (luxels + [rx + 0.5, ry + 0.5]) / LM_SIZE
    if mi:
        points = points + np.fromstring(entity.get('origin', '0 0 0'), sep=' ')
    mat_name = material(name)
    water = materials[mat_name]['shader'].lower() == 'water'
    solid = not water and entity.get('classname') != 'func_illusionary' and entity.get('solidity', entity.get('Solidity')) != '1'
    owner = 'brush_' + str(mi) if mi and args.interactive else ''
    key, first, count = add_triangles(mat_name, points, normals, uv, uv2, colors, triangles,
        mode='water' if water else 'lightmap', collision=solid, sky=sky_region(points), owner=owner)
    if face.disp_info_id >= 0 and fi in rects:
        rx, ry, w, h = rects[fi]
        displacement_ranges.append({'face': fi, 'displacement': face.disp_info_id,
                                    'group_key': key, 'first_vertex': first, 'vertices': count,
                                    'uv_min': ((np.array([rx, ry]) + 0.5) / LM_SIZE).tolist(),
                                    'uv_max': ((np.array([rx+w-1, ry+h-1]) + 0.5) / LM_SIZE).tolist()})
    world_faces += 1
print('WORLD_GEOMETRY', world_faces, flush=True)
print('COLLINEAR_FACE_PREFIXES', sum(np.linalg.norm(np.cross(p[1]-p[0], p[2]-p[0])) < 1.e-6 for p in face_points.values()), flush=True)


sys.path.insert(0, str(Path(__file__).resolve().parent))
from source_decal_geometry import clip_polygon


decal_count = 0


def emit_decal(name, corners, corner_uv, face_ids):
    global decal_count
    name = material(name)
    # A planar least-squares mapping also supports rotated and stretched overlays.
    source = np.concatenate([corners, np.ones((4, 1))], axis=1)
    mapping = np.linalg.lstsq(source, corner_uv, rcond=None)[0]
    for fi in face_ids:
        if fi not in face_points:
            continue
        face = faces[fi]
        if face.disp_info_id >= 0:
            continue
        n = np.asarray(planes[face.plane_index].normal)
        boundary = face_points[fi]
        plane_d = np.dot(n, boundary[0])
        projected = corners - (corners @ n - plane_d)[:, None] * n
        clipped = clip_polygon(projected, boundary)
        if len(clipped) < 3:
            continue
        uv = np.concatenate([clipped, np.ones((len(clipped), 1))], axis=1) @ mapping
        _, uv2 = face_uv(fi, clipped)
        points = clipped + n * 0.04
        add_triangles(name, points, np.tile(n, (len(points), 1)), uv, uv2,
                      np.ones((len(points), 4)), [[0, i, i+1] for i in range(1, len(points)-1)],
                      mode='decal', collision=False, sky=sky_region(points))
        decal_count += 1


# Compiled overlays include the road surface and long wall wiring.
overlay_lump = bsp.get_lump('LUMP_OVERLAYS')
for overlay in overlay_lump.overlays if overlay_lump else []:
    name = strings[texdata[infos[overlay.tex_info].texture_data_id].name_id]
    # Valve's vbsp/overlay.cpp stores BasisU in the first three unused Z
    # fields, and V's handedness in the fourth. SourceIO's plane property
    # swaps the local X/Y axes and ignores that handedness bit.
    basis_u = overlay.uv_points[:3, 2]
    basis_v = np.cross(overlay.normal, basis_u)
    if overlay.uv_points[3, 2] != 0:
        basis_v = -basis_v
    corners = np.asarray(overlay.origin) + overlay.uv_points[:, :1] * basis_u + overlay.uv_points[:, 1:2] * basis_v
    corner_uv = np.array([[overlay.u[0], overlay.v[0]], [overlay.u[0], overlay.v[1]],
                          [overlay.u[1], overlay.v[1]], [overlay.u[1], overlay.v[0]]])
    emit_decal(name, corners, corner_uv, overlay.face_ids)

# Entity decals include the site marks, arrows, posters and wall damage.
face_ids = np.array(list(face_points))
face_centers = np.array([np.mean(face_points[i], axis=0) for i in face_ids])
face_normals = np.array([planes[faces[i].plane_index].normal for i in face_ids])
face_dist = np.einsum('ij,ij->i', face_centers, face_normals)
for e in entities:
    if e['classname'] != 'infodecal':
        continue
    origin = np.fromstring(e['origin'], sep=' ')
    distance = np.abs(face_normals @ origin - face_dist)
    candidates = np.where(distance < 8)[0]
    candidates = sorted(candidates, key=lambda i: np.linalg.norm(face_centers[i] - origin))
    selected = None
    for i in candidates:
        boundary = face_points[face_ids[i]]
        n = face_normals[i]
        projected = origin - n * (np.dot(n, origin) - face_dist[i])
        center = boundary.mean(axis=0)
        inside = True
        for j, a in enumerate(boundary):
            edge = boundary[(j+1) % len(boundary)] - a
            side = np.cross(edge, n)
            if np.dot(projected - a, side) * np.dot(center - a, side) < -0.01:
                inside = False
                break
        if inside:
            selected = i
            break
    if selected is None:
        continue
    try:
        name = material(e['texture'])
    except FileNotFoundError as error:
        # Unpacked decorative decals may be absent in community BSPs. Record
        # the omission while retaining all playable geometry and collisions.
        missing.append({'decal': e['texture'], 'error': str(error)})
        print('MISSING_DECAL', e['texture'], str(error), flush=True)
        continue
    entry = materials[name]
    image_path = out / entry['texture']
    # PNG IHDR gives dimensions without retaining decoded Blender images.
    w, h = struct.unpack('>II', image_path.read_bytes()[16:24])
    w, h = w * entry['decal_scale'], h * entry['decal_scale']
    n = face_normals[selected]
    up = np.array([0, 0, 1]) if abs(n[2]) < 0.9 else np.array([0, 1, 0])
    right = np.cross(up, n)
    right /= np.linalg.norm(right)
    up = np.cross(n, right)
    corners = np.array([origin - right*w/2 + up*h/2, origin + right*w/2 + up*h/2,
                        origin + right*w/2 - up*h/2, origin - right*w/2 - up*h/2])
    parallel_faces = face_ids[(np.einsum('ij,j->i', face_normals, n) > 0.99) & (np.abs(face_dist - np.dot(n, origin)) < 8)]
    nearby_faces = [int(fi) for fi in parallel_faces if np.linalg.norm(face_points[fi].mean(axis=0) - origin) < max(w, h) + 1500]
    emit_decal(name, corners, np.array([[0, 0], [1, 0], [1, 1], [0, 1]]), nearby_faces)
print('DECAL_POLYGONS', decal_count, flush=True)

if args.decals_only:
    original = (out / 'source/geometry.bin').read_bytes()
    records, chunks, offset, remap = [], [], 0, {}
    for i, entry in enumerate(previous['groups']):
        if entry['mode'] == 'decal':
            continue
        size = entry['vertices'] * 14 * 4
        chunk = original[entry['offset']:entry['offset'] + size]
        remap[i] = len(records)
        records.append(dict(entry, offset=offset))
        chunks.append(chunk)
        offset += size
    for (name, mode, collision, cx, cz, owner), arrays in groups.items():
        if mode != 'decal':
            continue
        data = np.concatenate(arrays).astype('<f4')
        records.append(dict(material=name, mode=mode, collision=collision, chunk=[cx, cz], owner=owner, offset=offset, vertices=len(data)))
        chunk = data.tobytes()
        chunks.append(chunk)
        offset += len(chunk)
    for entry in previous.get('displacement_ranges', []):
        entry['group'] = remap[entry['group']]
    previous.update(groups=records, materials=materials)
    previous['stats'].update(decal_polygons=decal_count, triangles=sum(e['vertices'] // 3 for e in records))
    (out / 'source/geometry.bin').write_bytes(b''.join(chunks))
    (out / 'source/map_data.json').write_text(json.dumps(previous, indent=2), encoding='utf-8')
    report_path = out / 'import_report.json'
    report = json.loads(report_path.read_text(encoding='utf-8'))
    report['stats'] = previous['stats']
    report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('MAP_DECALS_REFRESH_COMPLETE', args.map, decal_count, flush=True)
    sys.exit(0)

# Import each distinct prop once, then transform instances into map coordinates.
from SourceIO.blender_bindings.models import import_model
from SourceIO.blender_bindings.operators.import_settings_base import ModelOptions
options = ModelOptions.default()
options.scale = 1.0
options.import_textures = False
options.import_animations = False
options.bodygroup_vis_switches = False
options.create_flex_drivers = False
prop_cache = {}


def prop_geometry(path, skin):
    key = (path.lower(), skin)
    if key in prop_cache:
        return prop_cache[key]
    tp = TinyPath(path.lower())
    data = cm.find_file(tp)
    if data is None:
        raise FileNotFoundError(path)
    container = import_model(tp, data, cm, options, SteamAppId.COUNTER_STRIKE_SOURCE)
    geometry = []
    # SourceIO imports all bodygroup alternatives. Map props use body 0.
    selected = [objects[0] for objects in container.bodygroups.values() if objects]
    for obj in selected:
        mesh = obj.data
        mesh.calc_loop_triangles()
        positions = np.array([v.co for v in mesh.vertices])
        uv_data = mesh.uv_layers.active.data
        all_uv = np.array([v.uv for v in uv_data])
        all_uv[:, 1] = 1 - all_uv[:, 1]
        skin0 = obj.get('skin_groups', {}).get('0', [])
        skinx = obj.get('skin_groups', {}).get(str(skin), skin0)
        for slot, mat in enumerate(mesh.materials):
            name = mat.get('full_path', mat.name)
            if skinx and skin0:
                base_names = [m if isinstance(m, str) else m.name for m in skin0]
                if mat.name in base_names:
                    replacement = skinx[base_names.index(mat.name)]
                    replacement = bpy.data.materials.get(replacement) if isinstance(replacement, str) else replacement
                    name = replacement.get('full_path', replacement.name)
            loops = np.array([t.loops for t in mesh.loop_triangles if t.material_index == slot], dtype=int).ravel()
            if not len(loops):
                continue
            ids = np.array([mesh.loops[i].vertex_index for i in loops])
            n = np.array([mesh.corner_normals[i].vector for i in loops])
            geometry.append((material(name), positions[ids], n, all_uv[loops]))
    for obj in [*container.objects, *container.attachments, *([container.armature] if container.armature else [])]:
        bpy.data.objects.remove(obj, do_unlink=True)
    prop_cache[key] = geometry
    return geometry


prop_instances = []
for prop in static_props.static_props:
    prop_instances.append((static_props.model_names[prop.prop_type], prop.origin, prop.rotation, prop.skin, prop.solid != 0, prop.scaling, ''))
entity_records = {}
for ei, e in enumerate(entities):
    if e['classname'] in (('prop_physics', 'prop_physics_multiplayer', 'prop_dynamic', 'prop_static') if args.interactive or args.physics_props else ('prop_physics_multiplayer', 'prop_dynamic', 'prop_static')):
        owner = 'entity_' + str(ei) if args.interactive or (args.physics_props and e['classname'].startswith('prop_physics')) else ''
        solid = e.get('solid', '6') != '0'
        prop_instances.append((e['model'], np.fromstring(e['origin'], sep=' '), np.fromstring(e.get('angles', '0 0 0'), sep=' '), int(e.get('skin', 0)), solid, [float(e.get('modelscale', 1))] * 3, owner))
        if owner:
            entity_records[owner] = dict(e)
for i, (path, origin, angles, skin, solid, scaling, owner) in enumerate(prop_instances):
    p, yaw, roll = np.radians(angles)
    rotation = np.array(Euler((roll, p, yaw), 'XYZ').to_matrix())
    for name, local, normals, uv in prop_geometry(path, skin):
        points = (local * scaling) @ rotation.T + origin
        n = normals @ rotation.T
        colors = np.ones((len(points), 4), np.float32)
        # Sample the compiled probes at unique vertices to avoid duplicate work.
        joined, inverse = np.unique(np.round(np.concatenate([points, n], axis=1), 3), axis=0, return_inverse=True)
        colors[:, :3] = ambient(joined[:, :3], joined[:, 3:])[inverse]
        add_triangles(name, points, n, uv, np.zeros_like(uv), colors, np.arange(len(points)).reshape(-1, 3), mode='vertex', collision=solid, sky=sky_region(points), owner=owner)
    if i % 40 == 0:
        print('PROP_INSTANCE', i, '/', len(prop_instances), flush=True)

# Sky cube faces keep the original Source orientation. They are drawn by a
# sky shader using the ray direction in Source coordinates.
world = next(e for e in entities if e['classname'] == 'worldspawn')
sky_textures = {}
for side in ('ft', 'bk', 'lf', 'rt', 'up', 'dn'):
    sky_name = 'skybox/' + world['skyname'] + side
    sky_vmt = cm.find_file(TinyPath('materials/' + sky_name + '.vmt'))
    # HDR sky materials can reference a separate LDR texture; their filename
    # itself may be an unsupported compressed HDR VTF rather than the base map.
    sky_base = VMT(sky_vmt, sky_name, cm).get_string('$basetexture', sky_name) if sky_vmt else sky_name
    sky_textures[side] = texture(sky_base)

# Write flat arrays; the Godot build step stores native meshes and collisions.
clip_hulls, water_hulls = [], []
brush_data, side_data = lump_bytes(18), lump_bytes(19)
def brush_hull(brush_index):
    first, count, contents = struct.unpack_from('<iii', brush_data, brush_index * 12)
    brush_planes = [planes[struct.unpack_from('<H', side_data, si * 8)[0]] for si in range(first, first+count)]
    normals = np.array([p.normal for p in brush_planes])
    distances = np.array([p.dist for p in brush_planes])
    hull = []
    for ids in itertools.combinations(range(count), 3):
        matrix = normals[list(ids)]
        if abs(np.linalg.det(matrix)) < 1.e-6:
            continue
        point = np.linalg.solve(matrix, distances[list(ids)])
        if np.all(normals @ point <= distances + 0.05):
            hull.append(point)
    return np.unique(np.round(hull, 3), axis=0) if len(hull) else np.empty((0, 3))

for bo in range(0, len(brush_data), 12):
    first, count, contents = struct.unpack_from('<iii', brush_data, bo)
    if not contents & (0x10000 | (0x20 if args.interactive else 0)):  # PLAYERCLIP | WATER
        continue
    hull = brush_hull(bo // 12)
    if len(hull) >= 4 and not sky_region(hull):
        (water_hulls if contents & 0x20 else clip_hulls).append(godot_xyz(hull).tolist())

if args.physics_props and not args.interactive:
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from source_prop_physics import extract_physics_props
    extract_physics_props(entity_records, cm)

if args.interactive:
    leaf_brushes = np.frombuffer(lump_bytes(17), '<u2')
    def model_hulls(model):
        pending, indices = [model.head_node], set()
        while pending:
            index = pending.pop()
            if index >= 0:
                pending.extend(struct.unpack_from('<ii', node_raw, index * 32 + 4))
            else:
                first, count = struct.unpack_from('<HH', leaf_raw, (-index - 1) * leaf_stride + 24)
                indices.update(int(x) for x in leaf_brushes[first:first+count])
        return [godot_xyz(hull).tolist() for i in indices if len(hull := brush_hull(i)) >= 4]
    for mi, e in model_entities.items():
        record = dict(e)
        record['hulls'] = model_hulls(models[mi])
        record['position'] = godot_xyz(np.fromstring(e.get('origin', '0 0 0'), sep=' '))[0].tolist()
        entity_records['brush_' + str(mi)] = record
    for record in entity_records.values():
        record.setdefault('position', godot_xyz(np.fromstring(record.get('origin', '0 0 0'), sep=' '))[0].tolist())
records = []
with (out / 'source' / 'geometry.bin').open('wb') as stream:
    for key, arrays in groups.items():
        name, mode, collision, cx, cz, owner = key
        data = np.concatenate(arrays)
        records.append({'material': name, 'mode': mode, 'collision': collision, 'chunk': [cx, cz], 'owner': owner, 'offset': stream.tell(), 'vertices': len(data)})
        stream.write(data.tobytes())
metadata = {
    'map': args.map, 'source_bsp': str(map_path), 'source_sha256': hashlib.sha256(map_path.read_bytes()).hexdigest(),
    'scale': SCALE, 'materials': materials, 'groups': records, 'sky': sky_textures, 'clip_hulls': clip_hulls,
    'material_substitutions': missing,
    'spawns': [{'team': e['classname'].removeprefix('info_player_'), 'position': godot_xyz(np.fromstring(e['origin'], sep=' '))[0].tolist(), 'yaw': float(e.get('angles', '0 0 0').split()[1]) - 90} for e in entities if e['classname'] in ('info_player_terrorist', 'info_player_counterterrorist')],
    'stats': {'world_faces': world_faces, 'lightmapped_faces': len(rects), 'displacements': len(disps.infos) if disps else 0, 'static_props': len(static_props.static_props), 'physics_props_frozen': len(prop_instances) - len(static_props.static_props), 'unique_prop_models': len(prop_cache), 'ambient_probes': len(probe_points), 'textures': len(texture_cache), 'decal_polygons': decal_count, 'clip_hulls': len(clip_hulls), 'triangles': sum(r['vertices'] // 3 for r in records)},
    'omissions': ['Source entity logic, bomb sites and physics prop simulation', 'Particles, soundscapes and ropes', 'Decals projected onto displacement surfaces', 'Directional bump lightmaps and Source cubemap reflections'],
}
if args.interactive:
    metadata.update({'water_hulls': water_hulls, 'entities': entity_records,
        'point_entities': [dict(e) for e in entities if not e.get('model', '').startswith('*')],
        'sky_room': {'enabled': sky_entity is not None, 'origin': sky_origin.tolist(), 'scale': sky_scale, 'area': sky_area,
                     'mins': sky_min.tolist(), 'maxs': sky_max.tolist()},
        'fog': next((dict(e) for e in entities if e['classname'] == 'env_fog_controller'), {})})
    metadata['stats']['physics_props_frozen'] = 0
    metadata['stats']['physics_props'] = sum(e['classname'].startswith('prop_physics') for e in entity_records.values())
    metadata['stats']['water_volumes'] = len(water_hulls)

if args.physics_props and not args.interactive:
    metadata['entities'] = entity_records
    metadata['stats']['physics_props'] = len(entity_records)
    metadata['stats']['physics_props_frozen'] -= len(entity_records)
    metadata['omissions'][0] = 'Source entity logic and bomb sites'
group_indices = {key: i for i, key in enumerate(groups)}
for entry in displacement_ranges:
    entry['group'] = group_indices[tuple(entry.pop('group_key'))]
metadata['displacement_ranges'] = displacement_ranges
(out / 'source' / 'map_data.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
(out / 'import_report.json').write_text(json.dumps({k: v for k, v in metadata.items() if k not in ('materials', 'groups', 'clip_hulls', 'displacement_ranges')}, indent=2), encoding='utf-8')
for image_import in (out / 'textures').glob('*.png.import'):
    text = image_import.read_text(encoding='utf-8')
    image_import.write_text(text.replace('mipmaps/generate=false', 'mipmaps/generate=true'), encoding='utf-8')
print('MAP_IMPORT_COMPLETE', json.dumps(metadata['stats']), flush=True)
