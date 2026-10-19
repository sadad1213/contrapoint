using Godot;
using System;
using System.Linq;
using Contrapoint.Gameplay;
using Contrapoint.UI;

namespace Contrapoint.Players;

public enum ObserverMode { FirstPerson, Chase, Free }

// Presentation only: observing never equips, enables or drives the target's weapons.
public partial class PlayerObserver : Node3D
{
	public Player? Target { get; private set; }
	public ObserverMode Mode { get; private set; } = ObserverMode.FirstPerson;
	public Camera3D Camera { get; private set; } = null!;
	public bool Available => _player.LocalInput && !_player.PickingClass && !_player.AwaitingInitialClass
		&& (_player.Team == PlayerTeam.Spectator || !_player.IsAlive && _player.Death.Active && DefuseRules.Active);
	public bool Following => _started && Available && Mode != ObserverMode.Free && ValidTarget(Target);
	public bool FirstPerson => Following && Mode == ObserverMode.FirstPerson;
	public ObserverViewmodel Viewmodel { get; private set; } = null!;
	private Player _player = null!;
	private Player? _maskedBody, _sampledTarget;
	private Label _name = null!;
	private CanvasLayer _hud = null!;
	private bool _started, _cameraOwned;
	private Vector3 _previousEye, _currentEye;
	private int _teleportRevision;
	private int? _resumePeer;
	private ulong _resumeDeadline;
	private readonly SphereShape3D _probe = new() { Radius = .16f };

	public void Bind(Player player)
	{
		_player = player; TopLevel = true; PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
		ProcessPriority = 140; ProcessPhysicsPriority = 110;
		Camera = new Camera3D { Name = "Camera", Near = .05f, Far = player.Camera.Far };
		AddChild(Camera);
		Viewmodel = new ObserverViewmodel { Name = "Viewmodel" }; AddChild(Viewmodel);
		_hud = new CanvasLayer { Layer = 4 }; AddChild(_hud);
		_name = new Label { Name = "ObservedPlayer", MouseFilter = Control.MouseFilterEnum.Ignore,
			HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
		_name.AddThemeFontOverride("font", GD.Load<FontFile>("res://assets/ui/css/scoreboard/verdanab.ttf"));
		_hud.AddChild(_name); _hud.Visible = false;
	}
	private bool ValidTarget(Player? p) => GodotObject.IsInstanceValid(p) && p != _player && p!.IsInsideTree()
		&& !p.IsQueuedForDeletion() && p.IsAlive && !p.AwaitingInitialClass && !p.PickingClass
		&& p.Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist
		&& (_player.Team == PlayerTeam.Spectator || p.Team == _player.Team);
	public bool Cycle(int step = 1)
	{
		if (!Available) return false;
		_resumePeer = null;
		var targets = GetTree().GetNodesInGroup("source_players").OfType<Player>().Where(ValidTarget)
			.OrderBy(p => p.NetworkPeerId).ThenBy(p => p.GetInstanceId()).ToArray();
		if (targets.Length == 0) { SetTarget(null); return false; }
		var index = Array.IndexOf(targets, Target);
		var next = index < 0 ? step < 0 ? targets.Length - 1 : 0 : ((index + step) % targets.Length + targets.Length) % targets.Length;
		if (Mode == ObserverMode.Free) Mode = ObserverMode.FirstPerson;
		_started = true; SetTarget(targets[next]); _player.Death.EndFreezeForObserver();
		return true;
	}
	public void Start()
	{
		if (!Available || _started) return;
		_started = true; Mode = ObserverMode.FirstPerson; Cycle();
	}
	public void ToggleMode()
	{
		if (!Available) return;
		_started = true;
		SetMode(Mode == ObserverMode.FirstPerson ? ObserverMode.Chase
			: Mode == ObserverMode.Chase && _player.Team == PlayerTeam.Spectator ? ObserverMode.Free : ObserverMode.FirstPerson);
	}
	public void SetMode(ObserverMode mode)
	{
		if (!Available || mode == ObserverMode.Free && _player.Team != PlayerTeam.Spectator) return;
		_resumePeer = null;
		Mode = mode; _started = true;
		if (Mode != ObserverMode.Free && !ValidTarget(Target)) Cycle();
		if (Mode == ObserverMode.Free)
		{
			if (_cameraOwned) _player.SourceTeleport(Camera.GlobalPosition - _player.Camera.Position - _player.Camera.GetParent<Node3D>().Position,
				Camera.GlobalRotation.Y, Camera.GlobalRotation.X);
			SetTarget(null); ReleaseCamera();
		}
		_player.Death.EndFreezeForObserver();
	}
	public void Resume(ObserverMode mode, int? peer)
	{
		if (!Available) return;
		Stop(); Mode = mode; _started = true;
		_resumePeer = mode == ObserverMode.Free ? null : peer;
		_resumeDeadline = Time.GetTicksMsec() + 5000;
	}
	private void SetTarget(Player? target)
	{
		if (Target == target) return;
		RestoreBody(); Viewmodel.Clear(); Target = target; _sampledTarget = null;
	}
	private void RestoreBody()
	{
		if (GodotObject.IsInstanceValid(_maskedBody) && GodotObject.IsInstanceValid(_maskedBody!.Body))
			_maskedBody.Body.SetObservedFirstPerson(false);
		_maskedBody = null;
	}
	public void Stop()
	{
		SetTarget(null); RestoreBody(); Viewmodel.Clear(); _started = false; _resumePeer = null;
		ReleaseCamera(); _hud.Visible = false;
	}
	private void ReleaseCamera()
	{
		if (!_cameraOwned) return;
		_cameraOwned = false; Camera.Current = false;
		if (_player.IsInsideTree() && !_player.IsQueuedForDeletion())
			(_player.Death.Active ? _player.Death.Camera : _player.IsThirdPerson ? _player.ThirdPerson.Camera : _player.FirstPerson ?? _player.Camera).MakeCurrent();
	}
	private Vector3 TargetEye(Player target) => target.Camera.GlobalPosition
		+ (!target.LocalInput && !target.IsServerBot ? target.Body.GlobalPosition - target.GlobalPosition : Vector3.Zero);
	public override void _PhysicsProcess(double delta)
	{
		if (!Following) return;
		var target = Target!; var eye = TargetEye(target);
		if (_sampledTarget != target || target.NetworkTeleportRevision != _teleportRevision || _currentEye.DistanceSquaredTo(eye) > 16)
			_previousEye = _currentEye = eye;
		else { _previousEye = _currentEye; _currentEye = eye; }
		_sampledTarget = target; _teleportRevision = target.NetworkTeleportRevision;
		if (Mode != ObserverMode.Chase) return;
		var offset = target.Camera.GlobalBasis * new Vector3(0, .2f, 2.5f);
		using var query = new PhysicsShapeQueryParameters3D { Shape = _probe, Transform = new(Basis.Identity, eye),
			Motion = offset, CollisionMask = 1, Exclude = new() { target.GetRid(), _player.GetRid() } };
		var fractions = GetWorld3D().DirectSpaceState.CastMotion(query);
		_chaseDistance = Mathf.Max(0, (fractions.Length > 0 ? fractions[0] : 1) * offset.Length() - .04f);
	}
	private float _chaseDistance;
	public override void _Process(double delta)
	{
		if (!Available) { if (_started || _cameraOwned) Stop(); return; }
		if (_resumePeer is { } peer)
		{
			var resumed = GetTree().GetNodesInGroup("source_players").OfType<Player>().FirstOrDefault(p => p.NetworkPeerId == peer && ValidTarget(p));
			if (resumed != null) { SetTarget(resumed); _resumePeer = null; }
			else if (Time.GetTicksMsec() >= _resumeDeadline) _resumePeer = null;
		}
		if (_started && Mode != ObserverMode.Free && _resumePeer == null && !ValidTarget(Target)) Cycle();
		if (!Following) { RestoreBody(); Viewmodel.Clear(); ReleaseCamera(); _hud.Visible = false; return; }
		var target = Target!;
		if (_sampledTarget != target) { _previousEye = _currentEye = TargetEye(target); _sampledTarget = target; }
		var eye = !target.LocalInput && !target.IsServerBot ? TargetEye(target)
			: _previousEye.Lerp(_currentEye, Mathf.Clamp((float)Engine.GetPhysicsInterpolationFraction(), 0, 1));
		Camera.GlobalTransform = new(target.Camera.GlobalBasis, eye);
		Camera.Fov = target.Camera.Fov; Camera.Far = target.Camera.Far;
		Camera.Environment = target.Camera.Environment; Camera.Attributes = target.Camera.Attributes;
		Camera.CullMask = target.Camera.CullMask & ~2u;
		if (FirstPerson)
		{
			if (_maskedBody != target) { RestoreBody(); _maskedBody = target; target.Body.SetObservedFirstPerson(true); }
			Camera.CullMask &= ~PlayerBody.ObservedLayer;
		}
		else
		{
			RestoreBody();
			Camera.GlobalPosition += (target.Camera.GlobalBasis * new Vector3(0, .2f, 2.5f)).Normalized() * _chaseDistance;
		}
		Camera.MakeCurrent(); _cameraOwned = true;
		_player.GetNode<CanvasLayer>("WeaponLayer").Visible = false;
		if (FirstPerson && target.IsBot) target.ViewmodelBob.AdvanceObserved(delta);
		Viewmodel.Present(FirstPerson ? target.ActiveWeapon : null, delta, GetWindow().Size);
		_hud.Transform = GetViewport().GetStretchTransform().AffineInverse();
		var size = (Vector2)GetWindow().Size; var scale = size.Y / 480;
		_name.Position = new(0, size.Y - 64 * scale); _name.Size = new(size.X, 18 * scale);
		_name.AddThemeFontSizeOverride("font_size", Math.Max(1, Mathf.RoundToInt(12 * scale / 1.129f)));
		_name.AddThemeColorOverride("font_color", target.Team == PlayerTeam.Terrorist ? new Color("f05a5a") : new Color("96c8ff"));
		_name.Text = target.DisplayName;
		_hud.Visible = _player.GetNode<CssHud>("HUD/Status").Enabled && !_player.ShowMatchScores && _player.Menu?.IsOpen != true;
	}
	public override void _ExitTree() { RestoreBody(); Viewmodel.Clear(); }
}
