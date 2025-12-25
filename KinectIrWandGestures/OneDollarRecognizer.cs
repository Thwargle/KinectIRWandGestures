using System;
using System.Collections.Generic;
using System.Linq;

namespace KinectIrWandGestures
{
    public sealed class OneDollarRecognizer
    {
        private readonly List<Template> _templates = new List<Template>();

        // Core $1 settings
        private const int NumPoints = 96;
        private const double SquareSize = 250.0;

        // Rotation search (classic $1)
        // Wider range = more tolerant but slightly slower.
        private const double AngleRange = 45.0 * (Math.PI / 180.0);       // ±45 degrees
        private const double AnglePrecision = 2.0 * (Math.PI / 180.0);    // stop search at 2 degrees
        private const double Phi = 0.5 * (-1.0 + 2.23606797749979);       // golden ratio constant

        /// <summary>
        /// Minimum score to accept a match. Default lowered for real-world wand jitter.
        /// If you record clean personal templates, you can raise this (0.65-0.80).
        /// </summary>
        public double MinScoreToAccept { get; set; } = 0.7;

        public int TemplateCount { get { return _templates.Count; } }

        public void ClearTemplates() { _templates.Clear(); }

        public void AddTemplate(string name, IEnumerable<Point2D> points)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Template name required.", nameof(name));

            var pts = points.Select(p => new P(p.X, p.Y)).ToList();
            if (pts.Count < 5) throw new ArgumentException("Too few points.", nameof(points));

            var norm = Normalize(pts);
            _templates.Add(new Template(name.Trim(), norm));
        }

        public RecognizeResult Recognize(IEnumerable<Point2D> points)
        {
            var pts = points.Select(p => new P(p.X, p.Y)).ToList();
            if (pts.Count < 10) return new RecognizeResult(false, "", 0, "Too few points");
            if (_templates.Count == 0) return new RecognizeResult(false, "", 0, "No templates loaded");

            var candidate = Normalize(pts);

            double bestDistance = double.PositiveInfinity;
            string bestName = "";

            for (int i = 0; i < _templates.Count; i++)
            {
                var t = _templates[i];

                // KEY IMPROVEMENT: find best match over a rotation range
                double d = DistanceAtBestAngle(candidate, t.Points, -AngleRange, AngleRange, AnglePrecision);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestName = t.Name;
                }
            }

            double halfDiag = 0.5 * Math.Sqrt(2 * SquareSize * SquareSize);
            double score = Clamp01(1.0 - (bestDistance / halfDiag));

            if (score < MinScoreToAccept)
                return new RecognizeResult(false, bestName, score, "Low confidence (score=" + score.ToString("0.00") + ")");

            return new RecognizeResult(true, bestName, score, "");
        }

        private static List<P> Normalize(List<P> pts)
        {
            pts = Resample(pts, NumPoints);
            pts = RotateToZero(pts);
            pts = ScaleToSquare(pts, SquareSize);
            pts = TranslateToOrigin(pts);
            return pts;
        }

        private static List<P> Resample(List<P> pts, int n)
        {
            double pathLen = PathLength(pts);
            if (pathLen < 1e-6)
            {
                // Degenerate stroke (all points same) -> duplicate
                var outDeg = new List<P>(n);
                for (int i = 0; i < n; i++) outDeg.Add(new P(pts[0].X, pts[0].Y));
                return outDeg;
            }

            double interval = pathLen / (n - 1);
            double D = 0.0;

            var newPts = new List<P>(n);
            newPts.Add(pts[0]);

            int iIdx = 1;
            while (iIdx < pts.Count)
            {
                var prev = pts[iIdx - 1];
                var cur = pts[iIdx];
                double d = Dist(prev, cur);

                if ((D + d) >= interval)
                {
                    double t = (interval - D) / d;
                    var q = new P(prev.X + t * (cur.X - prev.X), prev.Y + t * (cur.Y - prev.Y));
                    newPts.Add(q);

                    // Insert q so next segment continues from q -> cur
                    pts.Insert(iIdx, q);
                    D = 0.0;
                    iIdx++; // move past inserted point
                }
                else
                {
                    D += d;
                    iIdx++;
                }
            }

            while (newPts.Count < n) newPts.Add(new P(pts[pts.Count - 1].X, pts[pts.Count - 1].Y));
            return newPts;
        }

        private static List<P> RotateToZero(List<P> pts)
        {
            var c = Centroid(pts);
            double theta = Math.Atan2(c.Y - pts[0].Y, c.X - pts[0].X);
            return RotateBy(pts, -theta);
        }

        private static List<P> RotateBy(List<P> pts, double theta)
        {
            var c = Centroid(pts);
            double cos = Math.Cos(theta);
            double sin = Math.Sin(theta);

            var outPts = new List<P>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                double dx = p.X - c.X;
                double dy = p.Y - c.Y;
                outPts.Add(new P(dx * cos - dy * sin + c.X, dx * sin + dy * cos + c.Y));
            }
            return outPts;
        }

        private static List<P> ScaleToSquare(List<P> pts, double size)
        {
            var box = BoundingBox(pts);

            // Uniform scale by max dimension (rotation invariant-ish)
            double scale = Math.Max(box.W, box.H);
            if (scale < 1e-6) scale = 1.0;

            var outPts = new List<P>(pts.Count);
            double s = size / scale;

            // Scale around origin, translation handled later
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                outPts.Add(new P(p.X * s, p.Y * s));
            }
            return outPts;
        }

        private static List<P> TranslateToOrigin(List<P> pts)
        {
            var c = Centroid(pts);
            var outPts = new List<P>(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                outPts.Add(new P(p.X - c.X, p.Y - c.Y));
            }
            return outPts;
        }

        // --- KEY: rotation-tolerant distance ---
        private static double DistanceAtBestAngle(List<P> pts, List<P> template, double a, double b, double threshold)
        {
            // Golden Section Search for minimum distance
            double x1 = Phi * a + (1 - Phi) * b;
            double f1 = DistanceAtAngle(pts, template, x1);

            double x2 = (1 - Phi) * a + Phi * b;
            double f2 = DistanceAtAngle(pts, template, x2);

            while (Math.Abs(b - a) > threshold)
            {
                if (f1 < f2)
                {
                    b = x2;
                    x2 = x1;
                    f2 = f1;

                    x1 = Phi * a + (1 - Phi) * b;
                    f1 = DistanceAtAngle(pts, template, x1);
                }
                else
                {
                    a = x1;
                    x1 = x2;
                    f1 = f2;

                    x2 = (1 - Phi) * a + Phi * b;
                    f2 = DistanceAtAngle(pts, template, x2);
                }
            }

            return Math.Min(f1, f2);
        }

        private static double DistanceAtAngle(List<P> pts, List<P> template, double theta)
        {
            var rotated = RotateBy(pts, theta);
            return PathDistance(rotated, template);
        }

        private static double PathDistance(List<P> a, List<P> b)
        {
            double sum = 0;
            int n = Math.Min(a.Count, b.Count);
            for (int i = 0; i < n; i++) sum += Dist(a[i], b[i]);
            return sum / n;
        }

        private static double PathLength(List<P> pts)
        {
            double sum = 0;
            for (int i = 1; i < pts.Count; i++) sum += Dist(pts[i - 1], pts[i]);
            return sum;
        }

        private static Box BoundingBox(List<P> pts)
        {
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            return new Box(minX, minY, maxX - minX, maxY - minY);
        }

        private static P Centroid(List<P> pts)
        {
            double x = 0, y = 0;
            for (int i = 0; i < pts.Count; i++) { x += pts[i].X; y += pts[i].Y; }
            return new P(x / pts.Count, y / pts.Count);
        }

        private static double Dist(P a, P b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double Clamp01(double v) { if (v < 0) return 0; if (v > 1) return 1; return v; }

        private sealed class Template
        {
            public string Name { get; }
            public List<P> Points { get; }
            public Template(string name, List<P> points) { Name = name; Points = points; }
        }

        private sealed class P { public double X; public double Y; public P(double x, double y) { X = x; Y = y; } }
        private sealed class Box { public double MinX, MinY, W, H; public Box(double minX, double minY, double w, double h) { MinX = minX; MinY = minY; W = w; H = h; } }
    }
}
