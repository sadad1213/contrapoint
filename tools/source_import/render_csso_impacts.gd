extends SceneTree

func _initialize() -> void:
 call_deferred("preview")

func preview() -> void:
 root.size = Vector2i(1280, 720)
 var scene := Node3D.new()
 root.add_child(scene)
 var camera := Camera3D.new()
 scene.add_child(camera)
 camera.position = Vector3(0, 1, 8)
 camera.look_at(Vector3(0, 1, 0))
 var environment := WorldEnvironment.new()
 environment.environment = Environment.new()
 environment.environment.background_mode = Environment.BG_COLOR
 environment.environment.background_color = Color(0.12, 0.14, 0.17)
 environment.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
 environment.environment.ambient_light_color = Color.WHITE
 environment.environment.ambient_light_energy = 0.8
 scene.add_child(environment)
 var wall := StaticBody3D.new()
 scene.add_child(wall)
 var shape := CollisionShape3D.new()
 shape.shape = BoxShape3D.new()
 shape.shape.size = Vector3(30, 12, 0.1)
 wall.add_child(shape)
 var mesh := MeshInstance3D.new()
 mesh.mesh = BoxMesh.new()
 mesh.mesh.size = shape.shape.size
 var mat := StandardMaterial3D.new()
 mat.albedo_color = Color(0.44, 0.42, 0.38)
 mesh.material_override = mat
 wall.add_child(mesh)
 var effects: Array[Node3D] = []
 for i in range(3):
  var label := Label3D.new()
  scene.add_child(label)
  label.text = ["CSSO: concrete", "CSSO: wood", "CSSO: metal"][i]
  label.position = Vector3((i - 1) * 3, 2.8, 0.2)
  label.font_size = 48
  label.pixel_size = .006
  var effect: Node3D = load("res://weapons/effects/CssoImpact.cs").new()
  scene.add_child(effect)
  effect.call("Initialize", Vector3((i - 1) * 3, 1.4, .055), Vector3.BACK, ["concrete", "wood", "metal"][i])
  effect.set_process(false)
  effect.set_physics_process(false)
  effects.append(effect)
 await physics_frame
 await physics_frame
 for effect in effects:
  effect.call("_PhysicsProcess", 1.0 / 66)
 var last := 0.0
 for time in [.03, .12, .4, 1.0]:
  while last < time - .00001:
   var step: float = min(1.0 / 120, time - last)
   for effect in effects:
    if is_instance_valid(effect): effect.call("_Process", step)
   last += step
  await process_frame
  await process_frame
  await RenderingServer.frame_post_draw
  var path := "res://.godot/csso_impacts_%04d.png" % int(time * 1000)
  assert(root.get_texture().get_image().save_png(path) == OK)
  print("CSSO_IMPACT_RENDER ", path)
 # Exercise every imported surface family and verify automatic cleanup.
 for family in ["concrete", "metal", "wood", "brick", "tile", "glass", "dirt", "sand", "plaster", "plastic", "rock", "grass", "clay", "cardboard", "computer", "cloth", "rubber", "asphalt", "snow"]:
  var effect: Node3D = load("res://weapons/effects/CssoImpact.cs").new()
  scene.add_child(effect)
  effect.call("Initialize", Vector3(0, 0, .055), Vector3.BACK, family)
  effect.set_process(false)
  effect.set_physics_process(false)
  effect.call("_PhysicsProcess", 1.0 / 66)
  assert(effect.get_child_count() > 0, family)
  effect.call("_Process", .04)
  effect.call("_Process", 13.0)
  assert(effect.is_queued_for_deletion(), family)
 print("CSSO_IMPACT_FAMILIES_OK 19")

 # Check emitted smoke motion against the wall/floor/ceiling normal.
 for normal in [Vector3.BACK, Vector3.RIGHT, Vector3.UP, Vector3.DOWN]:
  var effect: Node3D = load("res://weapons/effects/CssoImpact.cs").new()
  scene.add_child(effect)
  effect.call("Initialize", Vector3(0, 1, 3), normal, "concrete")
  effect.set_process(false)
  effect.set_physics_process(false)
  effect.call("_PhysicsProcess", 1.0 / 66)
  var puff: MeshInstance3D = effect.get_node("impact_concrete_child_base")
  effect.call("_Process", .04)
  var first := puff.global_position
  effect.call("_Process", .01)
  var motion := (puff.global_position - first).normalized()
  assert(motion.dot(normal) > .999, "Dust must emit along the hit normal")
  # The elongated quad's local Y axis must follow its projected motion.
  for child in effect.get_children():
   if not child.name.to_lower().contains("burst"): continue
   var start: Vector3 = child.global_position
   effect.call("_Process", .001)
   var projected: Vector3 = camera.global_basis.inverse() * (child.global_position - start)
   projected.z = 0
   if projected.length() > .0001:
    var axis: Vector3 = camera.global_basis.inverse() * child.global_basis.y.normalized()
    assert(abs(axis.normalized().dot(projected.normalized())) > .98, "Trail rotation must follow motion")
  effect.free()
 print("CSSO_IMPACT_NORMALS_AND_TRAILS_OK wall floor ceiling")

 # Resolve a real firearm hit, then the received network presentation path.
 var player = load("res://scenes/player/player.tscn").instantiate()
 player.LocalInput = false
 player.position = Vector3(0, 0, 4)
 scene.add_child(player)
 player.set_physics_process(false)
 var combat = player.get_node("Combat")
 combat.set_physics_process(false)
 var inventory = player.get_node("View/Camera3D/Weapons")
 inventory.call("EquipSlot", 0)
 var gun = inventory.get("ActiveWeapon")
 assert(gun != null and gun.get("Equipped"))
 gun.call("_Process", 2.0)
 await physics_frame
 await physics_frame
 combat.call("QueueBullet", gun, 30)
 combat.call("_PhysicsProcess", .02)
 assert(combat.get("BulletsResolved") == 1)
 assert(combat.get("MarksCreated") > 0)
 var emitted := 0
 for child in combat.get_node("WorldEffects").get_children():
  if child.get_script() == load("res://weapons/effects/CssoImpact.cs"): emitted += 1
 assert(emitted > 0, "Actual firearm hit must create CSSO particles")
 # Marks may be disabled while impact particles remain enabled.
 combat.set("MaxMarks", 0)
 combat.call("PresentNetworkImpact", Transform3D.IDENTITY, Vector3(0, 1, .055), Vector3.BACK, wall, false, false)
 var received := 0
 for child in combat.get_node("WorldEffects").get_children():
  if child.get_script() == load("res://weapons/effects/CssoImpact.cs"): received += 1
 assert(received == emitted + 1)
 print("CSSO_IMPACT_FIREARM_AND_NETWORK_PRESENTATION_OK")
 scene.queue_free()
 await process_frame
 quit()
