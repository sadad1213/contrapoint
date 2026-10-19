"""Import the three stock CSS grenades, including held and thrown world models."""
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import re

from import_css_audio import events, keyvalues

PROJECT = Path(__file__).resolve().parents[2]
GAME = Path('C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source')
BLENDER = Path('C:/Program Files/Blender Foundation/Blender 5.2/blender.exe')
SOURCEIO = Path(tempfile.gettempdir()) / 'contrapoint-sourceio/SourceIO'
CONTENT = Path(tempfile.gettempdir()) / 'contrapoint-css-grenades'
STEMS = {'hegrenade': 'eq_fraggrenade', 'flashbang': 'eq_flashbang', 'smokegrenade': 'eq_smokegrenade'}


def sequence_events(path):
    data = path.read_bytes()
    count, offset = struct.unpack_from('<2i', data, 188)
    clips = {}
    for index in range(count):
        start = offset + index * 212
        label_offset = start + struct.unpack_from('<i', data, start + 4)[0]
        label = data[label_offset:data.index(b'\0', label_offset)].decode('ascii')
        count_events, offset_events = struct.unpack_from('<2i', data, start + 24)
        clips[label] = []
        for event in range(count_events):
            entry = start + offset_events + event * 80
            cycle, event_id, kind = struct.unpack_from('<fii', data, entry)
            option = data[entry + 12:entry + 76].split(b'\0')[0].decode('ascii')
            clips[label].append({'cycle': cycle, 'id': event_id, 'type': kind, 'option': option})
    return clips


def main():
    archive = GAME / 'cstrike/cstrike_pak_dir.vpk'
    vpk = GAME / 'bin/vpk.exe'
    entries = subprocess.check_output([str(vpk), 'l', str(archive)], text=True).splitlines()
    selected = [entry for entry in entries if any(
        entry.startswith(f'models/weapons/{prefix}_{stem}{suffix}.') and
        entry.endswith(('.mdl', '.vvd', '.dx90.vtx', '.phy'))
        for stem in STEMS.values() for prefix, suffix in [('v', ''), ('w', ''), ('w', '_thrown')])
        or any(entry.startswith(f'materials/models/weapons/v_models/{stem}/') or
               entry.startswith(f'materials/models/weapons/w_models/w_{stem}/') for stem in STEMS.values())
        or entry.startswith('materials/models/weapons/v_models/hands/')
        or any(entry.startswith(f'sound/weapons/{name}/') for name in STEMS)
        or entry == 'scripts/game_sounds_weapons.txt'
        or entry.startswith('materials/particle/particle_smokegrenade')]
    CONTENT.mkdir(parents=True, exist_ok=True)
    for entry in selected:
        (CONTENT / entry).parent.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(vpk), 'x', str(archive), *selected], cwd=CONTENT, check=True, stdout=subprocess.DEVNULL)

    def convert(job):
        name, variant = job
        output = PROJECT / 'assets/weapons' / name
        if variant != 'view':
            output /= variant
        output.mkdir(parents=True, exist_ok=True)
        source = output / 'source'
        source.mkdir(exist_ok=True)
        (source / '.gdignore').touch()
        stem = STEMS[name]
        model = f'v_{stem}' if variant == 'view' else f'w_{stem}' + ('_thrown' if variant == 'thrown' else '')
        command = [str(BLENDER), '--background', '--factory-startup', '--python-exit-code', '1',
                   '--python', str(PROJECT / 'tools/source_import/import_source1_model.py'), '--',
                   '--sourceio', str(SOURCEIO), '--content-root', str(CONTENT),
                   '--model', str(CONTENT / f'models/weapons/{model}.mdl'),
                   '--output', str(output), '--name', name + (f'_{variant}' if variant != 'view' else ''), '--overwrite']
        if variant != 'view':
            command.append('--no-animations')
        with (source / 'conversion.log').open('w', encoding='utf-8') as log:
            subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, check=True)
        print('CONVERTED', name, variant, flush=True)

    with ThreadPoolExecutor(max_workers=2) as pool:
        list(pool.map(convert, [(name, variant) for name in STEMS for variant in ['view', 'world', 'thrown']]))

    bank_path = PROJECT / 'assets/audio/css/bank.json'
    bank = json.loads(bank_path.read_text())
    definitions = dict(keyvalues((CONTENT / 'scripts/game_sounds_weapons.txt').read_text(errors='replace')))
    needed = set()
    for name, stem in STEMS.items():
        output = PROJECT / 'assets/weapons' / name
        records = []
        for entry in selected:
            if stem not in entry and f'weapons/{name}/' not in entry and '/hands/' not in entry:
                continue
            src = CONTENT / entry
            dst = output / 'source/css' / entry
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
            records.append({'path': entry, 'sha256': hashlib.sha256(src.read_bytes()).hexdigest()})
        mdl = CONTENT / f'models/weapons/v_{stem}.mdl'
        bank['animations'][name] = events(mdl)
        (output / 'source/sequence_events.json').write_text(json.dumps(sequence_events(mdl), indent=2))
        (output / 'source/provenance.json').write_text(json.dumps({'archive': str(archive), 'files': records}, indent=2))
        needed.update(event['sound'] for clip in bank['animations'][name].values() for event in clip)
    needed.update(name for name in definitions if any(word in name for word in ['flashbang', 'hegrenade', 'smokegrenade', 'basesmokeeffect', 'basegrenade.explode']))
    for name in sorted(needed):
        pairs = definitions[name]
        waves = [value for key, value in pairs if key == 'wave']
        waves += [wave for key, value in pairs if key == 'rndwave' for _, wave in value]
        if not waves:
            continue
        volume = next((value for key, value in pairs if key == 'volume'), '1')
        paths = [wave.lstrip('*#@><^)}$!?').replace('\\', '/').lower() for wave in waves]
        settings = dict(pairs)
        pitch = settings.get('pitch', 'PITCH_NORM')
        pitches = [100, 100] if pitch == 'PITCH_NORM' else [int(value.strip()) for value in pitch.split(',')]
        if len(pitches) == 1:
            pitches *= 2
        level = settings.get('soundlevel', '')
        match = re.fullmatch(r'SNDLVL_(\d+)dB', level)
        soundlevel = int(match[1]) if match else 50 + 20 / float(settings.get('compatibilityattenuation', '0.8'))
        bank['sounds'][name] = {'paths': paths, 'volume': 1 if volume == 'VOL_NORM' else float(volume.split(',')[0]),
                                'pitch': pitches, 'soundlevel': soundlevel, 'channel': settings.get('channel', 'CHAN_AUTO')}
        for path in paths:
            src = CONTENT / 'sound' / path
            if not src.exists():
                src.parent.mkdir(parents=True, exist_ok=True)
                subprocess.run([str(vpk), 'x', str(archive), 'sound/' + path], cwd=CONTENT, check=True, stdout=subprocess.DEVNULL)
            dst = PROJECT / 'assets/audio/css' / path
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dst)
    bank_path.write_text(json.dumps(bank, indent=2))
    audio_records = [{'path': 'sound/' + path, 'sha256': hashlib.sha256((CONTENT / 'sound' / path).read_bytes()).hexdigest()}
                     for name in needed if name in bank['sounds'] for path in bank['sounds'][name]['paths']]
    (PROJECT / 'assets/audio/css/grenade_provenance.json').write_text(json.dumps(
        {'archive': str(archive), 'files': audio_records}, indent=2))
    print('GRENADE_IMPORT_OK', sorted(needed), flush=True)


if __name__ == '__main__':
    main()
