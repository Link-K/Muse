namespace Muse.Workspace;

public sealed class OpenDocumentResult
{
	public bool Succeeded { get; }
	public string? Message { get; }
	public WorkspaceTabState? Tab { get; }

	public OpenDocumentResult(bool succeeded, string? message = null, WorkspaceTabState? tab = null)
	{
		Succeeded = succeeded;
		Message = message;
		Tab = tab;
	}

	public static OpenDocumentResult Success(WorkspaceTabState tab) => new OpenDocumentResult(true, null, tab);
	public static OpenDocumentResult Failure(string? message) => new OpenDocumentResult(false, message, null);
}
