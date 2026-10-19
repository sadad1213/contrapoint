using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.Weapons;

public partial class DefuseChecks : Node
{
	private int _checks;
	private Player? _opponent;
	private Player P => LanSession.Instance!.LocalPlayer!;
	private DefuseState De => DefuseState.Instance!;
	private void Check(bool value, string message) { _checks++; if (!value) throw new Exception(message); GD.Print("DE_OK " + message); }
	private async Task Until(Func<bool> test, string message, double seconds = 25)
	{
		var end = Time.GetTicksMsec() + (ulong)(seconds * 1000);
		while (!test()) { if (Time.GetTicksMsec() > end) throw new TimeoutException(message); await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
	}
	private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
	public override void _PhysicsProcess(double delta)
	{
		if (GodotObject.IsInstanceValid(_opponent) && _opponent!.IsAlive) _opponent.Simulate(default, (float)delta);
	}
	public override async void _Ready()
	{
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		Reparent(GetTree().Root); GetTree().CurrentScene = null;
		try
		{
			await Run(); GD.Print($"DE_CHECKS checks={_checks} failures=0"); GetTree().Quit();
		}
		catch (Exception e) { GD.PushError(e.ToString()); GD.Print($"DE_CHECKS checks={_checks} failures=1"); GetTree().Quit(1); }
	}
	private async Task Start()
	{
		MatchSession.Begin(new MatchSetup { ModeId = "defuse", FallDamage = false, FriendlyFire = false });
		GetTree().ChangeSceneToFile("res://scenes/maps/dust2.tscn");
		await Until(() => LanSession.Instance!.LocalPlayer?.AwaitingInitialClass == true, "map attached");
		DefuseRules.FreezeSeconds = .4f; DefuseRules.RestartSeconds = 30; DeathmatchRules.TimeLimitMinutes = 0;
		P.JoinTeam(2); P.JoinClass(1);
		_opponent = PlayerSceneCache.Load("res://scenes/player/player.tscn").Instantiate<Player>();
		_opponent.LocalInput = false; _opponent.IsBot = true; _opponent.NetworkPeerId = -1; _opponent.Team = PlayerTeam.CounterTerrorist;
		_opponent.SpawnPointsRootPath = P.SpawnPointsRootPath; _opponent.DebugControls = false;
		P.GetParent().AddChild(_opponent);
		await Until(() => De.Phase == DefusePhase.Freeze, "round starts");
	}
	private async Task AtSite()
	{
		var goal = De.BotGoal(P);
		P.SourceTeleport(goal + Vector3.Up * .06f, 0, 0);
		await Until(() => P.IsOnFloor(), "floor under bombsite");
		Check(De.BombSite(P) != null, "actual imported bomb trigger contains standing player");
		P.Inventory.EquipSlot(WeaponSlot.Objective);
		await Delay(1.1);
	}
	private async Task Plant()
	{
		P.SetObjectiveAttack(true);
		await Until(() => De.BombPlanted, "three-second plant", 5);
		P.SetObjectiveAttack(false);
		Check(!P.Inventory.Weapons.OfType<C4View>().Any() && De.Carrier == null, "plant consumes exactly one carried C4");
	}
	private void AimAt(Player p, Vector3 position)
	{
		var d = position - p.Camera.GlobalPosition;
		p.SetBotAim(Mathf.Atan2(-d.X, -d.Z), Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
	}
	private async Task Run()
	{
		await Start();
		Check(P.Money == 800 && P.Armor == 0 && !P.HasHelmet, "CSS starts with $800 and no free armor");
		Check(P.Inventory.Weapons.OfType<FirearmView>().Single().Kind == FirearmView.FirearmKind.Glock, "T default pistol and knife");
		Check(De.InBuyZone(P) && BuyCatalog.CanOpen(P), "buying in actual T buy zone");
		var origin = P.GlobalPosition; P.Simulate(new(new(1, 0), false, false, false), .1f);
		Check(P.GlobalPosition.IsEqualApprox(origin), "freeze period blocks movement");
		Check(P.Buy("usp").Length > 0 && P.Buy("defuser").Length > 0, "T cannot buy CT weapons or defusal kit");
		Check(_opponent!.Buy("defuser").Length == 0 && _opponent.HasDefuser && _opponent.Money == 600, "CT kit costs exactly $200");
		await Until(() => De.Phase == DefusePhase.Live && De.Carrier == P, "live round and bomb assignment");
		await AtSite();
		Check(!BuyCatalog.CanOpen(P), "buying outside base is blocked");
		P.SetObjectiveAttack(true); await Delay(1); P.SetObjectiveAttack(false); await Delay(.1);
		Check(!De.BombPlanted && De.PlantProgress == 0, "releasing attack cancels and clears partial plant");
		await Plant();
		var bomb = De.BombPosition;
		_opponent.SourceTeleport(bomb + new Vector3(.8f, .06f, 0), 0, 0);
		await Until(() => _opponent.IsOnFloor(), "defuser grounded"); AimAt(_opponent, bomb);
		_opponent.ObjectiveUseHeld = true; await Delay(1); _opponent.ObjectiveUseHeld = false; await Delay(.1);
		Check(De.BombPlanted && De.DefuseProgress == 0, "releasing use resets defuse progress");
		AimAt(_opponent, bomb); _opponent.ObjectiveUseHeld = true;
		await Until(() => De.Phase == DefusePhase.Ended, "kit defuse", 7);
		Check(De.Result == DefuseResult.BombDefused && De.CounterTerroristWins == 1 && _opponent.MatchKills == 3, "five-second kit defuse awards CT round and three frags");
		Check(P.MatchKills == 3, "plant awards three CSS scoreboard frags");
		P.Money = 1000; P.Armor = 80; P.HasHelmet = true;
		var old = P.GetInstanceId(); var scene = GetTree().CurrentScene.GetInstanceId();
		De._PhysicsProcess(31);
		await Until(() => LanSession.Instance!.LocalPlayer != null && P.GetInstanceId() != old, "next round map restoration");
		Check(GetTree().CurrentScene.GetInstanceId() != scene && De.RoundNumber == 2 && De.CounterTerroristWins == 1, "next round resets the map and preserves team round score");
		Check(P.IsAlive && P.Money == 3200 && P.Armor == 80 && P.HasHelmet && P.MatchKills == 3, "survivor keeps equipment and receives $1400 loss plus $800 plant bonus");
		Check(P.Inventory.Weapons.OfType<FirearmView>().Single().Kind == FirearmView.FirearmKind.Glock, "survivor inventory survives map reload");
		_opponent = null;
		await Start(); await Until(() => De.Phase == DefusePhase.Live && De.Carrier == P, "second scenario");
		await AtSite(); await Plant();
		P.TakeDamage(100);
		await Delay(.1);
		Check(De.Phase == DefusePhase.Live && De.BombPlanted, "eliminating all Ts does not end a planted-bomb round");
		P.Respawn(); Check(!P.IsAlive && !P.Death.CanRespawn, "manual and automatic respawns are blocked until next round");
		_opponent!.SourceTeleport(new(100, 100, 100), 0, 0);
		De._PhysicsProcess(46);
		Check(De.Result == DefuseResult.BombExploded && De.TerroristWins == 1, "detonation awards T round after planter dies");
		Check(!De.BombPlanted && !P.IsAlive, "detonation clears planted objective without reviving players");
		await Start(); await Until(() => De.Phase == DefusePhase.Live && De.Carrier == P, "drop scenario");
		await Delay(.1);
		P.Inventory.EquipSlot(WeaponSlot.Objective); P.DropActiveWeapon();
		Check(De.Carrier == null && GetTree().CurrentScene.GetChildren().OfType<ObjectivePickup>().Any(i => !i.Kit), "G drops the C4 as a physical world pickup");
		P.TakeDamage(100); await Delay(.1);
		Check(De.Result == DefuseResult.TerroristsEliminated && De.CounterTerroristWins == 1, "CT wins by eliminating Ts before plant");
	}
}
