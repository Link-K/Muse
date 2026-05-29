using Muse.Editor.Core.Commands;
using Muse.Editor.Core.Documents;
using Muse.Editor.Core.Sessions;
using Xunit;

namespace Muse.Editor.Core.Tests;

public sealed class EditorSessionContractTests
{
	[Fact]
	public void SuccessFactory_ShouldCreateSuccessfulResult()
	{
		var result = CommandExecutionResult.Success(3);

		Assert.True(result.Succeeded);
		Assert.Equal(3, result.DocumentVersion);
		Assert.Null(result.ErrorCode);
	}

	[Fact]
	public void Execute_ShouldAdvanceVersionAndEnableUndo()
	{
		var session = new FakeEditorSession(new FakeDocumentModel("doc-1", 1, false));

		var result = session.Execute(new FakeCommand("insert-paragraph"));

		Assert.True(result.Succeeded);
		Assert.Equal(2, result.DocumentVersion);
		Assert.True(session.CanUndo);
		Assert.False(session.CanRedo);
		Assert.True(session.Document.IsDirty);
	}

	[Fact]
	public void UndoAndRedo_ShouldPreserveSessionHistory()
	{
		var session = new FakeEditorSession(new FakeDocumentModel("doc-1", 1, false));
		session.Execute(new FakeCommand("apply-heading"));

		var undoResult = session.Undo();
		var redoResult = session.Redo();

		Assert.Equal(1, undoResult.DocumentVersion);
		Assert.Equal(2, redoResult.DocumentVersion);
		Assert.True(session.CanUndo);
		Assert.False(session.CanRedo);
	}

	private sealed record FakeDocumentModel(string DocumentId, int Version, bool IsDirty) : IDocumentModel;

	private sealed class FakeCommand : ICommand
	{
		public FakeCommand(string name)
		{
			Name = name;
		}

		public string Name { get; }

		public bool CanExecute(IDocumentModel document)
		{
			return !string.IsNullOrWhiteSpace(document.DocumentId);
		}
	}

	private sealed class FakeEditorSession : IEditorSession
	{
		private readonly Stack<FakeDocumentModel> _undoStack = new();
		private readonly Stack<FakeDocumentModel> _redoStack = new();

		public FakeEditorSession(FakeDocumentModel document)
		{
			Document = document;
			SessionId = Guid.NewGuid().ToString("N");
		}

		public string SessionId { get; }

		public IDocumentModel Document { get; private set; }

		public bool CanUndo => _undoStack.Count > 0;

		public bool CanRedo => _redoStack.Count > 0;

		public CommandExecutionResult Execute(ICommand command)
		{
			if (!command.CanExecute(Document))
			{
				return CommandExecutionResult.Failure(Document.Version, "command_blocked", "Command cannot execute for the current document.");
			}

			var current = (FakeDocumentModel)Document;
			_undoStack.Push(current);
			_redoStack.Clear();
			Document = current with { Version = current.Version + 1, IsDirty = true };
			return CommandExecutionResult.Success(Document.Version);
		}

		public CommandExecutionResult Undo()
		{
			if (!CanUndo)
			{
				return CommandExecutionResult.Failure(Document.Version, "undo_unavailable", "No command is available to undo.");
			}

			var current = (FakeDocumentModel)Document;
			_redoStack.Push(current);
			Document = _undoStack.Pop();
			return CommandExecutionResult.Success(Document.Version);
		}

		public CommandExecutionResult Redo()
		{
			if (!CanRedo)
			{
				return CommandExecutionResult.Failure(Document.Version, "redo_unavailable", "No command is available to redo.");
			}

			var current = (FakeDocumentModel)Document;
			_undoStack.Push(current);
			Document = _redoStack.Pop();
			return CommandExecutionResult.Success(Document.Version);
		}
	}
}
