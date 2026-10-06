using System.Text;
using Inventory.Agent.Services;
using Microsoft.Extensions.Options;

namespace Inventory.Agent.Tests;

public sealed class EnrollmentTokenStoreTests
{
    private const string TokenSecret =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task DirectTokenIsProtectedAtRestAndDeletedAfterUse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory();
        try
        {
            var token = CreateToken("11111111111111111111111111111111");

            await EnrollmentTokenStore.StageProtectedAsync(
                token,
                directory,
                cancellationToken);

            var protectedPath = Path.Combine(
                directory,
                EnrollmentTokenStore.ProtectedFileName);
            Assert.True(File.Exists(protectedPath));
            var bytes = await File.ReadAllBytesAsync(
                protectedPath,
                cancellationToken);
            Assert.DoesNotContain(
                token,
                Encoding.UTF8.GetString(bytes),
                StringComparison.Ordinal);

            var store = CreateStore(directory);
            Assert.Equal(
                token,
                await store.ReadAsync(cancellationToken));

            await store.DeleteAsync(cancellationToken);
            Assert.False(File.Exists(protectedPath));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task ExistingTokenFileRemainsSupportedAndIsDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory();
        try
        {
            var token = CreateToken("22222222222222222222222222222222");
            var tokenPath = Path.Combine(directory, "bootstrap.token");
            await File.WriteAllTextAsync(
                tokenPath,
                token,
                cancellationToken);
            var options = new AgentOptions
            {
                ServerBaseUrl = "https://inventory.example.test",
                DataDirectory = directory,
                EnrollmentTokenFilePath = tokenPath,
            };
            var store = new EnrollmentTokenStore(
                new StaticOptionsMonitor<AgentOptions>(options));

            Assert.Equal(
                token,
                await store.ReadAsync(cancellationToken));

            await store.DeleteAsync(cancellationToken);
            Assert.False(File.Exists(tokenPath));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-enrollment-token")]
    [InlineData("inv_enroll.token with spaces")]
    public async Task DirectTokenRejectsInvalidValues(string token)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory();
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(
                () => EnrollmentTokenStore.StageProtectedAsync(
                    token,
                    directory,
                    cancellationToken));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    [Fact]
    public async Task BootstrapCommandStagesTheTokenWithoutStartingTheService()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = CreateTemporaryDirectory();
        try
        {
            var token = CreateToken("33333333333333333333333333333333");
            var args = new[]
            {
                "--stage-enrollment-token",
                token,
                "--data-directory",
                directory,
            };

            Assert.True(AgentBootstrapCommand.IsRequested(args));
            await AgentBootstrapCommand.ExecuteAsync(
                args,
                cancellationToken);

            Assert.Equal(
                token,
                await CreateStore(directory).ReadAsync(
                    cancellationToken));

            var deleteArgs = new[]
            {
                "--delete-enrollment-token",
                "--data-directory",
                directory,
            };
            Assert.True(AgentBootstrapCommand.IsRequested(deleteArgs));
            await AgentBootstrapCommand.ExecuteAsync(
                deleteArgs,
                cancellationToken);
            Assert.False(
                File.Exists(
                    Path.Combine(
                        directory,
                        EnrollmentTokenStore.ProtectedFileName)));
        }
        finally
        {
            DeleteTemporaryDirectory(directory);
        }
    }

    private static EnrollmentTokenStore CreateStore(string directory) =>
        new(
            new StaticOptionsMonitor<AgentOptions>(
                new AgentOptions
                {
                    ServerBaseUrl =
                        "https://inventory.example.test",
                    DataDirectory = directory,
                }));

    private static string CreateToken(string profileId) =>
        $"inv_enroll.{profileId}.{TokenSecret}";

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"inventory-agent-token-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTemporaryDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StaticOptionsMonitor<T>(T value)
        : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(
            Action<T, string?> listener) =>
            null;
    }
}
