extends SceneTree

var errors: Array[String] = []
var warnings: Array[String] = []
var model_path := ""
var report_path := ""
var expected_clips := -1
var require_textures := false

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var index := 0
	while index < args.size():
		var flag := args[index]
		if flag == "--require-textures":
			require_textures = true
		elif flag in ["--model", "--report", "--expect-clips"] and index + 1 < args.size():
			index += 1
			match flag:
				"--model": model_path = args[index]
				"--report": report_path = args[index]
				"--expect-clips": expected_clips = int(args[index])
		else:
			push_error("Unknown or incomplete argument: " + flag)
			quit(1)
			return
		index += 1
	if model_path.is_empty():
		push_error("Usage: --model res://path/model.glb [--expect-clips N] [--require-textures] [--report PATH]")
		quit(1)
		return
	call_deferred("verify")

func check(condition: bool, message: String) -> void:
	if not condition:
		errors.append(message)
		push_error(message)

func verify() -> void:
	var packed := load(model_path) as PackedScene
	if packed == null:
		push_error("Could not load model: " + model_path)
		quit(1)
		return
	var model := packed.instantiate()
	root.add_child(model)
	var skeletons := model.find_children("*", "Skeleton3D", true, false)
	var players := model.find_children("*", "AnimationPlayer", true, false)
	var meshes := model.find_children("*", "MeshInstance3D", true, false)
	check(not meshes.is_empty(), "No mesh instances found")
	var bones := 0
	for skeleton: Skeleton3D in skeletons:
		bones += skeleton.get_bone_count()
	var clips: Array[Dictionary] = []
	for player: AnimationPlayer in players:
		player.callback_mode_process = AnimationMixer.ANIMATION_CALLBACK_MODE_PROCESS_MANUAL
		for name in player.get_animation_list():
			if name == "RESET":
				continue
			var animation := player.get_animation(name)
			check(animation.length > 0 and animation.get_track_count() > 0, "Empty animation: " + name)
			player.stop(true)
			player.play(name)
			player.seek(0, true)
			var first_poses: Array[Transform3D] = []
			for skeleton: Skeleton3D in skeletons:
				skeleton.force_update_all_bone_transforms()
				for bone in skeleton.get_bone_count():
					first_poses.append(skeleton.get_bone_global_pose(bone))
			var changed := 0
			for fraction in [0.25, 0.5, 0.75, 0.99]:
				player.seek(animation.length * fraction, true)
				var pose_index := 0
				for skeleton: Skeleton3D in skeletons:
					skeleton.force_update_all_bone_transforms()
					for bone in skeleton.get_bone_count():
						var pose := skeleton.get_bone_global_pose(bone)
						check(pose.is_finite(), "Invalid bone transform in " + name)
						if not pose.is_equal_approx(first_poses[pose_index]):
							changed += 1
						pose_index += 1
			if changed == 0 and bones > 0:
				warnings.append("No sampled skeletal movement in " + name + "; verify whether this is an intentional pose clip")
			clips.append({"name": name, "duration": animation.length,
				"tracks": animation.get_track_count(), "moving_bone_samples": changed})
	if expected_clips >= 0:
		check(clips.size() == expected_clips, "Expected %d clips, found %d" % [expected_clips, clips.size()])
	var surfaces: Array[Dictionary] = []
	var skinned_meshes := 0
	for mesh: MeshInstance3D in meshes:
		if mesh.skin != null:
			skinned_meshes += 1
			check(mesh.get_node_or_null(mesh.skeleton) is Skeleton3D, "Skin has no valid Skeleton3D: " + mesh.name)
		if mesh.mesh == null:
			check(false, "Empty mesh instance: " + mesh.name)
			continue
		for surface in mesh.mesh.get_surface_count():
			var material := mesh.get_active_material(surface)
			var textured := material is BaseMaterial3D and material.albedo_texture != null
			check(material != null, "Missing material on " + mesh.name)
			if require_textures:
				check(textured, "Missing albedo texture on " + mesh.name + " surface " + str(surface))
			surfaces.append({"mesh": mesh.name, "material": material.resource_name if material else "",
				"textured": textured, "texture_size": str(material.albedo_texture.get_size()) if textured else ""})
	var report := {"model": model_path, "godot_version": Engine.get_version_info().string,
		"status": "passed" if errors.is_empty() else "failed", "bones": bones,
		"meshes": meshes.size(), "skinned_meshes": skinned_meshes, "animations": clips,
		"surfaces": surfaces, "errors": errors, "warnings": warnings}
	if not report_path.is_empty():
		var file := FileAccess.open(report_path, FileAccess.WRITE)
		if file == null:
			check(false, "Cannot write verification report: " + report_path)
		else:
			file.store_string(JSON.stringify(report, "  "))
	print("SOURCE_MODEL_VERIFY ", JSON.stringify(report))
	model.free()
	quit(0 if errors.is_empty() else 1)
