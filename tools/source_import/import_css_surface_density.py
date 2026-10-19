"""Extract HE cover densities from the installed Source surface definitions."""
import json
from pathlib import Path
import re

game = Path(r'C:/Program Files (x86)/Steam/steamapps/common/Counter-Strike Source')
densities = {'default': 2000.0}
for filename in ('surfaceproperties.txt', 'surfaceproperties_hl2.txt'):
    text = (game / 'hl2/scripts' / filename).read_text()
    text = re.sub(r'//[^\n]*', '', text)
    for name, body in re.findall(r'"([^"\n]+)"\s*\{([^{}]*)\}', text):
        props = dict(re.findall(r'"([^"\n]+)"\s*"([^"\n]+)"', body))
        densities[name.lower()] = float(props.get('density', densities.get(props.get('base', 'default').lower(), 2000)))
output = Path(__file__).resolve().parents[2] / 'assets/physics/css_surface_density.json'
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(densities, indent=2) + '\n')
print(f'Imported {len(densities)} surface densities')
