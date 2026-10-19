using System;

namespace Contrapoint.UI;

// Network transports publish measured channel statistics here. An unconnected
// game has no traffic, latency, interpolation or server-framerate measurements.
public readonly record struct NetGraphSnapshot(
	float PingMilliseconds = 0, float IncomingKilobytes = 0, float OutgoingKilobytes = 0,
	float IncomingPacketsPerSecond = 0, float OutgoingPacketsPerSecond = 0,
	float InterpolationMilliseconds = 0, float Loss = 0, float Choke = 0,
	float ServerFramesPerSecond = 0, float ServerFrameDeviationMilliseconds = 0,
	int UpdateRate = 0, int CommandRate = 0, float LargePacketThreshold = 0,
	float IncomingStreamProgress = 0, float OutgoingStreamProgress = 0);

// Source's INetChannelInfo payload order, including the independent total size.
public readonly record struct NetGraphPayload(int LocalPlayer = 0, int OtherPlayers = 0,
	int Entities = 0, int Sounds = 0, int Events = 0, int UserMessages = 0,
	int EntityMessages = 0, int StringCommands = 0, int StringTables = 0, int Voice = 0)
{
	public int this[int index] => index switch
	{
		0 => LocalPlayer, 1 => OtherPlayers, 2 => Entities, 3 => Sounds, 4 => Events,
		5 => UserMessages, 6 => EntityMessages, 7 => StringCommands, 8 => StringTables, 9 => Voice,
		_ => throw new ArgumentOutOfRangeException(nameof(index))
	};
}

public enum NetGraphPacketState { Normal, Dropped, Invalid, Skipped, Choked }
public readonly record struct NetGraphPacket(int Bytes = 0, float LatencyMilliseconds = 0,
	NetGraphPacketState State = NetGraphPacketState.Normal, NetGraphPayload Payload = default);
public readonly record struct NetGraphCommand(int Bytes = 0, float InterpolationTicks = 0, bool Sent = false);

public sealed class NetGraphData
{
	public const int HistoryLength = 1024;
	private readonly NetGraphPacket[] _incoming = new NetGraphPacket[HistoryLength];
	private readonly NetGraphCommand[] _outgoing = new NetGraphCommand[HistoryLength];
	private int _incomingSequence;
	private int _outgoingSequence;
	public NetGraphSnapshot Snapshot { get; set; }
	public void RecordIncoming(NetGraphPacket packet) => _incoming[++_incomingSequence & (HistoryLength - 1)] = packet;
	public void RecordOutgoing(NetGraphCommand command) => _outgoing[++_outgoingSequence & (HistoryLength - 1)] = command;
	public NetGraphPacket Incoming(int age) => _incoming[(_incomingSequence - age) & (HistoryLength - 1)];
	public NetGraphCommand Outgoing(int age) => _outgoing[(_outgoingSequence - age) & (HistoryLength - 1)];
}
