using System;
using System.Threading.Tasks;

namespace Muse.Editor.Core
{
    /// <summary>
    /// Service responsible for activating a logical block in a Typora-style editor.
    /// Current implementation validates inputs and is ready to be integrated with
    /// editor session/caret positioning logic.
    /// </summary>
    public class TyporaBlockActivationService
    {
        /// <summary>
        /// Activate the block specified by <paramref name="blockIndex"/> inside the document
        /// identified by <paramref name="documentId"/>. This method performs input validation
        /// and returns a completed task; integration with editor UI/session should be added
        /// later.
        /// </summary>
        public Task ActivateBlockAsync(string documentId, int blockIndex)
        {
            if (string.IsNullOrWhiteSpace(documentId))
            {
                throw new ArgumentException("documentId must be provided", nameof(documentId));
            }

            if (blockIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blockIndex), "blockIndex must be non-negative");
            }

            // TODO: integrate with IEditorSession / view model to position caret and provide focus.
            return Task.CompletedTask;
        }
    }
}
