using System.Text.Json.Serialization;

namespace Bloom.Core.Rng;

/// <summary>The full state of a <see cref="Pcg32"/>; saving it and restoring it continues the same sequence.</summary>
public readonly record struct RngState(ulong State, ulong Increment);

/// <summary>
/// PCG32 (XSH RR) random number generator. All randomness in a run goes through one instance that is
/// saved with the run, so that a reloaded game continues exactly where it stopped. Do not use
/// <see cref="System.Random"/> in game rules: its state cannot be saved.
/// </summary>
[JsonConverter(typeof(Pcg32JsonConverter))]
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;
    private const ulong DefaultStream = 54UL;

    private readonly ulong _increment;
    private ulong _state;

    public Pcg32(ulong seed, ulong stream = DefaultStream)
    {
        _increment = (stream << 1) | 1UL;
        _state = 0;
        NextUInt();
        _state = unchecked(_state + seed);
        NextUInt();
    }

    private Pcg32(RngState state)
    {
        _state = state.State;
        _increment = state.Increment;
    }

    public RngState State => new(_state, _increment);

    public static Pcg32 FromState(RngState state)
    {
        if ((state.Increment & 1UL) == 0)
        {
            throw new ArgumentException("The increment of a PCG32 state must be odd.", nameof(state));
        }

        return new Pcg32(state);
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked((old * Multiplier) + _increment);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
    }

    /// <summary>Returns a uniform integer in [0, <paramref name="maxExclusive"/>), without modulo bias.</summary>
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        uint bound = (uint)maxExclusive;
        uint threshold = unchecked(0u - bound) % bound;
        while (true)
        {
            uint value = NextUInt();
            if (value >= threshold)
            {
                return (int)(value % bound);
            }
        }
    }

    /// <summary>Returns a uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxExclusive, minInclusive);
        return minInclusive + Next(maxExclusive - minInclusive);
    }

    /// <summary>Fisher–Yates shuffle in place.</summary>
    public void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
