namespace Muse.Workspace;

public sealed record SaveDocumentResult(
	bool Succeeded,
	string Code,
	string Message,
	WorkspaceTabState? Tab)
{
	public static SaveDocumentResult Success(WorkspaceTabState tab)
	{
		return new SaveDocumentResult(true, "saved", "Document saved.", tab);
	}

	public static SaveDocumentResult Failure(string code, string message)
	{
		return new SaveDocumentResult(false, code, message, null);
	}
}
