using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Muse.Rendering;

namespace Muse.ViewModels;

public partial class MainViewModel : ViewModelBase
{
	private readonly IMarkdownPreviewService _previewService;

	public MainViewModel()
		: this(new MarkdownPreviewService())
	{
	}

	internal MainViewModel(IMarkdownPreviewService previewService)
	{
		_previewService = previewService;
		RefreshPreview();
	}

	[ObservableProperty]
	private EditorMode _currentMode = EditorMode.Edit;

	[ObservableProperty]
	private SplitOrientation _currentSplitOrientation = SplitOrientation.Horizontal;

	[ObservableProperty]
	private string _markdownDraft = "# Muse\n\n在这里开始写 Markdown。";

	[ObservableProperty]
	private int _splitSourceCaretIndex;

	[ObservableProperty]
	private string _previewText = "预览区（占位）：当前无内容";

	[ObservableProperty]
	private string? _previewDiagnostic;

	public string HeaderText => CurrentMode switch
	{
		EditorMode.Edit => "编辑模式（默认）",
		EditorMode.Split => "分屏模式",
		_ => "阅读模式"
	};

	public bool IsEditMode => CurrentMode == EditorMode.Edit;

	public bool IsSplitMode => CurrentMode == EditorMode.Split;

	public bool IsReadMode => CurrentMode == EditorMode.Read;

	public bool IsSplitHorizontal => CurrentSplitOrientation == SplitOrientation.Horizontal;

	public bool IsSplitVertical => CurrentSplitOrientation == SplitOrientation.Vertical;

	public string SplitOrientationText => CurrentSplitOrientation == SplitOrientation.Horizontal
		? "当前分屏：左右"
		: "当前分屏：上下";

	public string PreviewPlaceholder => PreviewText;

	public bool HasPreviewDiagnostic => !string.IsNullOrWhiteSpace(PreviewDiagnostic);

	[RelayCommand]
	private void SwitchToEditMode()
	{
		CurrentMode = EditorMode.Edit;
	}

	[RelayCommand]
	private void SwitchToSplitMode()
	{
		CurrentMode = EditorMode.Split;
	}

	[RelayCommand]
	private void SwitchToReadMode()
	{
		CurrentMode = EditorMode.Read;
	}

	[RelayCommand]
	private void ToggleSplitOrientation()
	{
		CurrentSplitOrientation = CurrentSplitOrientation == SplitOrientation.Horizontal
			? SplitOrientation.Vertical
			: SplitOrientation.Horizontal;
	}

	partial void OnCurrentModeChanged(EditorMode value)
	{
		OnPropertyChanged(nameof(HeaderText));
		OnPropertyChanged(nameof(IsEditMode));
		OnPropertyChanged(nameof(IsSplitMode));
		OnPropertyChanged(nameof(IsReadMode));
		RefreshPreview();
	}

	partial void OnCurrentSplitOrientationChanged(SplitOrientation value)
	{
		OnPropertyChanged(nameof(IsSplitHorizontal));
		OnPropertyChanged(nameof(IsSplitVertical));
		OnPropertyChanged(nameof(SplitOrientationText));
	}

	partial void OnMarkdownDraftChanged(string value)
	{
		RefreshPreview();
	}

	partial void OnPreviewTextChanged(string value)
	{
		OnPropertyChanged(nameof(PreviewPlaceholder));
	}

	partial void OnPreviewDiagnosticChanged(string? value)
	{
		OnPropertyChanged(nameof(HasPreviewDiagnostic));
	}

	private void RefreshPreview()
	{
		var viewState = _previewService.Build(MarkdownDraft, CurrentMode, "light");
		PreviewText = viewState.PreviewText;
		PreviewDiagnostic = viewState.DiagnosticMessage;
	}
}

public enum EditorMode
{
	Edit,
	Split,
	Read
}

public enum SplitOrientation
{
	Horizontal,
	Vertical
}
