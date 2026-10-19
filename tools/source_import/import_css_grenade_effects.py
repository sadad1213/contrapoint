"""Decode CSS smoke and the shared Source explosion sprite; run in Blender."""
import hashlib
import importlib.util
import json
import math
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
from SourceIO.library.source1.vtf import load_texture_frames
from SourceIO.library.utils import FileBuffer

output = project / 'assets/effects/grenades'
output.mkdir(parents=True, exist_ok=True)
records = []
with tempfile.TemporaryDirectory(prefix='contrapoint-grenade-effects-') as temporary:
    extracted = Path(temporary)
    for archive_name, names in [('cstrike/cstrike_pak_dir.vpk', ['particle/particle_smokegrenade', 'particle/particle_smokegrenade_thick']),
                                ('hl2/hl2_textures_dir.vpk', ['sprites/zerogxplode'])]:
        archive = game / archive_name
        for name in names:
            entry = f'materials/{name}.vtf'
            path = extracted / entry
            path.parent.mkdir(parents=True, exist_ok=True)
            subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), entry], cwd=extracted, check=True, stdout=subprocess.DEVNULL)
            frames, height, width = load_texture_frames(FileBuffer(path))
            assert frames, name
            columns = math.ceil(math.sqrt(len(frames)))
            rows = math.ceil(len(frames) / columns)
            atlas = np.zeros((height * rows, width * columns, 4), dtype=np.float32)
            for index, pixels in enumerate(frames):
                x, y = index % columns, index // columns
                atlas[y * height:(y + 1) * height, x * width:(x + 1) * width] = pixels
            image = bpy.data.images.new(name, width=width * columns, height=height * rows, alpha=True)
            image.pixels.foreach_set(np.flipud(atlas).ravel())
            image.filepath_raw = str(output / (Path(name).name + '.png'))
            image.file_format = 'PNG'
            image.save()
            bpy.data.images.remove(image)
            records.append({'archive': str(archive), 'path': entry, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                            'frames': len(frames), 'columns': columns, 'rows': rows, 'frame_size': [width, height]})
(output / 'provenance.json').write_text(json.dumps({'files': records}, indent=2))
print('GRENADE_EFFECTS_OK', len(records))
