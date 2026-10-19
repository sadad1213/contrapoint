extends RigidBody3D

@export var source_data: Dictionary = {}

func source_bullet_damage(amount: float, point: Vector3, direction: Vector3, impulse: float) -> void:
	source_damage(amount, point, direction, impulse)

func source_damage(amount: float, point: Vector3, direction: Vector3, bullet_impulse: float = -1.0) -> void:
	if preload("res://assets/maps/source_network.gd").is_client(self):
		return
	if amount <= 0.0 or direction.is_zero_approx():
		return
	if int(source_data.get("nodamageforces", 0)) != 0:
		return
	if freeze and float(source_data.get("damagetoenablemotion", 0)) > 0 and amount >= float(source_data.damagetoenablemotion):
		freeze = false
	if not freeze:
		sleeping = false
		# Bullet impulse comes from CS:S ammo definitions, converted to kg m/s.
		# Other damage keeps its existing response. Offset hits also rotate the prop.
		var impulse := bullet_impulse if bullet_impulse >= 0.0 else amount * 0.15
		apply_impulse(direction.normalized() * impulse, point - global_position)
