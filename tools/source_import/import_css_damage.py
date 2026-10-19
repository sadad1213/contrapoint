"""Import stock CSS damage sounds, weapon scripts and bone-local MDL hitboxes.

Requires ICECipher (pip install ICECipher), or --dependencies pointing to a pip
--target directory. Reads the installed game; never changes it.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
from import_css_audio import keyvalues

PROJECT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game', type=Path, default=Path('C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source'))
    parser.add_argument('--dependencies', type=Path)
    args = parser.parse_args()
    if args.dependencies:
        sys.path.insert(0, str(args.dependencies))
    from ICECipher import IceKey
    archive = args.game / 'cstrike/cstrike_pak_dir.vpk'
    vpk = args.game / 'bin/vpk.exe'
    entries = subprocess.check_output([str(vpk), 'l', str(archive)], text=True).splitlines()
    scripts = ['scripts/game_sounds.txt', 'scripts/game_sounds_physics.txt', 'scripts/game_sounds_world.txt']
    selected = scripts + ['scripts/dsp_presets.txt'] + [p for p in entries if p.startswith('scripts/weapon_') and p.endswith('.ctx')]
    output = PROJECT / 'assets/physics/css_damage'
    output.mkdir(parents=True, exist_ok=True)
    records = []
    with tempfile.TemporaryDirectory(prefix='contrapoint-damage-') as directory:
        extracted = Path(directory)
        def extract(paths, source=archive):
            for p in paths:
                (extracted / p).parent.mkdir(parents=True, exist_ok=True)
            subprocess.run([str(vpk), 'x', str(source), *paths], cwd=directory, check=True, stdout=subprocess.DEVNULL)
            for p in paths:
                records.append({'path': p, 'sha256': hashlib.sha256((extracted / p).read_bytes()).hexdigest()})
        extract(selected)
        weapons = {}
        for path in selected:
            if not path.endswith('.ctx'):
                continue
            text = IceKey(0, list(b'd7NSuLq2')).Decrypt((extracted / path).read_bytes()).decode('utf8').rstrip('\0')
            props = dict(keyvalues(text)[0][1])
            weapons[Path(path).stem.removeprefix('weapon_')] = {
                'damage': int(props.get('damage', 42)), 'armor_ratio': float(props.get('weaponarmorratio', 1)),
                'range': float(props.get('range', 8192)), 'range_modifier': float(props.get('rangemodifier', .98)),
                'penetration': int(props.get('penetration', 1)), 'ammo': props.get('primary_ammo', ''),
            }
        (output / 'weapons.json').write_text(json.dumps(weapons, indent=2) + '\n')
        dsp_text = (extracted / 'scripts/dsp_presets.txt').read_text()
        dsp = {}
        import re
        for number in (137, 138, 139):
            body = re.search(r'\{\s*' + str(number) + r'\s+PARALLEL4(.*?)(?=\n\})', dsp_text, re.S).group(1)
            dsp[number] = {'cutoff': float(re.search(r'FLT\s+LP\s+(\d+)', body).group(1)),
                           'tone_frequency': 3000, 'tone_gain': float(re.search(r'LFO\s+SIN\s+3000.0\s+0.0\s+([\d.]+)', body).group(1))}
        (output / 'dsp_presets.json').write_text(json.dumps(dsp, indent=2) + '\n')
        definitions = {}
        for script in scripts:
            for name, pairs in keyvalues((extracted / script).read_text(errors='replace')):
                if not isinstance(pairs, list):
                    continue
                if name not in {'player.damagehelmet', 'player.damageheadshot', 'player.damagekevlar',
                                'flesh.bulletimpact', 'player.falldamage', 'player.death', 'player.deathheadshot',
                                'flesh.impactsoft', 'flesh.impacthard'}:
                    continue
                waves = [v for k, v in pairs if k == 'wave']
                for key, value in pairs:
                    if key == 'rndwave':
                        waves.extend(v for k, v in value if k == 'wave')
                props = dict(pairs)
                level = props.get('soundlevel', 'SNDLVL_NORM')
                level = {'SNDLVL_NORM': 75, 'SNDLVL_NONE': 0}.get(level, level)
                if isinstance(level, str):
                    level = float(level.removeprefix('SNDLVL_').removesuffix('dB'))
                definitions[name] = {'paths': [w.lstrip('*#@><^)}$!?').replace('\\', '/').lower() for w in waves],
                                     'volume': float(props.get('volume', '1').replace('VOL_NORM', '1')),
                                     'soundlevel': level}
        assert len(definitions) == 9, definitions.keys()
        sounds = sorted({'sound/' + p for d in definitions.values() for p in d['paths']})
        extract([p for p in sounds if p in entries])
        extract([p for p in sounds if p not in entries], args.game / 'hl2/hl2_sound_misc_dir.vpk')
        for path in sounds:
            dest = PROJECT / 'assets/audio/css' / path.removeprefix('sound/')
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(extracted / path, dest)
        bank_path = PROJECT / 'assets/audio/css/bank.json'
        bank = json.loads(bank_path.read_text())
        bank['sounds'].update(definitions)
        bank_path.write_text(json.dumps(bank, indent=2) + '\n')
    for agent in ('phoenix', 'arctic', 'guerilla', 'leet'):
        path = PROJECT / f'assets/players/{agent}/source/css/models/player/t_{agent}.mdl'
        data = path.read_bytes()
        assert data[:4] == b'IDST' and struct.unpack_from('<i', data, 4)[0] == 44
        count, start = struct.unpack_from('<ii', data, 156)
        bones = []
        for i in range(count):
            bone = start + i * 216
            name = bone + struct.unpack_from('<i', data, bone)[0]
            bones.append(data[name:data.index(b'\0', name)].decode('ascii'))
        count, start = struct.unpack_from('<ii', data, 172)
        assert count == 1
        _, count, offset = struct.unpack_from('<iii', data, start)
        boxes = []
        for i in range(count):
            bone, group, *bounds = struct.unpack_from('<ii6f', data, start + offset + i * 68)
            boxes.append({'bone': bones[bone], 'group': group, 'min': bounds[:3], 'max': bounds[3:]})
        (output / f'{agent}_hitboxes.json').write_text(json.dumps(boxes, indent=2) + '\n')
        records.append({'path': str(path.relative_to(PROJECT)), 'sha256': hashlib.sha256(data).hexdigest()})
    (output / 'provenance.json').write_text(json.dumps({'archive': str(archive), 'files': records}, indent=2) + '\n')
    print(f'CSS_DAMAGE_IMPORTED weapons={len(weapons)} sound_scripts={len(definitions)} waves={len(sounds)} agents=4')


if __name__ == '__main__':
    main()
