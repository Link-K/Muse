using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Muse.Services;
using Xunit;

namespace Muse.Tests;

[Collection("ClipboardIntegration")]
public sealed class FileDebugWriterTests : IDisposable
{
	private readonly string _originalCwd;
	private readonly string _tempRoot;

	public FileDebugWriterTests()
	{
		_originalCwd = Environment.CurrentDirectory;
		_tempRoot = Path.Combine(Path.GetTempPath(), "Muse-FileDebugWriter-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempRoot);
		Environment.CurrentDirectory = _tempRoot;
	}

	public void Dispose()
	{
		Environment.CurrentDirectory = Directory.Exists(_originalCwd) ? _originalCwd : AppContext.BaseDirectory;
		if (Directory.Exists(_tempRoot))
		{
			Directory.Delete(_tempRoot, true);
		}
	}

	[Fact]
	public async Task WriteDebugFileAsync_ShouldUseConfiguredDirectoryFromPreferences()
	{
		var settingsDir = Path.Combine(_tempRoot, ".muse", "settings");
		Directory.CreateDirectory(settingsDir);
		var settingsPath = Path.Combine(settingsDir, "conflict-log.json");
		await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(new
		{
			IsScopeActiveDocument = false,
			EventFilter = "All",
			DebugExportDirectory = "custom-debug"
		}));

		var writer = new FileDebugWriter();
		var outPath = await writer.WriteDebugFileAsync("debug-content");

		Assert.Equal(Path.Combine(_tempRoot, "custom-debug", "error-copy.txt"), outPath);
		Assert.True(File.Exists(outPath));
		Assert.Equal("debug-content", await File.ReadAllTextAsync(outPath));
	}
}
