using Godot;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.Players;
using Contrapoint.Weapons;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Network;

public partial class LanSession
{
	private readonly Dictionary<int, WeaponPickup> _weaponPickups = [];
	private readonly Dictionary<int, int> _weaponSequences = [];
	private int _nextWeaponPickup;

	public bool RequestWeaponDrop(Player player)
	{
		if (!IsClient) return false;
		if (WorldReady) RpcId(1, MethodName.WeaponDropRequested, player.CaptureNetworkState());
		return true;
	}
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void WeaponDropRequested(GDictionary state)
	{
		var player = ActionPlayer(state);
		player?.DropActiveWeapon();
	}

	public void BroadcastInventory(Player player, bool pickupSound = false)
	{
		if (!IsHosting) return;
		player.Inventory.NetworkRevision++;
		foreach (var peer in _readyPeers) RpcId(peer, MethodName.WeaponInventory, _mapEpoch, player.NetworkPeerId,
			player.Inventory.NetworkRevision, player.Inventory.CaptureContents(), player.ActiveWeapon?.Name.ToString() ?? "", pickupSound);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void WeaponInventory(int epoch, int playerId, int revision, GArray contents, string selected, bool pickupSound)
	{
		if (IsClient && epoch == _mapEpoch && _players.TryGetValue(playerId, out var player))
		{
			player.Inventory.ApplyContents(revision, contents, selected);
			if (pickupSound && player.LocalInput) player.ActiveWeapon?.Sounds.PlayWave("items/itempickup.wav", relay: false);
		}
	}

	public void RegisterWeaponPickup(WeaponPickup pickup)
	{
		if (!IsHosting || _weaponPickups.ContainsValue(pickup)) return;
		var id = ++_nextWeaponPickup;
		_weaponPickups[id] = pickup;
		foreach (var peer in _readyPeers) SendWeaponPickup(peer, id, pickup);
	}
	public void RemoveWeaponPickup(WeaponPickup pickup)
	{
		if (!IsHosting) return;
		var id = _weaponPickups.FirstOrDefault(p => p.Value == pickup).Key;
		if (id == 0) return;
		_weaponPickups.Remove(id);
		foreach (var peer in _readyPeers) RpcId(peer, MethodName.WeaponEntityRemoved, _mapEpoch, id);
	}
	private void SendWeaponPickup(int peer, int id, WeaponPickup pickup) => RpcId(peer, MethodName.WeaponEntityCreated,
		_mapEpoch, id, pickup.WeaponClass, pickup.GlobalTransform, pickup.Magazine, pickup.ExtraAmmo, pickup.Silenced, pickup.GrenadeAmmo, pickup.BurstMode);
	private void SendWeaponPickups(int peer)
	{
		foreach (var (id, pickup) in _weaponPickups)
			if (GodotObject.IsInstanceValid(pickup) && !pickup.IsQueuedForDeletion()) SendWeaponPickup(peer, id, pickup);
	}
	private void SendWeaponPhysics(int peer)
	{
		foreach (var (id, pickup) in _weaponPickups.ToArray())
		{
			if (!GodotObject.IsInstanceValid(pickup) || pickup.IsQueuedForDeletion())
			{
				_weaponPickups.Remove(id);
				foreach (var ready in _readyPeers) RpcId(ready, MethodName.WeaponEntityRemoved, _mapEpoch, id);
				continue;
			}
			RpcId(peer, MethodName.WeaponEntityPhysics, _mapEpoch, ++_sequence, id, pickup.GlobalTransform);
		}
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void WeaponEntityCreated(int epoch, int id, string name, Transform3D transform, int magazine, int reserve, bool silenced, int grenades, bool burst)
	{
		if (!IsClient || epoch != _mapEpoch || _scene == null || _weaponPickups.ContainsKey(id) || !WeaponPickup.IsSupported(name)) return;
		var pickup = new WeaponPickup { Name = "Weapon_" + id, WeaponClass = name, NetworkReplica = true,
			Magazine = magazine, ExtraAmmo = reserve, Silenced = silenced, GrenadeAmmo = grenades, BurstMode = burst };
		_scene.AddChild(pickup);
		pickup.ApplyNetworkTransform(transform);
		_weaponPickups[id] = pickup;
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = 2)]
	private void WeaponEntityPhysics(int epoch, int sequence, int id, Transform3D transform)
	{
		if (!IsClient || epoch != _mapEpoch || !transform.IsFinite()
			|| _weaponSequences.GetValueOrDefault(id) >= sequence || !_weaponPickups.TryGetValue(id, out var pickup)) return;
		_weaponSequences[id] = sequence;
		pickup.ApplyNetworkTransform(transform);
	}
	public void BroadcastWeaponImpact(Vector3 position, string path, float gain)
	{
		if (IsHosting) foreach (var peer in _readyPeers) RpcId(peer, MethodName.WorldSound, position, path, gain);
	}
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void WeaponEntityRemoved(int epoch, int id)
	{
		if (!IsClient || epoch != _mapEpoch) return;
		if (_weaponPickups.Remove(id, out var pickup) && GodotObject.IsInstanceValid(pickup)) pickup.QueueFree();
		_weaponSequences.Remove(id);
	}
	private void ClearWeaponPickups()
	{
		foreach (var pickup in _weaponPickups.Values)
			if (GodotObject.IsInstanceValid(pickup) && pickup.NetworkReplica) pickup.QueueFree();
		_weaponPickups.Clear(); _weaponSequences.Clear();
	}
}
