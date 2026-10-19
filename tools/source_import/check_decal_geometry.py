"""Regression for decals bleeding across BSP lightmap patches at T-junctions."""
import numpy as np
from source_decal_geometry import clip_polygon

boundary = np.array([[0, 0, 0], [2, 0, 0], [4, 0, 0], [4, 4, 0], [0, 4, 0]], dtype=float)
oversized = np.array([[-3, -3, 0], [7, -3, 0], [7, 7, 0], [-3, 7, 0]], dtype=float)
for face in (boundary, boundary[::-1]):
    clipped = clip_polygon(oversized, face)
    assert len(clipped) >= 4
    assert np.all(clipped >= -1.e-8) and np.all(clipped <= [4 + 1.e-8, 4 + 1.e-8, 1.e-8])
    area = np.linalg.norm(np.cross(clipped, np.roll(clipped, -1, axis=0)).sum(axis=0)) / 2
    assert abs(area - 16) < 1.e-8
    # A planar lightmap transform must now stay in this face's atlas rectangle.
    uv = (clipped[:, :2] + [16.5, 32.5]) / 2048
    assert np.all(uv >= np.array([16.5, 32.5]) / 2048)
    assert np.all(uv <= np.array([20.5, 36.5]) / 2048)
print('DECAL_GEOMETRY_CHECKS_OK')
