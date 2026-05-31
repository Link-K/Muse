using System;
using System.IO;
using System.Threading.Tasks;

namespace Muse.Services
{
	public class FileDebugWriter : IFileDebugWriter
	{
		private const string DefaultFileName = "error-copy.txt";
		private readonly string _debugDirectory;

		public FileDebugWriter(string? debugDirectory = null)
		{
			if (string.IsNullOrWhiteSpace(debugDirectory))
			{
				_debugDirectory = Path.Combine(Environment.CurrentDirectory, ".muse", "debug");
			}
			else
			{
				_debugDirectory = debugDirectory;
			}
		}

		public async Task<string?> WriteDebugFileAsync(string content)
		{
			try
			{
				Directory.CreateDirectory(_debugDirectory);
				var outPath = Path.Combine(_debugDirectory, DefaultFileName);
				await File.WriteAllTextAsync(outPath, content).ConfigureAwait(false);
				return outPath;
			}
			catch
			{
				return null;
			}
		}
	}
}
