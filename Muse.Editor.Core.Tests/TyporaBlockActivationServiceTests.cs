using System;
using System.Threading.Tasks;
using Xunit;

namespace Muse.Editor.Core.Tests;

public sealed class TyporaBlockActivationServiceTests
{
	[Fact]
	public async Task ActivateBlockAsync_Completes_ForValidInput()
	{
		var svc = new Muse.Editor.Core.TyporaBlockActivationService();

		await svc.ActivateBlockAsync("doc-1", 0);

		// If no exception is thrown the call is considered successful for this skeleton.
		Assert.True(true);
	}

	[Fact]
	public async Task ActivateBlockAsync_Throws_ForInvalidDocumentId()
	{
		var svc = new Muse.Editor.Core.TyporaBlockActivationService();

		await Assert.ThrowsAsync<ArgumentException>(() => svc.ActivateBlockAsync("", 0));
	}

	[Fact]
	public async Task ActivateBlockAsync_Throws_ForNegativeIndex()
	{
		var svc = new Muse.Editor.Core.TyporaBlockActivationService();

		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => svc.ActivateBlockAsync("doc-1", -1));
	}
}
