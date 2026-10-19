"""Import the local CSSO bullet-impact particle definitions and their texture sheets. Run in Blender."""
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

output = project / 'assets/effects/csso_impact'
output.mkdir(parents=True, exist_ok=True)
records = []

def simple(value):
    if isinstance(value, datamodel.Element):
        return dict(name=value.name, **{k: simple(v) for k, v in value.items()})
    if isinstance(value, (list, tuple)):
        return [simple(v) for v in value]
    return value

pcf = game / 'csso/particles/impact_fx.pcf'
dmx = datamodel.load(path=str(pcf))
systems = {e.name: simple(e) for e in dmx.elements if e.type == 'DmeParticleSystemDefinition'
           and e.name in ('impact_concrete', 'impact_metal', 'impact_wood', 'impact_brick', 'impact_tile', 'impact_glass', 'impact_dirt', 'impact_sand', 'impact_plaster', 'impact_plastic', 'impact_rock', 'impact_grass', 'impact_clay', 'impact_cardboard', 'impact_computer', 'impact_cloth', 'impact_rubber', 'impact_asphalt', 'impact_snow')}

def sheet(data):
    # VTF 7.3+ resource dictionary: 0x10 is Valve's particle sheet metadata.
    major, minor = struct.unpack_from('<II', data, 4)
    count = struct.unpack_from('<I', data, 68)[0] if (major, minor) >= (7, 3) else 0
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
        for mount in ('csso', 'cstrike', 'hl2'):
            loose = game / mount / entry
            if loose.is_file():
                records.append({'file': str(loose), 'sha256': hashlib.sha256(loose.read_bytes()).hexdigest()})
                return loose
        dest = root / entry
        dest.parent.mkdir(parents=True, exist_ok=True)
        for archive_name in ('csso/csso_pak_dir.vpk', 'cstrike/cstrike_pak_dir.vpk', 'hl2/hl2_textures_dir.vpk', 'hl2/hl2_misc_dir.vpk'):
            archive = game / archive_name
            subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), entry], cwd=root, capture_output=True)
            if dest.exists():
                records.append({'archive': str(archive), 'path': entry, 'sha256': hashlib.sha256(dest.read_bytes()).hexdigest()})
                return dest
        raise FileNotFoundError(entry)

    def descendants(system):
        if system['name'].startswith('impact_screen_'):
            return
        yield system
        for child in system.get('children', []):
            yield from descendants(child['child'])

    def flag(text, key):
        # Source accepts both $additive 1 and "$additive" "1".
        clean = re.sub(r'//[^\n]*', '', text)
        return bool(re.search(r'"?\$' + re.escape(key) + r'"?\s+"?1(?:"|\s|$)', clean, re.I))

    materials = {}
    for system in systems.values():
        for definition in descendants(system):
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
                              'additive': flag(text, 'additive'),
                              'mod2x': flag(text, 'mod2x'),
                              'refract': '"Refract"' in text,
                              'dual': flag(text, 'dualsequence'),
                              'source_material': text,
                              'depth_blend': flag(text, 'depthblend')}
            print('CSSO_TEXTURE', name, width, height, 'sequences', len(materials[mat]['sequences']))

(output / 'impact.json').write_text(json.dumps({'systems': systems, 'materials': materials}, indent=1))
(output / 'provenance.json').write_text(json.dumps({'pcf': str(pcf), 'pcf_sha256': hashlib.sha256(pcf.read_bytes()).hexdigest(), 'files': records}, indent=2))
print('CSSO_IMPACT_IMPORT_OK')
