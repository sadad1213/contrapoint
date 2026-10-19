using Godot;
using System;
using Contrapoint.Network;
using Contrapoint.Weapons;
using GDictionary = Godot.Collections.Dictionary;
using Key = Contrapoint.Network.PlayerStateKeys;

namespace Contrapoint.Players;

public partial class Player
{
	public int NetworkPeerId { get; set; }
	public int NetworkTeleportRevision { get; set; }
	public bool NetworkGrounded { get; private set; }
	private Vector3 _networkPosition;
	private float _networkYaw;
	private bool _hasNetworkState;

	public GDictionary CaptureNetworkState(bool includeInventory = false)
	{
		var animation = ActiveWeapon?.NetworkAnimation;
		using var animationName = animation?.CurrentAnimation;
		using var weaponName = ActiveWeapon?.Name;
		var clip = ActiveWeapon is KnifeView knife ? knife.NetworkClipName : animationName?.ToString() ?? "";
		var hasAnimation = clip.Length > 0;
		using var displayValue = Variant.From(DisplayName);
		using var agentValue = Variant.From(AgentId);
		using var weaponValue = Variant.From(weaponName?.ToString() ?? "");
		using var clipValue = Variant.From(clip);
		var state = new GDictionary
		{
			[Key.Bot] = IsBot, [Key.DisplayName] = displayValue,
			[Key.Position] = GlobalPosition, [Key.Velocity] = Velocity, [Key.Yaw] = Rotation.Y, [Key.Pitch] = AimPitch,
			[Key.Revision] = NetworkTeleportRevision,
			[Key.InventoryRevision] = Inventory.NetworkRevision,
			[Key.Autowepswitch] = Inventory.AutoSwitch,
			[Key.Magazine] = (ActiveWeapon as FirearmView)?.MagazineAmmo ?? -1,
			[Key.Reserve] = (ActiveWeapon as FirearmView)?.ReserveAmmo ?? 0,
			[Key.Grenades] = (ActiveWeapon as GrenadeView)?.Ammo ?? 0,
			[Key.MapEpoch] = LanSession.Instance?.MapEpoch ?? 0,
			[Key.Duck] = Movement.IsDucked, [Key.Ground] = LocalInput || IsBot ? IsOnFloor() || Movement.Noclip : NetworkGrounded,
			[Key.Walk] = MovementIntent.Walk, [Key.Agent] = agentValue, [Key.Health] = Health,
			[Key.Initial] = AwaitingInitialClass,
			[Key.Team] = (int)Team, [Key.Armor] = Armor, [Key.Helmet] = HasHelmet, [Key.Money] = Money,
			[Key.Zoom] = (ActiveWeapon as FirearmView)?.ZoomLevel ?? 0,
			[Key.Weapon] = weaponValue, [Key.Clip] = clipValue,
			[Key.Time] = hasAnimation ? animation!.CurrentAnimationPosition : 0, [Key.Silenced] = (ActiveWeapon as FirearmView)?.Silenced ?? false,
			[Key.Burst] = (ActiveWeapon as FirearmView)?.BurstMode ?? false
		};
		if (includeInventory) state[Key.Inventory] = Inventory.CaptureContents();
		return state;
	}

	private static string NetworkString(GDictionary state, Variant key)
	{
		using var value = state[key];
		return value.AsString();
	}

	public void ApplyNetworkState(GDictionary state, bool snap)
	{
		if (LocalInput || !state.ContainsKey(Key.Position) || !state.ContainsKey(Key.Velocity) || !state.ContainsKey(Key.Pitch) || !state.ContainsKey(Key.Yaw)) return;
		if (LanSession.Instance?.IsClient == true)
		{
			if (state.TryGetValue(Key.Bot, out var bot)) IsBot = bot.AsBool();
			if (state.TryGetValue(Key.DisplayName, out var displayName))
			{
				using (displayName) BotDisplayName = displayName.AsString();
			}
		}
		if (LanSession.Instance?.IsClient == true && state.TryGetValue(Key.Revision, out var revision))
		{
			if (revision.AsInt32() < NetworkTeleportRevision) return;
			NetworkTeleportRevision = revision.AsInt32();
		}
		var position = state[Key.Position].AsVector3(); var velocity = state[Key.Velocity].AsVector3();
		var yaw = state[Key.Yaw].AsSingle(); var pitch = state[Key.Pitch].AsSingle();
		if (!position.IsFinite() || !velocity.IsFinite() || !float.IsFinite(yaw) || !float.IsFinite(pitch) || position.LengthSquared() > 1e10f || velocity.LengthSquared() > 1e8f) return;
		_networkPosition = position; _networkYaw = yaw;
		var hosting = LanSession.Instance?.IsHosting == true;
		if (hosting && Contrapoint.Gameplay.DefuseRules.Active && Contrapoint.Gameplay.DefuseState.Instance?.MovementLocked(this) == true)
		{ position = GlobalPosition; velocity = Vector3.Zero; _networkPosition = position; }
		var reset = snap || !_hasNetworkState || GlobalPosition.DistanceTo(position) > 4;
		var renderedBody = Body.GlobalTransform;
		if (hosting || reset) { GlobalPosition = position; Rotation = new(0, yaw, 0); }
		// Keep host collision, gameplay eyes and relayed snapshots at the received pose.
		// Only the visible body trails behind it between network updates.
		if (reset) Body.Transform = Transform3D.Identity;
		else if (hosting) Body.GlobalTransform = renderedBody;
		_hasNetworkState = true;
		Velocity = velocity; _pitch = Mathf.Clamp(pitch, -1.55334f, 1.55334f); Camera.Rotation = new(_pitch, 0, 0);
		NetworkGrounded = state[Key.Ground].AsBool();
		Movement.ApplyNetworkDuck(state[Key.Duck].AsBool());
		MovementIntent = new(default, false, Movement.IsDucked, state[Key.Walk].AsBool());
		var health = Math.Clamp(state[Key.Health].AsInt32(), 0, MaxHealth);
		if (state.TryGetValue(Key.Initial, out var initial)) AwaitingInitialClass = initial.AsBool();
		if (state.TryGetValue(Key.Team, out var team) && Enum.IsDefined(typeof(PlayerTeam), team.AsInt32())) Team = (PlayerTeam)team.AsInt32();
		// The reliable death event supplies the exact pose and impulse before HP reaches 0.
		// An older unreliable alive snapshot can arrive after reliable PlayerDeath.
		// Only the reliable respawn event may bring a dead replica back to life.
		if (LanSession.Instance?.IsHosting != true && (LanSession.Instance?.IsClient != true || health > 0 && Death?.Active != true))
			SetVitals(health, state.TryGetValue(Key.Armor, out var armor) ? armor.AsInt32() : 0,
				state.TryGetValue(Key.Helmet, out var helmet) && helmet.AsBool());
		var agent = NetworkString(state, Key.Agent);
		if (agent != AgentId && PlayerBody.AgentTeam(agent) == Team) SetAgent(agent);
		var weaponName = NetworkString(state, Key.Weapon);
		// Read the packet once, rather than once per inventory candidate.
		var weapon = Inventory.FindWeapon(weaponName);
		if (LanSession.Instance?.IsHosting == true && state.TryGetValue(Key.Autowepswitch, out var autoSwitch)) Inventory.AutoSwitch = autoSwitch.AsBool();
		if (weapon == null) return;
		if (state.TryGetValue(Key.InventoryRevision, out var inventoryRevision) && inventoryRevision.AsInt32() != Inventory.NetworkRevision) return;
		Inventory.EquipNetwork(weapon);
		weapon.ApplyNetworkAnimation(NetworkString(state, Key.Clip), state[Key.Time].AsDouble());
		if (weapon is FirearmView gun) gun.ApplyNetworkSilencer(state[Key.Silenced].AsBool());
		if (weapon is FirearmView burstGun && state.TryGetValue(Key.Burst, out var burst)) burstGun.ApplyNetworkBurst(burst.AsBool());
		if (LanSession.Instance?.IsHosting == true && weapon is FirearmView serverGun && state.ContainsKey(Key.Magazine))
		{
			serverGun.RestoreMagazine(state[Key.Magazine].AsInt32());
			Inventory.SetReserveAmmo(serverGun.AmmoType, state[Key.Reserve].AsInt32(), serverGun.ReserveCapacity);
		}
		if (LanSession.Instance?.IsHosting == true && weapon is GrenadeView serverGrenade && state.ContainsKey(Key.Grenades))
			serverGrenade.RestoreAmmo(state[Key.Grenades].AsInt32());
	}

	public override void _Process(double delta)
	{
		if (LocalInput || !_hasNetworkState) return;
		var weight = 1 - Mathf.Exp(-25 * (float)delta);
		if (LanSession.Instance?.IsHosting == true)
		{
			Body.Transform = Body.Transform.InterpolateWith(Transform3D.Identity, weight);
			return;
		}
		GlobalPosition = GlobalPosition.Lerp(_networkPosition, weight);
		Rotation = new(0, Mathf.LerpAngle(Rotation.Y, _networkYaw, weight), 0);
	}

	private void ResetNetworkInterpolation()
	{
		_networkPosition = GlobalPosition;
		_networkYaw = Rotation.Y;
		if (Body != null) Body.Transform = Transform3D.Identity;
	}
}
