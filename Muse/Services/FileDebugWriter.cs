using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Muse.Services
{
	public class FileDebugWriter : IFileDebugWriter
	{
		private const string DefaultFileName = "error-copy.txt";
		private const string MuseSettingsDirectoryName = ".muse";
		private const string SettingsDirectoryName = "settings";
		private const string ConflictLogPreferencesFileName = "conflict-log.json";
		private readonly string? _configuredDebugDirectory;

		public FileDebugWriter(string? debugDirectory = null)
		{
			_configuredDebugDirectory = debugDirectory;
		}

		public async Task<string?> WriteDebugFileAsync(string content)
		{
			try
			{
				var debugDirectory = ResolveDebugDirectory();
				Directory.CreateDirectory(debugDirectory);
				var outPath = Path.Combine(debugDirectory, DefaultFileName);
				var line = $"{DateTimeOffset.Now:O} {content.Replace("\r", "\\r").Replace("\n", "\\n")}\n";
				await File.AppendAllTextAsync(outPath, line).ConfigureAwait(false);
				return outPath;
			}
			catch
			{
				return null;
			}
		}

		private string ResolveDebugDirectory()
		{
			if (!string.IsNullOrWhiteSpace(_configuredDebugDirectory))
			{
				return _configuredDebugDirectory;
			}

			var workspaceRoot = Environment.CurrentDirectory;
			var configuredDirectory = TryReadDebugDirectoryFromPreferences(workspaceRoot);
			if (!string.IsNullOrWhiteSpace(configuredDirectory))
			{
				if (Path.IsPathRooted(configuredDirectory))
				{
					return configuredDirectory;
				}

				return Path.GetFullPath(Path.Combine(workspaceRoot, configuredDirectory));
			}

			return Path.Combine(workspaceRoot, MuseSettingsDirectoryName, "debug");
		}

		private static string? TryReadDebugDirectoryFromPreferences(string workspaceRoot)
		{
			try
			{
				var settingsPath = Path.Combine(workspaceRoot, MuseSettingsDirectoryName, SettingsDirectoryName, ConflictLogPreferencesFileName);
				if (!File.Exists(settingsPath))
				{
					return null;
				}

				using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
				if (document.RootElement.TryGetProperty("DebugExportDirectory", out var debugDirectoryElement))
				{
					var value = debugDirectoryElement.GetString();
					return string.IsNullOrWhiteSpace(value) ? null : value;
				}
			}
			catch
			{
				// Ignore preference parsing issues and fall back to the default path.
			}

			return null;
		}
	}
}
