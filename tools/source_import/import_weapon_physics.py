"""Extract the installed CSS weapon PHY solids; run in background Blender."""
import importlib.util
import json
import sys
from pathlib import Path

project = Path(__file__).resolve().parents[2]
repo = Path.home() / 'AppData/Local/Temp/contrapoint-sourceio/SourceIO'
spec = importlib.util.spec_from_file_location('SourceIO', repo / '__init__.py', submodule_search_locations=[str(repo)])
module = importlib.util.module_from_spec(spec)
sys.modules['SourceIO'] = module
spec.loader.exec_module(module)
sys.path.insert(0, str(Path(__file__).parent))
from SourceIO.library.utils import FileBuffer, TinyPath
from source_prop_physics import extract_physics_props

result = {}
from import_css_batch import STEMS

for name in (*STEMS, 'hegrenade', 'flashbang', 'smokegrenade'):
    source = project / 'assets/weapons' / name / 'source/css'
    models = list((source / 'models/weapons').glob('w_*.phy'))
    if name == 'elite':
        models = [source / 'models/weapons/w_pist_elite_dropped.phy']
    if not models:
        raise FileNotFoundError(f'No original PHY for {name}')
    path = models[0].relative_to(source).with_suffix('.mdl').as_posix()

    class Content:
        def find_file(self, path):
            file = source / str(path)
            return FileBuffer(TinyPath(str(file))) if file.exists() else None

    entity = {'model': path, 'origin': '0 0 0'}
    extract_physics_props({0: entity}, Content())
    assert entity['original_phy']
    # World GLBs use SourceIO's weapon basis: Source +X becomes Godot +Z.
    # Map imports use a different model basis, so rotate their extracted solids.
    entity['physics_hulls'] = [[[-p[2], p[1], p[0]] for p in hull] for hull in entity['physics_hulls']]
    result['weapon_' + name] = {key: entity[key] for key in ('mass', 'surface', 'physics_hulls')}
    print(name, entity['mass'], entity['surface'], len(entity['physics_hulls']))

output = project / 'assets/physics/css_weapon_solids.json'
output.write_text(json.dumps(result, separators=(',', ':')), encoding='utf-8')
