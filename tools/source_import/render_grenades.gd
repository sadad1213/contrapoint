extends SceneTree

func _initialize() -> void:
	call_deferred("render_preview")

func render_preview() -> void:
	root.size = Vector2i(1280, 720)
	var scene := load("res://scenes/debug/weapon_preview.tscn").instantiate() as Node3D
	root.add_child(scene)
	var weapons := scene.get_node("Camera3D/Weapons")
	for weapon in weapons.get_children():
		weapon.set_process(false)
		weapon.set_physics_process(false)
		if weapon.visible: weapon.call("Holster")
	for pair in [["HEGrenade", "hegrenade"], ["Flashbang", "flashbang"], ["SmokeGrenade", "smokegrenade"]]:
		var weapon := weapons.get_node(NodePath(pair[0]))
		weapon.call("Equip")
		var animation := weapon.find_child("AnimationPlayer", true, false) as AnimationPlayer
		for sample in [["idle", 0.15], ["pullpin", 0.65], ["pullpin", 0.92], ["throw", 0.15], ["throw", 0.4]]:
			animation.stop(true)
			animation.play(sample[0])
			animation.seek(sample[1], true)
			await process_frame
			await process_frame
			await RenderingServer.frame_post_draw
			var path := "res://assets/weapons/%s/source/%s_%03d.png" % [pair[1], sample[0], int(sample[1] * 100)]
			assert(root.get_texture().get_image().save_png(path) == OK)
			print("GRENADE_RENDER ", path)
		weapon.call("Holster")
	scene.free()
	quit()
