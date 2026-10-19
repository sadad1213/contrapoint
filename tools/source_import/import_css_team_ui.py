"""Import stock CSS team/class menus, localization and background textures in Blender."""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.request
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
from SourceIO.library.source1.vtf import load_texture
from SourceIO.library.utils import FileBuffer
sys.path.insert(0, str(Path(__file__).resolve().parent))
from import_css_audio import keyvalues

output = project / 'assets/ui/css/team'
references = project / 'tools/source_import/references/team'
output.mkdir(parents=True, exist_ok=True)
references.mkdir(parents=True, exist_ok=True)
pages = ('urban', 'gsg9', 'sas', 'gign', 'terror', 'leet', 'arctic', 'guerilla', 'autoselect_ct', 'autoselect_t')
textures = ('cs_logo', 'round_corner_nw', 'round_corner_ne', 'round_corner_sw', 'round_corner_se',
            'solid_background', 'trans_background', 'ct_random', 't_random')
resources = ['resource/ui/' + name + '.res' for name in ('teammenu', 'classmenu_ct', 'classmenu_ter')]
resources += ['classes/' + name + '.res' for name in pages] + ['resource/clientscheme.res']
archive = game / 'cstrike/cstrike_pak_dir.vpk'
entries = subprocess.check_output([str(game / 'bin/vpk.exe'), 'l', str(archive)], text=True).splitlines()
files = resources + ['materials/vgui/gfx/vgui/' + name + '.vtf' for name in textures]
files += [p for p in entries if p in ('maps/de_dust2.txt', 'maps/de_inferno.txt', 'maps/default.txt')]
records = []

def record(source, data):
    records.append({'source': str(source), 'sha256': hashlib.sha256(data).hexdigest()})

def resource(path):
    return dict((key, dict(value)) for key, value in keyvalues(path.read_text().replace('""', '" "'))[0][1])

with tempfile.TemporaryDirectory(prefix='contrapoint-team-ui-') as temp:
    extracted = Path(temp)
    for path in files:
        (extracted / path).parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), *files], cwd=extracted, check=True)
    for path in files:
        record(str(archive) + ':' + path, (extracted / path).read_bytes())
    for path in resources:
        destination = references / path
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(extracted / path, destination)
    for name in textures:
        rgba, height, width = load_texture(FileBuffer(extracted / f'materials/vgui/gfx/vgui/{name}.vtf'))
        image = bpy.data.images.new(name, width=width, height=height, alpha=True)
        image.pixels.foreach_set(np.flipud(rgba.reshape(height, width, 4)).astype(np.float32).ravel())
        image.filepath_raw = str(output / (name + '.png'))
        image.file_format = 'PNG'
        image.save()
        bpy.data.images.remove(image)
    layouts = {name: resource(extracted / f'resource/ui/{name}.res') for name in ('teammenu', 'classmenu_ct', 'classmenu_ter')}
    classes = {name: resource(extracted / f'classes/{name}.res') for name in pages}
    maps = {}
    for path in files:
        if path.startswith('maps/'):
            data = (extracted / path).read_bytes()
            maps[Path(path).stem] = data.decode('utf-16' if data[:2] in (b'\xff\xfe', b'\xfe\xff') else 'utf-8', errors='replace').rstrip('\0')

strings = {}
tokens_used = {value['labeltext'].lstrip('#').lower()
               for section in (*layouts.values(), *classes.values())
               for value in section.values() if value.get('labeltext', '').startswith('#')}
for language in ('english', 'russian'):
    path = game / f'cstrike/resource/cstrike_{language}.txt'
    tokens = dict(re.findall(r'"([^"\n]+)"\s*"((?:[^"\\]|\\.)*)"', path.read_text(encoding='utf-16')))
    strings[language] = {key.lower(): value.replace('\\n', '\n').replace('\\"', '"') for key, value in tokens.items() if key.lower() in tokens_used}
    record(path, path.read_bytes())
scheme = dict(keyvalues((references / 'resource/clientscheme.res').read_text().replace('""', '" "'))[0][1])
definition = {'layouts': layouts, 'classes': classes, 'strings': strings, 'maps': maps,
              'colors': dict(scheme['colors']), 'settings': dict(scheme['basesettings'])}
(output / 'menus.json').write_text(json.dumps(definition, indent=2, ensure_ascii=False), encoding='utf-8')
font = Path('C:/Windows/Fonts/verdana.ttf')
shutil.copy2(font, output / font.name)
record(font, font.read_bytes())
for path in ('game/client/cstrike/VGUI/cstriketeammenu.cpp', 'game/client/cstrike/VGUI/cstrikeclassmenu.cpp',
             'game/client/cstrike/VGUI/counterstrikeviewport.cpp', 'game/client/game_controls/teammenu.cpp',
             'game/client/game_controls/classmenu.cpp', 'game/client/cstrike/clientmode_csnormal.cpp',
             'game/server/cstrike/cs_player.cpp', 'game/server/cstrike/cs_client.cpp'):
    url = 'https://raw.githubusercontent.com/nillerusr/source-engine/master/' + path
    data = urllib.request.urlopen(url).read()
    (references / Path(path).name).write_bytes(data)
    record(url, data)
(output / 'provenance.json').write_text(json.dumps(records, indent=2))
print('CSS_TEAM_UI_IMPORTED', len(resources), len(textures))
