using Godot;
using System.Collections.Generic;

namespace Contrapoint.Players;

// Sample only the bones owned by a layer. AnimationPlayer.Seek would first
// write both complete poses into the live skeleton, then discard half of each.
internal sealed class PlayerBodyPose(Skeleton3D skeleton, bool[] lowerBones)
{
	private readonly record struct Track(int Index, int Bone, Animation.TrackType Type);
	private readonly Dictionary<Animation, (Track[] Lower, Track[] Upper)> _tracks = [];
	public void Sample(Animation animation, double time, bool lower, Vector3[] positions, Quaternion[] rotations, Vector3[] scales)
	{
		if (!_tracks.TryGetValue(animation, out var layers))
		{
			var legs = new List<Track>();
			var torso = new List<Track>();
			for (var i = 0; i < animation.GetTrackCount(); i++)
			{
				if (!animation.TrackIsEnabled(i) || animation.TrackGetKeyCount(i) == 0) continue;
				var type = animation.TrackGetType(i);
				if (type is not (Animation.TrackType.Position3D or Animation.TrackType.Rotation3D or Animation.TrackType.Scale3D)) continue;
				using var path = animation.TrackGetPath(i);
				if (path.GetSubNameCount() != 1) continue;
				var bone = skeleton.FindBone(path.GetSubName(0));
				if (bone >= 0) (lowerBones[bone] ? legs : torso).Add(new(i, bone, type));
			}
			layers = (legs.ToArray(), torso.ToArray());
			_tracks.Add(animation, layers);
		}
		foreach (var track in lower ? layers.Lower : layers.Upper)
		{
			switch (track.Type)
			{
				case Animation.TrackType.Position3D: positions[track.Bone] = animation.PositionTrackInterpolate(track.Index, time); break;
				case Animation.TrackType.Rotation3D: rotations[track.Bone] = animation.RotationTrackInterpolate(track.Index, time).Normalized(); break;
				case Animation.TrackType.Scale3D: scales[track.Bone] = animation.ScaleTrackInterpolate(track.Index, time); break;
			}
		}
	}
}
