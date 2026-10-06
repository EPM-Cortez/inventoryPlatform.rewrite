using Inventory.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Inventory.Infrastructure.Operations;

public sealed class ImportPreviewCleanupService : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LogCleanupFailure =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(1, nameof(LogCleanupFailure)),
            "Import preview cleanup failed; expired payloads remain until the next run.");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportPreviewCleanupService> _logger;

    public ImportPreviewCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ImportPreviewCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    int purged;
                    do
                    {
                        await using var scope = _scopeFactory.CreateAsyncScope();
                        var budget = scope.ServiceProvider
                            .GetRequiredService<EfImportPreviewBudget>();
                        purged = await budget.PurgeExpiredAsync(stoppingToken);
                    }
                    while (purged == 100);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    LogCleanupFailure(_logger, exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }
}
