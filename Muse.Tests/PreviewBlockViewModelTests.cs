using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Muse.Editor.Rendering;
using Muse.Services;
using Xunit;

namespace Muse.Tests
{
	public sealed class PreviewBlockViewModelTests : IDisposable
	{
		private readonly IServiceProvider? _originalServiceProvider;

		public PreviewBlockViewModelTests()
		{
			_originalServiceProvider = GetServiceProvider();
		}

		public void Dispose()
		{
			SetServiceProvider(_originalServiceProvider);
		}

		[Fact]
		public async Task CopyCodeCommand_SetsClipboardText()
		{
			var tcs = new TaskCompletionSource<string?>();
			var fake = new RecordingClipboardService(tcs);

			var services = new ServiceCollection();
			services.AddSingleton<IClipboardService>(fake);
			SetServiceProvider(services.BuildServiceProvider());

			var block = new RenderedBlock(RenderedBlockKind.CodeFence, "```", "console.log('hi');", 1);
			var vm = new Muse.ViewModels.PreviewBlockViewModel(block);

			vm.CopyCodeCommand.Execute(null);

			var result = await Task.WhenAny(tcs.Task, Task.Delay(2000));
			Assert.True(tcs.Task.IsCompleted, "Clipboard was not invoked within timeout");
			Assert.Equal("console.log('hi');", tcs.Task.Result);
		}

		[Fact]
		public async Task CopyAnchorCommand_WhenHeading_SetsSlug()
		{
			var tcs = new TaskCompletionSource<string?>();
			var fake = new RecordingClipboardService(tcs);

			var services = new ServiceCollection();
			services.AddSingleton<IClipboardService>(fake);
			SetServiceProvider(services.BuildServiceProvider());

			var block = new RenderedBlock(RenderedBlockKind.Heading, "# Title", "Title Example", 1);
			var vm = new Muse.ViewModels.PreviewBlockViewModel(block);

			vm.CopyAnchorCommand.Execute(null);

			var result = await Task.WhenAny(tcs.Task, Task.Delay(2000));
			Assert.True(tcs.Task.IsCompleted, "Clipboard was not invoked for heading");
			Assert.Equal("title-example", tcs.Task.Result);
		}

		[Fact]
		public async Task CopyAnchorCommand_WhenNotHeading_DoesNotCallClipboard()
		{
			var tcs = new TaskCompletionSource<string?>();
			var fake = new RecordingClipboardService(tcs);

			var services = new ServiceCollection();
			services.AddSingleton<IClipboardService>(fake);
			SetServiceProvider(services.BuildServiceProvider());

			var block = new RenderedBlock(RenderedBlockKind.Paragraph, "para", "Just text", 1);
			var vm = new Muse.ViewModels.PreviewBlockViewModel(block);

			vm.CopyAnchorCommand.Execute(null);

			// wait a short time; the clipboard should not be called
			await Task.Delay(250);
			Assert.False(tcs.Task.IsCompleted, "Clipboard should not be invoked for non-heading");
		}

		private static IServiceProvider? GetServiceProvider()
		{
			var prop = typeof(Muse.App).GetProperty("ServiceProvider", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
			return prop?.GetValue(null) as IServiceProvider;
		}

		private static void SetServiceProvider(IServiceProvider? provider)
		{
			var prop = typeof(Muse.App).GetProperty("ServiceProvider", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
			var setter = prop?.GetSetMethod(nonPublic: true);
			setter?.Invoke(null, new object?[] { provider });
		}

		private sealed class RecordingClipboardService : IClipboardService
		{
			private readonly TaskCompletionSource<string?> _tcs;

			public RecordingClipboardService(TaskCompletionSource<string?> tcs)
			{
				_tcs = tcs;
			}

			public Task<bool> SetTextAsync(string text)
			{
				_tcs.TrySetResult(text);
				return Task.FromResult(true);
			}
		}
	}
}
