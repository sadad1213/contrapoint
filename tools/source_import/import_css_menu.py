"""Extract original CSS menu resources; run in background Blender with SourceIO."""
import hashlib
import importlib.util
import json
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

output = project / 'assets/ui/css'
references = project / 'tools/source_import/references/gameui'
output.mkdir(parents=True, exist_ok=True)
references.mkdir(parents=True, exist_ok=True)
textures = {
    'console/background01': 'background',
    'console/background01_widescreen': 'background_widescreen',
    'vgui/maps/menu_thumb_de_dust2': 'de_dust2',
    'vgui/maps/menu_thumb_de_inferno': 'de_inferno',
}
files = [f'materials/{name}.{ext}' for name in textures for ext in ['vtf', 'vmt']]
files += ['resource/gamemenu.res', 'resource/clientscheme.res', 'resource/enginevguilayout.res',
          'resource/game_menu.tga', 'resource/game_menu_mouseover.tga']
records = []
with tempfile.TemporaryDirectory(prefix='contrapoint-menu-') as temporary:
    extracted = Path(temporary)
    for entry in files:
        (extracted / entry).parent.mkdir(parents=True, exist_ok=True)
    archive = game / 'cstrike/cstrike_pak_dir.vpk'
    subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), *files], cwd=extracted, check=True, stdout=subprocess.DEVNULL)
    for name, target in textures.items():
        source = extracted / f'materials/{name}.vtf'
        rgba, height, width = load_texture(FileBuffer(source))
        image = bpy.data.images.new(target, width=width, height=height, alpha=True)
        image.pixels.foreach_set(np.flipud(rgba.reshape(height, width, 4)).astype(np.float32).ravel())
        image.filepath_raw = str(output / (target + '.png'))
        image.file_format = 'PNG'
        image.save()
        bpy.data.images.remove(image)
        records.append({'source': str(archive) + ':' + f'materials/{name}.vtf', 'output': target + '.png',
                        'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
    for name in ['gamemenu.res', 'clientscheme.res', 'enginevguilayout.res']:
        shutil.copy2(extracted / 'resource' / name, references / ('css_' + name))
    for name in ['game_menu.tga', 'game_menu_mouseover.tga']:
        shutil.copy2(extracted / 'resource' / name, output / name)

for relative in ['hl2/resource/newgamedialog.res', 'hl2/resource/newgamechapterpanel.res',
                 'hl2/resource/sourcescheme.res', 'platform/resource/SourceSchemeBase.res']:
    shutil.copy2(game / relative, references / Path(relative).name)
for name in ['cslogo.ttf']:
    source = game / 'cstrike/resource' / name
    shutil.copy2(source, output / name)
    records.append({'source': str(source), 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})

# UI sounds are shipped in the platform directory, outside the CSS VPK.
for name in ['buttonrollover.wav', 'buttonclick.wav', 'buttonclickrelease.wav']:
    candidates = [game / root / 'sound/ui' / name for root in ['platform', 'hl2', 'cstrike']]
    source = next((p for p in candidates if p.exists()), None)
    if source is None:
        # platform uses backslashes in VPK listings on Windows.
        with tempfile.TemporaryDirectory(prefix='contrapoint-ui-sound-') as temporary:
            extracted = Path(temporary)
            entry = 'sound/ui/' + name
            for archive in [game / 'platform/platform_misc_dir.vpk', game / 'hl2/hl2_sound_misc_dir.vpk']:
                if not archive.exists():
                    continue
                listing = subprocess.check_output([str(game / 'bin/vpk.exe'), 'l', str(archive)], text=True).replace('\\', '/')
                if entry not in listing:
                    continue
                (extracted / 'sound/ui').mkdir(parents=True, exist_ok=True)
                subprocess.run([str(game / 'bin/vpk.exe'), 'x', str(archive), entry], cwd=extracted, check=True, stdout=subprocess.DEVNULL)
                source = extracted / entry
                shutil.copy2(source, output / name)
                records.append({'source': str(archive) + ':' + entry, 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
                break
    else:
        shutil.copy2(source, output / name)
        records.append({'source': str(source), 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
(output / 'provenance.json').write_text(json.dumps(records, indent=2))
print('CSS_MENU_IMPORTED', len(textures), 'textures')
