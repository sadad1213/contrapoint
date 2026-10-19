"""Extract the installed CSSO butterfly and T Phoenix arms, then bake their viewmodel.
Run with system Python. The game installation is read-only.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import struct
import io
import zipfile
from PIL import Image

from import_css_audio import events, keyvalues

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--blender', type=Path, required=True)
parser.add_argument('--sourceio', type=Path, required=True)
parser.add_argument('--game', type=Path, default=Path('C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source'))
args = parser.parse_args()
project = Path(__file__).resolve().parents[2]
content = project / '.godot/csso_butterfly_content'
output = project / 'assets/weapons/butterfly'
archive = args.game / 'csso/csso_pak_dir.vpk'
vpk = args.game / 'bin/vpk.exe'
entries = subprocess.check_output([str(vpk), 'l', str(archive)], text=True).splitlines()
prefixes = ['models/weapons/v_knife_butterfly.', 'models/weapons/v_models/arms/glove_fullfinger/v_glove_fullfinger.',
            'materials/models/weapons/v_models/knife_butterfly/', 'materials/models/weapons/v_models/arms/glove_fullfinger/',
            'sound/weapons/knife_butterfly/']
selected = [p for p in entries if any(p.startswith(prefix) for prefix in prefixes) or
            p.startswith('materials/models/weapons/v_models/arms/bare/') and (p.endswith('.vmt') or any(s in p for s in
                ['bare_arm_133.', 'bare_arm_133_exp.', 'bare_arm_normal.', 'bare_arm_masks1.', 'bare_arm_fresnelrange.'])) or
            p.startswith('materials/models/weapons/customization/materials/material_specwarp.') or
            p.startswith('materials/models/weapons/v_models/arms/skin_gradient.')]
for path in selected:
    (content / path).parent.mkdir(parents=True, exist_ok=True)
subprocess.run([str(vpk), 'x', str(archive), *selected], cwd=content, check=True, stdout=subprocess.DEVNULL)
# SourceIO lacks the Character shader. Use the explicit VertexLitGeneric fallback
# supplied by CSSO for this exact skin, in the isolated extraction only.
skin_vmt = content / 'materials/models/weapons/v_models/arms/bare/bare_arm_133.vmt'
original_skin_vmt = skin_vmt.read_bytes()
skin_vmt.write_bytes(skin_vmt.with_name('bare_arm_133_cheap.vmt').read_bytes())
clips = ['draw', 'draw2', 'idle1', 'idle2', 'light_miss1', 'light_miss2', 'light_hit1', 'light_hit2',
         'light_backstab', 'light_backstab2', 'heavy_miss1', 'heavy_hit1', 'heavy_backstab', 'lookat01', 'lookat02', 'lookat03']
mdl = content / 'models/weapons/v_knife_butterfly.mdl'
arms = content / 'models/weapons/v_models/arms/glove_fullfinger/v_glove_fullfinger.mdl'
source = output / 'source'
source.mkdir(parents=True, exist_ok=True)
(source / '.gdignore').touch()
with (source / 'conversion.log').open('w', encoding='utf-8') as log:
    subprocess.run([str(args.blender), '--background', '--factory-startup', '--python-exit-code', '1',
                    '--python', str(project / 'tools/source_import/import_source1_model.py'), '--',
                    '--sourceio', str(args.sourceio.resolve()), '--content-root', str(content), '--model', str(mdl),
                    '--bonemerge-model', str(arms), '--output', str(output), '--name', 'butterfly',
                    '--overwrite', '--bake-sequences', '--skip-sequence-layer', 'hide_lefthand',
                    '--animations', *clips], stdout=log, stderr=subprocess.STDOUT, check=True)

audio = project / 'assets/audio/css'
bank_path = audio / 'bank.json'
bank = json.loads(bank_path.read_text(encoding='utf-8'))
animation_events = {name: value for name, value in events(mdl).items() if name in clips}
needed = {e['sound'] for clip in animation_events.values() for e in clip}
definitions_file = args.game / 'csso/scripts/game_sounds_weapons.txt'
definitions = dict(keyvalues(definitions_file.read_text(encoding='utf-8', errors='replace')))
for name in sorted(needed):
    if name in bank['sounds']:
        continue
    pairs = definitions[name]
    waves = [value for key, value in pairs if key == 'wave']
    for key, value in pairs:
        if key == 'rndwave': waves.extend(wave for _, wave in value)
    paths = [wave.lstrip('*#@><^)}$!?~').replace('\\', '/').lower() for wave in waves]
    assert paths, f'No WAV for {name}'
    volume = next((v for k, v in pairs if k == 'volume'), '1')
    bank['sounds'][name] = {'paths': paths, 'volume': 1 if volume == 'VOL_NORM' else float(volume)}
    for path in paths:
        target = audio / path
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(content / ('sound/' + path), target)
bank['animations']['butterfly'] = animation_events
bank_path.write_text(json.dumps(bank, indent=2) + '\n', encoding='utf-8')
records = []
for path in selected:
    original_bytes = original_skin_vmt if content / path == skin_vmt else (content / path).read_bytes()
    records.append({'path': path, 'sha256': hashlib.sha256(original_bytes).hexdigest()})
    if path.endswith(('.mdl', '.vvd', '.dx90.vtx', '.vmt', '.vtf')):
        target = source / 'csso' / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(original_bytes)
reflection_bsp = args.game / 'csso/maps/de_dust2.bsp'
reflection_path = 'materials/maps/de_dust2/c-1536_1152_112.vtf'
with reflection_bsp.open('rb') as bsp:
    bsp.seek(8 + 40 * 16)
    offset, size, _, _ = struct.unpack('<4i', bsp.read(16))
    bsp.seek(offset)
    with zipfile.ZipFile(io.BytesIO(bsp.read(size))) as pak:
        cube = pak.read(reflection_path)
assert struct.unpack_from('<II', cube, 4) == (7, 5)
assert struct.unpack_from('<i', cube, 52)[0] == 13, 'Expected DXT1 reflection cubemap'
width = struct.unpack_from('<H', cube, 16)[0]
data_offset = next(struct.unpack_from('<I', cube, 84 + i * 8)[0] for i in range(cube[68]) if cube[80 + i * 8:83 + i * 8] == b'0\0\0')
for mip in range(cube[56] - 1, 0, -1):
    side = max(1, width >> mip)
    data_offset += max(1, (side + 3) // 4) ** 2 * 8 * 6
face_bytes = max(1, (width + 3) // 4) ** 2 * 8
reflections = output / 'reflections'
reflections.mkdir(exist_ok=True)
for face in range(6):
    image = Image.frombytes('RGBA', (width, width), cube[data_offset + face * face_bytes:data_offset + (face + 1) * face_bytes], 'bcn', 1)
    image.save(reflections / f'dust2_{face}.png')
(source / 'reflection.vtf').write_bytes(cube)
(source / 'provenance.json').write_text(json.dumps({'archive': str(archive), 'arms': str(arms.relative_to(content)),
    'agent': 'tm_phoenix_varianta', 'agent_definition': 'csso/scripts/playermodels.txt',
    'material_fallback': 'bare_arm_133 uses its authored bare_arm_133_cheap.vmt VertexLitGeneric fallback for glTF conversion',
    'sequence_adaptation': 'Exclude hide_lefthand helper from the combined first-person knife/arms rig to retain the authored offhand pose',
    'files': records, 'sound_definitions': str(definitions_file),
    'reflection': {'bsp': str(reflection_bsp), 'entry': reflection_path, 'sha256': hashlib.sha256(cube).hexdigest()},
    'sound_definitions_sha256': hashlib.sha256(definitions_file.read_bytes()).hexdigest()}, indent=2), encoding='utf-8')
print('CSSO_BUTTERFLY_IMPORTED', len(clips), 'clips;', len(needed), 'sound definitions')
