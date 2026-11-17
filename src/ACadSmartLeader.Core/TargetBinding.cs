using System;
using System.Collections.Generic;
using System.Linq;

namespace ACadSmartLeader.Core;

/// <summary>The binding precedence is independent of the CAD database and can be verified without a host.</summary>
public static class TargetBinding
{
    public static T? Resolve<T>(T? saved, Func<T, bool> arrowIsOn, Func<T?> underArrow,
        Func<IEnumerable<T>> byDeviceId, out bool arrowOffTarget) where T : class
    {
        arrowOffTarget = false;
        if (saved != null && arrowIsOn(saved)) return saved;
        var nearby = underArrow();
        if (nearby != null) return nearby;
        // COPY can duplicate DEVICE_ID. A non-unique id cannot identify the intended target.
        var matches = byDeviceId().Take(2).ToArray();
        if (matches.Length != 1) return null;
        arrowOffTarget = true;
        return matches[0];
    }
}
