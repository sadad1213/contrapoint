using Godot;
using System;
using System.Linq;
using Contrapoint.Network;

namespace Contrapoint.Players;

public partial class Player
{
	public bool PickingClass { get; private set; }
	public bool AwaitingInitialClass { get; private set; }
	public bool ClassSelectionRequired { get; private set; }
	public static bool AllowSpectators { get; set; } = true;
	public bool CanSpectate => AllowSpectators && (Team is PlayerTeam.Unassigned or PlayerTeam.Spectator || !IsAlive);

	public void BeginInitialTeamSelection()
	{
		if (AwaitingInitialClass) return;
		AwaitingInitialClass = true;
		Health = 0;
		Team = PlayerTeam.Unassigned;
		Movement.Reset();
		GetNode<CollisionShape3D>("CollisionShape3D").SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
		Inventory.SetEnabled(false);
		SetMouseCaptured(false);
	}

	// Source command numbers are 0=auto, 1=spectator, 2=T, 3=CT.
	public bool JoinTeam(int number)
	{
		if (number is < 0 or > 3) return false;
		if (LanSession.Instance?.RequestTeamSelection(this, number, -1) == true) return true;
		if (number == 0)
		{
			var players = GetTree().GetNodesInGroup("source_players").OfType<Player>().Where(p => p != this).ToArray();
			var t = players.Count(p => p.Team == PlayerTeam.Terrorist);
			var ct = players.Count(p => p.Team == PlayerTeam.CounterTerrorist);
			number = t < ct ? 2 : ct < t ? 3 : GD.RandRange(2, 3);
		}
		if (number == 1 && !CanSpectate) return false;
		var next = number switch { 1 => PlayerTeam.Spectator, 2 => PlayerTeam.Terrorist, _ => PlayerTeam.CounterTerrorist };
		if (Contrapoint.Gameplay.DefuseRules.Active && Contrapoint.Gameplay.DefuseRules.LimitTeams > 0 && next != Team && next != PlayerTeam.Spectator)
		{
			var players = GetTree().GetNodesInGroup("source_players").OfType<Player>().Where(p => p != this).ToArray();
			var other = next == PlayerTeam.Terrorist ? PlayerTeam.CounterTerrorist : PlayerTeam.Terrorist;
			if (players.Count(p => p.Team == next) + 1 - players.Count(p => p.Team == other) > Contrapoint.Gameplay.DefuseRules.LimitTeams) return false;
		}
		var changed = Team != next;
		if (changed && IsAlive && Team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist) TakeDamage(Health);
		Team = next;
		PickingClass = next != PlayerTeam.Spectator;
		ClassSelectionRequired = changed && PickingClass;
		if (next == PlayerTeam.Spectator)
			EnterSpectator();
		LanSession.Instance?.BroadcastTeamSelection(this);
		PresentTeamSelection();
		return true;
	}

	public bool JoinClass(int number)
	{
		if (Team is not (PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist) || number is < 0 or > 8) return false;
		if (LanSession.Instance?.RequestTeamSelection(this, -1, number) == true) return true;
		var agents = PlayerBody.AgentsForTeam(Team);
		var first = Team == PlayerTeam.CounterTerrorist ? 5 : 1;
		var index = number == 0 ? GD.RandRange(0, 3) : Math.Clamp(number, first, first + 3) - first;
		var entering = ClassSelectionRequired;
		if (!entering && IsAlive) TakeDamage(Health);
		if (!SetAgent(agents[index])) return false;
		PickingClass = false;
		ClassSelectionRequired = false;
		AwaitingInitialClass = false;
		LanSession.Instance?.BroadcastTeamSelection(this);
		TeamMenu?.Close();
		if (entering) Respawn();
		return true;
	}

	public void ApplyTeamSelection(PlayerTeam team, string agent, bool picking, bool required)
	{
		if (PlayerBody.AgentTeam(agent) == team && AgentId != agent) SetAgent(agent);
		Team = team;
		PickingClass = picking;
		ClassSelectionRequired = required;
		if (!picking && team is PlayerTeam.Terrorist or PlayerTeam.CounterTerrorist) AwaitingInitialClass = false;
		if (team == PlayerTeam.Spectator)
			EnterSpectator();
		PresentTeamSelection();
	}

	public void CancelClassSelection()
	{
		if (ClassSelectionRequired) return;
		if (LanSession.Instance?.RequestTeamSelection(this, -1, -2) == true) return;
		PickingClass = false;
		LanSession.Instance?.BroadcastTeamSelection(this);
		TeamMenu?.Close();
	}

	private void EnterSpectator()
	{
		Death.Reset();
		SetThirdPerson(false);
		Inventory.SetEnabled(false);
		if (!Movement.Noclip) Movement.ToggleNoclip();
		Body.Hide();
		GetNode<CollisionShape3D>("CollisionShape3D").SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
	}

	private void PresentTeamSelection()
	{
		if (!LocalInput) return;
		if (PickingClass) TeamMenu?.ShowClasses();
		else TeamMenu?.Close();
	}
}
