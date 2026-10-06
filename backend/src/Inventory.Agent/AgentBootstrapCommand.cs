using Inventory.Agent.Services;

namespace Inventory.Agent;

internal static class AgentBootstrapCommand
{
    private const string StageTokenCommand = "--stage-enrollment-token";
    private const string DeleteTokenCommand = "--delete-enrollment-token";
    private const string DataDirectoryOption = "--data-directory";

    public static bool IsRequested(IReadOnlyList<string> args) =>
        args.Count > 0 &&
        (string.Equals(
             args[0],
             StageTokenCommand,
             StringComparison.Ordinal) ||
         string.Equals(
             args[0],
             DeleteTokenCommand,
             StringComparison.Ordinal));

    public static async Task ExecuteAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        if (args.Count == 4 &&
            string.Equals(
                args[0],
                StageTokenCommand,
                StringComparison.Ordinal) &&
            string.Equals(
                args[2],
                DataDirectoryOption,
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(args[1]) &&
            !string.IsNullOrWhiteSpace(args[3]))
        {
            await EnrollmentTokenStore.StageProtectedAsync(
                args[1],
                args[3],
                cancellationToken);
            return;
        }

        if (args.Count == 3 &&
            string.Equals(
                args[0],
                DeleteTokenCommand,
                StringComparison.Ordinal) &&
            string.Equals(
                args[1],
                DataDirectoryOption,
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(args[2]))
        {
            await EnrollmentTokenStore.DeleteProtectedAsync(
                args[2],
                cancellationToken);
            return;
        }

        throw new ArgumentException(
            "The enrollment-token bootstrap command is invalid.",
            nameof(args));
    }
}
