"""Extract stock CSS agents and their original animation dependencies."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import argparse
from concurrent.futures import ThreadPoolExecutor
from css_agents import AGENTS, model_name, animation_models

PROJECT = Path(__file__).resolve().parents[2]
GAME = Path('C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source')
BLENDER = Path('C:/Program Files/Blender Foundation/Blender 5.2/blender.exe')
SOURCEIO = Path(tempfile.gettempdir()) / 'contrapoint-sourceio/SourceIO'
CONTENT = Path(tempfile.gettempdir()) / 'contrapoint-css-player'


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--overwrite', action='store_true')
    parser.add_argument('--agents', nargs='+', choices=AGENTS, default=AGENTS)
    options = parser.parse_args()
    archive = GAME / 'cstrike/cstrike_pak_dir.vpk'
    vpk = GAME / 'bin/vpk.exe'
    entries = subprocess.check_output([str(vpk), 'l', str(archive)], text=True).splitlines()
    selected = [e for e in entries if
                any(e.startswith(f'materials/models/player/{model_name(a)}/') or
                    e in [f'models/player/{model_name(a)}.{ext}' for ext in ('mdl', 'vvd', 'dx90.vtx', 'phy')]
                    for a in options.agents) or
                any(e in animation_models(a) for a in options.agents) or
                e.startswith('materials/models/weapons/w_models/w_knife_t/') or
                e in [f'models/weapons/w_knife_t.{ext}' for ext in ('mdl', 'vvd', 'dx90.vtx')]]
    for e in selected:
        (CONTENT / e).parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(vpk), 'x', str(archive), *selected], cwd=CONTENT, check=True, stdout=subprocess.DEVNULL)
    def convert(agent):
        output = PROJECT / f'assets/players/{agent}'
        output.mkdir(parents=True, exist_ok=True)
        originals = output / 'source/css'
        originals.mkdir(parents=True, exist_ok=True)
        (originals.parent / '.gdignore').touch()
        records = []
        for entry in selected:
            if model_name(agent) not in entry and entry not in animation_models(agent):
                continue
            dest = originals / entry
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(CONTENT / entry, dest)
            records.append({'path': entry, 'sha256': hashlib.sha256(dest.read_bytes()).hexdigest()})
        (output / 'source/provenance.json').write_text(json.dumps({'archive': str(archive), 'files': records}, indent=2))
        if (output / f'{agent}.glb').exists() and not options.overwrite:
            return
        command = [str(BLENDER), '--background', '--factory-startup', '--python-exit-code', '1',
                   '--python', str(PROJECT / 'tools/source_import/import_source1_player.py'), '--',
                   '--sourceio', str(SOURCEIO), '--content-root', str(CONTENT),
                   '--model', str(CONTENT / f'models/player/{model_name(agent)}.mdl'),
                   '--output', str(output), '--name', agent, '--overwrite']
        with (output / 'source/conversion.log').open('w') as log:
            subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, check=True)
        print('AGENT_IMPORTED', agent, flush=True)
    with ThreadPoolExecutor(max_workers=2) as pool:
        list(pool.map(convert, options.agents))
    from css_agent_hitboxes import import_hitboxes
    import_hitboxes(PROJECT, options.agents)
    subprocess.run([str(BLENDER), '--background', '--factory-startup', '--python-exit-code', '1',
                    '--python', str(PROJECT / 'tools/source_import/import_css_ragdolls.py'),
                    '--', *options.agents], check=True)
    output = PROJECT / 'assets/players/knife'
    output.mkdir(parents=True, exist_ok=True)
    if not (output / 'knife_world.glb').exists():
        with (CONTENT / 'knife.log').open('w') as log:
            subprocess.run([str(BLENDER), '--background', '--factory-startup', '--python-exit-code', '1',
                            '--python', str(PROJECT / 'tools/source_import/import_source1_model.py'), '--',
                            '--sourceio', str(SOURCEIO), '--content-root', str(CONTENT),
                            '--model', str(CONTENT / 'models/weapons/w_knife_t.mdl'),
                            '--output', str(output), '--name', 'knife_world', '--no-animations', '--overwrite'],
                           stdout=log, stderr=subprocess.STDOUT, check=True)


if __name__ == '__main__':
    main()
