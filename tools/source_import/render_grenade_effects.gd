extends SceneTree

func _initialize() -> void:
	call_deferred("render_effects")

func capture(path: String) -> void:
	await process_frame
	await process_frame
	await RenderingServer.frame_post_draw
	assert(root.get_texture().get_image().save_png(path) == OK)
	print("GRENADE_EFFECT_RENDER ", path)

func render_effects() -> void:
	root.size = Vector2i(1280, 720)
	AudioServer.set_bus_mute(0, true)
	var scene := load("res://scenes/maps/dust2.tscn").instantiate() as Node3D
	root.add_child(scene)
	var player := scene.get_node("Player")
	player.set_physics_process(false)
	player.call("SetMouseCaptured", true)
	var weapons := player.get_node("View/Camera3D/Weapons")
	for weapon in weapons.get_children():
		weapon.set_process(false)
		weapon.set_physics_process(false)
	var camera := player.get_node("View/Camera3D") as Camera3D
	await physics_frame
	await physics_frame
	weapons.call("Equip", weapons.get_node("SmokeGrenade"))
	var grenade := weapons.get_node("SmokeGrenade")
	grenade.call("_Process", 1.0)
	var press := InputEventMouseButton.new()
	press.button_index = MOUSE_BUTTON_LEFT
	press.pressed = true
	grenade.call("_UnhandledInput", press)
	grenade.call("_Process", 1.0)
	press.pressed = false
	grenade.call("_UnhandledInput", press)
	grenade.call("_Process", 0.11)
	grenade.call("_PhysicsProcess", 1.0 / 66)
	var projectile: Node3D = grenade.get("LastProjectile")
	projectile.set_physics_process(false)
	var smoke_position := camera.global_position - camera.global_basis.z * 5 + Vector3.DOWN * 1.3
	projectile.global_position = smoke_position
	projectile.call("Detonate")
	grenade.call("_Process", 1.0)
	await physics_frame
	await physics_frame
	var cloud: Node = get_first_node_in_group("source_smoke")
	cloud.set_process(false)
	cloud.call("_Process", 6.0)
	await capture("res://.godot/grenade_smoke.png")
	player.global_position += smoke_position - camera.global_position
	await capture("res://.godot/grenade_inside_smoke.png")
	cloud.queue_free()
	await process_frame
	player.call("Respawn")
	var vision := player.get_node("GrenadeVision")
	vision.call("Flash", 3.0, 1.0)
	await capture("res://.godot/grenade_flash.png")
	scene.free()
	quit()
