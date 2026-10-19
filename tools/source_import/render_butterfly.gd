extends SceneTree

func _initialize() -> void:
	call_deferred("render_samples")

func render_samples() -> void:
	root.size = Vector2i(1280, 720)
	var scene := load("res://scenes/debug/weapon_preview.tscn").instantiate() as Node3D
	root.add_child(scene)
	for light in ["KeyLight", "FillLight"]:
		scene.get_node(light).reparent(scene.get_node("Camera3D"), false)
	if "--map" in OS.get_cmdline_user_args():
		scene.get_node("WorldEnvironment").free()
		var map := load("res://assets/maps/de_dust2/de_dust2.scn").instantiate() as Node3D
		root.add_child(map)
		var spawn := map.find_children("Spawn_terrorist_*", "Marker3D", true, false)[0] as Marker3D
		scene.get_node("Camera3D").global_position = spawn.global_position + Vector3.UP * 1.6
		scene.get_node("Camera3D").rotation.y = deg_to_rad(-150)
	var weapons := scene.get_node("Camera3D/Weapons")
	for weapon in weapons.get_children():
		weapon.set_process(false)
		if weapon.visible: weapon.call("Holster")
	var knife := weapons.get_node("Knife")
	knife.call("SetViewStyle", 1)
	knife.call("Equip")
	var model := knife.get_node("WeaponSway/Model") as Node3D
	var animation := model.find_child("AnimationPlayer", true, false) as AnimationPlayer
	animation.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
	for sample in [["idle1", 0.5], ["draw", 0.3], ["draw2", 0.6], ["lookat01", 1.2], ["lookat02", 2.0], ["lookat03", 2.4], ["light_miss1", 0.3], ["heavy_miss1", 0.4]]:
		animation.stop(true)
		animation.play(sample[0])
		animation.seek(sample[1], true)
		await process_frame
		await process_frame
		await RenderingServer.frame_post_draw
		var path := "res://assets/weapons/butterfly/source/%s_%03d.png" % [sample[0], int(sample[1] * 100)]
		assert(root.get_texture().get_image().save_png(path) == OK)
		print("BUTTERFLY_RENDER ", path)
	animation.play("idle1")
	animation.seek(.5, true)
	for angle in [0, 45, 90]:
		scene.get_node("Camera3D").rotation.y = deg_to_rad(angle - 150)
		await process_frame
		await process_frame
		await RenderingServer.frame_post_draw
		var path := "res://assets/weapons/butterfly/source/reflection_%03d.png" % angle
		assert(root.get_texture().get_image().save_png(path) == OK)
		print("BUTTERFLY_REFLECTION_RENDER ", path)
	scene.free()
	quit()
