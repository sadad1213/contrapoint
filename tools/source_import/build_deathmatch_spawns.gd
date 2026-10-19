extends SceneTree

# Offline generation: only walkable navigation reachable from original team
# spawns is eligible. Runtime needs just the resulting lightweight markers.
const HEIGHT := 72 * 0.0254
const WIDTH := 32 * 0.0254
const SPACING := 4.0

func _initialize() -> void:
	call_deferred("build")

func build() -> void:
	var maps := OS.get_cmdline_user_args()
	if maps.is_empty():
		maps = PackedStringArray(["de_dust2", "de_inferno", "de_cpl_strike"])
	for id in maps:
		if not (id.begins_with("de_") or id.begins_with("cs_") or id == "2000"):
			push_error("Only combat maps support generated DM spawns: " + id)
			quit(1)
			return
		await build_map(id)
	quit()

func build_map(id: String) -> void:
	var base := "res://assets/maps/" + id + "/"
	var map := load(base + id + ".scn").instantiate() as Node3D
	root.add_child(map)
	await physics_frame
	await physics_frame
	var mesh := NavigationMesh.new()
	mesh.geometry_parsed_geometry_type = NavigationMesh.PARSED_GEOMETRY_STATIC_COLLIDERS
	mesh.geometry_collision_mask = 3 # World and player clips.
	mesh.cell_size = 0.2
	mesh.cell_height = 0.1
	mesh.agent_height = 1.9
	mesh.agent_radius = 0.6
	mesh.agent_max_climb = 0.4
	mesh.agent_max_slope = rad_to_deg(acos(0.7))
	var geometry := NavigationMeshSourceGeometryData3D.new()
	NavigationServer3D.parse_source_geometry_data(mesh, geometry, map)
	NavigationServer3D.bake_from_source_geometry_data(mesh, geometry)
	assert(mesh.get_polygon_count() > 0, id + ": no walkable navigation")
	var nav_map := NavigationServer3D.map_create()
	NavigationServer3D.map_set_use_async_iterations(nav_map, false)
	NavigationServer3D.map_set_cell_size(nav_map, mesh.cell_size)
	NavigationServer3D.map_set_cell_height(nav_map, mesh.cell_height)
	NavigationServer3D.map_set_active(nav_map, true)
	var region := NavigationServer3D.region_create()
	NavigationServer3D.region_set_use_async_iterations(region, false)
	NavigationServer3D.region_set_enabled(region, true)
	NavigationServer3D.region_set_navigation_layers(region, 1)
	NavigationServer3D.region_set_navigation_mesh(region, mesh)
	NavigationServer3D.region_set_map(region, nav_map)
	NavigationServer3D.map_force_update(nav_map)
	var starts: Array[Vector3] = []
	for pattern in ["Spawn_terrorist_*", "Spawn_counterterrorist_*"]:
		var markers := map.find_children(pattern, "Marker3D", true, false)
		if not markers.is_empty():
			starts.append(NavigationServer3D.map_get_closest_point(nav_map, markers[0].global_position))
	assert(not starts.is_empty(), id + ": no original spawns")
	var vertices := mesh.get_vertices()
	var samples: Array[Vector3] = []
	for index in mesh.get_polygon_count():
		var polygon := mesh.get_polygon(index)
		var center := Vector3.ZERO
		for vertex in polygon:
			center += vertices[vertex]
		center /= polygon.size()
		var reachable := false
		for start in starts:
			var path := NavigationServer3D.map_get_path(nav_map, start, center, true)
			if not path.is_empty() and path[-1].distance_to(center) < 0.3:
				reachable = true
				break
		if not reachable:
			continue
		for triangle in range(1, polygon.size() - 1):
			var a := vertices[polygon[0]]
			var b := vertices[polygon[triangle]]
			var c := vertices[polygon[triangle + 1]]
			var steps := maxi(1, ceili(maxf(a.distance_to(b), a.distance_to(c)) / 2.0))
			for i in range(steps + 1):
				for j in range(steps + 1 - i):
					samples.append(a + (b - a) * float(i) / steps + (c - a) * float(j) / steps)
	seed(20261018)
	samples.shuffle()
	var output := Node3D.new()
	output.name = "DeathmatchSpawns"
	var points: Array[Vector3] = []
	var hull := BoxShape3D.new()
	hull.size = Vector3(WIDTH + 0.1, HEIGHT, WIDTH + 0.1)
	hull.margin = 0.001
	var space := map.get_world_3d().direct_space_state
	for sample in samples:
		var nearby := false
		for point in points:
			if point.distance_to(sample) < SPACING:
				nearby = true
				break
		if nearby:
			continue
		var hit := space.intersect_ray(PhysicsRayQueryParameters3D.create(sample + Vector3.UP * 0.4, sample + Vector3.DOWN, 3))
		if hit.is_empty() or (hit.normal as Vector3).y < 0.7:
			continue
		var foot: Vector3 = hit.position + Vector3.UP * 0.04
		var query := PhysicsShapeQueryParameters3D.new()
		query.shape = hull
		query.transform = Transform3D(Basis.IDENTITY, foot + Vector3.UP * HEIGHT / 2.0)
		query.collision_mask = 3
		if not space.intersect_shape(query, 1).is_empty():
			continue
		# Face an open direction, rather than starting with the camera at a wall.
		var yaw := 0.0
		var longest := -1.0
		var eye := foot + Vector3.UP * 64 * 0.0254
		for direction in 16:
			var angle := direction * TAU / 16.0
			var end := eye + Vector3.FORWARD.rotated(Vector3.UP, angle) * 12.0
			var wall := space.intersect_ray(PhysicsRayQueryParameters3D.create(eye, end, 1))
			var distance := 12.0 if wall.is_empty() else eye.distance_to(wall.position)
			if distance > longest:
				longest = distance
				yaw = angle
		var marker := Marker3D.new()
		marker.name = "Spawn_deathmatch_%03d" % points.size()
		output.add_child(marker)
		marker.owner = output
		marker.position = foot - Vector3.UP * 0.04
		marker.rotation.y = yaw
		points.append(foot)
	assert(points.size() >= 30, id + ": insufficient reachable spawns")
	var packed := PackedScene.new()
	assert(packed.pack(output) == OK)
	assert(ResourceSaver.save(packed, base + "deathmatch_spawns.tscn") == OK)
	print("DM_SPAWNS_BUILT map=", id, " points=", points.size(), " polygons=", mesh.get_polygon_count())
	NavigationServer3D.free_rid(region)
	NavigationServer3D.free_rid(nav_map)
	output.free()
	map.queue_free()
	await process_frame
