namespace KinectIrWandGestures
{
    public sealed class RecognizeResult
    {
        public bool Success { get; }
        public string Name { get; }
        public double Score { get; }
        public string Reason { get; }

        public RecognizeResult(bool success, string name, double score, string reason)
        {
            Success = success;
            Name = name ?? "";
            Score = score;
            Reason = reason ?? "";
        }
    }
}
