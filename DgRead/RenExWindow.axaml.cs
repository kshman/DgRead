using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DgRead;

public sealed record RenExResult(string FileName, bool Reopen);

public partial class RenExWindow : Window
{
	private TaskCompletionSource<RenExResult?>? _pendingResult;
	private bool _initialized;
	private string _extension = string.Empty;

	public RenExWindow()
	{
		InitializeComponent();
		ApplyLocalizedTexts();
		Closing += OnWindowClosing;
	}

	private void ApplyLocalizedTexts()
	{
		Title = T("Rename book");
		OriginalLabel.Text = T("Original filename");
		RenameToLabel.Text = T("Rename to");
		TitleLabel.Text = T("Title");
		AuthorLabel.Text = T("Author");
		NoLabel.Text = T("No.");
		ExtraLabel.Text = T("Extra Information");
		OkButton.Content = T("OK");
		ReopenButton.Content = T("Reopen");
		CancelButton.Content = T("Cancel");
	}

	private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
	{
		_pendingResult?.TrySetResult(null);
		_pendingResult = null;
	}

	public Task<RenExResult?> ShowAsync(Window owner, string filename)
	{
		if (_pendingResult != null)
		{
			Activate();
			return _pendingResult.Task;
		}

		ParseFileName(filename);
		_pendingResult = new TaskCompletionSource<RenExResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
		Show(owner);
		Dispatcher.UIThread.Post(() => TitleTextBox.Focus(), DispatcherPriority.Background);
		return _pendingResult.Task;
	}

	private void ParseFileName(string? filename)
	{
		_initialized = false;
		OriginalText.Text = filename ?? string.Empty;
		RenameToText.Text = string.Empty;
		TitleTextBox.Text = string.Empty;
		AuthorTextBox.Text = string.Empty;
		IndexTextBox.Text = string.Empty;
		ExtraTextBox.Text = string.Empty;
		_extension = string.Empty;

		if (string.IsNullOrWhiteSpace(filename))
			return;

		var baseName = filename;
		var ext = System.IO.Path.GetExtension(filename);
		if (!string.IsNullOrEmpty(ext))
		{
			_extension = ext;
			baseName = filename[..^ext.Length];
		}

		var work = baseName.Trim();

		if (work.StartsWith('['))
		{
			var end = work.IndexOf(']');
			if (end > 0)
			{
				AuthorTextBox.Text = work[1..end].Trim();
				work = work[(end + 1)..].Trim();
			}
		}

		var extraStart = work.LastIndexOf('(');
		var extraEnd = work.LastIndexOf(')');
		if (extraStart >= 0 && extraEnd > extraStart)
		{
			ExtraTextBox.Text = work[(extraStart + 1)..extraEnd].Trim();
			work = work[..extraStart].Trim();
		}

		var lastSpace = work.LastIndexOf(' ');
		if (lastSpace >= 0)
		{
			var tail = work[(lastSpace + 1)..].Trim();
			if (int.TryParse(tail, out _))
			{
				IndexTextBox.Text = tail;
				work = work[..lastSpace].Trim();
			}
		}

		TitleTextBox.Text = work;
		_initialized = true;
		UpdatePreview();
	}

	private void OnEntryChanged(object? sender, TextChangedEventArgs e) =>
		UpdatePreview();

	private void UpdatePreview()
	{
		if (!_initialized)
			return;

		var author = AuthorTextBox.Text?.Trim() ?? string.Empty;
		var title = TitleTextBox.Text?.Trim() ?? string.Empty;
		var index = IndexTextBox.Text?.Trim() ?? string.Empty;
		var extra = ExtraTextBox.Text?.Trim() ?? string.Empty;

		var sb = new StringBuilder();
		if (!string.IsNullOrWhiteSpace(author))
			sb.Append('[').Append(author).Append("] ");
		if (!string.IsNullOrWhiteSpace(title))
			sb.Append(title);
		if (!string.IsNullOrWhiteSpace(index))
			sb.Append(' ').Append(index);
		if (!string.IsNullOrWhiteSpace(extra))
			sb.Append(" (").Append(extra).Append(')');
		sb.Append(_extension);

		RenameToText.Text = sb.ToString();
	}

	private void OnTitleKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			AuthorTextBox.Focus();
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			Complete(null);
			e.Handled = true;
		}
	}

	private void OnAuthorKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			IndexTextBox.Focus();
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			Complete(null);
			e.Handled = true;
		}
	}

	private void OnIndexKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			ExtraTextBox.Focus();
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			Complete(null);
			e.Handled = true;
		}
	}

	private void OnExtraKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			CompleteWithReopen(false);
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			Complete(null);
			e.Handled = true;
		}
	}

	private void OnOkClick(object? sender, RoutedEventArgs e) =>
		CompleteWithReopen(false);

	private void OnReopenClick(object? sender, RoutedEventArgs e) =>
		CompleteWithReopen(true);

	private void OnCancelClick(object? sender, RoutedEventArgs e) =>
		Complete(null);

	private void CompleteWithReopen(bool reopen)
	{
		var name = RenameToText.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(name))
			return;
		Complete(new RenExResult(name, reopen));
	}

	private void Complete(RenExResult? result)
	{
		var pending = _pendingResult;
		_pendingResult = null;
		pending?.TrySetResult(result);
		if (IsVisible)
			Close();
	}
}
