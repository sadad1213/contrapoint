extends SceneTree

func _initialize() -> void:
	call_deferred("render_arsenal")

func render_arsenal() -> void:
	AudioServer.set_bus_mute(0, true)
	root.size = Vector2i(960, 540)
	var scene: Node3D = load("res://scenes/debug/weapon_preview.tscn").instantiate()
	root.add_child(scene)
	var weapons: Node3D = scene.get_node("Camera3D/Weapons")
	for weapon in weapons.get_children():
		weapon.set_process(false)
		if weapon.visible: weapon.call("Holster")
	var count := 0
	for weapon in weapons.get_children():
		var id := str(weapon.name).to_lower()
		if not FileAccess.file_exists("res://assets/physics/css_firearms.json"): quit(1); return
		var definitions: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/physics/css_firearms.json"))
		if not definitions.has(id): continue
		weapon.call("Equip")
		var player := weapon.find_child("AnimationPlayer", true, false) as AnimationPlayer
		var report: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://assets/weapons/%s/import_report.json" % id))
		var clips: Array = report.animations.map(func(a: Dictionary): return a.name)
		var samples := {}
		for clip: String in clips:
			if "idle" in clip and "idle" not in samples: samples["idle"] = clip
			if ("shoot" in clip or "fire" in clip) and "fire" not in samples: samples["fire"] = clip
			if ("reload" in clip or clip == "insert") and "reload" not in samples: samples["reload"] = clip
		for state: String in samples:
			var clip: String = samples[state]
			player.stop(true)
			player.play(clip)
			var time := .08 if state == "fire" else player.get_animation(clip).length * .45
			player.seek(time, true)
			await process_frame
			await process_frame
			await RenderingServer.frame_post_draw
			var path := "res://assets/weapons/%s/source/qa_%s.png" % [id, state]
			assert(root.get_texture().get_image().save_png(path) == OK)
			count += 1
		weapon.call("Holster")
	var camera: Camera3D = scene.get_node("Camera3D")
	for weapon in weapons.get_children():
		var id := str(weapon.name).to_lower()
		var path := "res://assets/weapons/%s/world/%s_world.glb" % [id, id]
		if not ResourceLoader.exists(path): continue
		var model: Node3D = load(path).instantiate()
		scene.add_child(model)
		var bounds := AABB()
		var first := true
		for mesh: MeshInstance3D in model.find_children("*", "MeshInstance3D", true, false):
			# Same per-instance treatment as WorldWeaponMaterials.Apply in gameplay.
			for surface in mesh.mesh.get_surface_count():
				if mesh.get_active_material(surface) is StandardMaterial3D:
					var material: StandardMaterial3D = mesh.get_active_material(surface).duplicate()
					material.metallic = 0
					material.roughness = .85
					material.metallic_specular = .15
					mesh.set_surface_override_material(surface, material)
			var box: AABB = mesh.global_transform * mesh.get_aabb()
			bounds = box if first else bounds.merge(box)
			first = false
		var center := bounds.get_center()
		var distance := maxf(bounds.size.length(), .25) * 1.1
		camera.position = center + Vector3(.7, .5, .8).normalized() * distance
		camera.look_at(center)
		await process_frame
		await RenderingServer.frame_post_draw
		assert(root.get_texture().get_image().save_png("res://assets/weapons/%s/world/source/qa_world.png" % id) == OK)
		count += 1
		model.free()
	print("ARSENAL_RENDER screenshots=", count, " renderer=", RenderingServer.get_current_rendering_driver_name())
	scene.free()
	quit()
