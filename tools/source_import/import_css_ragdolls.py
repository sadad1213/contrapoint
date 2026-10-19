"""Extract stock player PHY hulls, masses and joint limits. Run in Blender."""
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import numpy as np

project = Path(__file__).resolve().parents[2]
sourceio = Path(tempfile.gettempdir()) / 'contrapoint-sourceio/SourceIO'
spec = importlib.util.spec_from_file_location('SourceIO', sourceio / '__init__.py', submodule_search_locations=[str(sourceio)])
module = importlib.util.module_from_spec(spec)
sys.modules['SourceIO'] = module
spec.loader.exec_module(module)
module.register()
sys.path.insert(0, str(Path(__file__).resolve().parent))
from import_css_audio import keyvalues
from css_agents import AGENTS, model_name
from SourceIO.library.models.mdl.v44 import MdlV44
from SourceIO.library.models.phy.phy import Phy
from SourceIO.library.utils import FileBuffer

output = project / 'assets/physics/css_ragdolls'
output.mkdir(parents=True, exist_ok=True)

def leaves(node):
    result = [node.convex_leaf] if node.convex_leaf else []
    for child in (node.left_node, node.right_node):
        if child:
            result.extend(leaves(child))
    return result

selected = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else AGENTS
for agent in selected:
    root = project / f'assets/players/{agent}/source/css/models/player'
    mdl_path, phy_path = root / f'{model_name(agent)}.mdl', root / f'{model_name(agent)}.phy'
    mdl = MdlV44.from_buffer(FileBuffer(mdl_path))
    buffer = FileBuffer(phy_path)
    phy = Phy.from_buffer(buffer)
    kv = keyvalues(phy.kv.rstrip('\0'))
    solids = [dict(value) for key, value in kv if key == 'solid']
    constraints = [dict(value) for key, value in kv if key == 'ragdollconstraint']
    collision = [value for key, pairs in kv if key == 'collisionrules' for _, value in pairs]
    for solid, metadata in zip(phy.solids, solids):
        bone = next(b for b in mdl.bones if b.name == metadata['name'])
        parts = leaves(solid.collision_model.root_tree)
        count = max(i for leaf in parts for i in leaf.unique_vertices) + 1
        vertices = solid.collision_model.get_vertex_data(buffer, solid.collision_model.root_tree.convex_leaf, count) / .0254
        vertices[:, 2] *= -1
        hulls = []
        for leaf in parts:
            if leaf.has_children:
                continue
            points = vertices[sorted(leaf.unique_vertices)].copy()
            # Articulated PHY solids already use their owning bone's local axes.
            points *= .0254
            assert np.isfinite(points).all() and np.max(np.abs(points)) < 1, metadata['name']
            if len(points) >= 4:
                hulls.append(points.tolist())
        assert hulls, metadata['name']
        metadata['hulls'] = hulls
    result = {'agent': agent, 'solids': solids, 'constraints': constraints, 'collision_pairs': collision,
              'source': str(phy_path.relative_to(project)), 'sha256': hashlib.sha256(phy_path.read_bytes()).hexdigest()}
    (output / f'{agent}.json').write_text(json.dumps(result, indent=2) + '\n')
    print('CSS_RAGDOLL_IMPORTED', agent, len(solids), sum(len(s['hulls']) for s in solids))
