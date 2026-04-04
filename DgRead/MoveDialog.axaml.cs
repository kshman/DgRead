using System;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace DgRead;

public sealed class MoveDialogItem
{
	public int No { get; init; }
	public string Alias { get; init; } = string.Empty;
	public string Folder { get; init; } = string.Empty;
}

public partial class MoveDialog : Window
{
	private static int sLastSelected = -1;

	private readonly ObservableCollection<MoveDialogItem> _items = [];
	private bool _refreshing;
	private bool _modified;
	private string _fileName = string.Empty;
	private string? _resultPath;

	public MoveDialog()
	{
		InitializeComponent();
		ApplyLocalizedTexts();
		MoveListBox.ItemsSource = _items;

		MoveListBox.SelectionChanged += OnMoveSelectionChanged;

		AddHandler(KeyDownEvent, OnDialogKeyDown, RoutingStrategies.Tunnel, true);
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
		MoveUpButton.Content = T("Move up");
		MoveDownButton.Content = T("Move down");
		DeleteLocationButton.Content = T("Delete location");
		OkButton.Content = T("OK");
		CancelButton.Content = T("Cancel");
	}

	public async Task<string?> ShowForMoveAsync(Window owner, string fileName)
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
			AliasTextBox.Text = string.Empty;
		}

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
		DestTextBox.Text = _items[index].Folder;
		AliasTextBox.Text = _items[index].Alias;
		if (!_refreshing)
			sLastSelected = index;
	}

	private void OnMoveSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (!TryGetSelected(out var index, out var selected))
			return;

		DestTextBox.Text = selected.Folder;
		AliasTextBox.Text = selected.Alias;
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
			Configs.LastFolder = folder;
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

	private async void OnMoveListDoubleTapped(object? sender, RoutedEventArgs e)
	{
		try
		{
			await AcceptAndCloseSafeAsync();
		}
		catch { /* 무시 */ }
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
		var folder = DestTextBox.Text?.Trim() ?? string.Empty;
		if (!Directory.Exists(folder))
		{
			await SuppUi.OkAsync($"{T("The specified directory does not exist")}{Environment.NewLine}{folder}", T("Error"));
			return;
		}

		Configs.LastFolder = folder;
		_resultPath = Path.Combine(folder, _fileName);
		Close(true);
	}

	private void OnCancelClick(object? sender, RoutedEventArgs e) =>
		Close(false);

	private void OnDialogKeyDown(object? sender, KeyEventArgs e)
	{
		switch (e.Key)
		{
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
		}
	}

	private void OnDialogClosed(object? sender, EventArgs e)
	{
		if (_modified)
			Configs.CommitMoves();
	}
}
