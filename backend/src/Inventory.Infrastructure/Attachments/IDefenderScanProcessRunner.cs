namespace Inventory.Infrastructure.Attachments;

public sealed record DefenderScanProcessResult(
    bool Started,
    bool TimedOut,
    int? ExitCode,
    string StandardOutput,
    string StandardError);

public interface IDefenderScanProcessRunner
{
    Task<DefenderScanProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
