using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Muse.AI.RAG.VectorIndex
{
	/// <summary>
	/// Simple index builder that persists provided documents to a JSON file.
	/// This serves as a pluggable, testable skeleton for later real vector stores.
	/// </summary>
	public class InMemoryIndexBuilder : IIndexBuilder
	{
		private readonly string _outputPath;

		public InMemoryIndexBuilder(string? outputPath = null)
		{
			_outputPath = string.IsNullOrWhiteSpace(outputPath)
				? Path.Combine(Environment.CurrentDirectory, "rag_index")
				: outputPath!;
		}

		public async Task BuildIndexAsync(string[] documents)
		{
			if (documents == null) throw new ArgumentNullException(nameof(documents));

			Directory.CreateDirectory(_outputPath);
			var file = Path.Combine(_outputPath, "index.json");

			var doc = new { Count = documents.Length, Items = documents };
			var json = JsonSerializer.Serialize(doc);

			await File.WriteAllTextAsync(file, json).ConfigureAwait(false);
		}
	}
}
