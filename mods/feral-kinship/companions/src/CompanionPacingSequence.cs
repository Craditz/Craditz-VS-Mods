using System;
namespace FeralKinshipCompanions;

// Arrival at A starts the sequence; it is not itself a pacing leg.
internal sealed class CompanionPacingSequence
{
    private bool approached;
    private int remainingLegs;
    internal CompanionPacingSequence(int roundTrips)
    {
        if (roundTrips != 4 && roundTrips != 6) throw new ArgumentOutOfRangeException(nameof(roundTrips));
        remainingLegs = roundTrips * 2;
    }
    internal int? NextEndpointAfterArrival()
    {
        if (!approached) { approached = true; return 1; }
        if (remainingLegs <= 0 || --remainingLegs == 0) return null;
        return remainingLegs % 2 == 0 ? 1 : 0;
    }
}
