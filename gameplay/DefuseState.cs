using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.Network;
using Contrapoint.Players;
using Contrapoint.Weapons;
using GDictionary = Godot.Collections.Dictionary;
using GArray = Godot.Collections.Array;

namespace Contrapoint.Gameplay;

public enum DefusePhase { Waiting, Freeze, Live, Ended, Intermission }
public enum DefuseResult { None, TerroristsEliminated, CounterTerroristsEliminated, TargetSaved, BombExploded, BombDefused, Draw }

// The same authority drives local play, bots and LAN. Clients only display snapshots.
public partial class DefuseState : Node
{
	public static DefuseState? Instance { get; private set; }
	public DefusePhase Phase { get; private set; }
	public DefuseResult Result { get; private set; }
	public PlayerTeam Winner { get; private set; }
	public int RoundNumber { get; private set; }
	public int TerroristWins { get; private set; }
	public int CounterTerroristWins { get; private set; }
	public double Remaining { get; private set; }
	public double RoundElapsed { get; private set; }
	public double BombRemaining { get; private set; }
	public bool BombPlanted { get; private set; }
	public Vector3 BombPosition { get; private set; }
	public Player? Carrier { get; private set; }
	public Player? Planter { get; private set; }
	public Player? Defuser { get; private set; }
	public double PlantProgress { get; private set; }
	public double DefuseProgress { get; private set; }
	public bool ReloadingRound { get; private set; }
	public bool SpawningRound { get; private set; }
	private readonly HashSet<ulong> _spawned = [];
	private readonly HashSet<ulong> _initialized = [];
	private readonly List<ObjectivePickup> _pickups = [];
	private Node3D? _plantedModel;
	private Area3D? _plantSite;
	private double _publish, _beep, _matchElapsed, _restartIn = -1;
	private int _nextPickup, _tLosses, _ctLosses, _loserBonus = 1400;
	private bool _bombAssigned;
	private double _defuseLength;
	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }
	public Player[] Players => GetTree().GetNodesInGroup("source_players").OfType<Player>()
		.Where(p => !p.IsQueuedForDeletion()).ToArray();
	private static bool Valid(Player? player) => GodotObject.IsInstanceValid(player) && !player!.IsQueuedForDeletion();
	public void Begin()
	{
		var continuing = ReloadingRound;
		ReloadingRound = false;
		_spawned.Clear(); _initialized.Clear(); _pickups.Clear();
		_plantedModel = null; _plantSite = null;
		Carrier = Planter = Defuser = null;
		BombPlanted = _bombAssigned = false;
		BombRemaining = PlantProgress = DefuseProgress = 0;
		_beep = _publish = 0;
		if (!continuing)
		{
			RoundNumber = TerroristWins = CounterTerroristWins = _tLosses = _ctLosses = 0;
			_loserBonus = 1400;
			_matchElapsed = 0; _restartIn = -1;
		}
		Phase = continuing ? DefusePhase.Freeze : DefusePhase.Waiting;
		Remaining = continuing ? DefuseRules.FreezeSeconds : 0;
		RoundElapsed = 0; Winner = PlayerTeam.Unassigned; Result = DefuseResult.None;
		UpdateClock();
	}
	public void InitializePlayer(Player player)
	{
		if (!DefuseRules.Active || LanSession.Instance?.IsClient == true || !_initialized.Add(player.GetInstanceId())) return;
		player.Money = DefuseRules.StartMoney; player.HasDefuser = false;
		player.MatchKills = player.MatchDeaths = 0;
	}
	public bool CanSpawn(Player p) => SpawningRound || Phase == DefusePhase.Waiting
		|| (Phase == DefusePhase.Freeze || Phase == DefusePhase.Live && RoundElapsed < 20) && !_spawned.Contains(p.GetInstanceId());
	public void DidSpawn(Player p) { _spawned.Add(p.GetInstanceId()); InitializePlayer(p); }
	public bool MovementLocked(Player p) => Phase is DefusePhase.Freeze or DefusePhase.Intermission || p == Planter || p == Defuser;
	public bool CanAttack(Player p) => !DefuseRules.Active || Phase is not (DefusePhase.Freeze or DefusePhase.Intermission)
		&& p != Defuser && p != Planter;
	public bool CanBuy(Player p) => Phase is DefusePhase.Waiting or DefusePhase.Freeze or DefusePhase.Live
		&& RoundElapsed <= DefuseRules.BuyMinutes * 60 && InBuyZone(p);
	public Area3D? BombSite(Player p) => AreasAt(p, "source_bomb_target").FirstOrDefault();
	public Vector3 BotGoal(Player p)
	{
		if (BombPlanted) return BombPosition;
		if (p.Team == PlayerTeam.Terrorist)
		{
			var dropped = _pickups.FirstOrDefault(i => GodotObject.IsInstanceValid(i) && !i.Kit);
			if (dropped != null) return dropped.GlobalPosition;
			if (Valid(Carrier) && Carrier != p) return Carrier!.GlobalPosition;
		}
		var sites = GetTree().GetNodesInGroup("source_bomb_target").OfType<Area3D>().ToArray();
		if (sites.Length == 0) return p.GlobalPosition;
		var site = sites[(Math.Abs(p.NetworkPeerId) + RoundNumber) % sites.Length];
		var shape = site.GetChildren().OfType<CollisionShape3D>().FirstOrDefault();
		if (shape == null) return site.GlobalPosition;
		var bounds = shape.Shape.GetDebugMesh().GetAabb();
		var center = shape.ToGlobal(bounds.GetCenter());
		var ray = PhysicsRayQueryParameters3D.Create(center + Vector3.Up * bounds.Size.Y / 2,
			center - Vector3.Up * (bounds.Size.Y / 2 + 3), 1);
		var hit = p.GetWorld3D().DirectSpaceState.IntersectRay(ray);
		return hit.Count == 0 ? center : hit["position"].AsVector3();
	}
	public bool InBuyZone(Player p)
	{
		var zones = GetTree().GetNodesInGroup("source_buyzone").OfType<Area3D>().ToArray();
		if (zones.Length == 0)
		{
			var name = p.Team == PlayerTeam.Terrorist ? "Spawn_terrorist_*" : "Spawn_counterterrorist_*";
			return !p.SpawnPointsRootPath.IsEmpty && p.GetNodeOrNull<Node>(p.SpawnPointsRootPath) is { } root
				&& root.FindChildren(name, "Marker3D", true, false).OfType<Marker3D>()
					.Any(s => s.GlobalPosition.DistanceTo(p.GlobalPosition) <= 200 * SourceMovement.Unit);
		}
		return AreasAt(p, "source_buyzone").Any(area =>
		{
			var data = area.Get("source_data").AsGodotDictionary();
			var team = data.TryGetValue("TeamNum", out var value) && int.TryParse(value.AsString(), out var n) ? n : 0;
			return team == 0 || team == (p.Team == PlayerTeam.Terrorist ? 2 : 3);
		});
	}
	private IEnumerable<Area3D> AreasAt(Player p, string group)
	{
		// Query the current hull, never last tick's Area3D overlap after a teleport.
		var shape = p.GetNode<CollisionShape3D>("CollisionShape3D");
		using var query = new PhysicsShapeQueryParameters3D { Shape = shape.Shape, Transform = shape.GlobalTransform,
			CollisionMask = 8, CollideWithAreas = true, CollideWithBodies = false };
		var found = p.GetWorld3D().DirectSpaceState.IntersectShape(query, 64);
		return found.Select(hit => hit["collider"].AsGodotObject()).OfType<Area3D>()
			.Where(a => a.IsInGroup(group) && (!a.Get("enabled").VariantType.Equals(Variant.Type.Bool) || a.Get("enabled").AsBool())).ToArray();
	}
	public bool Grounded(Player p)
	{
		if (p.Movement.Noclip) return false;
		if (p.LocalInput || p.IsBot) return p.IsOnFloor();
		// Ground state on the server also requires actual nearby map geometry.
		if (!p.NetworkGrounded) return false;
		var ray = PhysicsRayQueryParameters3D.Create(p.GlobalPosition + Vector3.Up * .08f,
			p.GlobalPosition - Vector3.Up * .12f, 1, new() { p.GetRid() });
		return p.GetWorld3D().DirectSpaceState.IntersectRay(ray).Count > 0;
	}
	public bool CanPlant(Player p) => Phase == DefusePhase.Live && !BombPlanted && Carrier == p && p.IsAlive
		&& p.Team == PlayerTeam.Terrorist && p.ActiveWeapon is C4View && Grounded(p) && BombSite(p) != null;
	public bool CanDefuse(Player p)
	{
		if (!BombPlanted || !p.IsAlive || p.Team != PlayerTeam.CounterTerrorist || !Grounded(p)
			|| p.GlobalPosition.DistanceTo(BombPosition) > DefuseRules.UseDistance) return false;
		var aim = BombPosition + Vector3.Up * .08f - p.Camera.GlobalPosition;
		if ((-p.Camera.GlobalBasis.Z).Dot(aim.Normalized()) < .8f) return false;
		var ray = PhysicsRayQueryParameters3D.Create(p.Camera.GlobalPosition, BombPosition + Vector3.Up * .08f, 1, new() { p.GetRid() });
		return p.GetWorld3D().DirectSpaceState.IntersectRay(ray).Count == 0;
	}
	public void DropBomb(Player p, bool death = false)
	{
		if (!DefuseRules.Active || Carrier != p || LanSession.Instance?.IsClient == true) return;
		CancelPlant(); Carrier = null;
		foreach (var c4 in p.Inventory.Weapons.OfType<C4View>().ToArray()) p.Inventory.RemoveWeapon(c4);
		SpawnPickup(p, false, death);
		LanSession.Instance?.BroadcastInventory(p); Publish();
	}
	private void SpawnPickup(Player p, bool kit, bool death)
	{
		var pickup = new ObjectivePickup { ObjectiveId = ++_nextPickup, Kit = kit, PreviousOwner = p };
		GetTree().CurrentScene.AddChild(pickup);
		pickup.GlobalPosition = p.GlobalPosition + Vector3.Up * .4f;
		pickup.LinearVelocity = p.Velocity + (death ? Vector3.Zero : p.WeaponDropVelocity());
		_pickups.Add(pickup);
	}
	public void PlayerDied(Player p)
	{
		if (!DefuseRules.Active || LanSession.Instance?.IsClient == true) return;
		if (Carrier == p) DropBomb(p, true);
		if (Defuser == p) CancelDefuse();
		if (p.HasDefuser) { SpawnPickup(p, true, true); p.HasDefuser = false; }
	}
	public bool Pickup(ObjectivePickup item, Player p)
	{
		if (!DefuseRules.Active || !p.IsAlive || p.PickingClass || p.AwaitingInitialClass || LanSession.Instance?.IsClient == true) return false;
		if (item.Kit)
		{
			if (p.Team != PlayerTeam.CounterTerrorist || p.HasDefuser) return false;
			p.HasDefuser = true;
		}
		else
		{
			if (p.Team != PlayerTeam.Terrorist || Carrier != null || BombPlanted) return false;
			GiveBomb(p);
		}
		_pickups.Remove(item); item.QueueFree(); Publish(); return true;
	}
	private void GiveBomb(Player p)
	{
		Carrier = p; _bombAssigned = true; BombPosition = p.GlobalPosition;
		if (!p.Inventory.Weapons.OfType<C4View>().Any()) p.Inventory.AddWeapon(PlayerSceneCache.Load("res://weapons/c4_view.tscn"), false);
		LanSession.Instance?.BroadcastInventory(p);
	}
	private void CancelPlant() { Planter = null; _plantSite = null; PlantProgress = 0; }
	private void CancelDefuse() { Defuser = null; DefuseProgress = 0; }
	private void TickPlant(double delta)
	{
		var p = Carrier;
		if (!Valid(p) || !p!.ObjectiveAttackHeld || !CanPlant(p)) { CancelPlant(); return; }
		if (Planter != p)
		{
			Planter = p; _plantSite = BombSite(p); PlantProgress = 0;
			WorldSound("weapons/c4/c4_plant.wav", p.GlobalPosition);
		}
		else if (_plantSite != BombSite(p)) { CancelPlant(); return; }
		PlantProgress += delta;
		if (PlantProgress < DefuseRules.PlantSeconds) return;
		var site = _plantSite;
		BombPosition = p.GlobalPosition + Vector3.Up * .02f;
		BombRemaining = DefuseRules.BombSeconds; BombPlanted = true;
		Carrier = null; CancelPlant();
		foreach (var c4 in p.Inventory.Weapons.OfType<C4View>().ToArray()) p.Inventory.RemoveWeapon(c4);
		p.ObjectiveAttackHeld = false;
		// CSS awards three scoreboard frags for a successful plant/defuse.
		p.MatchKills += 3;
		LanSession.Instance?.BroadcastInventory(p);
		EmitSite(site, "BombPlanted", p);
		Announce("radio/bombpl.wav"); Publish();
	}
	private void EmitSite(Area3D? site, string output, Player? activator)
	{
		if (!GodotObject.IsInstanceValid(site)) return;
		GD.Load<GDScript>("res://assets/maps/source_io.gd").Call("emit", site!, site!.Get("source_data"), output, activator!);
	}
	private void TickBomb(double delta)
	{
		if (!BombPlanted) return;
		BombRemaining = Math.Max(0, BombRemaining - delta);
		// CSS checks detonation before completing a defuse on the same tick.
		if (BombRemaining <= 0)
		{
			BombPlanted = false; CancelDefuse();
			WorldSound("weapons/c4/c4_explode1.wav", BombPosition);
			Finish(PlayerTeam.Terrorist, DefuseResult.BombExploded);
			foreach (var p in Players.Where(p => p.IsAlive && DeathmatchRules.Playing(p)))
			{
				var units = p.GlobalPosition.DistanceTo(BombPosition) / SourceMovement.Unit;
				// CSS bomb radius damage ignores world visibility, with Gaussian falloff.
				var damage = 500 * Mathf.Exp(-units * units / (2 * Mathf.Pow(1750f / 3, 2)));
				if (units <= 1750) p.TakeBombDamage(damage, BombPosition);
			}
			Publish(); return;
		}
		if (!Valid(Defuser) || !Defuser!.ObjectiveUseHeld || !CanDefuse(Defuser)) CancelDefuse();
		if (Defuser == null)
		{
			var p = Players.FirstOrDefault(p => p.ObjectiveUseHeld && CanDefuse(p));
			if (p != null)
			{
				Defuser = p; _defuseLength = p.HasDefuser ? 5 : 10;
				WorldSound("weapons/c4/c4_disarm.wav", BombPosition);
			}
		}
		if (Defuser != null)
		{
			DefuseProgress += delta;
			if (DefuseProgress >= _defuseLength)
			{
				Defuser.MatchKills += 3;
				BombPlanted = false; CancelDefuse();
				Announce("radio/bombdef.wav"); Finish(PlayerTeam.CounterTerrorist, DefuseResult.BombDefused); Publish();
			}
		}
	}
	public void RecordDeath(Player victim, Player? killer, PlayerDamageKind kind)
	{
		if (!DefuseRules.Active || LanSession.Instance?.IsClient == true) return;
		victim.MatchDeaths++;
		if (kind == PlayerDamageKind.Bomb) return;
		if (killer == null || killer == victim) { victim.MatchKills--; return; }
		var teammate = DeathmatchRules.Teammates(victim, killer);
		killer.MatchKills += teammate ? -1 : 1;
		AddMoney(killer, teammate ? -3300 : 300);
	}
	public static void AddMoney(Player p, int amount)
	{
		p.Money = Math.Clamp(p.Money + amount, 0, 16000);
		LanSession.Instance?.BroadcastVitals(p);
	}
	public void Finish(PlayerTeam winner, DefuseResult reason)
	{
		if (Phase != DefusePhase.Live) return;
		Winner = winner; Result = reason; Phase = DefusePhase.Ended; Remaining = DefuseRules.RestartSeconds;
		if (winner == PlayerTeam.Terrorist) TerroristWins++;
		else if (winner == PlayerTeam.CounterTerrorist) CounterTerroristWins++;
		CancelPlant();
		if (reason != DefuseResult.BombDefused) Announce(winner == PlayerTeam.Terrorist ? "radio/terwin.wav" : winner == PlayerTeam.CounterTerrorist ? "radio/ctwin.wav" : "radio/rounddraw.wav");
		Publish();
	}
	private void PayRoundMoney()
	{
		if (Winner == PlayerTeam.Terrorist)
		{
			if (_tLosses > 1) _loserBonus = 1500;
			_tLosses = 0; _ctLosses++;
		}
		else if (Winner == PlayerTeam.CounterTerrorist)
		{
			if (_ctLosses > 1) _loserBonus = 1500;
			_ctLosses = 0; _tLosses++;
		}
		if ((_tLosses > 1 || _ctLosses > 1) && _loserBonus < 3000) _loserBonus += 500;
		foreach (var p in Players.Where(DeathmatchRules.Playing))
		{
			if (Winner == PlayerTeam.Unassigned) continue;
			if (Result == DefuseResult.TargetSaved && p.Team == PlayerTeam.Terrorist && p.IsAlive) continue;
			var amount = p.Team == Winner ? Result == DefuseResult.BombExploded ? 3500 : 3250 : _loserBonus;
			if (Result == DefuseResult.BombDefused && p.Team == PlayerTeam.Terrorist) amount += 800;
			AddMoney(p, amount);
		}
	}
	public void ScheduleRestart(double seconds) { _restartIn = seconds <= 0 ? -1 : seconds; Publish(); }
	private void StartRound()
	{
		RoundNumber++; RoundElapsed = 0; Phase = DefusePhase.Freeze; Remaining = DefuseRules.FreezeSeconds;
		_spawned.Clear(); SpawningRound = true;
		foreach (var p in Players.Where(p => DeathmatchRules.Playing(p) && !p.AwaitingInitialClass && !p.PickingClass))
			p.RespawnForDefuse(p.IsAlive);
		SpawningRound = false;
		Publish();
	}
	private bool TeamsReady => Players.Any(p => p.Team == PlayerTeam.Terrorist && !p.PickingClass && !p.AwaitingInitialClass)
		&& Players.Any(p => p.Team == PlayerTeam.CounterTerrorist && !p.PickingClass && !p.AwaitingInitialClass);
	public override void _PhysicsProcess(double delta)
	{
		if (!DefuseRules.Active || ReloadingRound || LanSession.Instance?.LocalPlayer == null) return;
		PresentBomb(delta);
		if (LanSession.Instance.IsClient) return;
		foreach (var p in Players)
		{
			InitializePlayer(p);
			if (!p.LocalInput && !p.IsBot && p.ObjectiveInputDeadline < Time.GetTicksMsec())
				p.ObjectiveAttackHeld = p.ObjectiveUseHeld = false;
		}
		_matchElapsed += delta;
		if (_restartIn >= 0)
		{
			_restartIn -= delta;
			if (_restartIn <= 0) { ReloadingRound = false; LanSession.Instance.RestartCurrentMap(); return; }
		}
		if (Phase == DefusePhase.Waiting && TeamsReady) StartRound();
		else if (Phase == DefusePhase.Freeze)
		{
			Remaining = Math.Max(0, Remaining - delta);
			if (Remaining <= 0) { Phase = DefusePhase.Live; Remaining = DefuseRules.RoundMinutes * 60; Announce("radio/letsgo.wav"); }
		}
		else if (Phase == DefusePhase.Live)
		{
			RoundElapsed += delta;
			Remaining = Math.Max(0, Remaining - delta);
			TickPlant(delta); TickBomb(delta);
			var players = Players.Where(p => DeathmatchRules.Playing(p) && !p.PickingClass && !p.AwaitingInitialClass).ToArray();
			if (!players.Any(p => p.IsAlive && p.Team == PlayerTeam.Terrorist) && !BombPlanted)
				Finish(PlayerTeam.CounterTerrorist, DefuseResult.TerroristsEliminated);
			else if (!players.Any(p => p.IsAlive && p.Team == PlayerTeam.CounterTerrorist))
				Finish(PlayerTeam.Terrorist, DefuseResult.CounterTerroristsEliminated);
			else if (Remaining <= 0 && !BombPlanted) Finish(PlayerTeam.CounterTerrorist, DefuseResult.TargetSaved);
		}
		else if (Phase == DefusePhase.Ended)
		{
			TickBomb(delta);
			Remaining = Math.Max(0, Remaining - delta);
			if (Remaining <= 0)
			{
				PayRoundMoney();
				var matchOver = DefuseRules.MaxRounds > 0 && RoundNumber >= DefuseRules.MaxRounds
					|| DefuseRules.WinLimit > 0 && Math.Max(TerroristWins, CounterTerroristWins) >= DefuseRules.WinLimit
					|| DeathmatchRules.TimeLimitMinutes > 0 && _matchElapsed >= DeathmatchRules.TimeLimitMinutes * 60;
				if (matchOver) { Phase = DefusePhase.Intermission; Remaining = 10; }
				else { ReloadingRound = true; RoundNumber++; LanSession.Instance.RestartCurrentMap(); return; }
			}
		}
		else if (Phase == DefusePhase.Intermission)
		{
			Remaining = Math.Max(0, Remaining - delta);
			if (Remaining <= 0) { ReloadingRound = false; LanSession.Instance.RestartCurrentMap(); return; }
		}
		if (Valid(Carrier)) BombPosition = Carrier!.GlobalPosition;
		else if (Carrier != null)
		{
			Carrier = null;
			var item = new ObjectivePickup { ObjectiveId = ++_nextPickup };
			GetTree().CurrentScene.AddChild(item); item.GlobalPosition = BombPosition + Vector3.Up * .3f; _pickups.Add(item);
		}
		if (!_bombAssigned && Phase is DefusePhase.Waiting or DefusePhase.Freeze)
		{
			var candidates = Players.Where(p => p.IsAlive && p.Team == PlayerTeam.Terrorist && !p.PickingClass && !p.AwaitingInitialClass).ToArray();
			if (candidates.Length > 0) GiveBomb(candidates[GD.RandRange(0, candidates.Length - 1)]);
		}
		_publish -= delta;
		if (_publish <= 0) { _publish = .1; Publish(); }
		UpdateClock();
	}
	private void UpdateClock() => RoundState.Instance?.SetObjectiveClock(Remaining, Phase != DefusePhase.Waiting, TerroristWins, CounterTerroristWins);
	private void Publish() { UpdateClock(); LanSession.Instance?.BroadcastMatchState(); }
	private void Announce(string path)
	{
		var p = LanSession.Instance?.LocalPlayer;
		p?.MovementAudio.Sounds.PlayWave(path, relay: false);
		LanSession.Instance?.BroadcastDefuseAnnouncement(path);
	}
	private void WorldSound(string path, Vector3 pos)
	{
		if (GetTree().CurrentScene is Node3D world) SourceSoundPlayer.PlayWorldWave(world, pos, path, 1);
		LanSession.Instance?.BroadcastWeaponImpact(pos, path, 1);
	}
	private void PresentBomb(double delta)
	{
		if (BombPlanted)
		{
			if (!GodotObject.IsInstanceValid(_plantedModel))
			{
				_plantedModel = GD.Load<PackedScene>("res://assets/weapons/c4/planted/c4_planted.glb").Instantiate<Node3D>();
				GetTree().CurrentScene.AddChild(_plantedModel); _plantedModel.GlobalPosition = BombPosition;
				WorldWeaponMaterials.Apply(_plantedModel);
			}
			_beep -= delta;
			if (_beep <= 0)
			{
				_beep = Math.Clamp(BombRemaining / Math.Max(1, DefuseRules.BombSeconds), .12, 1);
				if (GetTree().CurrentScene is Node3D world) SourceSoundPlayer.PlayWorldWave(world, BombPosition, "weapons/c4/c4_beep1.wav", 1);
			}
		}
		else if (GodotObject.IsInstanceValid(_plantedModel)) { _plantedModel!.QueueFree(); _plantedModel = null; }
	}
	public GDictionary Capture() => new()
	{
		["phase"] = (int)Phase, ["result"] = (int)Result, ["winner"] = (int)Winner, ["round"] = RoundNumber,
		["t"] = TerroristWins, ["ct"] = CounterTerroristWins, ["remaining"] = Remaining, ["elapsed"] = RoundElapsed,
		["planted"] = BombPlanted, ["bombtime"] = BombRemaining, ["position"] = BombPosition,
		["carrier"] = Valid(Carrier) ? Carrier!.NetworkPeerId : int.MinValue,
		["planter"] = Valid(Planter) ? Planter!.NetworkPeerId : int.MinValue,
		["defuser"] = Valid(Defuser) ? Defuser!.NetworkPeerId : int.MinValue,
		["plant"] = PlantProgress, ["defuse"] = DefuseProgress, ["length"] = _defuseLength,
		["items"] = new GArray(_pickups.Where(GodotObject.IsInstanceValid).Select(p => (Variant)new GDictionary
			{ ["id"] = p.ObjectiveId, ["kit"] = p.Kit, ["transform"] = p.GlobalTransform }))
	};
	public void Receive(GDictionary state)
	{
		Phase = (DefusePhase)state["phase"].AsInt32(); Result = (DefuseResult)state["result"].AsInt32();
		Winner = (PlayerTeam)state["winner"].AsInt32(); RoundNumber = state["round"].AsInt32();
		TerroristWins = state["t"].AsInt32(); CounterTerroristWins = state["ct"].AsInt32();
		Remaining = state["remaining"].AsDouble(); RoundElapsed = state["elapsed"].AsDouble();
		BombPlanted = state["planted"].AsBool(); BombRemaining = state["bombtime"].AsDouble(); BombPosition = state["position"].AsVector3();
		Carrier = Find(state["carrier"].AsInt32()); Planter = Find(state["planter"].AsInt32()); Defuser = Find(state["defuser"].AsInt32());
		PlantProgress = state["plant"].AsDouble(); DefuseProgress = state["defuse"].AsDouble(); _defuseLength = state["length"].AsDouble();
		var items = state["items"].AsGodotArray().Select(v => v.AsGodotDictionary()).ToArray();
		_pickups.RemoveAll(p => !GodotObject.IsInstanceValid(p));
		foreach (var p in _pickups.ToArray())
			if (!items.Any(i => i["id"].AsInt32() == p.ObjectiveId)) { _pickups.Remove(p); p.QueueFree(); }
		foreach (var item in items)
		{
			var p = _pickups.FirstOrDefault(p => p.ObjectiveId == item["id"].AsInt32());
			if (p == null)
			{
				p = new ObjectivePickup { ObjectiveId = item["id"].AsInt32(), Kit = item["kit"].AsBool(), Replica = true };
				GetTree().CurrentScene.AddChild(p); _pickups.Add(p);
			}
			p.GlobalTransform = item["transform"].AsTransform3D();
		}
		UpdateClock();
	}
	private Player? Find(int id) => Players.FirstOrDefault(p => p.NetworkPeerId == id);
}
