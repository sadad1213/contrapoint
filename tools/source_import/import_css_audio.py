"""Extract selected CSS audio and MDL sound events without modifying the game."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess
import tempfile

PROJECT = Path(__file__).resolve().parents[2]
GAME = Path(r"C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source")
ARCHIVE = GAME / "cstrike/cstrike_pak_dir.vpk"
OUTPUT = PROJECT / "assets/audio/css"


def keyvalues(text):
    tokens = iter(re.findall(r'"([^"\n]*)"|([{}])|([^\s{}"]+)', re.sub(r'//[^\n]*', '', text)))
    tokens = iter(next(part for part in token if part) for token in tokens)
    def block():
        pairs = []
        for key in tokens:
            if key == '}':
                break
            value = next(tokens)
            pairs.append((key.lower(), block() if value == '{' else value))
        return pairs
    return block()


def events(path):
    data = path.read_bytes()
    assert data[:4] == b'IDST' and struct.unpack_from('<i', data, 4)[0] in (44, 48, 49)
    count, offset = struct.unpack_from('<2i', data, 188)
    result = {}
    for index in range(count):
        start = offset + index * 212
        label_start = start + struct.unpack_from('<i', data, start + 4)[0]
        label = data[label_start:data.index(b'\0', label_start)].decode('ascii')
        event_count, event_offset = struct.unpack_from('<2i', data, start + 24)
        sounds = []
        for event_index in range(event_count):
            entry = start + event_offset + event_index * 80
            cycle, event_id = struct.unpack_from('<fi', data, entry)
            if event_id == 5004:
                sound = data[entry + 12:entry + 76].split(b'\0')[0].decode('ascii')
                sounds.append({'cycle': cycle, 'sound': sound.lower()})
        result[label] = sorted(sounds, key=lambda item: item['cycle'])
    return result


def main():
    vpk = GAME / 'bin/vpk.exe'
    entries = subprocess.check_output([str(vpk), 'l', str(ARCHIVE)], text=True).splitlines()
    materials = json.loads((PROJECT / 'assets/maps/de_dust2/source/map_data.json').read_text())['materials']
    selected = [entry for entry in entries if entry == 'scripts/game_sounds_weapons.txt'
                or entry.startswith('sound/player/footsteps/') and entry.endswith('.wav')
                or entry.startswith('sound/weapons/knife/') and entry.endswith('.wav')
                or entry == 'sound/weapons/zoom.wav'
                or entry in {f'materials/{name}.vmt' for name in materials}]
    OUTPUT.mkdir(parents=True, exist_ok=True)
    provenance = []
    with tempfile.TemporaryDirectory(prefix='contrapoint-audio-') as temporary:
        extracted = Path(temporary)
        for entry in selected:
            (extracted / entry).parent.mkdir(parents=True, exist_ok=True)
        subprocess.run([str(vpk), 'x', str(ARCHIVE), *selected], cwd=extracted, check=True, stdout=subprocess.DEVNULL)
        definitions = {}
        for name, pairs in keyvalues((extracted / 'scripts/game_sounds_weapons.txt').read_text(errors='replace')):
            if not isinstance(pairs, list):
                continue
            waves = [value for key, value in pairs if key == 'wave']
            for key, value in pairs:
                if key == 'rndwave':
                    waves.extend(wave for _, wave in value)
            if waves:
                volume = next((value for key, value in pairs if key == 'volume'), '1')
                definitions[name] = {'paths': [wave.lstrip('*#@><^)}$!?').replace('\\', '/').lower() for wave in waves],
                                     'volume': 1.0 if volume == 'VOL_NORM' else float(volume.split(',')[0])}
        animations = {}
        needed = set()
        for name in ['ak47', 'deagle', 'usp', 'm4a1', 'awp', 'scout']:
            mdl = next((PROJECT / f'assets/weapons/{name}/source/css/models/weapons').glob('v_*.mdl'))
            animations[name] = events(mdl)
            needed.update(event['sound'] for clip in animations[name].values() for event in clip)
            needed.add(f'weapon_{name}.single')
        needed.update(['weapon_usp.silencedshot', 'weapon_m4a1.silenced', 'weapon_knife.deploy',
                       'weapon_knife.slash', 'weapon_knife.hitwall', 'weapon_knife.hit', 'weapon_knife.stab', 'weapon_awp.zoom'])
        # Resolve every referenced script name now; never silently ship a missing sound.
        needed.discard('weapon_awp.zoom')
        missing = needed - definitions.keys()
        assert not missing, f'Missing definitions: {missing}; candidates: {[name for name in definitions if any(w in name for w in ["m4a1", "usp"])]}'
        bank = {name: definitions[name] for name in sorted(needed)}
        bank['weapon_awp.zoom'] = {'paths': ['weapons/zoom.wav'], 'volume': 0.6}
        for name in ['ak47', 'deagle', 'usp', 'm4a1', 'awp', 'scout']:
            for sound in (PROJECT / f'assets/weapons/{name}/sounds').glob('*.wav'):
                destination = OUTPUT / f'weapons/{name}/{sound.name}'
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(sound, destination)
        for entry in selected:
            source = extracted / entry
            provenance.append({'path': entry, 'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})
            if entry.startswith('sound/'):
                destination = OUTPUT / entry.removeprefix('sound/')
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(source, destination)
        surfaces = {}
        for name in materials:
            source = extracted / f'materials/{name}.vmt'
            if source.exists():
                match = re.search(r'"?\$surfaceprop"?\s+"?([^"\s{}]+)', source.read_text(errors='replace'), re.I)
                if match:
                    surfaces[name] = match[1].lower()
        for definition in bank.values():
            for path in definition['paths']:
                assert (OUTPUT / path).is_file(), path
        (OUTPUT / 'bank.json').write_text(json.dumps({'sounds': bank, 'animations': animations, 'surfaces': surfaces}, indent=2))
        (OUTPUT / 'provenance.json').write_text(json.dumps({'archive': str(ARCHIVE), 'files': provenance}, indent=2))
        print(f'CSS_AUDIO sounds={len(bank)} animation_sets={len(animations)} surface_materials={len(surfaces)}')


if __name__ == '__main__':
    main()
