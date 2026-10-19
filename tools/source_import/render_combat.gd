extends SceneTree

func _initialize() -> void:
 call_deferred('render_preview')

func render_preview() -> void:
 root.size = Vector2i(1280, 720)
 AudioServer.set_bus_mute(0, true)
 var scene = load('res://scenes/maps/dust2.tscn').instantiate()
 root.add_child(scene)
 var player = scene.get_node('Player')
 player.set_physics_process(false)
 var camera: Camera3D = player.get_node('View/Camera3D')
 var combat = player.get_node('Combat')
 combat.set_physics_process(false)
 var weapons = player.get_node('View/Camera3D/Weapons')
 for weapon in weapons.get_children():
  weapon.set_process(false)
 await physics_frame
 await physics_frame
 player.rotation.y = deg_to_rad(-150)
 var ray := PhysicsRayQueryParameters3D.create(camera.global_position, camera.global_position - camera.global_basis.z * 100)
 ray.exclude = [player.get_rid()]
 var hit: Dictionary = player.get_world_3d().direct_space_state.intersect_ray(ray)
 assert(not hit.is_empty(), 'Render needs a nearby wall')
 player.global_position += hit.position + hit.normal * 0.9 - camera.global_position
 camera.look_at(hit.position)
 var rest := camera.transform
 player.call('SetMouseCaptured', true)
 weapons.call('EquipSlot', 0)
 var rifle = weapons.get('ActiveWeapon')
 rifle.call('_Process', 2.0)
 for offset in [Vector2(-0.24, 0.15), Vector2(-0.12, 0.08), Vector2(0.04, 0.14), Vector2(0.18, 0.05)]:
  camera.transform = rest
  camera.rotate_object_local(Vector3.UP, offset.x)
  camera.rotate_object_local(Vector3.RIGHT, offset.y)
  combat.call('QueueBullet', rifle, 30)
  combat.call('_PhysicsProcess', 0.02)
 weapons.call('EquipSlot', 2)
 var knife = weapons.get('ActiveWeapon')
 knife.call('_Process', 2.0)
 for offset in [Vector2(-0.05, -0.08), Vector2(0.18, -0.12)]:
  camera.transform = rest
  camera.rotate_object_local(Vector3.UP, offset.x)
  camera.rotate_object_local(Vector3.RIGHT, offset.y)
  combat.call('QueueKnife', knife, false)
  combat.call('_PhysicsProcess', 0.11)
 camera.transform = rest
 await process_frame
 await process_frame
 await RenderingServer.frame_post_draw
 var image := root.get_texture().get_image()
 assert(image.save_png('res://.godot/combat_marks.png') == OK)
 player.call('SetMouseCaptured', true)
 weapons.call('EquipSlot', 0)
 rifle = weapons.get('ActiveWeapon')
 rifle.call('_Process', 2.0)
 combat.call('QueueBullet', rifle, 30)
 combat.call('_PhysicsProcess', 0.02)
 for tracer in combat.get_node('WorldEffects').get_children():
  tracer.set_process(false)
  tracer.call('_Process', 0.015)
 await process_frame
 await RenderingServer.frame_post_draw
 assert(root.get_texture().get_image().save_png('res://.godot/combat_tracer.png') == OK)
 print('COMBAT_RENDER marks=', combat.get('MarksCreated'))
 scene.free()
 OS.delay_msec(150)
 quit()
