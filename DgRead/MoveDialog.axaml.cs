using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DgRead;

public sealed class MoveDialogItem
{
	public int No { get; init; }
	public string Alias { get; init; } = string.Empty;
	public string Folder { get; init; } = string.Empty;
	public bool Enabled { get; init; }
	public string FolderDisplay => Enabled ? Folder : "••••••••";
}

public partial class MoveDialog : Window
{
	private static int sLastSelected = -1;

	private readonly ObservableCollection<MoveDialogItem> _items = [];
	private bool _refreshing;
	private bool _modified;
	private string _fileName = string.Empty;
	private string? _resultPath;
	private Point? _dragStartPoint;
	private MoveDialogItem? _dragStartItem;

	private const string MoveDragPrefix = "dgread-move-folder:";

	public MoveDialog()
	{
		InitializeComponent();
		ApplyLocalizedTexts();
		MoveListBox.ItemsSource = _items;

		MoveListBox.SelectionChanged += OnMoveSelectionChanged;

		AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel, true);
		Opened += OnDialogOpened;
		Closed += OnDialogClosed;
	}

	private void ApplyLocalizedTexts()
	{
		Title = T("Move book");
		GuideTextBlock.Text = T("Select destination for moving book");
		NoHeaderTextBlock.Text = T("No.");
		AliasHeaderTextBlock.Text = T("Alias");
		DirectoryHeaderTextBlock.Text = T("Directory");
		BrowseButton.Content = T("Browse");
		AddLocationButton.Content = T("Add location");
		AliasLabelTextBlock.Text = T("Alias");
		ApplyAliasButton.Content = T("Apply alias");
		ToolTip.SetTip(MoveUpButton, T("Move up"));
		ToolTip.SetTip(MoveDownButton, T("Move down"));
		ToolTip.SetTip(DeleteLocationButton, T("Delete location"));
		OkButton.Content = T("OK");
		CancelButton.Content = T("Cancel");
	}

	private void OnDialogOpened(object? sender, EventArgs e)
	{
		if (_items.Count == 0)
			return;

		if (MoveListBox.SelectedIndex < 0)
			EnsureIndex(Math.Clamp(sLastSelected, 0, _items.Count - 1));

		FocusMoveListLater();
	}

	public async Task<string?> ShowAsync(Window owner, string fileName)
	{
		_fileName = fileName;
		_resultPath = null;
		_modified = false;

		RefreshList();
		var accepted = await ShowDialog<bool>(owner);
		return accepted ? _resultPath : null;
	}

	private void RefreshList()
	{
		_refreshing = true;
		var selectedFolder = MoveListBox.SelectedItem is MoveDialogItem selected ? selected.Folder : null;

		_items.Clear();
		foreach (var move in Configs.Moves)
		{
			_items.Add(new MoveDialogItem
			{
				No = move.No,
				Alias = move.Alias,
				Folder = move.Folder,
				Enabled = move.Enabled,
			});
		}

		if (_items.Count > 0)
		{
			if (string.IsNullOrWhiteSpace(selectedFolder) || !EnsureFolder(selectedFolder))
			{
				var index = Math.Clamp(sLastSelected, 0, _items.Count - 1);
				EnsureIndex(index);
			}
		}
		else
		{
			MoveListBox.SelectedIndex = -1;
			DestTextBox.Text = string.Empty;
			AliasTextBox.Text = string.Empty;
		}

		UpdateSelectionUiState();
		_refreshing = false;
	}

	private bool EnsureFolder(string folder)
	{
		if (string.IsNullOrWhiteSpace(folder))
			return false;

		for (var i = 0; i < _items.Count; i++)
		{
			if (!_items[i].Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))
				continue;

			EnsureIndex(i);
			return true;
		}

		return false;
	}

	private void EnsureIndex(int index)
	{
		if (index < 0 || index >= _items.Count)
			return;

		MoveListBox.SelectedIndex = index;
		MoveListBox.ScrollIntoView(_items[index]);
		MoveListBox.Focus();
		DestTextBox.Text = _items[index].FolderDisplay;
		AliasTextBox.Text = _items[index].Alias;
		UpdateSelectionUiState();
		if (!_refreshing)
			sLastSelected = index;
	}

	private void OnMoveSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (!TryGetSelected(out var index, out var selected))
			return;

		DestTextBox.Text = selected.Folder;
		if (!selected.Enabled)
			DestTextBox.Text = selected.FolderDisplay;
		AliasTextBox.Text = selected.Alias;
		UpdateSelectionUiState();
		if (!_refreshing)
			sLastSelected = index;
	}

	private async void OnBrowseClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (!StorageProvider.CanOpen)
				return;

			var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
			{
				AllowMultiple = false,
				Title = T("Select directory")
			});

			if (folders.Count == 0)
				return;

			var folder = folders[0].TryGetLocalPath();
			if (string.IsNullOrWhiteSpace(folder))
				return;

			DestTextBox.Text = folder;
		}
		catch (Exception ex)
		{
			await SuppUi.OkAsync($"{T("Failed to open folder")}{Environment.NewLine}{ex.Message}", T("Error"));
		}
	}

	private async void OnAddLocationClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			var folder = DestTextBox.Text?.Trim() ?? string.Empty;
			if (string.IsNullOrWhiteSpace(folder))
				return;

			if (!Directory.Exists(folder))
			{
				await SuppUi.OkAsync($"{T("The specified directory does not exist")}{Environment.NewLine}{folder}", T("Error"));
				return;
			}

			if (EnsureFolder(folder))
				return;

			var alias = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
			if (string.IsNullOrWhiteSpace(alias))
				alias = folder;

			var added = Configs.AddMove(folder, alias);
			switch (added)
			{
				case 1:
					await SuppUi.OkAsync(T("Failed to add the location"), T("Error"));
					return;
				case 2:
					EnsureFolder(folder);
					return;
			}

			_modified = true;
			RefreshList();
			EnsureFolder(folder);
		}
		catch (Exception ex)
		{
			await SuppUi.OkAsync($"{T("Failed to add the location")}{Environment.NewLine}{ex.Message}", T("Error"));
		}
	}

	private async void OnDeleteLocationClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (!TryGetSelected(out var selectedIndex, out _))
				return;

			if (!await SuppUi.YesNoAsync(T("Delete selected location?"), T("Confirm")))
				return;

			Configs.DeleteMove(selectedIndex);
			_modified = true;
			RefreshList();
			if (_items.Count > 0)
				EnsureIndex(Math.Clamp(selectedIndex, 0, _items.Count - 1));
		}
		catch (Exception ex)
		{
			await SuppUi.OkAsync($"{T("Delete location")}{Environment.NewLine}{ex.Message}", T("Error"));
		}
	}

	private void OnMoveUpClick(object? sender, RoutedEventArgs e) =>
		MoveSelectedBy(-1);

	private void OnMoveDownClick(object? sender, RoutedEventArgs e) =>
		MoveSelectedBy(1);

	private void MoveSelectedBy(int delta)
	{
		if (!TryGetSelected(out var oldIndex, out _))
			return;

		var newIndex = oldIndex + delta;
		if (newIndex < 0 || newIndex >= _items.Count)
			return;

		if (!Configs.MoveMove(oldIndex, newIndex))
			return;

		_modified = true;
		RefreshList();
		EnsureIndex(newIndex);
	}

	private void OnApplyAliasClick(object? sender, RoutedEventArgs e)
	{
		if (!TryGetSelected(out var selectedIndex, out var selectedItem))
			return;
		if (!selectedItem.Enabled)
			return;

		var alias = AliasTextBox.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(alias))
			return;

		Configs.EditMove(selectedIndex, selectedItem.Folder, alias);
		_modified = true;
		RefreshList();
		EnsureIndex(Math.Clamp(selectedIndex, 0, _items.Count - 1));
	}

	private bool TryGetSelected(out int index, [NotNullWhen(true)] out MoveDialogItem? item)
	{
		item = null;
		index = -1;

		if (MoveListBox.SelectedItem is not MoveDialogItem selected)
			return false;

		index = _items.IndexOf(selected);
		if (index < 0)
			return false;

		item = selected;
		return true;
	}

	private async void OnMoveListDoubleTapped(object? sender, TappedEventArgs e)
	{
		try
		{
			await AcceptAndCloseSafeAsync();
		}
		catch { /* 무시 */ }
	}

	private void OnMoveListPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		_dragStartPoint = e.GetPosition(MoveListBox);
		_dragStartItem = TryGetItemFromEventSource(e.Source, out _, out var item) ? item : null;
	}

	private async void OnMoveListPointerMoved(object? sender, PointerEventArgs e)
	{
		try
		{
			if (_dragStartPoint is null || _dragStartItem is null)
				return;

			if (!e.GetCurrentPoint(MoveListBox).Properties.IsLeftButtonPressed)
			{
				_dragStartPoint = null;
				_dragStartItem = null;
				return;
			}

			var now = e.GetPosition(MoveListBox);
			if (Math.Abs(now.X - _dragStartPoint.Value.X) < 4 && Math.Abs(now.Y - _dragStartPoint.Value.Y) < 4)
				return;

			var data = new DataTransfer();
			data.Add(DataTransferItem.Create(DataFormat.Text, $"{MoveDragPrefix}{_dragStartItem.Folder}"));
			await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
		}
		catch { /* 무시 */ }
		finally
		{
			_dragStartPoint = null;
			_dragStartItem = null;
		}
	}

	private void OnMoveListDragOver(object? sender, DragEventArgs e)
	{
		if (!TryGetDraggedFolder(e.DataTransfer, out _) || !TryGetItemFromEventSource(e.Source, out _, out _))
		{
			e.DragEffects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		e.DragEffects = DragDropEffects.Move;
		e.Handled = true;
	}

	private void OnMoveListDrop(object? sender, DragEventArgs e)
	{
		if (!TryGetItemFromEventSource(e.Source, out var newIndex, out _))
			return;

		if (!TryGetDraggedFolder(e.DataTransfer, out var folder))
			return;

		var oldIndex = -1;
		for (var i = 0; i < _items.Count; i++)
		{
			if (!_items[i].Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))
				continue;
			oldIndex = i;
			break;
		}

		if (oldIndex < 0 || oldIndex == newIndex)
			return;

		if (!Configs.MoveMove(oldIndex, newIndex))
			return;

		_modified = true;
		RefreshList();
		EnsureIndex(newIndex);
		e.Handled = true;
	}

	private async void OnOkClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			await AcceptAndCloseSafeAsync();
		}
		catch { /* 무시 */ }
	}

	private async Task AcceptAndCloseSafeAsync()
	{
		try
		{
			await AcceptAndClose();
		}
		catch (Exception ex)
		{
			await SuppUi.OkAsync($"{T("Move book")}{Environment.NewLine}{ex.Message}", T("Error"));
		}
	}

	private async Task AcceptAndClose()
	{
		if (TryGetSelected(out _, out var selectedItem) && !selectedItem.Enabled)
		{
			await SuppUi.OkAsync(T("The selected location is unavailable"), T("Error"));
			return;
		}

		var folder = selectedItem?.Folder ?? (DestTextBox.Text?.Trim() ?? string.Empty);
		if (!Directory.Exists(folder))
		{
			await SuppUi.OkAsync($"{T("The specified directory does not exist")}{Environment.NewLine}{folder}", T("Error"));
			return;
		}

		_resultPath = Path.Combine(folder, _fileName);
		Close(true);
	}

	private void OnCancelClick(object? sender, RoutedEventArgs e) =>
		Close(false);

	private void OnDialogKeyDown(object? sender, KeyEventArgs e)
	{
		if (_items.Count == 0)
			return;

		// ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
		switch (e.Key)
		{
			case Key.Enter:
				_ = AcceptAndCloseSafeAsync();
				e.Handled = true;
				break;
			case Key.Escape:
				Close(false);
				e.Handled = true;
				break;
			case Key.Delete:
				if (!MoveListBox.IsKeyboardFocusWithin)
					break;
				OnDeleteLocationClick(this, new RoutedEventArgs());
				e.Handled = true;
				break;
			case Key.F2:
				AliasTextBox.Focus();
				AliasTextBox.SelectAll();
				e.Handled = true;
				break;
			case Key.Up when e.KeyModifiers.HasFlag(KeyModifiers.Control):
				MoveSelectedBy(-1);
				e.Handled = true;
				break;
			case Key.Down when e.KeyModifiers.HasFlag(KeyModifiers.Control):
				MoveSelectedBy(1);
				e.Handled = true;
				break;
			case Key.Up when e.KeyModifiers == KeyModifiers.None && !IsTextInputFocused():
				MoveSelectionBy(-1);
				e.Handled = true;
				break;
			case Key.Down when e.KeyModifiers == KeyModifiers.None && !IsTextInputFocused():
				MoveSelectionBy(1);
				e.Handled = true;
				break;
			case Key.PageUp when e.KeyModifiers == KeyModifiers.None && !IsTextInputFocused():
				MoveSelectionBy(-10);
				e.Handled = true;
				break;
			case Key.PageDown when e.KeyModifiers == KeyModifiers.None && !IsTextInputFocused():
				MoveSelectionBy(10);
				e.Handled = true;
				break;
			case Key.Home when e.KeyModifiers == KeyModifiers.None && !IsTextInputFocused():
				EnsureIndex(0);
				e.Handled = true;
				break;
			case Key.End when e.KeyModifiers == KeyModifiers.None && !IsTextInputFocused():
				EnsureIndex(_items.Count - 1);
				e.Handled = true;
				break;
		}
	}

	private void FocusMoveListLater()
	{
		Dispatcher.UIThread.Post(() =>
		{
			if (_items.Count == 0)
				return;

			if (MoveListBox.SelectedIndex < 0)
				EnsureIndex(Math.Clamp(sLastSelected, 0, _items.Count - 1));

			MoveListBox.Focus();
		}, DispatcherPriority.Background);
	}

	private void MoveSelectionBy(int delta)
	{
		if (_items.Count == 0)
			return;

		var current = MoveListBox.SelectedIndex;
		if (current < 0)
			current = Math.Clamp(sLastSelected, 0, _items.Count - 1);

		var next = Math.Clamp(current + delta, 0, _items.Count - 1);
		EnsureIndex(next);
	}

	private bool IsTextInputFocused()
	{
		var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
		return focused is TextBox || (focused as Visual)?.GetSelfAndVisualAncestors().OfType<TextBox>().Any() == true;
	}

	private void UpdateSelectionUiState()
	{
		var enabled = !TryGetSelected(out _, out var selectedItem) || selectedItem.Enabled;
		AliasTextBox.IsEnabled = enabled;
		ApplyAliasButton.IsEnabled = enabled;
		OkButton.IsEnabled = enabled;
	}

	private bool TryGetItemFromEventSource(object? source, out int index, [NotNullWhen(true)] out MoveDialogItem? item)
	{
		index = -1;
		item = null;

		if (source is not Visual visual)
			return false;

		var listBoxItem = visual.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
		if (listBoxItem?.DataContext is not MoveDialogItem moveItem)
			return false;

		index = _items.IndexOf(moveItem);
		if (index < 0)
			return false;

		item = moveItem;
		return true;
	}

	private static bool TryGetDraggedFolder(IDataTransfer dataTransfer, [NotNullWhen(true)] out string? folder)
	{
		folder = null;

		var text = dataTransfer.TryGetText();
		if (string.IsNullOrWhiteSpace(text) || !text.StartsWith(MoveDragPrefix, StringComparison.Ordinal))
			return false;

		folder = text[MoveDragPrefix.Length..].Trim();
		return !string.IsNullOrWhiteSpace(folder);
	}

	private void OnDialogClosed(object? sender, EventArgs e)
	{
		if (_modified)
			Configs.CommitMoves();
	}
}
