using System;
namespace FeralKinshipCompanions;

internal sealed class FollowIdleWaitingWindow
{
    internal const long LongWaitMs = 180000;
    internal const long EasterCooldownMs = 1800000;
    private bool initialized;
    private double x, y, z;
    private int dimension;
    private long safeSince;
    private long lastObserved;
    private bool eggUsed;
    internal void Observe(long now, double px, double py, double pz, int dim, bool safe)
    {
        double dx = px-x, dy = py-y, dz = pz-z;
        if (!initialized || dimension != dim || dx*dx+dy*dy+dz*dz > 0.25*0.25)
        {
            initialized = true; x=px; y=py; z=pz; dimension=dim;
            safeSince=now; eggUsed=false;
        }
        if (!safe || now-lastObserved > 2500 || now < lastObserved) safeSince=now;
        lastObserved=now;
    }
    internal bool CanRoll(long now, long nextEggAt, double roll) => initialized && !eggUsed
        && now-lastObserved <= 2500 && now-safeSince >= LongWaitMs && now >= nextEggAt
        && roll >= 0 && roll < 0.02;
    internal void MarkEmitted() => eggUsed=true;
}
