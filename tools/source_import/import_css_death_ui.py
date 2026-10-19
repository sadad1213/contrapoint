"""Import the installed CSS freeze panel, fonts and materials. Run in Blender."""
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
import urllib.request

import bpy
import numpy as np

project = Path(__file__).resolve().parents[2]
game = Path(r'C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source')
archive = game / 'cstrike/cstrike_pak_dir.vpk'
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

output = project / 'assets/ui/css/death'
references = project / 'tools/source_import/references/death'
output.mkdir(parents=True, exist_ok=True)
references.mkdir(parents=True, exist_ok=True)
records = []

def record(source, destination):
    records.append({'source': str(source), 'output': str(destination), 'sha256': hashlib.sha256(destination.read_bytes()).hexdigest()})

textures = ['hud/avatar_glow_64', 'hud/freeze_nemesis', 'hud/freeze_revenge',
            'vgui/avatar_default-t_64', 'vgui/avatar_default_64']
resources = ['resource/ui/freezepanel_basic.res', 'resource/clientscheme.res']
files = resources + ['sound/ui/freeze_cam.wav'] + [f'materials/{name}.{ext}' for name in textures for ext in ('vmt', 'vtf')]
with tempfile.TemporaryDirectory(prefix='contrapoint-freezepanel-') as temporary:
    extracted = Path(temporary)
    for entry in files:
        (extracted / entry).parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), *files], cwd=extracted, check=True)
    sound = project / 'assets/audio/css/ui/freeze_cam.wav'
    sound.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(extracted / 'sound/ui/freeze_cam.wav', sound)
    record(f'{archive}:sound/ui/freeze_cam.wav', sound)
    for entry in resources:
        destination = references / Path(entry).name
        shutil.copy2(extracted / entry, destination)
        record(f'{archive}:{entry}', destination)
    for name in textures:
        source = extracted / f'materials/{name}.vtf'
        rgba, height, width = load_texture(FileBuffer(source))
        image = bpy.data.images.new(Path(name).name, width=width, height=height, alpha=True)
        image.pixels.foreach_set(np.flipud(rgba.reshape(height, width, 4)).astype(np.float32).ravel())
        image.filepath_raw = str(output / (Path(name).name + '.png'))
        image.file_format = 'PNG'
        image.save()
        bpy.data.images.remove(image)
        record(f'{archive}:materials/{name}.vtf', source)
        shutil.copy2(extracted / f'materials/{name}.vmt', references / (Path(name).name + '.vmt'))

layout = dict(keyvalues((references / 'freezepanel_basic.res').read_text())[0][1])
panel = dict(layout['freezepanelbg'])
panel = {key: dict(value) if isinstance(value, list) else value for key, value in panel.items()}
scheme = (references / 'clientscheme.res').read_text()
fonts = {}
for name in ('FreezeSmall', 'FreezeMedium', 'FreezeLarge'):
    block = re.search(r'"' + name + r'"[^{}]*\{[^{}]*\{([^{}]*)\}', scheme).group(1)
    block = re.sub(r'^.*\[\$OSX\].*$', '', block, flags=re.M).replace('[!$OSX]', '')
    fonts[name.lower()] = dict(keyvalues(block))
strings = {}
for language in ('english', 'russian'):
    source = game / f'cstrike/resource/cstrike_{language}.txt'
    text = source.read_text(encoding='utf-16')
    strings[language] = {key.lower(): value.strip() for key, value in re.findall(r'"(FreezePanel_[^"\r\n]+)"\s*"([^"]*)"', text)}
    (references / f'freeze_strings_{language}.json').write_text(json.dumps(strings[language], ensure_ascii=False, indent=2), encoding='utf-8')
    record(source, source)

font = Path('C:/Windows/Fonts/verdanab.ttf')
shutil.copy2(font, output / font.name)
record(font, output / font.name)
# Positive GDI lfHeight is a Windows font cell, whereas Godot's font_size is em.
data = font.read_bytes()
tables = {}
for i in range(struct.unpack_from('>H', data, 4)[0]):
    tag, _, offset, length = struct.unpack_from('>4sIII', data, 12 + i * 16)
    tables[tag] = offset
em = struct.unpack_from('>H', data, tables[b'head'] + 18)[0]
ascent, descent = struct.unpack_from('>HH', data, tables[b'OS/2'] + 74)
(output / 'freezepanel.json').write_text(json.dumps({'panel': panel, 'fonts': fonts, 'strings': strings,
    'font_cell_to_em': em / (ascent + descent)}, ensure_ascii=False, indent=2), encoding='utf-8')

for path in ('game/client/cstrike/cs_hud_freezepanel.cpp', 'game/client/cstrike/VGUI/bordered_panel.cpp',
             'game/client/cstrike/VGUI/backgroundpanel.cpp'):
    url = 'https://raw.githubusercontent.com/nillerusr/source-engine/master/' + path
    destination = references / Path(path).name
    destination.write_bytes(urllib.request.urlopen(url).read())
    record(url, destination)
(output / 'provenance.json').write_text(json.dumps(records, indent=2))
print('CSS_DEATH_UI_IMPORTED textures=5 fonts=3 layout=FreezePanel_Basic.res')
