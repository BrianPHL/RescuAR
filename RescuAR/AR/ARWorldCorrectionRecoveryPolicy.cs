namespace RescuAR.AR;

/// <summary>Invalidates walking evidence once per corrected AR world, allowing
/// new evidence to accumulate while placement recovery remains pending.</summary>
public sealed class ARWorldCorrectionRecoveryPolicy
{
    private long session, generation;

    public bool Observe(long sessionGeneration, long correctionGeneration)
    {
        if (sessionGeneration <= 0 || correctionGeneration <= 0 ||
            session == sessionGeneration && correctionGeneration <= generation)
            return false;
        session = sessionGeneration;
        generation = correctionGeneration;
        return true;
    }
}
