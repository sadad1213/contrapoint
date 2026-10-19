extends SceneTree

const BASE := "res://assets/maps/de_dust2/"

func source_point(p: Vector3) -> Vector3:
	return Vector3(p.x, p.z, -p.y) * 0.0254

func _initialize() -> void:
	call_deferred("render_map")

func render_map() -> void:
	root.size = Vector2i(1440, 900)
	root.msaa_3d = Viewport.MSAA_4X
	var map := load(BASE + "de_dust2.scn").instantiate() as Node3D
	root.add_child(map)
	var camera := Camera3D.new()
	camera.fov = 75
	camera.near = 0.05
	camera.far = 2000
	root.add_child(camera)
	camera.current = true
	for sample in [
		["t_spawn", Vector3(-832, -760, 240), Vector3(-400, -1000, 160)],
		["mid", Vector3(-480, 400, 130), Vector3(-300, 1800, 0)],
		["a_site", Vector3(1150, 2200, 220), Vector3(400, 2400, 100)],
		["b_site", Vector3(-1700, 2400, 180), Vector3(-1200, 2800, 80)],
		["b_floor", Vector3(-1900, 2200, 70), Vector3(-1350, 2700, 40)],
		["b_approach", Vector3(-1900, 1900, 70), Vector3(-1450, 2150, 35)],
		["long_floor", Vector3(1450, 900, 65), Vector3(1400, 1500, 25)],
		["tunnels", Vector3(-1600, 1100, 180), Vector3(-1900, 1600, 100)],
		["overview", Vector3(-400, 1200, 3000), Vector3(-400, 1200, 0)],
	]:
		camera.position = source_point(sample[1])
		camera.look_at(source_point(sample[2]), Vector3.FORWARD if sample[0] == "overview" else Vector3.UP)
		for frame in 6:
			await process_frame
		await RenderingServer.frame_post_draw
		assert(root.get_texture().get_image().save_png(BASE + "source/" + sample[0] + ".png") == OK)
		print("MAP_RENDER ", sample[0])
	map.queue_free()
	quit()
