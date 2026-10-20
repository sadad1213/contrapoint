using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Contrapoint.Weapons;

// Immutable Source sheet metadata. Sample on the CPU using the original frame
// durations; the GPU only fetches the selected rectangles by integer index.
internal sealed class CssoParticleSheet
{
	private static readonly Dictionary<string, CssoParticleSheet> Cache = new();
	internal readonly Vector4[] Rects;
	internal readonly ImageTexture Texture;
	private readonly Dictionary<JsonElement, Sequence> _definitions = new();
	internal sealed class Sequence
	{
		internal readonly float Duration;
		private readonly bool _clamp;
		private readonly float[] _lengths;
		private readonly int _offset;
		internal Sequence(JsonElement definition, int offset)
		{
			Duration = definition.GetProperty("duration").GetSingle();
			_clamp = definition.GetProperty("clamp").GetBoolean();
			_offset = offset;
			var frames = definition.GetProperty("frames");
			_lengths = new float[frames.GetArrayLength()];
			for (var i = 0; i < _lengths.Length; i++) _lengths[i] = frames[i].GetProperty("duration").GetSingle();
		}
		internal (int Current, int Next, float Blend) Sample(float time)
		{
			time = _clamp ? Math.Min(time, Duration - .0001f) : time % Duration;
			for (var i = 0; i < _lengths.Length; i++)
			{
				var length = _lengths[i];
				if (time <= length || i == _lengths.Length - 1)
				{
					var next = i + 1 < _lengths.Length ? i + 1 : _clamp ? i : 0;
					return (_offset + i, _offset + next, Mathf.Clamp(time / Math.Max(length, .0001f), 0, 1));
				}
				time -= length;
			}
			return (_offset, _offset, 0);
		}
	}
	private CssoParticleSheet(JsonElement material)
	{
		var rects = new List<Vector4>();
		foreach (var sequence in material.GetProperty("sequences").EnumerateObject())
		{
			var cached = new Sequence(sequence.Value, rects.Count);
			_definitions.Add(sequence.Value, cached);
			foreach (var frame in sequence.Value.GetProperty("frames").EnumerateArray())
			{
				var uv = frame.GetProperty("uv");
				rects.Add(new(uv[0].GetSingle(), uv[1].GetSingle(), uv[2].GetSingle(), uv[3].GetSingle()));
			}
		}
		Rects = rects.ToArray();
		var data = new float[Rects.Length * 4];
		for (var i = 0; i < Rects.Length; i++)
		{
			data[i * 4] = Rects[i].X; data[i * 4 + 1] = Rects[i].Y;
			data[i * 4 + 2] = Rects[i].Z; data[i * 4 + 3] = Rects[i].W;
		}
		var bytes = new byte[data.Length * sizeof(float)];
		Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
		using var image = Image.CreateFromData(Rects.Length, 1, false, Image.Format.Rgbaf, bytes);
		Texture = ImageTexture.CreateFromImage(image);
	}
	internal static CssoParticleSheet Get(string key, JsonElement material)
	{
		if (!Cache.TryGetValue(key, out var sheet)) Cache.Add(key, sheet = new(material));
		return sheet;
	}
	internal Sequence GetSequence(JsonElement definition) => _definitions[definition];
}

internal sealed class CssoParticleBatch
{
	internal readonly MultiMeshInstance3D Instance;
	internal readonly CssoParticleSheet Sheet;
	internal int Count;
	private float[] _buffer = [];
	private Aabb _bounds;
	internal CssoParticleBatch(Node3D parent, string name, Mesh quad, ShaderMaterial material, CssoParticleSheet? sheet = null)
	{
		Sheet = sheet!;
		Instance = new MultiMeshInstance3D
		{
			Name = name, TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			MaterialOverride = material,
			Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = quad }
		};
		parent.AddChild(Instance);
	}
	internal void Allocate(int capacity)
	{
		Instance.Multimesh.InstanceCount = capacity;
		Instance.Multimesh.VisibleInstanceCount = 0;
		_buffer = new float[capacity * 20];
	}
	internal void Begin() => Count = 0;
	internal int Add(Transform3D transform, Color color, int frame, int next, int second, float blend)
	{
		var slot = Count++;
		var i = slot * 20;
		var basis = transform.Basis;
		var origin = transform.Origin;
		// MultiMesh buffer stores the 3x4 transform by ROW, unlike Basis' columns.
		_buffer[i] = basis.X.X; _buffer[i + 1] = basis.Y.X; _buffer[i + 2] = basis.Z.X; _buffer[i + 3] = origin.X;
		_buffer[i + 4] = basis.X.Y; _buffer[i + 5] = basis.Y.Y; _buffer[i + 6] = basis.Z.Y; _buffer[i + 7] = origin.Y;
		_buffer[i + 8] = basis.X.Z; _buffer[i + 9] = basis.Y.Z; _buffer[i + 10] = basis.Z.Z; _buffer[i + 11] = origin.Z;
		_buffer[i + 12] = color.R; _buffer[i + 13] = color.G; _buffer[i + 14] = color.B; _buffer[i + 15] = color.A;
		_buffer[i + 16] = frame; _buffer[i + 17] = next; _buffer[i + 18] = second; _buffer[i + 19] = blend;
		// Exact world-space quad bounds, including rotation and trail length.
		var extent = (basis.X.Abs() + basis.Y.Abs()) * .5f;
		var bounds = new Aabb(origin - extent, extent * 2).Grow(.001f);
		_bounds = slot == 0 ? bounds : _bounds.Merge(bounds);
		return slot;
	}
	internal void Flush()
	{
		var multi = Instance.Multimesh;
		if (Count > 0)
		{
			multi.CustomAabb = _bounds;
			multi.Buffer = _buffer;
		}
		multi.VisibleInstanceCount = Count;
	}
}
