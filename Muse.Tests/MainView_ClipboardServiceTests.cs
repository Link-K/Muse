using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Muse.Services;
using Muse.Views;
using Muse.ViewModels;
using Xunit;

namespace Muse.Tests
{
	[Collection("ClipboardIntegration")]
	public class MainView_ClipboardServiceTests : IDisposable
	{
		private readonly string _originalCwd;
		private readonly string _tempDir;

		public MainView_ClipboardServiceTests()
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

		private class FakeClipboardSuccess : IClipboardService
		{
			public Task<bool> SetTextAsync(string text) => Task.FromResult(true);
		}

		private class FakeClipboardFailure : IClipboardService
		{
			public Task<bool> SetTextAsync(string text) => Task.FromResult(false);
		}

		[Fact]
		public async Task CopyErrorDetails_WithClipboardSuccess_SetsCopiedFeedbackAndWritesFile()
		{
			var vm = new MainViewModel(new Muse.Rendering.MarkdownPreviewService(), new Muse.Workspace.InMemoryWorkspaceService(enableBackgroundAutoSave: false), false);
			vm.ConflictLogPreferenceSaveErrorMessage = "测试剪贴板成功";

			var view = new MainView();
			view.DataContext = vm;
			view.ClipboardService = new FakeClipboardSuccess();

			var fakeWriter = new TestFileWriter();
			view.FileDebugWriter = fakeWriter;

			await view.CopyErrorDetailsAsync();

			Assert.NotNull(fakeWriter.WrittenPath);
			Assert.Equal("测试剪贴板成功", fakeWriter.WrittenContent);

			Assert.False(vm.SaveFeedbackIsError);
			Assert.Contains("已复制到剪贴板", vm.SaveFeedbackMessage);
		}

		[Fact]
		public async Task CopyErrorDetails_WithClipboardFailure_WritesFileAndShowsPath()
		{
			var vm = new MainViewModel(new Muse.Rendering.MarkdownPreviewService(), new Muse.Workspace.InMemoryWorkspaceService(enableBackgroundAutoSave: false), false);
			vm.ConflictLogPreferenceSaveErrorMessage = "测试剪贴板失败";

			var view = new MainView();
			view.DataContext = vm;
			view.ClipboardService = new FakeClipboardFailure();

			var fakeWriter = new TestFileWriter();
			view.FileDebugWriter = fakeWriter;

			await view.CopyErrorDetailsAsync();

			Assert.NotNull(fakeWriter.WrittenPath);
			Assert.Equal("测试剪贴板失败", fakeWriter.WrittenContent);

			Assert.False(vm.SaveFeedbackIsError);
			Assert.Contains("已写入", vm.SaveFeedbackMessage);
			Assert.Contains("error-copy.txt", vm.SaveFeedbackMessage);
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
