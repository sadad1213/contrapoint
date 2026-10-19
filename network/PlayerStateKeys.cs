using Godot;

namespace Contrapoint.Network;

// A string converted to Variant owns a native string and a managed finalizer.
// Retain the fixed wire keys once instead of allocating them for every field
// of every player's 30 Hz snapshot. Keep String keys for protocol compatibility.
internal static class PlayerStateKeys
{
	public static readonly Variant Bot = "bot";
	public static readonly Variant DisplayName = "display_name";
	public static readonly Variant Position = "position";
	public static readonly Variant Velocity = "velocity";
	public static readonly Variant Yaw = "yaw";
	public static readonly Variant Pitch = "pitch";
	public static readonly Variant ViewAngles = "view_angles";
	public static readonly Variant Flash = "flash";
	public static readonly Variant ViewmodelOffset = "viewmodel_offset";
	public static readonly Variant Revision = "revision";
	public static readonly Variant InventoryRevision = "inventory_revision";
	public static readonly Variant Autowepswitch = "autowepswitch";
	public static readonly Variant Magazine = "magazine";
	public static readonly Variant Reserve = "reserve";
	public static readonly Variant Grenades = "grenades";
	public static readonly Variant MapEpoch = "map_epoch";
	public static readonly Variant Duck = "duck";
	public static readonly Variant Ground = "ground";
	public static readonly Variant Walk = "walk";
	public static readonly Variant Agent = "agent";
	public static readonly Variant Health = "health";
	public static readonly Variant Money = "money";
	public static readonly Variant Initial = "initial";
	public static readonly Variant Team = "team";
	public static readonly Variant Armor = "armor";
	public static readonly Variant Helmet = "helmet";
	public static readonly Variant Zoom = "zoom";
	public static readonly Variant Weapon = "weapon";
	public static readonly Variant Clip = "clip";
	public static readonly Variant Time = "time";
	public static readonly Variant Silenced = "silenced";
	public static readonly Variant Burst = "burst";
	public static readonly Variant Inventory = "inventory";
}
