"""Extract the installed CS:S scoreboard layout, artwork and typography in Blender."""
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

output = project / 'assets/ui/css/scoreboard'
references = project / 'tools/source_import/references/scoreboard'
output.mkdir(parents=True, exist_ok=True)
references.mkdir(parents=True, exist_ok=True)
archive = game / 'cstrike/cstrike_pak_dir.vpk'
textures = ['vgui/scoreboard/panel-scoreboard', 'vgui/scoreboard/scoreboard-select', 'hud/scoreboard_dead', 'hud/scoreboard_clock',
            'vgui/avatar_default_64', 'vgui/avatar_default-t_64']
resources = ['resource/ui/scoreboard.res', 'resource/clientscheme.res']
files = resources + [f'materials/{name}.vtf' for name in textures]
records = []
def record(source, data):
    records.append({'source': str(source), 'sha256': hashlib.sha256(data).hexdigest()})

with tempfile.TemporaryDirectory(prefix='contrapoint-scoreboard-') as temporary:
    extracted = Path(temporary)
    for name in files:
        (extracted / name).parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), *files], cwd=extracted, check=True)
    for name in files:
        record(str(archive) + ':' + name, (extracted / name).read_bytes())
    for name in resources:
        destination = references / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(extracted / name, destination)
    for name in textures:
        rgba, height, width = load_texture(FileBuffer(extracted / f'materials/{name}.vtf'))
        bitmap = bpy.data.images.new(Path(name).name, width=width, height=height, alpha=True)
        bitmap.pixels.foreach_set(np.flipud(rgba.reshape(height, width, 4)).astype(np.float32).ravel())
        bitmap.filepath_raw = str(output / (Path(name).name + '.png'))
        bitmap.file_format = 'PNG'
        bitmap.save()
        bpy.data.images.remove(bitmap)
    layout = {key: dict(value) for key, value in keyvalues((extracted / resources[0]).read_text().replace('""', '" "'))[0][1]}

strings = {}
for language in ('english', 'russian'):
    path = game / f'cstrike/resource/cstrike_{language}.txt'
    tokens = dict(re.findall(r'"([^"\n]+)"\s*"((?:[^"\\]|\\.)*)"', path.read_text(encoding='utf-16')))
    strings[language] = {key.lower(): value for key, value in tokens.items() if key.lower().startswith(('cstrike_scoreboard_', 'cstrike_sb_', 'cstrike_team_'))}
    record(path, path.read_bytes())
for font in ('verdana.ttf', 'verdanab.ttf'):
    path = Path('C:/Windows/Fonts') / font
    shutil.copy2(path, output / font)
    record(path, path.read_bytes())
(output / 'scoreboard.json').write_text(json.dumps({'layout': layout, 'strings': strings}, indent=2, ensure_ascii=False), encoding='utf-8')
(output / 'provenance.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
print('CSS_SCOREBOARD_IMPORTED')
