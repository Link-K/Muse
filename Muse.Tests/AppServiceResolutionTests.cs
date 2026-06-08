using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Muse.Services;
using Muse.ViewModels;
using Muse.Views;
using Xunit;

namespace Muse.Tests;

[Collection("ClipboardIntegration")]
public sealed class AppServiceResolutionTests : IDisposable
{
	private readonly IServiceProvider? _originalServiceProvider;
	private readonly string _originalCwd;
	private readonly string _tempRoot;

	public AppServiceResolutionTests()
	{
		_originalServiceProvider = GetServiceProvider();
		_originalCwd = Environment.CurrentDirectory;
		_tempRoot = Path.Combine(Path.GetTempPath(), "Muse-AppResolve-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempRoot);
		Environment.CurrentDirectory = _tempRoot;
	}

	public void Dispose()
	{
		SetServiceProvider(_originalServiceProvider);
		Environment.CurrentDirectory = Directory.Exists(_originalCwd) ? _originalCwd : AppContext.BaseDirectory;
		if (Directory.Exists(_tempRoot))
		{
			Directory.Delete(_tempRoot, true);
		}
	}

	[Fact]
	public void Resolve_WhenServiceProviderIsMissing_ShouldThrowClearException()
	{
		SetServiceProvider(null);

		var ex = Assert.Throws<InvalidOperationException>(() => App.Resolve<IClipboardService>());
		Assert.Contains("服务容器尚未初始化", ex.Message);
	}

	[Fact]
	public void Resolve_WhenServiceProviderIsConfigured_ShouldReturnRegisteredService()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IClipboardService, FakeClipboardService>();
		SetServiceProvider(services.BuildServiceProvider());

		var resolved = App.Resolve<IClipboardService>();

		Assert.IsType<FakeClipboardService>(resolved);
	}

	[Fact]
	public void Resolve_WhenWorkspaceServiceIsRegistered_ShouldReturnSharedInstance()
	{
		var services = new ServiceCollection();
		services.AddSingleton<Muse.Workspace.IWorkspaceService, Muse.Workspace.InMemoryWorkspaceService>();
		SetServiceProvider(services.BuildServiceProvider());

		var first = App.Resolve<Muse.Workspace.IWorkspaceService>();
		var second = App.Resolve<Muse.Workspace.IWorkspaceService>();

		Assert.Same(first, second);
	}

	[Fact]
	public async Task MainView_CopyErrorDetails_WhenContainerUnavailable_ShouldFallBackToDefaultWriter()
	{
		SetServiceProvider(null);
		var vm = new MainViewModel(new Muse.Rendering.MarkdownPreviewService(), new Muse.Workspace.InMemoryWorkspaceService(enableBackgroundAutoSave: false), false)
		{
			ConflictLogPreferenceSaveErrorMessage = "fallback-content"
		};

		var view = new MainView
		{
			DataContext = vm,
			ClipboardService = new FailingClipboardService(),
			FileDebugWriter = null
		};

		await view.CopyErrorDetailsAsync();

		var outPath = Path.Combine(_tempRoot, ".muse", "debug", "error-copy.txt");
		Assert.True(File.Exists(outPath));
		Assert.Equal("fallback-content", await File.ReadAllTextAsync(outPath));
		Assert.Contains("error-copy.txt", vm.SaveFeedbackMessage);
		Assert.False(vm.SaveFeedbackIsError);
	}

	private static IServiceProvider? GetServiceProvider()
	{
		var property = typeof(App).GetProperty("ServiceProvider", BindingFlags.Public | BindingFlags.Static);
		return property?.GetValue(null) as IServiceProvider;
	}

	private static void SetServiceProvider(IServiceProvider? serviceProvider)
	{
		var property = typeof(App).GetProperty("ServiceProvider", BindingFlags.Public | BindingFlags.Static);
		var setter = property?.GetSetMethod(nonPublic: true);
		setter?.Invoke(null, new object?[] { serviceProvider });
	}

	private sealed class FakeClipboardService : IClipboardService
	{
		public Task<bool> SetTextAsync(string text)
		{
			return Task.FromResult(true);
		}
	}

	private sealed class FailingClipboardService : IClipboardService
	{
		public Task<bool> SetTextAsync(string text)
		{
			return Task.FromResult(false);
		}
	}
}
