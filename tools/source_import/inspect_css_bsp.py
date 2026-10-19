"""Inspect the installed BSP without importing Blender or modifying Source files."""
import json
import re
import struct
import sys
from pathlib import Path

raw = Path(sys.argv[1]).read_bytes()
def lump(i):
    offset, size = struct.unpack_from('<ii', raw, 8 + i * 16)
    return raw[offset:offset + size]

entities = [dict(re.findall(r'"([^"\n]*)"\s*"([^"\n]*)"', block))
            for block in re.findall(r'\{([^{}]*)\}', lump(0).decode('ascii', errors='replace'))]
def leaf_at(point):
    index = 0
    while index >= 0:
        plane, front, back = struct.unpack_from('<iii', lump(5), index * 32)
        x, y, z, distance = struct.unpack_from('<4f', lump(1), plane * 20)
        index = front if sum(a*b for a, b in zip(point, (x, y, z))) >= distance else back
    offset = (-index - 1) * 32
    return {'id': -index - 1, 'area': struct.unpack_from('<H', lump(10), offset + 6)[0] & 511,
            'bounds': struct.unpack_from('<6h', lump(10), offset + 8)}

for e in entities:
    if 'origin' in e:
        e['_leaf'] = leaf_at([float(x) for x in e['origin'].split()])
output = Path(sys.argv[2])
output.write_text(json.dumps(entities, indent=2))
print(json.dumps([e for e in entities if e['classname'] in (
    'sky_camera', 'env_fog_controller', 'func_door', 'func_door_rotating',
    'func_rotating', 'func_breakable_surf', 'func_brush', 'func_fish_pool',
    'env_fire', 'ambient_generic', 'light_environment', 'point_devshot_camera')], indent=2))
