using Godot;
using System.Collections.Generic;

namespace Contrapoint.Players;

// Keep managed scene wrappers alive across player churn and map reloads.
// Bots instantiate these same player/model/weapon templates repeatedly.
internal static class PlayerSceneCache
{
	private static readonly Dictionary<string, PackedScene> Scenes = new();
	public static PackedScene Load(string path)
	{
		if (!Scenes.TryGetValue(path, out var scene)) Scenes[path] = scene = GD.Load<PackedScene>(path);
		return scene;
	}
	public static void Release()
	{
		CssWeaponPoses.Release();
		foreach (var scene in Scenes.Values) scene.Dispose();
		Scenes.Clear();
	}
}
