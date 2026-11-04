using System;
using System.Collections.Generic;

namespace Druidry;

internal sealed class DruidClientState
{
    public HashSet<string> UnlockedForms { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AvailableForms { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string ActiveForm { get; private set; } = string.Empty;
    public bool HasTrueForm { get; private set; }
    private long cooldownEndsAt;
    public int CooldownMilliseconds
    {
        get
        {
            long remaining = cooldownEndsAt - Environment.TickCount64;
            return remaining <= 0 ? 0 : (int)Math.Min(int.MaxValue, remaining);
        }
    }

    public void Update(DruidStatePacket packet)
    {
        UnlockedForms.Clear();
        AvailableForms.Clear();
        foreach (string code in packet.UnlockedForms ?? Array.Empty<string>()) UnlockedForms.Add(code);
        foreach (string code in packet.AvailableForms ?? Array.Empty<string>()) AvailableForms.Add(code);
        ActiveForm = packet.ActiveForm ?? string.Empty;
        HasTrueForm = packet.HasTrueForm;
        cooldownEndsAt = Environment.TickCount64 + Math.Max(0, packet.CooldownMilliseconds);
    }
}
