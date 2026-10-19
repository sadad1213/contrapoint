extends SceneTree

func _initialize() -> void:
	call_deferred("verify")

func verify() -> void:
	var packed = load("res://assets/weapons/karambit/karambit.glb") as PackedScene
	assert(packed != null, "GLB failed to import")
	var model = packed.instantiate()
	root.add_child(model)
	var skeletons = model.find_children("*", "Skeleton3D", true, false)
	var players = model.find_children("*", "AnimationPlayer", true, false)
	assert(skeletons.size() == 1 and players.size() == 1)
	var skeleton: Skeleton3D = skeletons[0]
	var player: AnimationPlayer = players[0]
	var clips = []
	for name in player.get_animation_list():
		if name == "RESET":
			continue
		var anim = player.get_animation(name)
		assert(anim.get_track_count() > 0 and anim.length > 0)
		player.play(name)
		player.seek(0.0, true)
		skeleton.force_update_all_bone_transforms()
		var first_poses = []
		for i in skeleton.get_bone_count():
			first_poses.append(skeleton.get_bone_global_pose(i))
		var changed = 0
		for fraction in [0.25, 0.5, 0.75]:
			player.seek(anim.length * fraction, true)
			skeleton.force_update_all_bone_transforms()
			for i in skeleton.get_bone_count():
				var pose: Transform3D = skeleton.get_bone_global_pose(i)
				assert(pose.is_finite(), "Invalid bone transform")
				if not pose.is_equal_approx(first_poses[i]):
					changed += 1
		assert(changed > 0, "Animation has no motion: " + name)
		clips.append({"name": name, "duration": anim.length, "tracks": anim.get_track_count(), "moving_bone_samples": changed})
	assert(clips.size() == 7, "Expected seven clips")
	var surfaces = []
	for mesh in model.find_children("*", "MeshInstance3D", true, false):
		assert(mesh.skin != null)
		for i in mesh.mesh.get_surface_count():
			var material = mesh.get_active_material(i) as StandardMaterial3D
			assert(material != null and material.albedo_texture != null, "Missing albedo texture")
			surfaces.append({"material": material.resource_name, "texture_size": str(material.albedo_texture.get_size())})
	var report = {"godot_version": Engine.get_version_info().string, "bones": skeleton.get_bone_count(), "animations": clips, "surfaces": surfaces}
	var file = FileAccess.open("res://assets/weapons/karambit/godot_verification.json", FileAccess.WRITE)
	file.store_string(JSON.stringify(report, "  "))
	print("GODOT_KARAMBIT_OK ", JSON.stringify(report))
	model.free()
	quit(0)
