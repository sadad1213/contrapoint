"""Extract installed CS:S personalities without modifying the game installation."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--game', type=Path, default=Path('C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source'))
args = parser.parse_args()
project = Path(__file__).resolve().parents[2]
archive = args.game / 'cstrike/cstrike_pak_dir.vpk'
with tempfile.TemporaryDirectory(prefix='contrapoint-bots-') as temporary:
    subprocess.run([str(args.game / 'bin/vpk.exe'), 'x', str(archive), 'botprofile.db'], cwd=temporary, check=True)
    data = (Path(temporary) / 'botprofile.db').read_bytes()
reference = project / 'tools/source_import/references/bots'
reference.mkdir(parents=True, exist_ok=True)
(reference / 'botprofile.db').write_bytes(data)
lines = [re.sub(r'//.*', '', line).strip() for line in data.decode().splitlines()]
lines = [line for line in lines if line]
defaults, templates, profiles = {}, {}, []
i = 0
while i < len(lines):
    header = lines[i].split()
    i += 1
    fields = {}
    while lines[i].lower() != 'end':
        key, value = [part.strip() for part in lines[i].split('=', 1)]
        if key == 'WeaponPreference':
            fields.setdefault(key, []).append(value)
        else:
            fields[key] = value
        i += 1
    i += 1
    if header[0] == 'Default':
        defaults = fields
    elif header[0] == 'Template':
        templates[header[1]] = fields
    else:
        result = dict(defaults)
        for template in header[0].split('+'):
            result.update(templates[template])
        result.update(fields)
        profiles.append(dict(name=' '.join(header[1:]), skill=float(result['Skill']) / 100,
                             aggression=float(result['Aggression']) / 100,
                             reaction=float(result['ReactionTime']), attack_delay=float(result['AttackDelay']),
                             difficulty=result['Difficulty'].lower().split('+'),
                             weapons=result['WeaponPreference'], skin=int(result['Skin'])))
output = project / 'assets/bots'
output.mkdir(parents=True, exist_ok=True)
(output / 'profiles.json').write_text(json.dumps(profiles, indent=2) + '\n')
(output / 'provenance.json').write_text(json.dumps(dict(source=str(archive) + ':botprofile.db',
    sha256=hashlib.sha256(data).hexdigest(), profiles=len(profiles)), indent=2) + '\n')
print('CSS_BOT_PROFILES_IMPORTED', len(profiles))
