using System;
using System.Collections.Generic;

namespace FeralKinshipCompanions;
internal static class RecoveryTextPages
{
    // Caller supplies the engine's scaled text measurement. Long asset codes
    // and unbroken IDs are split across pages too; no diagnostic is discarded.
    internal static List<string> Split(string text, Func<string, double> height, double availableHeight)
    {
        var pages = new List<string>();
        int offset = 0;
        while (offset < text.Length) {
            int remaining = text.Length - offset;
            int low = 1, high = Math.Min(4096, remaining), fit = 0;
            while (low <= high) {
                int mid = low + (high - low) / 2;
                if (height(text.Substring(offset, mid)) <= availableHeight) { fit = mid; low = mid + 1; }
                else high = mid - 1;
            }
            if (fit == 0) fit = 1;
            if (fit < remaining) {
                int boundary = text.LastIndexOfAny(new[] { ' ', '\n' }, offset + fit - 1, fit) - offset;
                if (boundary >= fit / 2) fit = boundary + 1;
            }
            pages.Add(text.Substring(offset, fit)); offset += fit;
        }
        if (pages.Count == 0) pages.Add(string.Empty);
        return pages;
    }
}
