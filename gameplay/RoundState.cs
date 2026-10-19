using Godot;
using System;
using Contrapoint.Network;

namespace Contrapoint.Gameplay;

// The host owns the clock; clients render replicated time through HudSnapshot.
public partial class RoundState : Node
{
	public static RoundState? Instance { get; private set; }
	public string ModeId { get; private set; } = "sandbox";
	public bool RestartPending { get; private set; }
	public int MatchNumber { get; private set; }
	public int TerroristKills { get; private set; }
	public int CounterTerroristKills { get; private set; }
	private double _elapsed, _publishAt;
	private bool _reloading;
	[Export] public double RemainingSeconds { get; set; }
	[Export] public bool TimerRunning { get; set; }
	public int DisplaySeconds => double.IsFinite(RemainingSeconds)
		? (int)Math.Clamp(Math.Ceiling(RemainingSeconds), 0, 5999) : 0;
	public override void _EnterTree() => Instance = this;
	public override void _ExitTree() { if (Instance == this) Instance = null; }

	public void BeginMatch(string mode)
	{
		ModeId = mode;
		DeathmatchRules.FreeForAll = mode != "teamdeathmatch";
		_elapsed = _publishAt = 0;
		RestartPending = _reloading = false;
		TerroristKills = CounterTerroristKills = 0;
		MatchNumber++;
		RefreshTimeLimit();
		if (mode == "defuse") DefuseState.Instance?.Begin();
	}
	public void SetObjectiveClock(double seconds, bool running, int t, int ct)
	{
		RemainingSeconds = seconds; TimerRunning = running;
		TerroristKills = t; CounterTerroristKills = ct;
	}

	public void RefreshTimeLimit()
	{
		if (RestartPending || _reloading) return;
		TimerRunning = DeathmatchRules.Active && DeathmatchRules.TimeLimitMinutes > 0;
		RemainingSeconds = TimerRunning ? Math.Max(0, DeathmatchRules.TimeLimitMinutes * 60 - _elapsed) : 0;
	}

	public void ScheduleRestart(double seconds)
	{
		if (!DeathmatchRules.Active || _reloading) return;
		RestartPending = TimerRunning = true;
		RemainingSeconds = Math.Max(0, seconds);
		LanSession.Instance?.BroadcastMatchState();
	}
	public void CancelRestart()
	{
		RestartPending = false;
		RefreshTimeLimit();
		LanSession.Instance?.BroadcastMatchState();
	}

	public void RecordKill(Contrapoint.Players.Player killer)
	{
		if (killer.Team == Contrapoint.Players.PlayerTeam.Terrorist) TerroristKills++;
		else CounterTerroristKills++;
		LanSession.Instance?.BroadcastMatchState();
	}

	public void Receive(string mode, bool ffa, double seconds, bool running, bool restarting, int number, int t, int ct)
	{
		ModeId = mode; DeathmatchRules.FreeForAll = ffa;
		RemainingSeconds = seconds; TimerRunning = running; RestartPending = restarting;
		MatchNumber = number; TerroristKills = t; CounterTerroristKills = ct;
	}

	public override void _PhysicsProcess(double delta)
	{
		var net = LanSession.Instance;
		if (net?.IsClient == true || !DeathmatchRules.Active || _reloading || net?.LocalPlayer == null) return;
		_elapsed += delta;
		if (!RestartPending) RefreshTimeLimit();
		else RemainingSeconds = Math.Max(0, RemainingSeconds - delta);
		if (TimerRunning && RemainingSeconds <= 0)
		{
			if (!RestartPending) ScheduleRestart(DeathmatchRules.RestartDelay);
			else { _reloading = true; net.RestartCurrentMap(); }
		}
		_publishAt -= delta;
		if (_publishAt <= 0) { _publishAt = .25; net.BroadcastMatchState(); }
	}
}
