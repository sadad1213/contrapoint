extends SceneTree

func _initialize() -> void:
	call_deferred("verify_map")

func verify_map() -> void:
	var scene := load("res://scenes/maps/dust2.tscn").instantiate() as Node3D
	root.add_child(scene)
	await process_frame
	await physics_frame
	var player := scene.get_node("Player") as CharacterBody3D
	var start := player.position
	var at_t_spawn := false
	for marker: Node in scene.get_node("Dust2").get_children():
		if marker is Marker3D and str(marker.name).begins_with("Spawn_terrorist_"):
			at_t_spawn = at_t_spawn or start.distance_to(marker.position + Vector3.UP * 0.04) < 0.1
	assert(at_t_spawn, "Player must start at one of the imported T spawns")
	var meshes := scene.get_node("Dust2").find_children("*", "MeshInstance3D", true, false)
	var triangles := 0
	var lightmapped := 0
	for node: MeshInstance3D in meshes:
		var mesh := node.mesh
		assert(mesh != null and mesh.get_surface_count() == 1)
		var arrays := mesh.surface_get_arrays(0)
		assert(arrays[Mesh.ARRAY_VERTEX].size() > 0)
		var mat := mesh.surface_get_material(0) as ShaderMaterial
		assert(mat != null)
		assert(mat.get_shader_parameter("base_texture") != null)
		if mat.resource_name.ends_with("_lightmap"):
			assert(arrays[Mesh.ARRAY_TEX_UV2].size() == arrays[Mesh.ARRAY_VERTEX].size())
			assert(mat.get_shader_parameter("lightmap_texture") != null)
			lightmapped += 1
		triangles += arrays[Mesh.ARRAY_VERTEX].size() / 3
	var map := scene.get_node("Dust2")
	var spawn_count := 0
	for marker in map.get_children():
		if not marker is Marker3D:
			continue
		var from: Vector3 = marker.global_position + Vector3.UP * 0.3
		var query := PhysicsRayQueryParameters3D.create(from, from + Vector3.DOWN * 3)
		query.exclude = [player.get_rid()]
		var hit := player.get_world_3d().direct_space_state.intersect_ray(query)
		assert(not hit.is_empty(), "No collision floor beneath " + marker.name)
		spawn_count += 1
	for frame in 120:
		await physics_frame
	assert(player.is_on_floor(), "Player did not settle on the T spawn floor")
	assert(player.position.is_finite())
	assert(player.get_node("View/Camera3D").far > 1000)
	assert(spawn_count == 40)
	assert(lightmapped > 0 and triangles > 100000)
	# Read UVs back from native Godot meshes. A non-square or skewed displacement
	# must never sample another surface's patch in the shared lightmap atlas.
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/maps/de_dust2/source/map_data.json"))
	assert(data.displacement_ranges.size() == 69)
	for entry: Dictionary in data.displacement_ranges:
		var node := map.get_node("Chunk_%04d" % int(entry.group)) as MeshInstance3D
		var uv: PackedVector2Array = node.mesh.surface_get_arrays(0)[Mesh.ARRAY_TEX_UV2]
		var lower := Vector2(entry.uv_min[0], entry.uv_min[1])
		var upper := Vector2(entry.uv_max[0], entry.uv_max[1])
		var observed_min := Vector2.INF
		var observed_max := -Vector2.INF
		for i in range(int(entry.first_vertex), int(entry.first_vertex + entry.vertices)):
			assert(uv[i].x >= lower.x - 0.00001 and uv[i].y >= lower.y - 0.00001, "Displacement UV below its own atlas patch")
			assert(uv[i].x <= upper.x + 0.00001 and uv[i].y <= upper.y + 0.00001, "Displacement UV outside its own atlas patch")
			observed_min = observed_min.min(uv[i])
			observed_max = observed_max.max(uv[i])
		assert(observed_min.is_equal_approx(lower) and observed_max.is_equal_approx(upper), "Displacement must use the full lightmap patch")
	print("MAP_VERIFY_OK meshes=", meshes.size(), " triangles=", triangles, " spawns=", spawn_count, " player_floor=", player.position)
	scene.queue_free()
	quit()
