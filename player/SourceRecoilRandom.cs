using System;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Contrapoint.Players;

// Source MD5_PseudoRandom -> SeedFileLineHash("KickBack") ->
// CUniformRandomStream::RandomInt. References in nillerusr/source-engine:
// tier1/checksum_md5.cpp, game/shared/util_shared.cpp, vstdlib/random.cpp.
// Local physics ticks stand in for Source's user-command numbers.
internal static class SourceRecoilRandom
{
	// Same shuffled Park-Miller stream as CSSO's weapon recoil table generator.
	internal sealed class FloatStream(int seed)
	{
		private int _state = seed > 0 ? -seed : seed;
		private int _previous;
		private readonly int[] _shuffle = new int[32];
		public float Range(float low, float high)
		{
			var value = Math.Min((float)(Next(ref _state, ref _previous, _shuffle) / 2147483647.0), 1f - 1.2e-7f);
			return low + (high - low) * value;
		}
	}

	public static int KickBack(int commandNumber, int maximum)
	{
		Span<byte> command = stackalloc byte[4];
		BinaryPrimitives.WriteInt32LittleEndian(command, commandNumber);
		Span<byte> digest = stackalloc byte[16];
		MD5.HashData(command, digest);
		var predictionSeed = BinaryPrimitives.ReadUInt32LittleEndian(digest[6..]) & 0x7fffffff;
		Span<byte> seedFile = stackalloc byte[16];
		seedFile.Clear(); // additionalSeed = 0
		BinaryPrimitives.WriteUInt32LittleEndian(seedFile, predictionSeed);
		"KickBack"u8.CopyTo(seedFile[8..]);
		uint crc = 0xffffffff;
		foreach (var value in seedFile)
		{
			crc ^= value;
			for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
		}
		var seed = unchecked((int)~crc);
		var state = seed < 0 ? seed : -seed;
		var previous = 0;
		Span<int> shuffle = stackalloc int[32];
		var range = (uint)(maximum + 1);
		var maxAcceptable = 0x7fffffffu - (0x80000000u % range);
		uint sample;
		do { sample = (uint)Next(ref state, ref previous, shuffle); } while (sample > maxAcceptable);
		return (int)(sample % range);
	}

	private static int Step(int state)
	{
		var quotient = state / 127773;
		state = 16807 * (state - quotient * 127773) - 2836 * quotient;
		return state < 0 ? state + 2147483647 : state;
	}

	private static int Next(ref int state, ref int previous, Span<int> shuffle)
	{
		if (state <= 0 || previous == 0)
		{
			state = unchecked(-state) < 1 ? 1 : unchecked(-state);
			for (var i = 39; i >= 0; i--)
			{
				state = Step(state);
				if (i < 32) shuffle[i] = state;
			}
			previous = shuffle[0];
		}
		state = Step(state);
		var index = previous / 67108864;
		previous = shuffle[index];
		shuffle[index] = state;
		return previous;
	}
}
