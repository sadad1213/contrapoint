extends SceneTree

func _initialize() -> void:
	call_deferred("render_maps")

func source(p: Vector3) -> Vector3:
	return Vector3(p.x, p.z, -p.y) * 0.0254

func render_maps() -> void:
	root.size = Vector2i(1280, 720)
	root.msaa_3d = Viewport.MSAA_4X
	for id in ["de_winter_dust2"]:
		var base: String = "res://assets/maps/" + id + "/"
		var map := load(base + id + ".scn").instantiate() as Node3D
		root.add_child(map)
		var camera := Camera3D.new()
		camera.fov = 75
		camera.near = 0.025
		camera.far = 2000
		root.add_child(camera)
		camera.current = true
		var samples: Array = [
			["overview", Vector3(1420, 1900, 240), Vector3(1100, 2600, 100)],
			["site_b", Vector3(-1400, 2300, 170), Vector3(-1530, 2800, 40)],
			["mid", Vector3(0, 1200, 160), Vector3(-450, 1900, -80)],
			["tunnel", Vector3(-1700, 1400, 100), Vector3(-1700, 1800, 100)],
		]
		for team in ["terrorist", "counterterrorist"]:
			var spawn := map.find_children("Spawn_" + team + "_*", "Marker3D", true, false)[0] as Marker3D
			var eye := spawn.position + Vector3.UP * 1.6
			samples.append([team, Vector3(eye.x, -eye.z, eye.y) / 0.0254,
				Vector3((eye - spawn.basis.z * 10).x, -(eye - spawn.basis.z * 10).z, eye.y) / 0.0254])
		for sample in samples:
			camera.position = source(sample[1])
			camera.look_at(source(sample[2]))
			for frame in 12:
				await process_frame
			await RenderingServer.frame_post_draw
			var picture := root.get_texture().get_image()
			assert(picture.save_png(base + "source/" + sample[0] + ".png") == OK)
			if sample[0] == "overview":
				picture.resize(128, 72, Image.INTERPOLATE_LANCZOS)
				var thumb := Image.create(128, 128, false, Image.FORMAT_RGB8)
				thumb.blit_rect(picture, Rect2i(0, 0, 128, 72), Vector2i(0, 12))
				assert(thumb.save_png("res://assets/ui/css/" + id + ".png") == OK)
			print("COMMUNITY_RENDER ", id, " ", sample[0])
		map.queue_free()
		camera.queue_free()
		await process_frame
	print("COMMUNITY_RENDER_OK")
	quit()
