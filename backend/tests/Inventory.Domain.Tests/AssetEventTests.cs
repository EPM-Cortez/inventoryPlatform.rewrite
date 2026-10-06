using Inventory.Domain.Assets;
using Inventory.Domain.Common;

namespace Inventory.Domain.Tests;

public sealed class AssetEventTests
{
    [Fact]
    public void EventRequiresAUtcTimestamp()
    {
        var exception = Assert.Throws<DomainRuleException>(
            () => AssetEvent.Create(
                Guid.NewGuid(),
                AssetEventType.AssetCreated,
                new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.FromHours(1)),
                Guid.NewGuid(),
                "Administrator",
                AssetEventSource.Api,
                "Created.",
                "{}"));

        Assert.Equal("utc_required", exception.Code);
    }
}
