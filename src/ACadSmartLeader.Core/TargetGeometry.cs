using System;
using System.Collections.Generic;
using System.Linq;

namespace ACadSmartLeader.Core;

public readonly struct Point3
{
    public Point3(double x, double y, double z = 0) { X = x; Y = y; Z = z; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public double DistanceTo(Point3 b) => Math.Sqrt((X-b.X)*(X-b.X) + (Y-b.Y)*(Y-b.Y) + (Z-b.Z)*(Z-b.Z));
}

public sealed class TargetCandidate
{
    public string Handle { get; }
    public double Distance { get; }
    public int DrawOrder { get; }
    public TargetCandidate(string handle, double distance, int drawOrder) { Handle = handle; Distance = distance; DrawOrder = drawOrder; }
}

public static class TargetGeometry
{
    public static double Tolerance(double textHeight, double? configured = null)
    {
        if (configured.HasValue && (double.IsNaN(configured.Value) || double.IsInfinity(configured.Value) || configured.Value <= 0))
            throw new ArgumentOutOfRangeException(nameof(configured));
        return Math.Max(1e-6, configured ?? Math.Abs(textHeight) * 0.01);
    }

    public static TargetCandidate? Nearest(IEnumerable<TargetCandidate> candidates, double tolerance) => candidates
        .Where(c => !double.IsNaN(c.Distance) && c.Distance >= 0 && c.Distance <= tolerance)
        .OrderBy(c => c.Distance).ThenByDescending(c => c.DrawOrder).FirstOrDefault();

    public static Point3 ClosestOnSegment(Point3 p, Point3 a, Point3 b)
    {
        var x = b.X-a.X; var y = b.Y-a.Y; var z = b.Z-a.Z;
        var length = x*x + y*y + z*z;
        var t = length == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X-a.X)*x + (p.Y-a.Y)*y + (p.Z-a.Z)*z) / length));
        return new Point3(a.X+t*x, a.Y+t*y, a.Z+t*z);
    }

    public static Point3 ClosestOnBox(Point3 p, Point3 min, Point3 max) => new Point3(
        Math.Max(min.X, Math.Min(max.X, p.X)), Math.Max(min.Y, Math.Min(max.Y, p.Y)), Math.Max(min.Z, Math.Min(max.Z, p.Z)));
}
