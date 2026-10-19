"""Import CSS firearm definitions/audio and installed CSSO accuracy profiles."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from import_css_audio import keyvalues, events
from import_css_batch import STEMS

PROJECT = Path(__file__).resolve().parents[2]
GAME = Path('C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source')
CSSO = {name: name for name in STEMS}
CSSO.update(usp='usp_silencer', m4a1='m4a1_silencer', scout='ssg08',
            galil='galilar', glock='glock18', m3='nova', mp5navy='mp5sd',
            p228='p250', sg550='scar20', sg552='sg553', tmp='mp9')
RESERVES = {'BULLET_PLAYER_762MM': 90, 'BULLET_PLAYER_556MM': 90,
            'BULLET_PLAYER_556MM_BOX': 200, 'BULLET_PLAYER_338MAG': 30,
            'BULLET_PLAYER_9MM': 120, 'BULLET_PLAYER_45ACP': 100,
            'BULLET_PLAYER_50AE': 35, 'BULLET_PLAYER_357SIG': 52,
            'BULLET_PLAYER_57MM': 100, 'BULLET_PLAYER_BUCKSHOT': 32}

KINDS = ['AK47', 'Deagle', 'USP', 'M4A1', 'AWP', 'Scout', 'AUG', 'Elite', 'Famas',
         'FiveSeven', 'G3SG1', 'Galil', 'Glock', 'M249', 'M3', 'MAC10', 'MP5Navy',
         'P228', 'P90', 'SG550', 'SG552', 'TMP', 'UMP45', 'XM1014']


def write_scenes():
    definitions = json.loads((PROJECT / 'assets/physics/css_firearms.json').read_text())
    for index, kind in enumerate(KINDS[6:], 6):
        name = kind.lower()
        (PROJECT / f'weapons/{name}_view.tscn').write_text(
            '[gd_scene load_steps=4 format=3]\n\n'
            '[ext_resource type="Script" path="res://weapons/FirearmView.cs" id="1_controller"]\n'
            '[ext_resource type="Script" path="res://weapons/WeaponSway.cs" id="2_sway"]\n'
            f'[ext_resource type="PackedScene" path="res://assets/weapons/{name}/{name}.glb" id="3_model"]\n\n'
            f'[node name="{kind}" type="Node3D"]\nscript = ExtResource("1_controller")\nKind = {index}\n\n'
            '[node name="WeaponSway" type="Node3D" parent="."]\nscript = ExtResource("2_sway")\n\n'
            '[node name="Model" parent="WeaponSway" instance=ExtResource("3_model")]\n'
            'rotation_degrees = Vector3(0, 180, 0)\n'
            f'scale = Vector3({1 if definitions[name]["built_right_handed"] else -1}, 1, 1)\n')
    # Keep map players' initial loadout small. Only the arsenal preview owns all
    # 24 viewmodels at once; gameplay instantiates purchased/picked-up weapons.
    rig = ['[gd_scene load_steps=20 format=3]\n',
           '[ext_resource type="PackedScene" path="res://weapons/weapon_rig.tscn" id="1_base"]']
    rig += [f'[ext_resource type="PackedScene" path="res://weapons/{kind.lower()}_view.tscn" id="{i}"]'
            for i, kind in enumerate(KINDS[6:], 2)]
    rig += ['\n[node name="Weapons" instance=ExtResource("1_base")]']
    rig += [f'\n[node name="{kind}" parent="." instance=ExtResource("{i}")]'
            for i, kind in enumerate(KINDS[6:], 2)]
    (PROJECT / 'weapons/arsenal_rig.tscn').write_text('\n'.join(rig) + '\n')


def main():
    sys.path.insert(0, str(Path(tempfile.gettempdir()) / 'contrapoint-css-tools'))
    from ICECipher import IceKey
    archive = GAME / 'cstrike/cstrike_pak_dir.vpk'
    vpk = GAME / 'bin/vpk.exe'
    bank_path = PROJECT / 'assets/audio/css/bank.json'
    bank = json.loads(bank_path.read_text())
    definitions, accuracy, provenance = {}, {}, []
    with tempfile.TemporaryDirectory(prefix='contrapoint-arsenal-data-') as temporary:
        root = Path(temporary)
        paths = ['scripts/game_sounds_weapons.txt'] + [f'scripts/weapon_{name}.ctx' for name in STEMS]
        def extract(paths):
            for path in paths:
                (root / path).parent.mkdir(parents=True, exist_ok=True)
            subprocess.run([str(vpk), 'x', str(archive), *paths], cwd=root, check=True, stdout=subprocess.DEVNULL)
            for path in paths:
                provenance.append({'source': str(archive) + ':' + path,
                                   'sha256': hashlib.sha256((root / path).read_bytes()).hexdigest()})
        extract(paths)
        sound_defs = {}
        for name, pairs in keyvalues((root / paths[0]).read_text()):
            if not isinstance(pairs, list):
                continue
            props = dict(pairs)
            waves = [v for k, v in pairs if k == 'wave']
            waves += [v for k, values in pairs if k == 'rndwave' for _, v in values]
            if waves:
                sound_defs[name] = {'paths': [w.lstrip('*#@><^)}$!?').replace('\\', '/').lower() for w in waves],
                                    'volume': float(props.get('volume', '1').replace('VOL_NORM', '1').split(',')[0])}
        needed = {'default.clipempty_rifle', 'default.clipempty_pistol'}
        for name, stem in STEMS.items():
            ctx = root / f'scripts/weapon_{name}.ctx'
            decoded = IceKey(0, list(b'd7NSuLq2')).Decrypt(ctx.read_bytes()).decode().rstrip('\0')
            props = dict(keyvalues(decoded)[0][1])
            source = PROJECT / f'assets/weapons/{name}/source/css/models/weapons/v_{stem}.mdl'
            if not source.exists():
                source = Path(tempfile.gettempdir()) / f'contrapoint-css-full-arsenal/models/weapons/v_{stem}.mdl'
            bank['animations'][name] = events(source)
            # Stock TMP MDL requests this event, but stock CSS defines no sound
            # script for it. Source also leaves that event silent.
            if name == 'tmp':
                for clip in bank['animations'][name].values():
                    clip[:] = [e for e in clip if e['sound'] != 'weapon_tmp.deploy']
            needed.update(e['sound'] for clip in bank['animations'][name].values() for e in clip)
            single = dict(props['sounddata'])['single_shot'].lower()
            needed.add(single)
            ammo = props['primary_ammo']
            definitions[name] = {
                'magazine': int(props['clip_size']), 'ammo': ammo, 'reserve': RESERVES[ammo],
                'speed': float(props.get('maxplayerspeed', 250)),
                'cycle': float(props.get('cycletime', .1)), 'pellets': int(props.get('bullets', 1)),
                'automatic': name not in ('deagle', 'usp', 'elite', 'glock', 'p228', 'fiveseven'),
                'built_right_handed': props.get('builtrighthanded', '0') == '1',
                'sound': single, 'animation_extension': props.get('playeranimationextension', 'pistol').upper(),
            }
            mod = GAME / f'csso/scripts/weapon_{CSSO[name]}.txt'
            mod_props = dict(keyvalues(mod.read_text())[0][1])
            accuracy[name] = {'source_weapon': CSSO[name], 'values': {k: float(v) for k, v in mod_props.items()
                              if isinstance(v, str) and (k.startswith(('spread', 'inaccuracy', 'recovery', 'recoil'))
                              or k in ('maxplayerspeed', 'maxplayerspeedalt', 'cycletime', 'cycletimealt', 'fullauto'))}}
            provenance.append({'source': str(mod), 'sha256': hashlib.sha256(mod.read_bytes()).hexdigest()})
            originals = PROJECT / f'assets/weapons/{name}/source'
            originals.mkdir(parents=True, exist_ok=True)
            (originals / '.gdignore').touch()
            (originals / f'weapon_{name}.txt').write_text(decoded)
            shutil.copy2(mod, originals / ('csso_' + mod.name))
        missing = needed - sound_defs.keys()
        if missing:
            raise RuntimeError(f'Missing CSS sound definitions: {sorted(missing)}')
        for name in needed:
            bank['sounds'][name] = sound_defs[name]
        waves = sorted({'sound/' + path for name in needed for path in sound_defs[name]['paths']})
        extract(waves)
        for path in waves:
            target = PROJECT / 'assets/audio/css' / path[6:]
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(root / path, target)
        bank_path.write_text(json.dumps(bank, indent=2) + '\n')
    output = PROJECT / 'assets/physics'
    (output / 'css_firearms.json').write_text(json.dumps(definitions, indent=2) + '\n')
    (output / 'csso_accuracy.json').write_text(json.dumps(accuracy, indent=2) + '\n')
    (output / 'arsenal_provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')
    write_scenes()
    print('ARSENAL_DATA', json.dumps(definitions, indent=2))


if __name__ == '__main__':
    main()
