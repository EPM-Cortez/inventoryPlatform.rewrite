namespace Inventory.Infrastructure.Configuration;

public static class ProductionPathValidator
{
    public static string PrepareDurableDirectory(
        string? path,
        string contentRoot,
        string settingName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(
                $"{settingName} must be an absolute local durable-storage path.");
        }

        var fullPath = Path.GetFullPath(path);
        ValidateLocalDurablePath(fullPath, contentRoot, settingName);
        Directory.CreateDirectory(fullPath);

        try
        {
            var probePath = Path.Combine(
                fullPath,
                $".inventory-write-probe-{Guid.NewGuid():N}");
            using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            probe.WriteByte(0);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"{settingName} must be writable by the service account.",
                exception);
        }

        return fullPath;
    }

    public static string PrepareDurableFilePath(
        string path,
        string contentRoot,
        string settingName)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(
                $"{settingName} must be an absolute local durable-storage path.");
        }

        var fullPath = Path.GetFullPath(path);
        ValidateLocalDurablePath(fullPath, contentRoot, settingName);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException(
                $"{settingName} must include a directory.");
        _ = PrepareDurableDirectory(directory, contentRoot, settingName);
        return fullPath;
    }

    public static string RequireDurableExistingFile(
        string? path,
        string contentRoot,
        string settingName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException(
                $"{settingName} must be an absolute local durable-storage file.");
        }

        var fullPath = Path.GetFullPath(path);
        ValidateLocalDurablePath(fullPath, contentRoot, settingName);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"{settingName} does not exist.");
        }

        return fullPath;
    }

    private static void ValidateLocalDurablePath(
        string fullPath,
        string contentRoot,
        string settingName)
    {
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal) ||
            fullPath.StartsWith("//", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{settingName} cannot use UNC or network storage.");
        }

        RejectPathWithin(
            fullPath,
            Path.GetFullPath(contentRoot),
            settingName,
            "the deployed application directory");
        RejectPathWithin(
            fullPath,
            Path.GetFullPath(Path.GetTempPath()),
            settingName,
            "temporary storage");

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                $"{settingName} must have a local filesystem root.");
        }

        try
        {
            var driveType = new DriveInfo(root).DriveType;
            if (driveType is DriveType.Network or
                DriveType.Removable or
                DriveType.CDRom or
                DriveType.Ram)
            {
                throw new InvalidOperationException(
                    $"{settingName} must use fixed local durable storage.");
            }
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"{settingName} has an invalid filesystem root.",
                exception);
        }
    }

    private static void RejectPathWithin(
        string candidate,
        string parent,
        string settingName,
        string description)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(
                Path.GetPathRoot(candidate),
                Path.GetPathRoot(parent),
                comparison))
        {
            return;
        }

        var relative = Path.GetRelativePath(parent, candidate);
        if (relative == "." ||
            (!relative.Equals("..", comparison) &&
             !relative.StartsWith(
                 $"..{Path.DirectorySeparatorChar}",
                 comparison)))
        {
            throw new InvalidOperationException(
                $"{settingName} cannot use {description}.");
        }
    }
}
