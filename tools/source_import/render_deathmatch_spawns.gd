extends SceneTree

func _initialize() -> void:
	call_deferred("render_spawns")

func render_spawns() -> void:
	root.size = Vector2i(960, 600)
	root.msaa_3d = Viewport.MSAA_4X
	for id in ["de_dust2", "de_inferno", "de_cpl_strike"]:
		var base: String = "res://assets/maps/" + id + "/"
		var map := load(base + id + ".scn").instantiate() as Node3D
		root.add_child(map)
		var points := load(base + "deathmatch_spawns.tscn").instantiate() as Node3D
		map.add_child(points)
		var camera := Camera3D.new()
		camera.fov = 75
		camera.near = 0.05
		camera.far = 2000
		root.add_child(camera)
		camera.current = true
		for index in [0, 1, 2]:
			var marker := points.get_child(index) as Marker3D
			camera.global_transform = marker.global_transform
			camera.global_position += Vector3.UP * (64 * 0.0254 + 0.04)
			for frame in 8:
				await process_frame
			await RenderingServer.frame_post_draw
			var path := "res://.godot/dm-spawn-%s-%d.png" % [id, index]
			assert(root.get_texture().get_image().save_png(path) == OK)
			print("DM_SPAWN_RENDER ", path, " at=", marker.position)
		camera.queue_free()
		map.queue_free()
		await process_frame
	quit()
