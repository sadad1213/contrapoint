using Godot;

namespace Contrapoint.Players;

public partial class SourceWorldSound : AudioStreamPlayer3D
{
	public override void _EnterTree() => SourceSoundPlayer.RetainWorldVoice();
	public override void _ExitTree()
	{
		Stop();
		Stream = null;
		SourceSoundPlayer.ReleaseWorldVoice();
	}
}
