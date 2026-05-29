namespace Muse.Editor.Core.Commands;

public sealed record CommandExecutionResult(
	bool Succeeded,
	int DocumentVersion,
	string? ErrorCode = null,
	string? ErrorMessage = null)
{
	public static CommandExecutionResult Success(int documentVersion)
	{
		return new CommandExecutionResult(true, documentVersion);
	}

	public static CommandExecutionResult Failure(int documentVersion, string errorCode, string errorMessage)
	{
		return new CommandExecutionResult(false, documentVersion, errorCode, errorMessage);
	}
}
