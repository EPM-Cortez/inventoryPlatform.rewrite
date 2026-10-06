using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Inventory.Contracts.V1;
using Microsoft.Extensions.Options;

namespace Inventory.Agent.Services;

public sealed record AgentApiResult<T>(
    bool Success,
    bool Retryable,
    T? Value,
    string? ProblemType,
    string? Detail,
    TimeSpan? RetryAfter);

public interface IAgentApiClient
{
    Task<AgentApiResult<AgentEnrollmentResponse>> EnrollAsync(
        AgentEnrollmentRequest request,
        CancellationToken cancellationToken);

    Task<AgentApiResult<AgentCheckInResponse>> CheckInAsync(
        AgentCheckInRequest request,
        CancellationToken cancellationToken);
}

public sealed class AgentApiClient : IAgentApiClient
{
    private const int MaximumResponseBytes = 65_536;

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<AgentOptions> _options;

    public AgentApiClient(
        HttpClient httpClient,
        IOptionsMonitor<AgentOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Inventory.Agent/1.0");
    }

    public Task<AgentApiResult<AgentEnrollmentResponse>> EnrollAsync(
        AgentEnrollmentRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<AgentEnrollmentRequest, AgentEnrollmentResponse>(
            AgentProtocol.EnrollmentPath,
            request,
            cancellationToken);

    public Task<AgentApiResult<AgentCheckInResponse>> CheckInAsync(
        AgentCheckInRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<AgentCheckInRequest, AgentCheckInResponse>(
            AgentProtocol.CheckInPath,
            request,
            cancellationToken);

    private async Task<AgentApiResult<TResponse>>
        SendAsync<TRequest, TResponse>(
            string path,
            TRequest request,
            CancellationToken cancellationToken)
    {
        using var requestMessage = new HttpRequestMessage(
            HttpMethod.Post,
            BuildUri(path))
        {
            Content = JsonContent.Create(
                request,
                options: AgentProtocol.SnapshotJsonOptions),
        };
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeoutSource.CancelAfter(
            TimeSpan.FromSeconds(
                _options.CurrentValue.HttpTimeoutSeconds));
        try
        {
            using var response = await _httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);
            var body = await ReadBoundedBodyAsync(
                response,
                timeoutSource.Token);
            if (response.IsSuccessStatusCode)
            {
                var value = JsonSerializer.Deserialize<TResponse>(
                    body,
                    AgentProtocol.SnapshotJsonOptions)
                    ?? throw new InvalidDataException(
                        "The inventory server returned an empty response.");
                return new AgentApiResult<TResponse>(
                    true,
                    false,
                    value,
                    null,
                    null,
                    null);
            }

            var (type, detail) = ReadProblem(body);
            return new AgentApiResult<TResponse>(
                false,
                IsRetryable(response.StatusCode),
                default,
                type,
                detail,
                ReadRetryAfter(response));
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return new AgentApiResult<TResponse>(
                false,
                true,
                default,
                "agent_http_timeout",
                "The inventory server request timed out.",
                null);
        }
        catch (HttpRequestException exception)
        {
            return new AgentApiResult<TResponse>(
                false,
                true,
                default,
                "agent_http_unavailable",
                Limit(exception.Message, 500),
                null);
        }
        catch (JsonException exception)
        {
            return new AgentApiResult<TResponse>(
                false,
                false,
                default,
                "agent_invalid_server_response",
                Limit(exception.Message, 500),
                null);
        }
        catch (InvalidDataException exception)
        {
            return new AgentApiResult<TResponse>(
                false,
                false,
                default,
                "agent_invalid_server_response",
                Limit(exception.Message, 500),
                null);
        }
    }

    private Uri BuildUri(string path)
    {
        var origin = new Uri(
            _options.CurrentValue.ServerBaseUrl,
            UriKind.Absolute);
        return new Uri(origin, path);
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static async Task<byte[]> ReadBoundedBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength >
            MaximumResponseBytes)
        {
            throw new InvalidDataException(
                "The inventory server response exceeds its size limit.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(
                chunk,
                cancellationToken);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaximumResponseBytes)
            {
                throw new InvalidDataException(
                    "The inventory server response exceeds its size limit.");
            }

            await buffer.WriteAsync(
                chunk.AsMemory(0, read),
                cancellationToken);
        }
    }

    private static (string? Type, string? Detail) ReadProblem(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return (
                root.TryGetProperty("type", out var type)
                    ? Limit(type.GetString(), 500)
                    : null,
                root.TryGetProperty("detail", out var detail)
                    ? Limit(detail.GetString(), 500)
                    : "Inventory server returned HTTP error.");
        }
        catch (JsonException)
        {
            return (
                "agent_http_error",
                Limit(Encoding.UTF8.GetString(body), 500));
        }
    }

    private static TimeSpan? ReadRetryAfter(
        HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta > TimeSpan.FromHours(1)
                ? TimeSpan.FromHours(1)
                : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay <= TimeSpan.Zero
                ? TimeSpan.Zero
                : delay > TimeSpan.FromHours(1)
                    ? TimeSpan.FromHours(1)
                    : delay;
        }

        return null;
    }

    private static string? Limit(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maximumLength
                ? value
                : value[..maximumLength];
}
