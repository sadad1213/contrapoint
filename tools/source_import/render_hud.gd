extends SceneTree

func _initialize() -> void:
	call_deferred("render_hud")

func render_hud() -> void:
	root.mode = Window.MODE_WINDOWED
	root.size = Vector2i(1280, 720)
	var scene = load("res://scenes/maps/dust2.tscn").instantiate()
	root.add_child(scene)
	var player = scene.get_node("Player")
	player.set_physics_process(false)
	await physics_frame
	await physics_frame
	player.rotation.y = deg_to_rad(-150)
	player.call("SetMouseCaptured", true)
	var weapons = player.get_node("View/Camera3D/Weapons")
	weapons.call("EquipSlot", 0)
	var gun = weapons.get("ActiveWeapon")
	gun.call("_Process", 2.0)
	for weapon in weapons.get_children():
		weapon.set_process(false)
	await capture("hud_dust2_720")
	root.size = Vector2i(1920, 1080)
	await capture("hud_dust2_1080")
	root.size = Vector2i(1024, 768)
	player.set("Armor", 75)
	player.set("HasHelmet", true)
	player.call("TakeDamage", 80)
	root.get_node("RoundState").set("RemainingSeconds", 85)
	gun.call("HandleCommand", "attack", true)
	gun.call("HandleCommand", "attack", false)
	await capture("hud_dust2_768_low_health")
	root.get_node("RoundState").set("RemainingSeconds", 0)
	scene.free()
	print("HUD_RENDER_COMPLETE")
	quit()

func capture(name: String) -> void:
	for frame in 5:
		await process_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png("res://.godot/" + name + ".png") == OK)
