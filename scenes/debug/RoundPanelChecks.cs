using Godot;
using System;
using System.Threading.Tasks;
using Contrapoint.Gameplay;
using Contrapoint.Players;
using Contrapoint.UI;

public partial class RoundPanelChecks : Node
{
	private int _checks;
	private void Check(bool value, string message)
	{
		_checks++; if (!value) throw new Exception(message);
	}
	private async Task Frames()
	{
		for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}
	public override async void _Ready()
	{
		try
		{
			GetWindow().Mode = Window.ModeEnum.Windowed; GetWindow().Size = new(1280, 720);
			var background = new ColorRect { Color = new Color("59635a"), Size = new(1920, 1080), MouseFilter = Control.MouseFilterEnum.Ignore };
			AddChild(background);
			var layer = new CanvasLayer(); AddChild(layer);
			var panel = new CssRoundPanel(); layer.AddChild(panel);
			var de = DefuseState.Instance!;
			foreach (var result in new[] { DefuseResult.BombExploded, DefuseResult.BombDefused, DefuseResult.TerroristsEliminated, DefuseResult.CounterTerroristsEliminated, DefuseResult.TargetSaved, DefuseResult.Draw })
			{
				var state = de.Capture();
				state["phase"] = (int)DefusePhase.Ended; state["result"] = (int)result;
				state["winner"] = (int)(result is DefuseResult.BombExploded or DefuseResult.CounterTerroristsEliminated ? PlayerTeam.Terrorist : result == DefuseResult.Draw ? PlayerTeam.Unassigned : PlayerTeam.CounterTerrorist);
				state["summary"] = new Godot.Collections.Dictionary { ["mvp"] = result == DefuseResult.Draw ? "" : "Player",
					["reason"] = result == DefuseResult.BombExploded ? "winpanel_mvp_award_bombplant" : result == DefuseResult.BombDefused ? "winpanel_mvp_award_bombdefuse" : "winpanel_mvp_award_kills",
					["fact"] = "funfact_killed_enemies", ["player"] = "Player", ["value"] = 3 };
				// Use the same serializable snapshot that LAN peers consume.
				de.Receive(GD.BytesToVar(GD.VarToBytes(state)).AsGodotDictionary());
				panel.UpdatePanel(de, 0, new(1280, 720));
				Check(panel.Visible && panel.Title.Length > 0, result + " displays its localized title");
				Check(result == DefuseResult.Draw ? panel.MvpText.Length == 0 : panel.Description.Length > 0 && panel.MvpText.Contains("Player"), result + " uses replicated result and MVP");
				Check(panel.Position.DistanceTo(new(475, 430.5f)) < .01f && panel.Size == new Vector2(330, 207), "original CSS placement at 720p");
				await Frames();
				if (DisplayServer.GetName() != "headless")
				{
					await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
					GetViewport().GetTexture().GetImage().SavePng($"res://.godot/round_panel_{result}.png");
				}
			}
			panel.UpdatePanel(de, 0, new(1920, 1080));
			Check(panel.Size == new Vector2(495, 310.5f), "1080p proportional geometry");
			var start = de.Capture(); start["phase"] = (int)DefusePhase.Freeze; de.Receive(start);
			panel.UpdatePanel(de, .75, new(1280, 720));
			Check(panel.Visible && Math.Abs(panel.Opacity - .5f) < .001f, "next round fades over CSS default 1.5 seconds");
			panel.UpdatePanel(de, .8, new(1280, 720));
			Check(!panel.Visible, "completed fade hides the panel");
			GD.Print($"ROUND_PANEL_CHECKS checks={_checks} failures=0"); GetTree().Quit();
		}
		catch (Exception e) { GD.PushError(e.ToString()); GD.Print($"ROUND_PANEL_CHECKS checks={_checks} failures=1"); GetTree().Quit(1); }
	}
}
