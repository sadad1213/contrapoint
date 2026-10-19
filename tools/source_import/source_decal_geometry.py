"""Planar decal clipping, including BSP faces with collinear T-junction vertices."""
import numpy as np


def clip_polygon(poly, boundary):
    boundary = np.asarray(boundary, dtype=float)
    poly = np.asarray(poly, dtype=float)
    # BSP T-junction repair can leave the first three vertices on one line.
    # Sum every edge contribution instead of taking only that first triangle.
    relative = boundary - boundary[0]
    n = np.cross(relative, np.roll(relative, -1, axis=0)).sum(axis=0)
    length = np.linalg.norm(n)
    if length < 1.e-8:
        return np.empty((0, 3))
    n /= length
    for i, a in enumerate(boundary):
        axis = np.cross(n, boundary[(i + 1) % len(boundary)] - a)
        if np.linalg.norm(axis) < 1.e-6:
            continue
        axis /= np.linalg.norm(axis)
        result = []
        for j, cur in enumerate(poly):
            prev = poly[j - 1]
            dc, dp = np.dot(cur - a, axis), np.dot(prev - a, axis)
            if (dc >= 0) != (dp >= 0):
                result.append(prev + (cur - prev) * (dp / (dp - dc)))
            if dc >= 0:
                result.append(cur)
        poly = np.asarray(result)
        if len(poly) < 3:
            return np.empty((0, 3))
    return poly
