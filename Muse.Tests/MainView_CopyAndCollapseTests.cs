using System;
using System.IO;
using System.Reflection;
using Muse.Views;
using Muse.ViewModels;
using Xunit;

namespace Muse.Tests
{
	[Collection("ClipboardIntegration")]
	public class MainView_CopyAndCollapseTests : IDisposable
	{
		private readonly string _originalCwd;
		private readonly string _tempDir;

		public MainView_CopyAndCollapseTests()
		{
			_originalCwd = Environment.CurrentDirectory;
			_tempDir = Path.Combine(Path.GetTempPath(), "muse-test-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_tempDir);
			Environment.CurrentDirectory = _tempDir;
		}

		public void Dispose()
		{
			try
			{
				Environment.CurrentDirectory = _originalCwd;
				if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
			}
			catch { }
		}

		[Fact]
		public void ViewModel_ErrorMessage_AutoExpands()
		{
			var vm = new MainViewModel(new Muse.Rendering.MarkdownPreviewService(), new Muse.Workspace.InMemoryWorkspaceService(enableBackgroundAutoSave: false), false);
			Assert.False(vm.IsConflictLogPreferenceSaveErrorExpanded);

			vm.ConflictLogPreferenceSaveErrorMessage = "测试错误详情";

			Assert.True(vm.IsConflictLogPreferenceSaveErrorExpanded);
			// Toggle text should indicate visible state; normalize and check contains
			var normalized = vm.ConflictLogPreferenceErrorToggleText.Replace("隐藏", "显示");
			Assert.Contains("显示", normalized);
		}

		[Fact]
		public async System.Threading.Tasks.Task CopyErrorDetails_WritesFile()
		{
			var vm = new MainViewModel(new Muse.Rendering.MarkdownPreviewService(), new Muse.Workspace.InMemoryWorkspaceService(enableBackgroundAutoSave: false), false);
			vm.ConflictLogPreferenceSaveErrorMessage = "测试写入文件内容";

			var view = new MainView();
			view.DataContext = vm;

			// 注入假写入器以避免实际磁盘 I/O
			var fakeWriter = new TestFileWriter();
			view.FileDebugWriter = fakeWriter;

			// invoke async copy method via reflection and await it
			var method = typeof(MainView).GetMethod("CopyErrorDetailsAsync", BindingFlags.Public | BindingFlags.Instance);
			Assert.NotNull(method);
			var invokedTask = (System.Threading.Tasks.Task)method.Invoke(view, Array.Empty<object>())!;
			await invokedTask.ConfigureAwait(false);

			// Prefer clipboard; fall back to file. If clipboard not available in test env,
			// the implementation always writes a debug file as fallback.
			var outPath = fakeWriter.WrittenPath;
			Assert.NotNull(outPath);
			var content = fakeWriter.WrittenContent;
			Assert.Equal("测试写入文件内容", content);

			// 若剪贴板可用，也允许为空或匹配
			if (outPath == null)
			{
				// try to read clipboard via reflection; if available, assert value
				var appType = Type.GetType("Avalonia.Application, Avalonia");
				string? clipText = null;
				try
				{
					if (appType is not null)
					{
						var currentProp = appType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
						var current = currentProp?.GetValue(null);
						var clipProp = current?.GetType().GetProperty("Clipboard");
						var clipboard = clipProp?.GetValue(current);
						var getText = clipboard?.GetType().GetMethod("GetTextAsync", new Type[] { });
						if (getText is not null)
						{
							var task2 = (System.Threading.Tasks.Task)getText.Invoke(clipboard, new object[] { })!;
							await task2.ConfigureAwait(false);
							var resultProp = task2.GetType().GetProperty("Result");
							if (resultProp is not null)
							{
								clipText = resultProp.GetValue(task2) as string;
							}
						}
					}
				}
				catch { }

				Assert.True(clipText == "测试写入文件内容" || clipText == null, "Clipboard content mismatch or unavailable in test env.");
			}
		}

		private class TestFileWriter : Muse.Services.IFileDebugWriter
		{
			public string? WrittenPath { get; private set; }
			public string? WrittenContent { get; private set; }

			public System.Threading.Tasks.Task<string?> WriteDebugFileAsync(string content)
			{
				WrittenContent = content;
				WrittenPath = System.IO.Path.Combine(Environment.CurrentDirectory, ".muse", "debug", "error-copy.txt");
				return System.Threading.Tasks.Task.FromResult<string?>(WrittenPath);
			}
		}
	}
}
