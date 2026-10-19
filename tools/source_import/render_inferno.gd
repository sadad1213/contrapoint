extends SceneTree

const BASE := "res://assets/maps/de_inferno/"

func _initialize() -> void:
	call_deferred("render_map")

func source_vector(text: String) -> Vector3:
	var p := text.split(" ", false)
	return Vector3(float(p[0]), float(p[2]), -float(p[1])) * 0.0254

func render_map() -> void:
	root.size = Vector2i(1440, 900)
	root.msaa_3d = Viewport.MSAA_4X
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(BASE + "source/map_data.json"))
	var map := load(BASE + "de_inferno.scn").instantiate() as Node3D
	root.add_child(map)
	var camera := Camera3D.new()
	camera.near = 0.025
	camera.far = 2000
	root.add_child(camera)
	camera.current = true
	var samples := []
	for entity: Dictionary in data.point_entities:
		if entity.classname != "point_devshot_camera" or not entity.cameraname in ["tspawn", "tspawnb", "middle", "bombsitea", "bombsiteb", "interiora", "longhall", "ctspawnl", "sniperloft"]:
			continue
		var angles: PackedStringArray = entity.angles.split(" ")
		var pitch := deg_to_rad(float(angles[0]))
		var yaw := deg_to_rad(float(angles[1]))
		var direction := Vector3(cos(pitch)*cos(yaw), -sin(pitch), -cos(pitch)*sin(yaw))
		var position := source_vector(entity.origin)
		samples.append([entity.cameraname, position, position + direction, float(entity.get("fov", 90))])
	for i in data.water_hulls.size():
		var bounds := AABB(Vector3(data.water_hulls[i][0][0], data.water_hulls[i][0][1], data.water_hulls[i][0][2]), Vector3.ZERO)
		for p: Array in data.water_hulls[i]:
			bounds = bounds.expand(Vector3(p[0], p[1], p[2]))
		var center := bounds.get_center()
		center.y = bounds.end.y
		samples.append(["water_%d" % i, center + Vector3(1, 1.5, 2), center, 75.0])
	samples.append(["gate_closed", source_vector("1216 -100 320"), source_vector("1216 -268 310"), 75.0])
	for sample: Array in samples:
		camera.position = sample[1]
		camera.look_at(sample[2])
		camera.fov = sample[3]
		for frame in 8:
			await process_frame
		await RenderingServer.frame_post_draw
		assert(root.get_texture().get_image().save_png(BASE + "source/" + sample[0] + ".png") == OK)
		print("INFERNO_RENDER ", sample[0])
	var trigger_body := CharacterBody3D.new()
	trigger_body.collision_layer = 0
	root.add_child(trigger_body)
	map.get_node("brush_15").call("on_entered", trigger_body)
	await create_timer(1.15).timeout
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png(BASE + "source/gate_open.png") == OK)
	trigger_body.queue_free()
	camera.queue_free()
	map.queue_free()
	await process_frame
	var game := load("res://scenes/maps/inferno.tscn").instantiate() as Node3D
	root.add_child(game)
	for frame in 25:
		await physics_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png(BASE + "source/player_spawn.png") == OK)
	game.get_node("Player").call("SetMouseCaptured", false)
	game.queue_free()
	await process_frame
	quit()
