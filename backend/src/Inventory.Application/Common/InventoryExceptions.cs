namespace Inventory.Application.Common;

public abstract class InventoryException : Exception
{
    protected InventoryException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public sealed class ResourceNotFoundException : InventoryException
{
    public ResourceNotFoundException(string resourceName, object identifier)
        : base(
            "not_found",
            $"{resourceName} '{identifier}' was not found.")
    {
    }
}

public sealed class ResourceConflictException : InventoryException
{
    public ResourceConflictException(string code, string message, Exception? innerException = null)
        : base(code, message, innerException)
    {
    }
}

public sealed class PermissionDeniedException : InventoryException
{
    public PermissionDeniedException(string code, string message)
        : base(code, message)
    {
    }
}

public sealed class RequestAuthenticationException : InventoryException
{
    public RequestAuthenticationException(string code, string message)
        : base(code, message)
    {
    }
}

public sealed class RequestRateLimitException : InventoryException
{
    public RequestRateLimitException(
        string code,
        string message,
        int retryAfterSeconds)
        : base(code, message)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retryAfterSeconds, 1);

        RetryAfterSeconds = retryAfterSeconds;
    }

    public int RetryAfterSeconds { get; }
}

public sealed class RequestValidationException : InventoryException
{
    public RequestValidationException(
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? errors = null)
        : base(code, message)
    {
        Errors = errors ?? new Dictionary<string, string[]>(StringComparer.Ordinal);
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class PersistenceConflictException : Exception
{
    public PersistenceConflictException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
