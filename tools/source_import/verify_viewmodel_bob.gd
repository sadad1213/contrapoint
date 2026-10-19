extends SceneTree

var checks := 0
var failures := 0

func _initialize() -> void:
 call_deferred('verify')

func check(condition: bool, message: String) -> void:
 checks += 1
 if not condition:
  failures += 1
  push_error(message)

func verify() -> void:
 var player = load('res://scenes/player/player.tscn').instantiate()
 root.add_child(player)
 player.set_physics_process(false)
 var bob = player.get_node('ViewmodelBob')
 bob.set_process(false)
 bob.set('UseNewBob', false)
 bob.set('SwayScale', 0)
 var inventory = player.get_node('View/Camera3D/Weapons')
 var knife = inventory.get_node('Knife')
 var sway: Node3D = knife.get_node('WeaponSway')
 var camera: Camera3D = player.get_node('View/Camera3D')
 var camera_rest := camera.transform
 var endpoints: Array[Transform3D] = []
 for fps in [30, 60, 144]:
  bob.call('Reset')
  player.velocity = Vector3.ZERO
  bob.call('_Process', 1.0 / fps)
  check(bob.get('Offset').is_equal_approx(Transform3D.IDENTITY), 'Stationary arms must not bob')
  player.velocity = Vector3(0, 0, -250 * 0.0254)
  for frame in fps * 4:
   bob.call('_Process', 1.0 / fps)
   var offset: Transform3D = bob.get('Offset')
   check(offset.is_finite() and offset.origin.length() < 0.02, 'Walking bob must stay finite and within Source amplitude')
  endpoints.append(bob.get('Offset'))
  check(is_equal_approx(bob.get('SmoothedSpeed'), 250), 'Bob must follow actual horizontal speed in Source units')
  check(bob.get('Offset').origin.length() > 0.0001, 'Walking must move the arms')
  sway.call('_Process', 1.0 / fps)
  check(sway.transform.is_equal_approx(bob.get('Offset')), 'Equipped weapon must receive the shared bob transform')
  check(camera.transform.is_equal_approx(camera_rest), 'Weapon bob must leave aim/camera unchanged')
  var phase: float = bob.get('PhaseSeconds')
  inventory.call('EquipSlot', 0)
  var rifle_sway: Node3D = inventory.get('ActiveWeapon').get_node('WeaponSway')
  rifle_sway.call('_Process', 1.0 / fps)
  check(is_equal_approx(bob.get('PhaseSeconds'), phase), 'Weapon switch must preserve gait phase')
  check(rifle_sway.transform.is_equal_approx(bob.get('Offset')), 'Switched weapon must use the same gait')
  inventory.call('EquipSlot', 2)
  player.velocity = Vector3(0, 7, -250 * 0.0254)
  bob.call('_Process', 1.0 / fps)
  check(bob.get('PhaseSeconds') > phase, 'Airborne momentum must continue the cycle')
  player.velocity = Vector3.ZERO
  for frame in fps:
   bob.call('_Process', 1.0 / fps)
  check(bob.get('Offset').is_equal_approx(Transform3D.IDENTITY), 'Bob must settle completely after stopping')
 for endpoint in endpoints:
  check(endpoint.origin.distance_to(endpoints[0].origin) < 0.001, 'Bob amplitude/phase must remain consistent at 30/60/144 FPS')
 player.velocity = Vector3(0, 0, -100)
 for frame in 144:
  bob.call('_Process', 1.0 / 144)
 check(bob.get('SmoothedSpeed') <= 320, 'Bhop speed must not amplify bob beyond the Source cap')
 player.get_node('Movement').call('ToggleNoclip')
 player.velocity = Vector3(0, 0, -100)
 for frame in 144:
  bob.call('_Process', 1.0 / 144)
 check(bob.get('Offset').is_equal_approx(Transform3D.IDENTITY), 'Noclip must not play walking bob')
 player.call('Respawn', true)
 check(bob.get('Offset').is_equal_approx(Transform3D.IDENTITY) and bob.get('PhaseSeconds') == 0, 'Respawn must reset gait state')
 print('VIEWMODEL_BOB checks=', checks, ' failures=', failures)
 player.free()
 quit(0 if failures == 0 else 1)
