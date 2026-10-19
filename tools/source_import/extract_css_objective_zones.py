"""Repair missing objective brushes without reimporting map geometry or lighting.

Run in the existing SourceIO Blender environment, with --sourceio and --map-data.
"""
import argparse
import hashlib
import importlib.util
import itertools
import json
import struct
import sys
from pathlib import Path
import numpy as np

p = argparse.ArgumentParser()
p.add_argument('--sourceio', type=Path, required=True)
p.add_argument('--map-data', type=Path, required=True)
a = p.parse_args(sys.argv[sys.argv.index('--') + 1:])
spec = importlib.util.spec_from_file_location('SourceIO', a.sourceio / '__init__.py', submodule_search_locations=[str(a.sourceio)])
module = importlib.util.module_from_spec(spec)
sys.modules['SourceIO'] = module
spec.loader.exec_module(module)
module.register()
from SourceIO.library.source1.bsp.bsp_file import open_bsp
from SourceIO.library.source1.bsp.lumps import *
from SourceIO.library.shared.content_manager import ContentManager
from SourceIO.library.shared.app_id import SteamAppId
from SourceIO.library.utils import FileBuffer, TinyPath

d = json.loads(a.map_data.read_text(encoding='utf-8'))
source = Path(d['source_bsp'])
assert hashlib.sha256(source.read_bytes()).hexdigest() == d['source_sha256']
bsp = open_bsp(TinyPath(str(source)), FileBuffer(TinyPath(str(source))), ContentManager(), SteamAppId.COUNTER_STRIKE_SOURCE)
def lump(n):
    return bsp._get_lump_buffer(n, bsp.info.lumps[n]).read()
planes = bsp.get_lump('LUMP_PLANES').planes
models = bsp.get_lump('LUMP_MODELS').models
nodes, leaves, brushes, sides = lump(5), lump(10), lump(18), lump(19)
leaf_stride = 56 if bsp.info.version[0] < 20 and bsp.info.lumps[10].version == 0 else 32
leafbrushes = np.frombuffer(lump(17), '<u2')
def hull(index):
    first, count, _ = struct.unpack_from('<iii', brushes, index*12)
    ps = [planes[struct.unpack_from('<H', sides, i*8)[0]] for i in range(first, first+count)]
    normals = np.array([p.normal for p in ps]); distances = np.array([p.dist for p in ps])
    points = []
    for ids in itertools.combinations(range(count), 3):
        matrix = normals[list(ids)]
        if abs(np.linalg.det(matrix)) < 1e-6:
            continue
        point = np.linalg.solve(matrix, distances[list(ids)])
        if np.all(normals @ point <= distances + .05):
            points.append(point)
    return np.unique(np.round(points, 3), axis=0) if points else np.empty((0,3))
count = 0
for e in bsp.get_lump('LUMP_ENTITIES').entities:
    if e['classname'] not in ('func_bomb_target', 'func_buyzone'):
        continue
    mi = int(e['model'][1:]); pending = [models[mi].head_node]; indices = set()
    while pending:
        i = pending.pop()
        if i >= 0:
            pending.extend(struct.unpack_from('<ii', nodes, i*32+4))
        else:
            first, size = struct.unpack_from('<HH', leaves, (-i-1)*leaf_stride+24)
            indices.update(int(i) for i in leafbrushes[first:first+size])
    e = dict(e)
    e['hulls'] = [(h[:, [0,2,1]] * [.0254,.0254,-.0254]).tolist() for i in sorted(indices) if len(h := hull(i)) >= 4]
    origin = np.fromstring(e.get('origin','0 0 0'), sep=' ')
    e['position'] = (origin[[0,2,1]] * [.0254,.0254,-.0254]).tolist()
    assert e['hulls'], e
    d['entities']['brush_' + str(mi)] = e
    count += 1
a.map_data.write_text(json.dumps(d, indent=2), encoding='utf-8')
print('CSS_OBJECTIVE_BRUSHES', count, flush=True)
