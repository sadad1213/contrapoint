using Godot;

namespace Contrapoint.Players;

// All agents share the CSS ValveBiped skeleton. Load the additional authored
// upper-body sequences once instead of duplicating them into eight player GLBs.
internal static class CssWeaponPoses
{
	private static AnimationLibrary? _library;
	public static AnimationLibrary Library
	{
		get
		{
			if (_library != null) return _library;
			var source = PlayerSceneCache.Load("res://assets/players/weapon_poses/phoenix.glb").Instantiate();
			_library = ((AnimationPlayer)source.FindChild("AnimationPlayer", true, false)).GetAnimationLibrary("");
			source.Free();
			return _library;
		}
	}
	public static void Release() { _library?.Dispose(); _library = null; }
}
