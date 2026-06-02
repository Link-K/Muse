using System.Threading.Tasks;
using Muse.Export;
using Xunit;

namespace Muse.Tests;

public sealed class ExportServiceTests
{
	[Fact]
	public async Task ExportMarkdownToHtmlAsync_ConvertsHeadingAndCode()
	{
		var svc = new ExportService();
		var md = "# Title\n```csharp\nConsole.WriteLine(1);\n```";

		var html = await svc.ExportMarkdownToHtmlAsync(md);

		Assert.Contains("<h1", html);
		Assert.Contains("Console.WriteLine(1);", html);
	}

	[Fact]
	public async Task ExportMarkdownToHtmlAsync_Empty_ReturnsHtml()
	{
		var svc = new ExportService();
		var html = await svc.ExportMarkdownToHtmlAsync(null!);
		Assert.NotNull(html);
	}
}
