extends SceneTree

# Add occluders to existing native maps without rebuilding their gameplay,
# materials, collision or meshes. Pass map IDs after --. Back up .scn files first.
const Occlusion = preload("res://tools/source_import/map_occlusion.gd")

func _initialize() -> void:
	call_deferred("build")

func build() -> void:
	var maps := OS.get_cmdline_user_args()
	assert(not maps.is_empty(), "Pass map IDs after --")
	for map_id in maps:
		var base := "res://assets/maps/" + map_id + "/"
		var path := base + map_id + ".scn"
		var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(base + "source/map_data.json"))
		var map := (load(path) as PackedScene).instantiate() as Node3D
		var previous := map.get_node_or_null("WorldOccluder")
		if previous != null:
			previous.free()
		var instance := Occlusion.build(map, data)
		var packed := PackedScene.new()
		assert(packed.pack(map) == OK)
		var temporary := base + map_id + ".occlusion.scn"
		assert(ResourceSaver.save(packed, temporary) == OK)
		assert(DirAccess.rename_absolute(temporary, path) == OK)
		print("MAP_OCCLUDER_COMPLETE map=", map_id, " vertices=", instance.occluder.vertices.size(), " triangles=", instance.occluder.indices.size() / 3)
		map.free()
	quit()
