using System;

namespace KinectIrWandGestures
{
    public sealed class Point2D
    {
        public double X { get; }
        public double Y { get; }
        public DateTime T { get; }

        public Point2D(double x, double y, DateTime t)
        {
            X = x;
            Y = y;
            T = t;
        }
    }
}
