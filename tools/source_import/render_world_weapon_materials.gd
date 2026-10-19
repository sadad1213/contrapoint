extends SceneTree

func _initialize() -> void:
	call_deferred("run")

func meshes(node: Node) -> Array[MeshInstance3D]:
	var result: Array[MeshInstance3D] = []
	if node is MeshInstance3D:
		result.append(node)
	for child in node.get_children():
		result.append_array(meshes(child))
	return result

func run() -> void:
	root.size = Vector2i(1280, 720)
	root.msaa_3d = Viewport.MSAA_4X
	var map: Node3D = load("res://assets/maps/de_inferno/de_inferno.scn").instantiate()
	root.add_child(map)
	current_scene = map
	var spawn := map.find_children("Spawn_terrorist_*", "Marker3D", true, false)[0] as Marker3D
	var center := spawn.global_position + Vector3(0, 1.0, 0)
	var camera := Camera3D.new()
	root.add_child(camera)
	camera.position = center + Vector3(0, 1.8, 1.3)
	camera.look_at(center)
	camera.fov = 50
	camera.current = true
	var names := ["ak47", "hegrenade", "flashbang", "smokegrenade"]
	var models: Array[Node3D] = []
	for i in names.size():
		var id: String = names[i]
		var model := load("res://assets/weapons/%s/world/%s_world.glb" % [id, id]).instantiate() as Node3D
		root.add_child(model)
		model.position = center + Vector3((i - 1.5) * 0.4, 0, 0)
		model.rotation_degrees.z = 90
		models.append(model)
		for mesh in meshes(model):
			for surface in mesh.mesh.get_surface_count():
				var material: StandardMaterial3D = mesh.get_active_material(surface)
				print(id, " ", material.resource_name, " metallic=", material.metallic, " roughness=", material.roughness,
					" specular=", material.metallic_specular, " cull=", material.cull_mode)
	for variant in ["original", "back_faces_culled", "matte", "runtime"]:
		if variant == "runtime":
			for model in models:
				model.hide()
			for i in names.size():
				var pickup: Node3D = load("res://weapons/weapon_pickup.tscn").instantiate()
				pickup.set("WeaponClass", "weapon_" + names[i])
				pickup.set("freeze", true)
				root.add_child(pickup)
				pickup.global_transform = models[i].global_transform
			for i in 3:
				var projectile: Node3D = load("res://weapons/GrenadeProjectile.cs").new()
				projectile.call("ConfigureNetwork", i, null)
				projectile.set("NetworkReplica", true)
				root.add_child(projectile)
				projectile.position = center + Vector3((i - 1) * 0.4, 0, 0.45)
		elif variant != "original":
			for model in models:
				for mesh in meshes(model):
					for surface in mesh.mesh.get_surface_count():
						var material: StandardMaterial3D = mesh.get_active_material(surface).duplicate()
						material.cull_mode = BaseMaterial3D.CULL_BACK
						if variant == "matte":
							material.metallic = 0
							material.roughness = 0.85
							material.metallic_specular = 0.15
						mesh.set_surface_override_material(surface, material)
		for frame in 8:
			await process_frame
		await RenderingServer.frame_post_draw
		var output := OS.get_environment("TEMP").path_join("contrapoint_world_materials_" + variant + ".png")
		root.get_texture().get_image().save_png(output)
		print("WORLD_MATERIAL_RENDER ", output)
	quit()
