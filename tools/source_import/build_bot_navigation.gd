extends SceneTree

# Bake once against the imported collision. No match-time geometry parsing.
func _initialize() -> void:
	call_deferred("build")

func build() -> void:
	var maps := OS.get_cmdline_user_args()
	if maps.is_empty():
		maps = PackedStringArray(["de_dust2", "de_inferno", "de_cpl_strike"])
	for id in maps:
		if id not in ["de_dust2", "de_inferno", "de_cpl_strike", "de_cache", "2000"]:
			push_error("Unsupported bot map: " + id)
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
	mesh.geometry_collision_mask = 3
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
	assert(ResourceSaver.save(mesh, base + "bot_navigation.tres") == OK)
	print("BOT_NAVIGATION_BUILT map=", id, " polygons=", mesh.get_polygon_count())
	map.queue_free()
	await process_frame
