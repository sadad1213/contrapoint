using System;
using Contrapoint.UI;

namespace Contrapoint.Players;

// Add through the console's public registry; its UI/parser remain independently owned.
public static class PlayerCameraCommands
{
	private static bool _registered;
	public static void Register()
	{
		if (_registered) return;
		_registered = true;
		ConsoleCommands.RegisterVariable("mp_allowspectators", 1, 0, 1, _ => Player.AllowSpectators ? 1 : 0,
			(_, value) => Player.AllowSpectators = value != 0, "Allow players to join the spectator team.", false, true, serverOnly: true);
		ConsoleCommands.Register("chooseteam", (args, movement, print) =>
		{
			if (args.Count != 1) { print("Usage: chooseteam", true); return; }
			movement.GetParent<Player>().TeamMenu?.Open();
		});
		ConsoleCommands.Register("jointeam", (args, movement, print) =>
		{
			if (args.Count != 2 || !int.TryParse(args[1], out var team) || !movement.GetParent<Player>().JoinTeam(team))
				print("Usage: jointeam <0=auto|1=spectator|2=T|3=CT>", true);
		}, () => new[] { "0", "1", "2", "3" });
		ConsoleCommands.Register("joinclass", (args, movement, print) =>
		{
			if (args.Count != 2 || !int.TryParse(args[1], out var playerClass) || !movement.GetParent<Player>().JoinClass(playerClass))
				print("Usage: joinclass <0=auto|1-4=T|5-8=CT>", true);
		}, () => new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8" });
		foreach (var name in new[] { "thirdperson", "firstperson" })
			ConsoleCommands.Register(name, (args, movement, print) =>
			{
				if (args.Count != 1) { print("Usage: " + name, true); return; }
				var player = movement.GetParent<Player>();
				if (!player.LocalInput) { print("This command requires a local player.", true); return; }
				player.SetThirdPerson(name == "thirdperson");
				print(name, false);
			}, cheat: name == "thirdperson");
		ConsoleCommands.Register("cl_playermodel", (args, movement, print) =>
		{
			var player = movement.GetParent<Player>();
			if (args.Count == 1) { print("cl_playermodel = " + player.AgentId + "; available: " + string.Join(", ", PlayerBody.AgentIds), false); return; }
			if (args.Count != 2 || !player.SetAgent(args[1]))
			{ print("Usage: cl_playermodel <" + string.Join('|', PlayerBody.AgentIds) + ">", true); return; }
			print("cl_playermodel = " + player.AgentId, false);
		}, () => PlayerBody.AgentIds, cheat:true);
	}
}
