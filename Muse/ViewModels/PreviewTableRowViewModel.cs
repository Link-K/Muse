using System.Linq;

namespace Muse.ViewModels;

public sealed class PreviewTableRowViewModel
{
	public PreviewTableRowViewModel(string[] cells, bool isDivider, bool isHeader)
	{
		Cells = cells;
		IsDivider = isDivider;
		IsHeader = isHeader;
		CellViewModels = Cells.Select(c => new PreviewTableCellViewModel(c, IsHeader)).ToArray();
	}

	public string[] Cells { get; }

	public bool IsDivider { get; }

	public bool IsHeader { get; }

	public bool ShowCells => !IsDivider;

	public PreviewTableCellViewModel[] CellViewModels { get; }
}
