namespace Muse.ViewModels;

public sealed class PreviewTableCellViewModel
{
	public PreviewTableCellViewModel(string value, bool isHeader)
	{
		Value = value;
		CellBackground = isHeader ? "#26FFFFFF" : "#14FFFFFF";
		CellFontWeight = isHeader ? "Bold" : "Normal";
	}

	public string Value { get; }

	public string CellBackground { get; }

	public string CellFontWeight { get; }
}
