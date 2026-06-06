namespace Muse.Workspace;

public sealed record WorkspaceMutationResult(
    bool Succeeded,
    string? Code,
    string? Message,
    string? AffectedPath)
{
    public static WorkspaceMutationResult Success(string code, string path)
        => new WorkspaceMutationResult(true, code, null, path);

    public static WorkspaceMutationResult Failure(string code, string message)
        => new WorkspaceMutationResult(false, code, message, null);
}