"""Decode selected CSS impact textures in an isolated background Blender process."""
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
game = Path(r'C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source')
sourceio = Path(tempfile.gettempdir()) / 'contrapoint-sourceio/SourceIO'
spec = importlib.util.spec_from_file_location('SourceIO', sourceio / '__init__.py', submodule_search_locations=[str(sourceio)])
module = importlib.util.module_from_spec(spec)
sys.modules['SourceIO'] = module
spec.loader.exec_module(module)
module.register()
from SourceIO.library.source1.vtf import load_texture
from SourceIO.library.utils import FileBuffer

families = [('concrete', range(1, 6)), ('wood', range(1, 6)), ('metal', range(6, 8))]
names = [f'decals/{family}/shot{number}' for family, numbers in families for number in numbers]
names += ['decals/manhackcut', 'decals/manhackcut2', 'decals/manhackcut3']
archive = game / 'cstrike/cstrike_pak_dir.vpk'
output = project / 'assets/effects/css'
output.mkdir(parents=True, exist_ok=True)
records = []
with tempfile.TemporaryDirectory(prefix='contrapoint-impacts-') as temporary:
    extracted = Path(temporary)
    files = [f'materials/{name}.{extension}' for name in names for extension in ['vmt', 'vtf']]
    for entry in files:
        (extracted / entry).parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), *files], cwd=extracted, check=True, stdout=subprocess.DEVNULL)
    for name in names:
        source = extracted / f'materials/{name}.vtf'
        rgba, height, width = load_texture(FileBuffer(source))
        image = bpy.data.images.new(name, width=width, height=height, alpha=True)
        image.pixels.foreach_set(np.flipud(rgba.reshape(height, width, 4)).astype(np.float32).ravel())
        image.filepath_raw = str(output / (name.replace('/', '_') + '.png'))
        image.file_format = 'PNG'
        image.save()
        bpy.data.images.remove(image)
        records.append({'source': f'materials/{name}.vtf', 'sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                        'material': (extracted / f'materials/{name}.vmt').read_text(errors='replace')})
(output / 'provenance.json').write_text(json.dumps({'archive': str(archive), 'textures': records}, indent=2))
print('CSS_IMPACTS textures=', len(records))
