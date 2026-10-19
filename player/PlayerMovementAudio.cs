using Godot;

namespace Contrapoint.Players;

// CS:S UpdateStepSound suppresses footsteps below 150 u/s; normal running
// cadence is 300 ms. CheckJumpButton uses the current surface's step sound.
public partial class PlayerMovementAudio : Node
{
	public bool FootstepsEnabled { get; set; } = true;
	public SourceSoundPlayer Sounds { get; private set; } = null!;
	private Player _player = null!;
	private SourceMovement _movement = null!;
	private float _stepRemaining;
	private int _foot;
	private int _previousWaterLevel;
	private readonly RandomNumberGenerator _random = new();

	public override void _Ready()
	{
		_player = GetParent<Player>();
		_movement = _player.GetNode<SourceMovement>("Movement");
		Sounds = new SourceSoundPlayer { Name = "Sounds", Enabled = _player.LocalInput || _player.IsBot };
		AddChild(Sounds);
		_random.Randomize();
		_movement.Jumped += () => Footstep(1);
		_movement.Landed += speed => { if (speed > 140) Footstep(speed > 350 ? 1 : 0.5f); };
	}

	public void Reset()
	{
		_stepRemaining = 0;
		_foot = 0;
		_previousWaterLevel = 0;
		Sounds.StopAll();
	}

	public void Tick(float delta, PlayerCommand command)
	{
		_stepRemaining = Mathf.Max(0, _stepRemaining - delta);
		var enteredWater = _movement.WaterLevel > 0 && _previousWaterLevel == 0;
		_previousWaterLevel = _movement.WaterLevel;
		if (enteredWater && _player.HasSimulationControl && !_movement.Noclip)
		{
			Sounds.PlayWave($"player/footsteps/slosh{_random.RandiRange(1, 4)}.wav", 0.6f);
			SourceWaterRipple.Spawn(_player.GetParent<Node3D>(), _player.GlobalPosition + Vector3.Up, _player.GlobalPosition + Vector3.Down);
		}
		if (_movement.IsOnLadder)
		{
			// Ladder movement includes vertical speed. Keep crouch climbing silent;
			// Source's ladder interval is 350 ms plus its 100 ms ladder modifier.
			if (_player.HasSimulationControl && !_movement.Noclip && FootstepsEnabled
				&& !command.Duck && !_movement.IsDucked && _player.Velocity.Length() >= 60 * SourceMovement.Unit && _stepRemaining <= 0)
			{
				var variant = (_foot++ % 2) * 2 + (int)_random.RandiRange(1, 2);
				Sounds.PlayWave($"player/footsteps/ladder{variant}.wav", 0.5f);
				_stepRemaining = 0.45f;
			}
			return;
		}
		if (!_player.HasSimulationControl || _movement.Noclip || !_player.IsOnFloor()
			|| command.Walk || command.Duck || _movement.IsDucked || _movement.HorizontalSpeed < 150 || _stepRemaining > 0) return;
		Footstep(0.5f);
	}

	private void Footstep(float gain)
	{
		if (!FootstepsEnabled || !_player.HasSimulationControl || _movement.Noclip) return;
		var ray = PhysicsRayQueryParameters3D.Create(_player.GlobalPosition + Vector3.Up * 0.12f,
			_player.GlobalPosition + Vector3.Down * 0.3f);
		ray.Exclude = new() { _player.GetRid() };
		var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(ray);
		var surface = "concrete";
		if (hit.TryGetValue("collider", out var collider) && collider.AsGodotObject() is Node node)
		{
			if (node.HasMeta("source_surfaces"))
			{
				var surfaces = node.GetMeta("source_surfaces").AsStringArray();
				var shape = hit["shape"].AsInt32();
				if (shape >= 0 && shape < surfaces.Length) surface = surfaces[shape];
			}
			else if (node.HasMeta("source_surface")) surface = node.GetMeta("source_surface").AsString();
			else if (node.GetParent() is MeshInstance3D mesh && mesh.GetActiveMaterial(0) is { } material)
				surface = SourceSoundPlayer.SurfaceForMaterial(material.ResourceName);
		}
		var family = surface switch
		{
			"sand" => "sand", "dirt" => "dirt", "grass" => "grass", "gravel" => "gravel",
			"wood" => "wood", "wood_panel" or "woodpanel" => "woodpanel", "tile" => "tile",
			"metal" or "solidmetal" or "metal_box" => "metal", "metalgrate" or "grate" => "metalgrate",
			"chainlink" => "chainlink", "duct" or "metalvent" => "duct", "mud" => "mud", _ => "concrete"
		};
		if (_movement.WaterLevel > 0) family = _movement.WaterLevel >= 2 ? "wade" : "slosh";
		// Left/right have separate variant pairs, as in Source surface sounds.
		var variant = (_foot++ % 2) * 2 + (int)_random.RandiRange(1, 2);
		Sounds.PlayWave($"player/footsteps/{family}{variant}.wav", gain);
		if (_movement.WaterLevel > 0)
			SourceWaterRipple.Spawn(_player.GetParent<Node3D>(), _player.GlobalPosition + Vector3.Up, _player.GlobalPosition + Vector3.Down);
		_stepRemaining = 0.3f;
	}
}
