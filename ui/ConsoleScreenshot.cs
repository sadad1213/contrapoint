using Godot;
using System;
using Contrapoint.Players;

namespace Contrapoint.UI;

public static class ConsoleScreenshot
{
	public static async void Save(Player player, bool jpeg, Action<string, bool> print)
	{
		if (DisplayServer.GetName() == "headless") { print("Screenshots require a rendered viewport.", true); return; }
		await player.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		if (!GodotObject.IsInstanceValid(player) || !player.IsInsideTree()) return;
		var directory = ProjectSettings.GlobalizePath("user://screenshots");
		DirAccess.MakeDirRecursiveAbsolute(directory);
		var path = directory + "/" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + (jpeg ? ".jpg" : ".png");
		using var image = player.GetViewport().GetTexture().GetImage();
		var error = jpeg ? image.SaveJpg(path, .9f) : image.SavePng(path);
		print(error == Error.Ok ? "Wrote " + path : "Screenshot failed: " + error, error != Error.Ok);
	}
}
