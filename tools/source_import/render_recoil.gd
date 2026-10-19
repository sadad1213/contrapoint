extends SceneTree

func _initialize() -> void:
 call_deferred('render_preview')

func render_preview() -> void:
 root.size = Vector2i(1280, 720)
 AudioServer.set_bus_mute(0, true)
 var scene = load('res://scenes/maps/dust2.tscn').instantiate()
 root.add_child(scene)
 var player = scene.get_node('Player')
 for frame in 50:
  await physics_frame
 player.set_physics_process(false)
 player.rotation.y = deg_to_rad(-150)
 var recoil = player.get_node('Recoil')
 recoil.set_physics_process(false)
 recoil.set_process(false)
 var combat = player.get_node('Combat')
 combat.set_physics_process(false)
 var weapons = player.get_node('View/Camera3D/Weapons')
 for weapon in weapons.get_children():
  weapon.set_process(false)
 player.call('SetMouseCaptured', true)
 weapons.call('EquipSlot', 0)
 var rifle = weapons.get('ActiveWeapon')
 rifle.call('_Process', 2.0)
 recoil.call('Reset')
 await process_frame
 await RenderingServer.frame_post_draw
 assert(root.get_texture().get_image().save_png('res://.godot/recoil_before.png') == OK)
 var trigger := InputEventMouseButton.new()
 trigger.button_index = MOUSE_BUTTON_LEFT
 trigger.pressed = true
 rifle.call('_UnhandledInput', trigger)
 for frame in 60:
  recoil.call('_PhysicsProcess', 1.0 / 66)
  rifle.call('_Process', 1.0 / 66)
  recoil.call('_Process', 1.0 / 66)
  combat.call('_PhysicsProcess', 1.0 / 66)
  if frame % 6 == 0:
   await process_frame
 trigger.pressed = false
 rifle.call('_UnhandledInput', trigger)
 var peak: Vector2 = recoil.get('PunchDegrees')
 var view_peak: Vector2 = recoil.get('ViewPunchDegrees')
 await process_frame
 await RenderingServer.frame_post_draw
 assert(root.get_texture().get_image().save_png('res://.godot/recoil_burst.png') == OK)
 for frame in 100:
  recoil.call('_PhysicsProcess', 1.0 / 66)
  rifle.call('_Process', 1.0 / 66)
  recoil.call('_Process', 1.0 / 66)
 await process_frame
 await RenderingServer.frame_post_draw
 assert(root.get_texture().get_image().save_png('res://.godot/recoil_recovered.png') == OK)
 print('RECOIL_RENDER burst=', peak, ' camera=', view_peak, ' recovered=', recoil.get('PunchDegrees'), ' camera_recovered=', recoil.get('ViewPunchDegrees'))
 scene.free()
 OS.delay_msec(150)
 quit()
