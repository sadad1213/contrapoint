"""Import the installed vanilla CSS bomb, kit and objective audio, read-only."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess

project = Path(__file__).resolve().parents[2]
game = Path('C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source')
content = Path(os.environ['TEMP']) / 'contrapoint-css-objective'
archive = game / 'cstrike/cstrike_pak_dir.vpk'
vpk = game / 'bin/vpk.exe'
entries = subprocess.check_output([str(vpk), 'l', str(archive)], text=True).splitlines()
models = ['v_c4', 'w_c4', 'w_c4_planted', 'w_defuser']
selected = [e for e in entries if any(e.startswith('models/weapons/' + m + '.') and e.endswith(('.mdl', '.vvd', '.dx90.vtx', '.phy')) for m in models)
            or e.startswith(('materials/models/weapons/v_models/c4/', 'materials/models/weapons/v_models/hands/',
                             'materials/models/weapons/w_models/w_c4/', 'materials/models/weapons/w_models/w_eq_defuser/', 'sound/weapons/c4/'))
            or e in ['sound/radio/' + n + '.wav' for n in ['bombdef', 'bombpl', 'ctwin', 'terwin', 'rounddraw', 'go', 'letsgo', 'locknload']]
            or e == 'sound/items/defuser_equip.wav']
for e in selected:
    (content / e).parent.mkdir(parents=True, exist_ok=True)
subprocess.run([str(vpk), 'x', str(archive), *selected], cwd=content, check=True)
for model, destination, name in [('v_c4', 'c4', 'c4'), ('w_c4', 'c4/world', 'c4_world'),
                                  ('w_c4_planted', 'c4/planted', 'c4_planted'), ('w_defuser', 'defuser/world', 'defuser_world')]:
    output = project / 'assets/weapons' / destination
    output.mkdir(parents=True, exist_ok=True)
    log = output / 'source/conversion.log'
    log.parent.mkdir(exist_ok=True)
    (log.parent / '.gdignore').touch()
    args = ['C:/Program Files/Blender Foundation/Blender 5.2/blender.exe', '--background', '--factory-startup', '--python-exit-code', '1',
            '--python', str(project / 'tools/source_import/import_source1_model.py'), '--', '--sourceio',
            str(Path(os.environ['TEMP']) / 'contrapoint-sourceio/SourceIO'), '--content-root', str(content),
            '--model', str(content / ('models/weapons/' + model + '.mdl')), '--output', str(output), '--name', name, '--overwrite']
    if model != 'v_c4':
        args.append('--no-animations')
    with log.open('w', encoding='utf-8') as stream:
        subprocess.run(args, stdout=stream, stderr=subprocess.STDOUT, check=True)
    print('CONVERTED', model, flush=True)
for e in selected:
    if e.startswith('sound/'):
        target = project / 'assets/audio/css' / e[6:]
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(content / e, target)
report = project / 'assets/weapons/c4/source/provenance.json'
report.write_text(json.dumps({'archive': str(archive), 'files': [{'path': e, 'sha256': hashlib.sha256((content/e).read_bytes()).hexdigest()} for e in selected]}, indent=2))
