using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DgRead;

public sealed class PageSelectItem
{
	public int PageNo { get; init; }
	public string NoText { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
	public string DateText { get; init; } = string.Empty;
	public string SizeText { get; init; } = string.Empty;
}

public partial class PageWindow : Window
{
	private const int PagingSize = 12;

	private readonly List<PageSelectItem> _items = [];
	private TaskCompletionSource<int>? _pendingResult;
	private Window? _owner;
	private int _selectedPage;
	private bool _disposeRequested;

	public PageWindow()
	{
		InitializeComponent();
		ApplyLocalizedTexts();
		ResetBook();

		Closing += OnWindowClosing;
		AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel, true);
	}

	private void ApplyLocalizedTexts()
	{
		Title = T("Page selection");
		NoHeaderTextBlock.Text = T("No.");
		FilenameHeaderTextBlock.Text = T("Filename");
		DateHeaderTextBlock.Text = T("Date");
		SizeHeaderTextBlock.Text = T("Size");
		GoToPageButton.Content = T("Go to page");
		CancelButton.Content = T("Cancel");
	}

	private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
	{
		if (_disposeRequested)
			return;

		e.Cancel = true;
		CompleteSelection(-1);
	}

	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		switch (e.Key)
		{
			case Key.Escape:
				CompleteSelection(-1);
				e.Handled = true;
				break;
			case Key.Enter:
				CompleteSelection(GetSelectedPage());
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

	private void OnPageListDoubleTapped(object? sender, RoutedEventArgs e) =>
		CompleteSelection(GetSelectedPage());

	private void OnGoToPageClick(object? sender, RoutedEventArgs e) =>
		CompleteSelection(GetSelectedPage());

	private void OnCancelClick(object? sender, RoutedEventArgs e) =>
		CompleteSelection(-1);

	private int GetSelectedPage()
	{
		if (PageListBox.SelectedItem is PageSelectItem item)
			return item.PageNo;

		return -1;
	}

	private void CompleteSelection(int selectedPage)
	{
		if (_owner != null)
		{
			_owner.IsEnabled = true;
			_owner.Activate();
			_owner = null;
		}

		if (IsVisible)
			Hide();

		var pending = _pendingResult;
		_pendingResult = null;
		pending?.TrySetResult(selectedPage);
	}

	private void MoveSelection(int delta)
	{
		if (_items.Count == 0)
			return;

		var baseIndex = PageListBox.SelectedIndex >= 0 ? PageListBox.SelectedIndex : 0;
		MoveSelectionTo(baseIndex + delta);
	}

	private void MoveSelectionTo(int index)
	{
		if (_items.Count == 0)
			return;

		var clamped = Math.Clamp(index, 0, _items.Count - 1);
		PageListBox.SelectedIndex = clamped;
		EnsureCurrentSelectionVisible();
	}

	private void EnsureCurrentSelectionVisible()
	{
		var selectedItem = PageListBox.SelectedItem;
		Dispatcher.UIThread.Post(() =>
		{
			if (selectedItem != null)
				PageListBox.ScrollIntoView(selectedItem);
			PageListBox.Focus();
		}, DispatcherPriority.Background);
	}

	private void RefreshSelection()
	{
		if (_items.Count == 0)
		{
			PageListBox.SelectedIndex = -1;
			return;
		}

		var index = Math.Clamp(_selectedPage, 0, _items.Count - 1);
		PageListBox.SelectedIndex = index;
		EnsureCurrentSelectionVisible();
	}

	public void SetBook(BookBase book)
	{
		var entries = book.GetEntriesInfo();
		_items.Clear();
		_items.AddRange(entries.Select(entry => new PageSelectItem
		{
			PageNo = entry.PageNo,
			NoText = (entry.PageNo + 1).ToString(CultureInfo.InvariantCulture),
			Name = entry.Name,
			DateText = entry.Modified?.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
			SizeText = Doumi.SizeToString(entry.Size)
		}));

		PageListBox.ItemsSource = null;
		PageListBox.ItemsSource = _items;
		PageInfoTextBlock.Text = $"{T("Total page")}: {book.TotalPage}";
	}

	public void ResetBook()
	{
		_items.Clear();
		PageListBox.ItemsSource = null;
		PageInfoTextBlock.Text = T("[No Book]");
	}

	public Task<int> ShowAsync(Window owner, int page)
	{
		if (_items.Count == 0)
			return Task.FromResult(-1);

		if (_pendingResult != null)
		{
			Activate();
			return _pendingResult.Task;
		}

		_selectedPage = page;
		RefreshSelection();

		_owner = owner;
		_owner.IsEnabled = false;

		_pendingResult = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
		Show(owner);
		Activate();
		return _pendingResult.Task;
	}

	public void DisposeDialog()
	{
		if (_disposeRequested)
			return;

		_disposeRequested = true;
		_owner?.IsEnabled = true;

		_owner = null;
		if (IsVisible)
			Hide();

		Close();
	}
}
