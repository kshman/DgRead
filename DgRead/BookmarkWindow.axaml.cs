using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace DgRead;

internal sealed class BookmarkListItem
{
	public required int Id { get; init; }
	public required string Path { get; init; }
	public required int Page { get; init; }
	public required string FileName { get; init; }
	public required string PathText { get; init; }
	public required string PageText { get; init; }
	public required string CreatedText { get; init; }
	public IBrush? FileNameBrush { get; init; }
}

internal sealed record BookmarkSelection(string Path, int Page);

internal partial class BookmarkWindow : Window
{
	private const int PagingSize = 12;

	private readonly List<BookmarkListItem> _items = [];
	private readonly Dictionary<string, bool> _pathChecks = new(StringComparer.OrdinalIgnoreCase);

	private readonly IBrush _nameBrush;
	private readonly SolidColorBrush _missingBrush;

	public BookmarkWindow()
	{
		InitializeComponent();
		_nameBrush = PageHeaderTextBlock.Foreground ?? new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
		_missingBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x57));

		ApplyLocalizedTexts();
		AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, true);
		Opened += OnWindowOpened;
	}

	private void ApplyLocalizedTexts()
	{
		Title = T("Bookmark manager");
		TitleTextBlock.Text = T("Bookmark manager");
		FilenameHeaderTextBlock.Text = T("Filename");
		PageHeaderTextBlock.Text = T("Page");
		CreatedHeaderTextBlock.Text = T("Added");
		ToolTip.SetTip(CheckFilesButton, T("Check bookmark files"));
	}

	private void OnWindowOpened(object? sender, EventArgs e)
	{
		Dispatcher.UIThread.Post(() =>
		{
			BookmarkListBox.Focus();
			if (BookmarkListBox.SelectedIndex < 0 && _items.Count > 0)
				BookmarkListBox.SelectedIndex = 0;
			EnsureCurrentSelectionVisible();
		}, DispatcherPriority.Background);
	}

	public Task<BookmarkSelection?> ShowAsync(Window owner)
	{
		RefreshItems();
		return ShowDialog<BookmarkSelection?>(owner);
	}

	private void RefreshItems(int preferredId = -1)
	{
		var selectedId = preferredId;
		if (selectedId < 0 && BookmarkListBox.SelectedItem is BookmarkListItem selected)
			selectedId = selected.Id;

		_items.Clear();
		foreach (var bm in Configs.Bookmarks.OrderByDescending(x => x.Created).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Page))
		{
			var fileName = Path.GetFileName(bm.Path);
			if (string.IsNullOrWhiteSpace(fileName))
				fileName = bm.Path;

			var isMissing = _pathChecks.TryGetValue(bm.Path, out var exists) && !exists;
			_items.Add(new BookmarkListItem
			{
				Id = bm.Id,
				Path = bm.Path,
				Page = bm.Page,
				FileName = fileName,
				PathText = bm.Path,
				PageText = (bm.Page + 1).ToString(CultureInfo.InvariantCulture),
				CreatedText = bm.Created == DateTime.MinValue ? string.Empty : bm.Created.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
				FileNameBrush = isMissing ? _missingBrush : _nameBrush,
			});
		}

		BookmarkListBox.ItemsSource = null;
		BookmarkListBox.ItemsSource = _items;

		if (_items.Count == 0)
		{
			BookmarkListBox.SelectedIndex = -1;
			return;
		}

		var index = selectedId >= 0 ? _items.FindIndex(x => x.Id == selectedId) : 0;
		if (index < 0)
			index = 0;

		BookmarkListBox.SelectedIndex = index;
		EnsureCurrentSelectionVisible();
	}

	private void EnsureCurrentSelectionVisible()
	{
		var selected = BookmarkListBox.SelectedItem;
		Dispatcher.UIThread.Post(() =>
		{
			if (selected != null)
				BookmarkListBox.ScrollIntoView(selected);
			BookmarkListBox.Focus();
		}, DispatcherPriority.Background);
	}

	private void OnBookmarkListDoubleTapped(object? sender, RoutedEventArgs e) =>
		CompleteWithSelection();

	private async void OnCheckFilesClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			await CheckBookmarkFilesAsync();
		}
		catch (Exception ex)
		{
			await SuppUi.OkAsync($"{T("Check bookmark files")}{Environment.NewLine}{ex.Message}", T("Error"));
		}
	}

	private async Task CheckBookmarkFilesAsync()
	{
		if (_items.Count == 0)
			return;

		if (_items.Count >= 50)
		{
			var ok = await SuppUi.YesNoAsync(T("There are 50+ bookmarks and it may take some time. Continue?"), T("Confirm"));
			if (!ok)
				return;
		}

		var paths = Configs.Bookmarks.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		var checks = await Task.Run(() =>
		{
			var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
			foreach (var path in paths)
				map[path] = Directory.Exists(path) || File.Exists(path);
			return map;
		});

		_pathChecks.Clear();
		foreach (var pair in checks)
			_pathChecks[pair.Key] = pair.Value;

		RefreshItems();
	}

	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		switch (e.Key)
		{
			case Key.Enter:
				CompleteWithSelection();
				e.Handled = true;
				break;
			case Key.Escape:
				Close(null);
				e.Handled = true;
				break;
			case Key.Delete:
				_ = DeleteSelectedBookmarkAsync();
				e.Handled = true;
				break;
			case Key.Up:
				MoveSelection(-1);
				e.Handled = true;
				break;
			case Key.Down:
				MoveSelection(1);
				e.Handled = true;
				break;
			case Key.PageUp:
				MoveSelection(-PagingSize);
				e.Handled = true;
				break;
			case Key.PageDown:
				MoveSelection(PagingSize);
				e.Handled = true;
				break;
			case Key.Home:
				MoveSelectionTo(0);
				e.Handled = true;
				break;
			case Key.End:
				MoveSelectionTo(_items.Count - 1);
				e.Handled = true;
				break;
		}
	}

	private void CompleteWithSelection()
	{
		if (BookmarkListBox.SelectedItem is not BookmarkListItem selected)
		{
			Close(null);
			return;
		}

		Close(new BookmarkSelection(selected.Path, selected.Page));
	}

	private async Task DeleteSelectedBookmarkAsync()
	{
		if (BookmarkListBox.SelectedItem is not BookmarkListItem selected)
			return;

		var ok = await SuppUi.YesNoAsync(
			$"{selected.FileName} ({selected.PageText}){Environment.NewLine}{Environment.NewLine}{T("Delete selected bookmark?")}",
			T("Confirm"));
		if (!ok)
			return;

		if (!Configs.RemoveBookmark(selected.Id))
		{
			await SuppUi.OkAsync(T("Failed to delete bookmark"), T("Error"));
			return;
		}

		RefreshItems();
	}

	private void MoveSelection(int delta)
	{
		if (_items.Count == 0)
			return;

		var baseIndex = BookmarkListBox.SelectedIndex >= 0 ? BookmarkListBox.SelectedIndex : 0;
		MoveSelectionTo(baseIndex + delta);
	}

	private void MoveSelectionTo(int index)
	{
		if (_items.Count == 0)
			return;

		var clamped = Math.Clamp(index, 0, _items.Count - 1);
		BookmarkListBox.SelectedIndex = clamped;
		EnsureCurrentSelectionVisible();
	}
}
