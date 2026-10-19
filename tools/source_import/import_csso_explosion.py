"""Import the local CSSO HE particle definitions and their texture sheets. Run in Blender."""
import hashlib
import importlib.util
import json
import re
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

import bpy
import numpy as np

project = Path(__file__).resolve().parents[2]
game = Path('C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source')
sourceio = Path(tempfile.gettempdir()) / 'contrapoint-sourceio/SourceIO'
spec = importlib.util.spec_from_file_location('SourceIO', sourceio / '__init__.py', submodule_search_locations=[str(sourceio)])
module = importlib.util.module_from_spec(spec)
sys.modules['SourceIO'] = module
spec.loader.exec_module(module)
module.register()
from SourceIO.library.utils import datamodel, FileBuffer
from SourceIO.library.source1.vtf import load_texture

output = project / 'assets/effects/csso_explosion'
output.mkdir(parents=True, exist_ok=True)
records = []

def simple(value):
    if isinstance(value, datamodel.Element):
        return dict(name=value.name, **{k: simple(v) for k, v in value.items()})
    if isinstance(value, (list, tuple)):
        return [simple(v) for v in value]
    return value

pcf = game / 'csso/particles/explosions_fx.pcf'
dmx = datamodel.load(path=str(pcf))
systems = {e.name: simple(e) for e in dmx.elements if e.type == 'DmeParticleSystemDefinition'
           and e.name in ('explosion_hegrenade_interior', 'explosion_hegrenade_dirt')}

def sheet(data):
    # VTF 7.3+ resource dictionary: 0x10 is Valve's particle sheet metadata.
    count = struct.unpack_from('<I', data, 68)[0]
    for i in range(count):
        tag, offset = struct.unpack_from('<II', data, 80 + i * 8)
        if tag != 0x10:
            continue
        size, version, sequences = struct.unpack_from('<III', data, offset)
        cursor = offset + 12
        result = {}
        for _ in range(sequences):
            index, clamp, frames, duration = struct.unpack_from('<IIIf', data, cursor)
            cursor += 16
            samples = []
            for _ in range(frames):
                values = struct.unpack_from('<' + 'f' * (17 if version else 5), data, cursor)
                cursor += len(values) * 4
                samples.append({'duration': values[0], 'uv': list(values[1:5])})
            result[str(index)] = {'clamp': bool(clamp), 'duration': duration, 'frames': samples}
        return result
    return {'0': {'clamp': True, 'duration': 1, 'frames': [{'duration': 1, 'uv': [0, 0, 1, 1]}]}}

with tempfile.TemporaryDirectory(prefix='contrapoint-csso-explosion-') as temp:
    root = Path(temp)
    def extract(entry):
        dest = root / entry
        dest.parent.mkdir(parents=True, exist_ok=True)
        for archive_name in ('csso/csso_pak_dir.vpk', 'cstrike/cstrike_pak_dir.vpk', 'hl2/hl2_textures_dir.vpk', 'hl2/hl2_misc_dir.vpk'):
            archive = game / archive_name
            subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), entry], cwd=root, capture_output=True)
            if dest.exists():
                records.append({'archive': str(archive), 'path': entry, 'sha256': hashlib.sha256(dest.read_bytes()).hexdigest()})
                return dest
        raise FileNotFoundError(entry)

    materials = {}
    for system in systems.values():
        for child in system['children']:
            definition = child['child']
            mat = definition['material'].replace('\\', '/')
            if mat in materials:
                continue
            text = extract('materials/' + mat).read_text()
            match = re.search(r'"\$(?:basetexture|normalmap)"\s*"([^"]+)"', text, re.I)
            texture_name = match[1].replace('\\', '/')
            vtf = extract('materials/' + texture_name + '.vtf')
            pixels, height, width = load_texture(FileBuffer(vtf))
            assert pixels is not None, vtf
            name = texture_name.replace('/', '_') + '.png'
            image = bpy.data.images.new(name, width=width, height=height, alpha=True)
            image.pixels.foreach_set(np.flipud(pixels).ravel())
            image.filepath_raw = str(output / name)
            image.file_format = 'PNG'
            image.save()
            bpy.data.images.remove(image)
            materials[mat] = {'texture': name, 'sequences': sheet(vtf.read_bytes()),
                              'additive': bool(re.search(r'"\$additive"\s*"1"', text, re.I)),
                              'mod2x': bool(re.search(r'"\$mod2x"\s*"1"', text, re.I)),
                              'refract': '"Refract"' in text,
                              'dual': '$dualsequence' in text,
                              'depth_blend': bool(re.search(r'"\$depthblend"\s*"1"', text, re.I))}
            print('CSSO_TEXTURE', name, width, height, 'sequences', len(materials[mat]['sequences']))

(output / 'explosion.json').write_text(json.dumps({'systems': systems, 'materials': materials}, indent=1))
(output / 'provenance.json').write_text(json.dumps({'pcf': str(pcf), 'pcf_sha256': hashlib.sha256(pcf.read_bytes()).hexdigest(), 'files': records}, indent=2))
print('CSSO_EXPLOSION_IMPORT_OK')
