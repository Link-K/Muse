namespace Muse.Editor.Core.Documents;

public interface IDocumentModel
{
	string DocumentId { get; }

	int Version { get; }

	bool IsDirty { get; }
}
