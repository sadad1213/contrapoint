extends SceneTree

# Run with a real renderer, --audio-driver Dummy and --disable-vsync:
# --script tools/source_import/benchmark_map.gd -- de_inferno C:/temp/map-bench
# Native map only (no player/UI), frozen for repeatable render comparisons.
const Occlusion = preload("res://tools/source_import/map_occlusion.gd")
const WARMUP_FRAMES := 90
const SAMPLE_FRAMES := 180

func _initialize() -> void:
	call_deferred("benchmark")

func source(text: String) -> Vector3:
	var p := text.split(" ", false)
	return Vector3(float(p[0]), float(p[2]), -float(p[1])) * 0.0254

func benchmark() -> void:
	var args := OS.get_cmdline_user_args()
	var map_id := args[0] if args.size() > 0 else "de_inferno"
	var output := args[1] if args.size() > 1 else OS.get_user_data_dir() + "/map-bench/" + map_id
	assert(DirAccess.make_dir_recursive_absolute(output) == OK)
	Engine.max_fps = 0
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)
	root.mode = Window.MODE_WINDOWED
	root.content_scale_mode = Window.CONTENT_SCALE_MODE_DISABLED
	root.size = Vector2i(1920, 1080)
	var base := "res://assets/maps/" + map_id + "/"
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(base + "source/map_data.json"))
	var map := (load(base + map_id + ".scn") as PackedScene).instantiate() as Node3D
	map.process_mode = Node.PROCESS_MODE_DISABLED
	if not map.has_node("WorldOccluder"):
		Occlusion.build(map, data)
	root.add_child(map)
	# Physics props must not fall between A/B captures.
	for body: RigidBody3D in map.find_children("*", "RigidBody3D", true, false):
		body.freeze = true
	for ui: CanvasLayer in map.find_children("*", "CanvasLayer", true, false):
		ui.hide()
	var camera := Camera3D.new()
	camera.near = 0.025
	camera.far = 2000
	camera.fov = 90
	root.add_child(camera)
	camera.current = true
	RenderingServer.viewport_set_measure_render_time(root.get_viewport_rid(), true)
	var samples: Array = []
	for entity: Dictionary in data.get("point_entities", []):
		if entity.classname != "point_devshot_camera" or not entity.get("cameraname", "") in ["tspawn", "middle", "bombsitea", "bombsiteb", "interiora", "longhall"]:
			continue
		var angles: PackedStringArray = entity.get("angles", "0 0 0").split(" ", false)
		var pitch := deg_to_rad(float(angles[0]))
		var yaw := deg_to_rad(float(angles[1]))
		var direction := Vector3(cos(pitch) * cos(yaw), -sin(pitch), -cos(pitch) * sin(yaw))
		var position := source(entity.origin)
		samples.append([entity.cameraname, position, position + direction])
	if map_id == "de_dust2":
		samples = [["t_spawn", source("-832 -760 240"), source("-400 -1000 160")], ["mid", source("-480 400 130"), source("-300 1800 0")], ["a_site", source("1150 2200 220"), source("400 2400 100")], ["b_site", source("-1700 2400 180"), source("-1200 2800 80")], ["tunnels", source("-1600 1100 180"), source("-1900 1600 100")]]
	elif map_id == "kz_jayverb000":
		samples = [["spawn", source("-1144 720 20"), source("-864 1092 0")], ["courtyard", source("200 0 180"), source("-800 -800 150")], ["bhop", source("-400 -3100 260"), source("-650 -4000 110")]]
	if samples.is_empty():
		var spawn: Dictionary = data.spawns[0]
		var p := Vector3(spawn.position[0], spawn.position[1], spawn.position[2]) + Vector3.UP * 1.6
		for yaw in [0, 90, 180, 270]:
			samples.append(["spawn_%d" % yaw, p, p + Vector3.FORWARD.rotated(Vector3.UP, deg_to_rad(yaw))])
	var rows: Array = []
	await physics_frame
	await physics_frame
	for sample: Array in samples:
		camera.position = sample[1]
		camera.look_at(sample[2])
		var point_query := PhysicsPointQueryParameters3D.new()
		point_query.position = camera.global_position
		point_query.collision_mask = 1
		if not root.world_3d.direct_space_state.intersect_point(point_query).is_empty():
			push_warning("Skipping camera inside solid geometry: " + str(sample[0]))
			continue
		# Alternate order to reduce bias from shader warmup and GPU clock changes.
		var modes := [false, true] if rows.size() % 4 == 0 else [true, false]
		for enabled: bool in modes:
			root.use_occlusion_culling = enabled
			for frame in WARMUP_FRAMES:
				await process_frame
			var times: Array[float] = []
			var draws := 0.0
			var primitives := 0.0
			var cpu := 0.0
			var gpu := 0.0
			var previous := Time.get_ticks_usec()
			for frame in SAMPLE_FRAMES:
				await process_frame
				var now := Time.get_ticks_usec()
				times.append((now - previous) / 1000.0)
				previous = now
				draws += root.get_render_info(Viewport.RENDER_INFO_TYPE_VISIBLE, Viewport.RENDER_INFO_DRAW_CALLS_IN_FRAME)
				primitives += root.get_render_info(Viewport.RENDER_INFO_TYPE_VISIBLE, Viewport.RENDER_INFO_PRIMITIVES_IN_FRAME)
				cpu += RenderingServer.viewport_get_measured_render_time_cpu(root.get_viewport_rid())
				gpu += RenderingServer.viewport_get_measured_render_time_gpu(root.get_viewport_rid())
			times.sort()
			var elapsed := 0.0
			for t in times:
				elapsed += t
			var row := {"map": map_id, "camera": sample[0], "occlusion": enabled, "fps": 1000.0 * SAMPLE_FRAMES / elapsed, "median_ms": times[SAMPLE_FRAMES / 2], "p95_ms": times[int(SAMPLE_FRAMES * 0.95)], "draw_calls": draws / SAMPLE_FRAMES, "primitives": primitives / SAMPLE_FRAMES, "render_cpu_ms": cpu / SAMPLE_FRAMES, "render_gpu_ms": gpu / SAMPLE_FRAMES}
			rows.append(row)
			print("MAP_BENCH ", JSON.stringify(row))
			await RenderingServer.frame_post_draw
			assert(root.get_texture().get_image().save_png(output.path_join("%s_%s.png" % [sample[0], "on" if enabled else "off"])) == OK)
	var file := FileAccess.open(output.path_join("results.json"), FileAccess.WRITE)
	file.store_string(JSON.stringify({"resolution": [root.size.x, root.size.y], "renderer": ProjectSettings.get_setting("rendering/renderer/rendering_method", "forward_plus"), "adapter": RenderingServer.get_video_adapter_name(), "samples": rows}, "\t"))
	file.close()
	print("MAP_BENCH_COMPLETE ", output)
	map.queue_free()
	quit()
