using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace DgRead;

internal partial class PassWindow : Window
{
    public PassWindow()
    {
        InitializeComponent();
        Title = T("Password");
        Opened += OnOpened;
        Closing += OnClosing;
    }

    private bool _closingByResult;

    private void OnOpened(object? sender, System.EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CodeTextBox.Focus();
            CodeTextBox.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingByResult)
            return;

        e.Cancel = true;
        CloseWithResult(null);
    }

    private void OnCodeKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CloseWithResult(CodeTextBox.Text ?? string.Empty);
                e.Handled = true;
                break;
            case Key.Escape:
                CloseWithResult(null);
                e.Handled = true;
                break;
        }
    }

    private TaskCompletionSource<string?>? _pending;

    public Task<string?> ShowAsync(Window? owner)
    {
        if (_pending != null)
            return _pending.Task;

        CodeTextBox.Text = string.Empty;
        _pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (owner != null)
            _ = ShowDialog(owner);
        else
            Show();

        return _pending.Task;
    }

    private void CloseWithResult(string? value)
    {
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(value);

        if (IsVisible)
        {
            _closingByResult = true;
            Close();
            _closingByResult = false;
        }
    }
}
