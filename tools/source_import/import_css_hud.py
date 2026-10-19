"""Extract stock CSS HUD assets. Run with Blender and the existing SourceIO checkout."""
import hashlib
import importlib.util
import json
import re
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

import bpy
import numpy as np

project = Path(__file__).resolve().parents[2]
game = Path(r'C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source')
sourceio = Path(tempfile.gettempdir()) / 'contrapoint-sourceio/SourceIO'
spec = importlib.util.spec_from_file_location('SourceIO', sourceio / '__init__.py', submodule_search_locations=[str(sourceio)])
module = importlib.util.module_from_spec(spec)
sys.modules['SourceIO'] = module
spec.loader.exec_module(module)
module.register()
from SourceIO.library.source1.vtf import load_texture
from SourceIO.library.utils import FileBuffer

output = project / 'assets/ui/css/hud'
references = project / 'tools/source_import/references/hud'
output.mkdir(parents=True, exist_ok=True)
references.mkdir(parents=True, exist_ok=True)
textures = ['sprites/640hud1', 'sprites/radar', 'sprites/radar_trans',
            'overviews/de_dust2_radar', 'overviews/de_inferno_radar']
files = [f'materials/{name}.vtf' for name in textures]
files += ['scripts/hudlayout.res', 'scripts/hudanimations.txt', 'scripts/mod_textures.txt',
          'resource/overviews/de_dust2.txt', 'resource/overviews/de_inferno.txt']
records = []
archive = game / 'cstrike/cstrike_pak_dir.vpk'
with tempfile.TemporaryDirectory(prefix='contrapoint-hud-') as temporary:
    extracted = Path(temporary)
    for entry in files:
        (extracted / entry).parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), *files], cwd=extracted, check=True)
    for name in textures:
        source = extracted / f'materials/{name}.vtf'
        rgba, height, width = load_texture(FileBuffer(source))
        image = bpy.data.images.new(Path(name).name, width=width, height=height, alpha=True)
        image.pixels.foreach_set(np.flipud(rgba.reshape(height, width, 4)).astype(np.float32).ravel())
        image.filepath_raw = str(output / (Path(name).name + '.png'))
        image.file_format = 'PNG'
        image.save()
        bpy.data.images.remove(image)
        records.append({'source': str(archive) + ':' + f'materials/{name}.vtf',
                        'output': Path(name).name + '.png', 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
    for entry in files[len(textures):]:
        shutil.copy2(extracted / entry, references / Path(entry).name)
    overviews = {}
    for map_id in ['de_dust2', 'de_inferno']:
        text = (extracted / f'resource/overviews/{map_id}.txt').read_text()
        values = dict(re.findall(r'"([^"]+)"\s*"([^"]+)"', text))
        overviews[map_id] = {key: float(values[key]) for key in ['pos_x', 'pos_y', 'scale']}
    (output / 'overviews.json').write_text(json.dumps(overviews, indent=2))
for name in ['cstrike.ttf', 'cs.ttf', 'csd.ttf']:
    source = game / 'cstrike/resource' / name
    shutil.copy2(source, output / name)
    records.append({'source': str(source), 'output': name, 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
(output / 'provenance.json').write_text(json.dumps(records, indent=2))
print('CSS_HUD_IMPORTED', len(records))
