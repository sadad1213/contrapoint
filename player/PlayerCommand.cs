using Godot;

namespace Contrapoint.Players;

// One physics tick of intent. Bots or network prediction can supply the same command.
public readonly record struct PlayerCommand(Vector2 Move, bool Jump, bool Duck, bool Walk)
{
	// A discrete wheel pulse remains a fresh press even on consecutive ticks.
	public bool JumpPressed { get; init; }
}
