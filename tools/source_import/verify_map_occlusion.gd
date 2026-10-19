extends SceneTree

# Compare native scenes with their pre-optimization backups. Every stored node,
# mesh/material, collision shape and gameplay property must remain unchanged;
# the only allowed addition is the WorldOccluder node.
var compared_resources := {}
var checks := 0

func _initialize() -> void:
	call_deferred("verify")

func same(a: Variant, b: Variant, context: String) -> void:
	checks += 1
	assert(typeof(a) == typeof(b), context + ": type changed")
	if a is Resource:
		assert(a.get_class() == b.get_class(), context + ": resource class changed")
		if a.resource_path.begins_with("res://") and not "::" in a.resource_path:
			assert(a.resource_path == b.resource_path, context + ": external resource changed")
			return
		var key := Vector2i(a.get_instance_id(), b.get_instance_id())
		if compared_resources.has(key):
			return
		compared_resources[key] = true
		for property: Dictionary in a.get_property_list():
			if (int(property.usage) & PROPERTY_USAGE_STORAGE) == 0 or property.name == "resource_path":
				continue
			same(a.get(property.name), b.get(property.name), context + "/" + str(property.name))
	elif a is Array:
		assert(a.size() == b.size(), context + ": array length changed")
		for i in a.size():
			same(a[i], b[i], context + "[%d]" % i)
	elif a is Dictionary:
		assert(a.size() == b.size(), context + ": dictionary length changed")
		for key: Variant in a:
			assert(b.has(key), context + ": dictionary key lost")
			same(a[key], b[key], context + "/" + str(key))
	else:
		assert(a == b, context + ": value changed")

func nodes(state: SceneState) -> Dictionary:
	var result := {}
	for i in state.get_node_count():
		var path := str(state.get_node_path(i)).trim_prefix("./")
		var properties := {}
		for p in state.get_node_property_count(i):
			properties[state.get_node_property_name(i, p)] = state.get_node_property_value(i, p)
		result[path] = {"type": state.get_node_type(i), "owner": state.get_node_owner_path(i), "instance": state.get_node_instance(i), "groups": state.get_node_groups(i), "properties": properties}
	return result

func verify() -> void:
	var args := OS.get_cmdline_user_args()
	assert(args.size() >= 2, "Pass backup directory, then map IDs after --")
	for map_id in args.slice(1):
		var original := load(args[0].path_join(map_id + ".scn")) as PackedScene
		var updated := load("res://assets/maps/%s/%s.scn" % [map_id, map_id]) as PackedScene
		assert(original != null and updated != null)
		var before := nodes(original.get_state())
		var after := nodes(updated.get_state())
		assert(after.has("WorldOccluder"), "Missing occluder: " + map_id)
		assert(after.WorldOccluder.type == &"OccluderInstance3D")
		after.erase("WorldOccluder")
		same(before, after, map_id)
		print("MAP_OCCLUSION_PRESERVED map=", map_id, " nodes=", before.size())
	print("MAP_OCCLUSION_VERIFY_OK checks=", checks)
	quit()
