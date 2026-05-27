namespace DeepSeekBrowser.Services.Harness;

public static class HarnessPatchMetrics
{
    private static int _accepted;
    private static int _rejected;

    public static void RecordAccepted() => Interlocked.Increment(ref _accepted);
    public static void RecordRejected() => Interlocked.Increment(ref _rejected);

    public static (int Accepted, int Rejected, double? Rate) Snapshot()
    {
        var a = Volatile.Read(ref _accepted);
        var r = Volatile.Read(ref _rejected);
        var total = a + r;
        double? rate = total > 0 ? (double)a / total : null;
        return (a, r, rate);
    }
}
