extends SceneTree

func _initialize() -> void:
 call_deferred('verify')

func verify() -> void:
 var preview = load('res://scenes/debug/weapon_preview.tscn').instantiate()
 root.add_child(preview)
 var knife = preview.get_node('Camera3D/Weapons/Knife')
 knife.set_process(false)
 var animation: AnimationPlayer = knife.find_children('*', 'AnimationPlayer', true, false)[0]
 var skeleton: Skeleton3D = knife.find_children('*', 'Skeleton3D', true, false)[0]
 # Finish draw, then sample across the next idle seam, including its wrap frame.
 for frame in 1500:
  knife.call('_Process', 1.0 / 240.0)
  if animation.current_animation == 'idle':
   break
 var remaining := 5.0 - animation.current_animation_position - 0.05
 while remaining > 0.000001:
  var dt := minf(remaining, 1.0 / 240.0)
  knife.call('_Process', dt)
  remaining -= dt
 var peak_position := 0.0
 var peak_angle := 0.0
 var previous: Array[Transform3D] = []
 for bone in skeleton.get_bone_count():
  previous.append(skeleton.get_bone_global_pose(bone))
 for frame in 252:
  knife.call('_Process', 1.0 / 240.0)
  skeleton.force_update_all_bone_transforms()
  for bone in skeleton.get_bone_count():
   var pose := skeleton.get_bone_global_pose(bone)
   peak_position = maxf(peak_position, pose.origin.distance_to(previous[bone].origin) * 240)
   peak_angle = maxf(peak_angle, pose.basis.get_rotation_quaternion().normalized().angle_to(previous[bone].basis.get_rotation_quaternion().normalized()) * 240)
   previous[bone] = pose
 print('IDLE_SEAM peak_position_m_s=', peak_position, ' peak_rotation_rad_s=', peak_angle)
 var smooth := peak_position < 0.2 and peak_angle < 6.0
 if not smooth:
  push_error('Idle seam moves too quickly: expected <0.2 m/s and <6 rad/s across the wrap and settling transition.')
 preview.free()
 quit(0 if smooth else 1)
