using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Players;
using Contrapoint.UI;

public partial class TeamChecks : Node3D
{
	private int _checks;
	private Player _player = null!;
	private void Check(bool value, string message) { _checks++; if (!value) throw new InvalidOperationException(message); }
	private async Task Frame(int count = 1) { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
	private void Key(Key key) => _player._Input(new InputEventKey { PhysicalKeycode = key, Pressed = true });
	public override async void _Ready()
	{
		try
		{
			SourceDeathRules.AutoRespawn = false;
			var floor = new StaticBody3D { Position = new(0, -.5f, 0) };
			floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(100, 1, 100) } }); AddChild(floor);
			_player = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>();
			_player.DebugControls = false; AddChild(_player); _player.SetPhysicsProcess(false);
			await Frame(3);
			var menu = _player.TeamMenu!;
			menu.OpenInitial();
			Check(!_player.IsAlive && !_player.Body.Visible && _player.AwaitingInitialClass, "Initial selection has no living body.");
			Check(menu.IsOpen && _player.Team == PlayerTeam.Unassigned && !_player.MouseCaptured, "Initial team menu blocks gameplay and releases mouse.");
			Key(Godot.Key.Escape); Check(menu.IsOpen, "An unassigned player cannot cancel.");
			Check(menu.GetNode<Control>("Root/TeamPage/ctbutton").Position == new Vector2(76, 148), "Use original CSS button coordinates.");
			Key(Godot.Key.Key2);
			Check(menu.IsClassSelection && _player.Team == PlayerTeam.CounterTerrorist, "Second team button selects CT and opens classes.");
			Check(!_player.IsAlive && !_player.Body.Visible && !_player.Death.Active, "Choosing a team must not spawn or kill a player before class selection.");
			Key(Godot.Key.Key1);
			Check(!menu.IsOpen && _player.AgentId == "urban" && _player.MouseCaptured && _player.IsAlive, "First CT class enters play as SEAL.");
			_player.SetMouseCaptured(true);
			_player._Input(new InputEventKey { PhysicalKeycode = Godot.Key.M, Pressed = true });
			Check(menu.IsOpen, "M opens the team menu through the default bind.");
			Key(Godot.Key.Escape); Check(!menu.IsOpen && _player.MouseCaptured, "Escape restores play after reopening.");
			foreach (var agent in PlayerBody.CounterTerroristAgentIds)
			{
				_player.SetAgent(agent); _player.Respawn(); _player.Body._Process(.1);
				Check(_player.Team == PlayerTeam.CounterTerrorist, agent + ": correct team");
				var animations = (AnimationPlayer)_player.Body.FindChild("AnimationPlayer", true, false);
				Check(animations.GetAnimationList().Length >= 122 && animations.HasAnimation("Idle_Reload_M4"), agent + ": original CT animation set");
				Check(_player.Body.WorldWeapon != null && _player.Body.Skeleton.GetBoneCount() >= 49, agent + ": skeleton and held weapon");
				_player.TakeDamage(1000);
				Check(_player.Death.Ragdoll?.Bones.Count == 16, agent + ": original PHY ragdoll");
				_player.Respawn();
				Check(_player.Body.Visible && _player.Body.WorldWeapon != null, agent + ": respawn restores body");
				menu.Open(); _player.JoinTeam(3);
				Check(menu.IsClassSelection && !_player.ClassSelectionRequired, "Existing class can cancel.");
				Key(Godot.Key.Escape); Check(!menu.IsOpen, "Existing class Escape closes menu.");
			}
			_player.JoinTeam(2); _player.JoinClass(2);
			Check(_player.AgentId == "leet" && _player.Team == PlayerTeam.Terrorist, "Original T class numbering retained.");
			Check(!_player.JoinTeam(1), "Living assigned players cannot spectate outside freeze time.");
			_player.TakeDamage(1000); Check(_player.JoinTeam(1), "Dead players can enter spectators.");
			Check(_player.Team == PlayerTeam.Spectator && _player.Movement.Noclip && !_player.Body.Visible && !_player.Death.Active, "Spectator has free camera, no body or death camera.");
			_player.SetMouseCaptured(true);
			var old = _player.Position; _player.Simulate(new(new(1, 0), false, false, false), .1f);
			Check(_player.HasLocalControl && _player.Position != old, "Dead spectator can move.");
			_player.JoinTeam(3); _player.JoinClass(8);
			Check(_player.AgentId == "gign" && _player.IsAlive && !_player.Movement.Noclip, "Leaving spectators restores CT collision and life.");
			var remote = GD.Load<PackedScene>("res://scenes/player/player.tscn").Instantiate<Player>(); remote.LocalInput = false; AddChild(remote);
			remote.ApplyNetworkState(_player.CaptureNetworkState(), true);
			Check(remote.Team == PlayerTeam.CounterTerrorist && remote.AgentId == "gign", "Snapshot carries CT team/model together.");
			remote.QueueFree();
			foreach (var id in new[] { "dust2", "inferno", "kz_jayverb000", "de_cpl_strike" })
			{
				var map = GD.Load<PackedScene>($"res://scenes/maps/{id}.tscn").Instantiate<Node3D>();
				var player = map.GetNode<Player>("Player");
				var initialJoin = id == "dust2";
				player.LocalInput = initialJoin;
				if (!initialJoin) player.Team = PlayerTeam.CounterTerrorist;
				var spawnEvents = 0;
				player.Respawned += () => spawnEvents++;
				AddChild(map);
				if (initialJoin)
				{
					await Frame(3);
					Check(spawnEvents == 0 && !player.IsAlive && player.Team == PlayerTeam.Unassigned, "Loading a map must not respawn as T before the menu.");
					player.JoinTeam(3);
					player.Respawn();
					Check(spawnEvents == 0 && !player.IsAlive && !player.Body.Visible, "CT selection and early respawn requests must stay in observation.");
					player.JoinClass(5);
					Check(spawnEvents == 1 && player.IsAlive && !player.AwaitingInitialClass, "Class selection produces exactly one initial spawn.");
					player.SetPhysicsProcess(false);
				}
				var markers = player.GetNode<Node3D>(player.SpawnPointsRootPath).FindChildren("Spawn_counterterrorist_*", "Marker3D", true, false).OfType<Marker3D>().ToArray();
				Check(markers.Length > 0, id + ": original CT spawn markers");
				for (var i = 0; i < 8; i++)
				{
					player.Respawn();
					Check(markers.Any(m => player.GlobalPosition.DistanceTo(m.GlobalPosition + Vector3.Up * .04f) < .001f && player.GlobalBasis.IsEqualApprox(m.GlobalBasis)), id + ": CT spawn and yaw");
				}
				map.QueueFree(); await Frame();
			}
			if (OS.GetCmdlineUserArgs().Contains("--render-team-menu"))
			{
				menu.Open(); await Frame(15); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
				GetViewport().GetTexture().GetImage().SavePng("res://.godot/css_team_menu.png");
				_player.JoinTeam(3);
				foreach (var agent in PlayerBody.CounterTerroristAgentIds)
				{
					menu.GetNode<Button>("Root/ClassPage/" + agent).GrabFocus(); await Frame(25);
					await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
					GetViewport().GetTexture().GetImage().SavePng($"res://assets/players/{agent}/source/menu_preview.png");
				}
			}
			GD.Print($"TEAM_CHECKS checks={_checks} failures=0"); GetTree().Quit();
		}
		catch (Exception error) { GD.PushError($"TEAM_CHECKS after {_checks}: {error}"); GetTree().Quit(1); }
	}
}
