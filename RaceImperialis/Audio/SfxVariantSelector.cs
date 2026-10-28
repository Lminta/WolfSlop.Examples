namespace RaceImperialis.Audio;

/// <summary>Stable engine timbres per car, randomized transient sounds.</summary>
public sealed class SfxVariantSelector(Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;
    private readonly Dictionary<SfxCue, int> _previous = [];

    public int Select(SfxCue cue, int count, int emitterId = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        // Matching idle/load list indices give each car its own stable pair,
        // including when a voice is recreated after leaving hearing range.
        if (cue is SfxCue.EngineIdle or SfxCue.EngineLoad)
            return (int)(Math.Abs((long)emitterId) % count);
        if (cue is SfxCue.CountdownTick or SfxCue.CountdownGo)
            return 0;
        var hasPrevious = _previous.TryGetValue(cue, out var previous) && previous < count;
        var index = count == 1 ? 0 : _random.Next(count - (hasPrevious ? 1 : 0));
        if (count > 1 && hasPrevious && index >= previous) index++;
        _previous[cue] = index;
        return index;
    }
}
