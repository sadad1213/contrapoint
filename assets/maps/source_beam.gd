extends MeshInstance3D

@export var start := Vector3.ZERO
@export var finish := Vector3.UP

func _process(_delta: float) -> void:
	var camera := get_viewport().get_camera_3d()
	if not camera:
		return
	position = (start + finish) * 0.5
	var up := (finish - start).normalized()
	var right := (camera.global_position - global_position).cross(up).normalized()
	if right.length_squared() > 0.5:
		basis = Basis(right, up, right.cross(up))
