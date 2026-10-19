"""Import stock CSS buy layouts, item cards, prices and artwork in Blender."""
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
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

output = project / 'assets/ui/css/buy'
references = project / 'tools/source_import/references/buy'
output.mkdir(parents=True, exist_ok=True)
archive = game / 'cstrike/cstrike_pak_dir.vpk'
entries = subprocess.check_output([str(game / 'bin/vpk.exe'), 'l', str(archive)], text=True).splitlines()
items = ('ak47', 'm4a1', 'awp', 'scout', 'usp45', 'deserteagle', 'hegrenade', 'flashbang', 'smokegrenade', 'kevlar', 'kevlar_helmet',
         'aug', 'elites', 'famas', 'fiveseven', 'g3sg1', 'galil', 'glock18', 'm249', 'm3', 'mac10', 'mp5', 'p228', 'p90', 'sg550', 'sg552', 'tmp', 'ump45', 'xm1014')
classes = (*items, 'base_weapon', 'base_weapon_small', 'base_equipment', 'pistols', 'rifles', 'equipment', 'cancelbutton')
files = [p for p in entries if p.startswith('resource/ui/buy') and p.endswith('.res')]
files += ['classes/' + name + '.res' for name in classes]
files += ['materials/vgui/gfx/vgui/' + name + '.vtf' for name in items]
records = []
for path in files:
    (references / path).parent.mkdir(parents=True, exist_ok=True)
subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), *files], cwd=references, check=True, stdout=subprocess.DEVNULL)
for path in files:
    records.append({'source': str(archive) + ':' + path, 'sha256': hashlib.sha256((references / path).read_bytes()).hexdigest()})

def resource(path):
    text = path.read_text().replace('""', '" "')
    pairs = keyvalues(text)
    result = {}
    for key, value in pairs:
        if key == '#base':
            result.update(resource(path.parent / value))
        elif isinstance(value, list):
            for name, fields in value:
                if isinstance(fields, list):
                    result.setdefault(name, {}).update(dict(fields))
    return result

layouts = {Path(p).stem: resource(references / p) for p in files if p.startswith('resource/ui/')}
cards = {name: resource(references / f'classes/{name}.res') for name in (*items, 'pistols', 'rifles', 'equipment', 'cancelbutton')}
for name in items:
    rgba, height, width = load_texture(FileBuffer(references / f'materials/vgui/gfx/vgui/{name}.vtf'))
    image = bpy.data.images.new(name, width=width, height=height, alpha=True)
    image.pixels.foreach_set(np.flipud(rgba.reshape(height, width, 4)).astype(np.float32).ravel())
    image.filepath_raw = str(output / (name + '.png'))
    image.file_format = 'PNG'
    image.save()
    bpy.data.images.remove(image)
strings = {}
for language in ('english', 'russian'):
    path = game / f'cstrike/resource/cstrike_{language}.txt'
    tokens = dict(re.findall(r'"([^"\n]+)"\s*"((?:[^"\\]|\\.)*)"', path.read_text(encoding='utf-16')))
    strings[language] = {k.lower(): v.replace('\\n', '\n').replace('\\"', '"') for k, v in tokens.items() if k.lower().startswith('cstrike_')}
prices = {fields['command'][4:]: int(fields['cost']) for layout in layouts.values() for fields in layout.values()
          if fields.get('command', '').startswith('buy ') and 'cost' in fields}
(output / 'menus.json').write_text(json.dumps({'layouts': layouts, 'cards': cards, 'strings': strings, 'prices': prices}, ensure_ascii=False, indent=2), encoding='utf-8')
(output / 'provenance.json').write_text(json.dumps(records, indent=2))
print('CSS_BUY_UI_IMPORTED', prices)
