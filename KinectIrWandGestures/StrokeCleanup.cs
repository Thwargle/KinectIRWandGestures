using System;
using System.Collections.Generic;
using System.Linq;

namespace KinectIrWandGestures
{
    public static class StrokeCleanup
    {
        public static List<Point2D> CleanStroke(List<Point2D> raw, double jumpPx = 60, int gapMs = 120, double minSegmentLenPx = 120)
        {
            if (raw == null || raw.Count < 8) return raw ?? new List<Point2D>();

            var segments = SplitIntoSegments(raw, jumpPx, gapMs);

            var kept = segments
                .Select(seg => new { seg, len = PathLength(seg) })
                .Where(x => x.len >= minSegmentLenPx && x.seg.Count >= 10)
                .OrderByDescending(x => x.len)
                .ToList();

            if (kept.Count == 0)
            {
                var fallback = segments
                    .Select(seg => new { seg, len = PathLength(seg) })
                    .OrderByDescending(x => x.len)
                    .First().seg;
                return fallback;
            }

            return kept.First().seg;
        }

        private static List<List<Point2D>> SplitIntoSegments(List<Point2D> pts, double jumpPx, int gapMs)
        {
            var segments = new List<List<Point2D>>();
            var current = new List<Point2D> { pts[0] };

            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];

                double dist = Dist(a, b);
                double dtMs = (b.T - a.T).TotalMilliseconds;

                if (dist > jumpPx || dtMs > gapMs)
                {
                    if (current.Count > 0) segments.Add(current);
                    current = new List<Point2D>();
                }
                current.Add(b);
            }

            if (current.Count > 0) segments.Add(current);
            return segments;
        }

        private static double PathLength(List<Point2D> pts)
        {
            double sum = 0;
            for (int i = 1; i < pts.Count; i++) sum += Dist(pts[i - 1], pts[i]);
            return sum;
        }

        private static double Dist(Point2D a, Point2D b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
