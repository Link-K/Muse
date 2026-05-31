using Muse.Editor.Rendering;
using Muse.Rendering;
using Muse.ViewModels;
using Muse.Workspace;
using Xunit;

namespace Muse.Tests;

public sealed class PreviewBlockViewModelTests
{
	[Fact]
	public void TableRow_ShouldKeepEmptyCells()
	{
		var block = new RenderedBlock(RenderedBlockKind.TableRow, "| A |  | C |", "| A |  | C |", 1);
		var vm = new PreviewBlockViewModel(block);

		Assert.True(vm.ShowTableCells);
		Assert.Equal(3, vm.TableCells.Length);
		Assert.Equal("A", vm.TableCells[0]);
		Assert.Equal(string.Empty, vm.TableCells[1]);
		Assert.Equal("C", vm.TableCells[2]);
	}

	[Fact]
	public void TableDivider_ShouldNotRenderCells()
	{
		var block = new RenderedBlock(RenderedBlockKind.TableRow, "| --- | :---: | ---: |", "| --- | :---: | ---: |", 2);
		var vm = new PreviewBlockViewModel(block);

		Assert.True(vm.IsTableDivider);
		Assert.False(vm.ShowTableCells);
	}

	[Fact]
	public void TableRows_ShouldUseAlignedDisplayTextAcrossSameSegment()
	{
		var previewService = new FixedPreviewService(
			new RenderedBlock(RenderedBlockKind.TableRow, "| Name | Role |", "| Name | Role |", 1),
			new RenderedBlock(RenderedBlockKind.TableRow, "| --- | --- |", "| --- | --- |", 2),
			new RenderedBlock(RenderedBlockKind.TableRow, "| A | Developer |", "| A | Developer |", 3),
			new RenderedBlock(RenderedBlockKind.TableRow, "| Bob | QA |", "| Bob | QA |", 4));

		using var vm = new MainViewModel(previewService, new InMemoryWorkspaceService(enableBackgroundAutoSave: false), false);

		Assert.Contains("| Name | Role      |", vm.PreviewBlocks[0].TableDisplayText);
		Assert.Contains("| ---- | --------- |", vm.PreviewBlocks[0].TableDisplayText);
		Assert.Contains("| A    | Developer |", vm.PreviewBlocks[0].TableDisplayText);
		Assert.Contains("| Bob  | QA        |", vm.PreviewBlocks[0].TableDisplayText);
	}

	[Fact]
	public void TableSegment_ShouldRenderAsSingleVisibleBlock()
	{
		var previewService = new FixedPreviewService(
			new RenderedBlock(RenderedBlockKind.TableRow, "| Name | Role |", "| Name | Role |", 1),
			new RenderedBlock(RenderedBlockKind.TableRow, "| --- | --- |", "| --- | --- |", 2),
			new RenderedBlock(RenderedBlockKind.TableRow, "| Bob | QA |", "| Bob | QA |", 3));

		using var vm = new MainViewModel(previewService, new InMemoryWorkspaceService(enableBackgroundAutoSave: false), false);

		Assert.True(vm.PreviewBlocks[0].IsRenderable);
		Assert.False(vm.PreviewBlocks[1].IsRenderable);
		Assert.False(vm.PreviewBlocks[2].IsRenderable);
		Assert.Contains("| Name | Role |", vm.PreviewBlocks[0].TableDisplayText);
		Assert.Contains("| ---- | ---- |", vm.PreviewBlocks[0].TableDisplayText);
		Assert.Contains("| Bob  | QA   |", vm.PreviewBlocks[0].TableDisplayText);
		Assert.True(vm.PreviewBlocks[0].ShowTableGrid);
		Assert.Equal(3, vm.PreviewBlocks[0].TableRows.Length);
		Assert.False(vm.PreviewBlocks[0].TableRows[0].IsDivider);
		Assert.True(vm.PreviewBlocks[0].TableRows[1].IsDivider);
		Assert.False(vm.PreviewBlocks[0].TableRows[2].IsDivider);
	}

	private sealed class FixedPreviewService : IMarkdownPreviewService
	{
		private readonly RenderedBlock[] _blocks;

		public FixedPreviewService(params RenderedBlock[] blocks)
		{
			_blocks = blocks;
		}

		public PreviewViewState Build(string markdown, EditorMode mode, string? theme = null)
		{
			return new PreviewViewState(string.Empty, false, null, _blocks);
		}
	}
}
