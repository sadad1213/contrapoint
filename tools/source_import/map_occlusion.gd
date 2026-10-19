extends RefCounted

# Use only the actual opaque, static world surfaces. Collision hulls also contain
# invisible player clips; doors, props, foliage, decals and water must not seal
# openings in the occlusion buffer. Keep the original triangles so baking cannot
# simplify a doorway shut or change a displacement's silhouette.
static func build(map: Node3D, data: Dictionary) -> OccluderInstance3D:
	var vertices := PackedVector3Array()
	var indices := PackedInt32Array()
	var vertex_ids := {}
	for i in data.groups.size():
		var entry: Dictionary = data.groups[i]
		var material: Dictionary = data.materials[entry.material]
		if entry.get("owner", "") != "" or entry.mode != "lightmap":
			continue
		if material.get("transparent", false) or material.get("alpha_test", false):
			continue
		var node := map.get_node_or_null("Chunk_%04d" % i) as MeshInstance3D
		assert(node != null, "Missing static world chunk %d" % i)
		var points: PackedVector3Array = node.mesh.surface_get_arrays(0)[Mesh.ARRAY_VERTEX]
		for triangle in range(0, points.size(), 3):
			if (points[triangle + 1] - points[triangle]).cross(points[triangle + 2] - points[triangle]).length_squared() < 1e-12:
				continue
			for j in 3:
				var point: Vector3 = node.transform * points[triangle + j]
				if not vertex_ids.has(point):
					vertex_ids[point] = vertices.size()
					vertices.append(point)
				indices.append(vertex_ids[point])
	assert(not indices.is_empty(), "No opaque world geometry for occlusion")
	var occluder := ArrayOccluder3D.new()
	occluder.set_arrays(vertices, indices)
	var instance := OccluderInstance3D.new()
	instance.name = "WorldOccluder"
	instance.occluder = occluder
	map.add_child(instance)
	instance.owner = map
	return instance
