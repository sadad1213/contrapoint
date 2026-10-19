"""Extract the stock CS:S sniper HUD textures using the existing SourceIO setup."""
import hashlib
import importlib.util
import json
from pathlib import Path
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

output = project / 'assets/ui/css/scope'
output.mkdir(parents=True, exist_ok=True)
archive = game / 'cstrike/cstrike_pak_dir.vpk'
records = []
with tempfile.TemporaryDirectory(prefix='contrapoint-scope-') as temporary:
    extracted = Path(temporary)
    textures = ['sprites/scope_arc', 'overlays/scope_lens']
    files = [f'materials/{name}.{ext}' for name in textures for ext in ['vtf', 'vmt']]
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
(output / 'provenance.json').write_text(json.dumps(records, indent=2))
print('CSS_SCOPE_IMPORTED', len(records))
