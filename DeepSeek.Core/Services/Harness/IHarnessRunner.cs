namespace DeepSeekBrowser.Services.Harness;

public interface IHarnessRunner
{
    Task<HarnessRunResult> RunAsync(
        HarnessRunRequest request,
        HarnessRunCallbacks callbacks,
        CancellationToken ct);
}
