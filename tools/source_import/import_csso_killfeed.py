"""Import installed CSSO HudDeathNotice resources without modifying the game. Run in Blender."""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys
import tempfile

import bpy
import numpy as np

project = Path(__file__).resolve().parents[2]
game = Path('C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source')
mod = game / 'csso'
archive = mod / 'csso_pak_dir.vpk'
sourceio = Path(tempfile.gettempdir()) / 'contrapoint-sourceio/SourceIO'
spec = importlib.util.spec_from_file_location('SourceIO', sourceio / '__init__.py', submodule_search_locations=[str(sourceio)])
module = importlib.util.module_from_spec(spec)
sys.modules['SourceIO'] = module
spec.loader.exec_module(module)
module.register()
from SourceIO.library.source1.vtf import load_texture
from SourceIO.library.utils import FileBuffer
sys.path.insert(0, str(Path(__file__).resolve().parent))
from import_css_audio import keyvalues

output = project / 'assets/ui/csso/killfeed'
references = project / 'tools/source_import/references/killfeed'
output.mkdir(parents=True, exist_ok=True)
references.mkdir(parents=True, exist_ok=True)
records = []

def record(source, path, destination):
    records.append({'source': str(source), 'output': str(destination.relative_to(project)), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})

layout_source = mod / 'scripts/hudlayout.res'
scheme_source = mod / 'resource/clientscheme.res'
layout = dict(dict(keyvalues(layout_source.read_text())[0][1])['huddeathnotice'])
scheme = scheme_source.read_text()
font_block = re.search(r'"HUDDeathNoticeNameFont"[^{}]*\{[^{}]*\{([^{}]*)\}', scheme).group(1)
font_settings = dict(keyvalues(font_block))
for source in (layout_source, scheme_source):
    destination = references / source.name
    shutil.copy2(source, destination)
    record(source, source, destination)
font_source = mod / 'resource/stratum2-bold.ttf'
font_output = output / font_source.name
shutil.copy2(font_source, font_output)
record(font_source, font_source, font_output)
data = font_source.read_bytes()
tables = {}
for index in range(struct.unpack_from('>H', data, 4)[0]):
    tag, _, offset, _ = struct.unpack_from('>4sIII', data, 12 + index * 16)
    tables[tag] = offset
em = struct.unpack_from('>H', data, tables[b'head'] + 18)[0]
ascent, descent = struct.unpack_from('>HH', data, tables[b'OS/2'] + 74)

icons = ['ak47', 'm4a1_silencer', 'm4a1_silencer_off', 'usp_silencer', 'usp_silencer_off',
         'deagle', 'awp', 'ssg08', 'knife', 'knife_t', 'hegrenade', 'suicide',
         'headshot', 'penetrate', 'noscope', 'blind', 'thrusmoke', 'inair']
icons += ['aug', 'elite', 'famas', 'fiveseven', 'g3sg1', 'galilar', 'glock18', 'm249', 'nova', 'mac10',
          'mp5sd', 'p250', 'p90', 'scar20', 'sg553', 'mp9', 'ump45', 'xm1014']
files = [f'materials/vgui/hud/svg/{name}.svg' for name in icons]
files += [f'materials/vgui/hud/deathnotice_corner{index}.vtf' for index in range(1, 5)]
with tempfile.TemporaryDirectory(prefix='contrapoint-killfeed-') as directory:
    extracted = Path(directory)
    for entry in files:
        (extracted / entry).parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), *files], cwd=extracted, check=True)
    for entry in files:
        source = extracted / entry
        if entry.endswith('.svg'):
            destination = output / source.name
            shutil.copy2(source, destination)
        else:
            rgba, height, width = load_texture(FileBuffer(source))
            image = bpy.data.images.new(source.stem, width=width, height=height, alpha=True)
            image.pixels.foreach_set(np.flipud(rgba.reshape(height, width, 4)).astype(np.float32).ravel())
            destination = output / (source.stem + '.png')
            image.filepath_raw = str(destination)
            image.file_format = 'PNG'
            image.save()
            bpy.data.images.remove(image)
        record(f'{archive}:{entry}', source, destination)
base_height = int(re.search(r'"ProportionalBase.Height"\s*"(\d+)"', scheme)[1])
(output / 'layout.json').write_text(json.dumps({'layout': layout, 'font': font_settings,
    'base_height': base_height, 'font_cell_to_em': em / (ascent + descent)}, indent=2))
(output / 'provenance.json').write_text(json.dumps(records, indent=2))
print(f'CSSO_KILLFEED_IMPORTED icons={len(icons)} corners=4 base_height={base_height}')
