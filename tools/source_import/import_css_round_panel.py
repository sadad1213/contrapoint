"""Extract the installed CS:S round win panel. Run with Blender."""
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

output = project / 'assets/ui/css/round'
reference = project / 'tools/source_import/references/round'
output.mkdir(parents=True, exist_ok=True)
reference.mkdir(parents=True, exist_ok=True)
textures = ['win_panel_bgstar', 'win_panel_mvpstar', 'avatar_glow_64', 'avatar_default_64']
files = ['resource/ui/win_round.res', 'resource/clientscheme.res', 'scripts/hudlayout.res']
files += [f'materials/hud/{name}.vtf' for name in textures]
files += [f'materials/hud/{name}.vmt' for name in textures]
records = []
with tempfile.TemporaryDirectory(prefix='contrapoint-round-') as directory:
    extracted = Path(directory)
    for name in files:
        (extracted / name).parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), *files], cwd=extracted, check=True)
    for name in files:
        source = extracted / name
        records.append({'source': str(archive) + ':' + name, 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
    for name in files[:3]:
        shutil.copy2(extracted / name, reference / Path(name).name)
    for name in textures:
        shutil.copy2(extracted / f'materials/hud/{name}.vmt', reference / f'{name}.vmt')
    for name in textures:
        rgba, height, width = load_texture(FileBuffer(extracted / f'materials/hud/{name}.vtf'))
        bitmap = bpy.data.images.new(name, width=width, height=height, alpha=True)
        bitmap.pixels.foreach_set(np.flipud(rgba.reshape(height, width, 4)).astype(np.float32).ravel())
        bitmap.filepath_raw = str(output / f'{name}.png')
        bitmap.file_format = 'PNG'
        bitmap.save()
        bpy.data.images.remove(bitmap)
    layout = {key: dict(value) for key, value in keyvalues((extracted / files[0]).read_text())[0][1]}
strings = {}
for language in ('english', 'russian'):
    path = game / f'cstrike/resource/cstrike_{language}.txt'
    tokens = dict(re.findall(r'"([^"\n]+)"\s*"((?:[^"\\]|\\.)*)"', path.read_text(encoding='utf-16')))
    strings[language] = {key.lower(): value.replace('\\n', '\n') for key, value in tokens.items() if key.lower().startswith(('winpanel_', 'funfact_'))}
    records.append({'source': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
(output / 'round.json').write_text(json.dumps({'layout': layout, 'strings': strings}, ensure_ascii=False, indent=2), encoding='utf-8')
(output / 'provenance.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
print('CSS_ROUND_PANEL_IMPORTED')
