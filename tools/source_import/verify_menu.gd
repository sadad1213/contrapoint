extends SceneTree

func _initialize() -> void:
	# Run with the Mono Godot executable and -- --menu-test-profile.
	call_deferred("change_scene_to_file", "res://scenes/debug/menu_checks.tscn")
