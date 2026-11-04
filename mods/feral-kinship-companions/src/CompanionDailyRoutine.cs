#nullable enable
using System;

namespace FeralKinshipCompanions;

// Calendar-scaled effort keeps a working day independent of tree/block counts.
// Only continuously observed owner-online time is admitted by the caller.
internal static class CompanionDailyRoutine
{
    internal static double ObservedHours(double previous, double now, double seconds, double speed, double calendarMultiplier)
    {
        if (!double.IsFinite(previous) || !double.IsFinite(now) || previous < 0 || now < previous
            || !double.IsFinite(seconds) || seconds <= 0 || seconds > 5
            || !double.IsFinite(speed) || speed < 0 || !double.IsFinite(calendarMultiplier) || calendarMultiplier < 0) return 0;
        double delta = now - previous;
        double expected = seconds * speed * calendarMultiplier / 3600d;
        return double.IsFinite(expected) && delta <= expected * 1.5 + .0001 ? delta : 0;
    }

    internal static double BurdenChange(double hours, double hoursPerDay, bool working, bool resting, bool indoorBed, bool nourished)
    {
        if (!double.IsFinite(hours) || hours <= 0 || !double.IsFinite(hoursPerDay) || hoursPerDay <= 0) return 0;
        double days = hours / hoursPerDay;
        if (working) return days * 48; // 26 effort for the ordinary 13-hour work window.
        return -days * (resting ? nourished ? indoorBed ? 288 : 96 : 24 : 12);
    }

    internal static bool IsBedtime(double hour, double hoursPerDay, int earlierMorning, int laterBedtime)
    {
        if (!double.IsFinite(hour) || !double.IsFinite(hoursPerDay) || hoursPerDay <= 0) return false;
        double scale = hoursPerDay / 24d;
        return hour < (6.5 - Math.Max(0, earlierMorning)) * scale
            || hour >= (19.5 + Math.Max(0, laterBedtime)) * scale;
    }
}
