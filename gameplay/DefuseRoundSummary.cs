using System;
using System.Collections.Generic;
using System.Linq;
using Contrapoint.Players;
using GDictionary = Godot.Collections.Dictionary;

namespace Contrapoint.Gameplay;

public partial class DefuseState
{
	public GDictionary RoundSummary { get; private set; } = new();
	private readonly Dictionary<ulong, int> _roundKills = new();
	private Player? _roundPlanter, _roundDefuser;
	private void ResetRoundSummary()
	{
		RoundSummary = new(); _roundKills.Clear(); _roundPlanter = _roundDefuser = null;
	}
	private void CompleteRoundSummary()
	{
		var best = Players.Where(p => p.Team == Winner)
			.OrderByDescending(p => _roundKills.GetValueOrDefault(p.GetInstanceId())).FirstOrDefault();
		var mvp = Result == DefuseResult.BombExploded ? _roundPlanter
			: Result == DefuseResult.BombDefused ? _roundDefuser
			: best != null && _roundKills.GetValueOrDefault(best.GetInstanceId()) > 0 ? best : null;
		var reason = Result == DefuseResult.BombExploded ? "winpanel_mvp_award_bombplant"
			: Result == DefuseResult.BombDefused ? "winpanel_mvp_award_bombdefuse" : "winpanel_mvp_award_kills";
		RoundSummary = new() { ["mvp"] = Valid(mvp) ? mvp!.DisplayName : "", ["reason"] = reason,
			["team"] = (int)Winner, ["fact"] = "", ["player"] = "", ["value"] = 0 };
		var top = Players.OrderByDescending(p => _roundKills.GetValueOrDefault(p.GetInstanceId())).FirstOrDefault();
		if (top != null && _roundKills.GetValueOrDefault(top.GetInstanceId()) > 0)
		{
			RoundSummary["fact"] = "funfact_killed_enemies";
			RoundSummary["player"] = top.DisplayName;
			RoundSummary["value"] = _roundKills[top.GetInstanceId()];
		}
		else if (RoundElapsed < 30)
		{
			RoundSummary["fact"] = "funfact_short_round";
			RoundSummary["value"] = (int)Math.Floor(RoundElapsed);
		}
	}
}
