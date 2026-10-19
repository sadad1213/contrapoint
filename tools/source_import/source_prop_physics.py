"""Extract original Source PHY hulls for independently moving map props."""
import itertools
import re

import numpy as np
from mathutils import Euler
from SourceIO.library.models.mdl.v44 import MdlV44
from SourceIO.library.models.phy.phy import Phy
from SourceIO.library.utils import TinyPath
from SourceIO.library.utils.math_utilities import vector_transform_v


def extract_physics_props(entities, content):
    cache = {}
    for entity in entities.values():
        path = entity['model'].lower()
        if path not in cache:
            mdl = MdlV44.from_buffer(content.find_file(TinyPath(path)))
            buffer = content.find_file(TinyPath(path).with_suffix('.phy'))
            hulls, mass, surface = [], 10.0, 'concrete'
            original = buffer is not None
            if buffer is not None:
                phy = Phy.from_buffer(buffer)
                match = re.search(r'"mass"\s+"([\d.]+)"', phy.kv)
                if match:
                    mass = float(match[1])
                match = re.search(r'"surfaceprop"\s+"([^"\n]+)"', phy.kv)
                if match:
                    surface = match[1]

                def collect(node):
                    leaves = [node.convex_leaf] if node.convex_leaf else []
                    for child in (node.left_node, node.right_node):
                        if child:
                            leaves += collect(child)
                    return leaves

                for solid in phy.solids:
                    leaves = collect(solid.collision_model.root_tree)
                    count = max(i for leaf in leaves for i in leaf.unique_vertices) + 1
                    vertices = solid.collision_model.get_vertex_data(buffer, solid.collision_model.root_tree.convex_leaf, count) / 0.0254
                    vertices[:, 2] *= -1
                    for leaf in leaves:
                        if leaf.has_children:
                            continue
                        points = vertices[sorted(leaf.unique_vertices)].copy()
                        if mdl.bones and leaf.bone_id > 0:
                            points = vector_transform_v(points, mdl.bones[leaf.bone_id - 1].pose_to_bone.copy())
                        if len(points) >= 4:
                            hulls.append(points)
            if not hulls:
                hulls = [np.array(list(itertools.product(*zip(mdl.header.hull_min, mdl.header.hull_max))))]
            cache[path] = hulls, mass, surface, original
        hulls, mass, surface, original = cache[path]
        pitch, yaw, roll = np.radians(np.fromstring(entity.get('angles', '0 0 0'), sep=' '))
        rotation = np.array(Euler((roll, pitch, yaw), 'XYZ').to_matrix())
        entity.update(mass=mass, surface=surface, original_phy=original,
                      position=(np.fromstring(entity['origin'], sep=' ')[[0, 2, 1]] * [0.0254, 0.0254, -0.0254]).tolist(),
                      physics_hulls=[(((hull * float(entity.get('modelscale', 1))) @ rotation.T)[:, [0, 2, 1]] * [0.0254, 0.0254, -0.0254]).tolist() for hull in hulls])
