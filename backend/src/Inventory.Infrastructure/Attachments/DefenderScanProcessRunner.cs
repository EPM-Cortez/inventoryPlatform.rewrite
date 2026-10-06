using System.Diagnostics;

namespace Inventory.Infrastructure.Attachments;

public sealed class DefenderScanProcessRunner : IDefenderScanProcessRunner
{
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(5);

    public async Task<DefenderScanProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            timeout,
            TimeSpan.Zero);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(executablePath, arguments),
        };
        if (!process.Start())
        {
            return new DefenderScanProcessResult(
                Started: false,
                TimedOut: false,
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: string.Empty);
        }

        var standardOutput =
            process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var standardError =
            process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(waitSource.Token);
        }
        catch (OperationCanceledException)
        {
            await TerminateAndWaitAsync(process);
            await DrainOutputAsync(standardOutput, standardError);
            if (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new DefenderScanProcessResult(
                Started: true,
                TimedOut: true,
                ExitCode: null,
                StandardOutput: string.Empty,
                StandardError: string.Empty);
        }

        return new DefenderScanProcessResult(
            Started: true,
            TimedOut: false,
            ExitCode: process.ExitCode,
            StandardOutput: await standardOutput,
            StandardError: await standardError);
    }

    private static ProcessStartInfo CreateStartInfo(
        string executablePath,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task TerminateAndWaitAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        try
        {
            using var exitSource = new CancellationTokenSource(TerminationTimeout);
            await process.WaitForExitAsync(exitSource.Token);
        }
        catch (InvalidOperationException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task DrainOutputAsync(
        Task<string> standardOutput,
        Task<string> standardError)
    {
        try
        {
            using var drainSource = new CancellationTokenSource(
                TerminationTimeout);
            await Task.WhenAll(standardOutput, standardError)
                .WaitAsync(drainSource.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
