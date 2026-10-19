"""Decode the stock CSS blood effect textures. Run in background Blender."""
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import re
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

output = project / 'assets/effects/css_damage'
output.mkdir(parents=True, exist_ok=True)
names = ['blood_core', 'blood_gore', 'blood_drop', 'blood_puff', 'spark']
records = []
with tempfile.TemporaryDirectory(prefix='contrapoint-blood-') as directory:
    extracted = Path(directory)
    for archive, extension in [('hl2/hl2_misc_dir.vpk', 'vmt'), ('hl2/hl2_textures_dir.vpk', 'vtf')]:
        paths = [f'materials/effects/{name}.{extension}' for name in names]
        for path in paths:
            (extracted / path).parent.mkdir(parents=True, exist_ok=True)
        subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(game / archive), *paths], cwd=directory, check=True, stdout=subprocess.DEVNULL)
    for name in names:
        source = extracted / f'materials/effects/{name}.vtf'
        rgba, height, width = load_texture(FileBuffer(source))
        material = (extracted / f'materials/effects/{name}.vmt').read_text(errors='replace')
        pixels = np.flipud(rgba.reshape(height, width, 4)).astype(np.float32)
        if '"Subrect"' in material:
            x, y = map(int, re.search(r'"\$Pos"\s+"(\d+) (\d+)"', material).groups())
            width, height = map(int, re.search(r'"\$Size"\s+"(\d+) (\d+)"', material).groups())
            pixels = pixels[y:y+height, x:x+width].copy()
        image = bpy.data.images.new(name, width=width, height=height, alpha=True)
        image.pixels.foreach_set(pixels.ravel())
        image.filepath_raw = str(output / f'{name}.png')
        image.file_format = 'PNG'
        image.save()
        bpy.data.images.remove(image)
        records.append({'source': f'materials/effects/{name}.vtf', 'sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                        'material': material})
(output / 'provenance.json').write_text(json.dumps({'archives': ['hl2/hl2_misc_dir.vpk', 'hl2/hl2_textures_dir.vpk'], 'textures': records}, indent=2) + '\n')
print('CSS_DAMAGE_EFFECTS_IMPORTED', len(records))
