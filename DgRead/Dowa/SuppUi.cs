using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia;
using Avalonia.Input;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace DgRead.Dowa;

internal static class SuppUi
{
	private static async Task<bool> ShowMessageBoxAsync(Window? owner, string text, string title, bool hasReturn)
	{
		if (Dispatcher.UIThread.CheckAccess())
			return await ShowImpl();

		return await Dispatcher.UIThread.InvokeAsync(async () => await ShowImpl());

		async Task<bool> ShowImpl()
		{
			var tcs = new TaskCompletionSource<bool>();

			// Determine owner if not provided
			var ownerWindow = owner ?? (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

			var dlg = new Window
			{
				Title = title,
				SizeToContent = SizeToContent.WidthAndHeight,
				WindowStartupLocation = ownerWindow is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
				CanResize = false,
				CanMinimize = false,
				CanMaximize = false,
				MinWidth = 300,
				Padding = new Thickness(12)
			};

			var textBlock = new TextBlock
			{
				Text = text,
				TextWrapping = TextWrapping.Wrap,
				Margin = new Thickness(20, 10, 20, 36)
			};

			var buttonPanel = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				HorizontalAlignment = HorizontalAlignment.Center,
				Spacing = 8
			};

			if (hasReturn)
			{
				// 예/아니오
				AddButton(T("Yes"), true);
				AddButton(T("No"), false);
			}
			else
			{
				// OK만
				AddButton(T("Ok"), true);
			}

			var root = new StackPanel();
			root.Children.Add(textBlock);
			root.Children.Add(buttonPanel);
			dlg.Content = root;

			// If the dialog is closed by other means, ensure we set a default result
			dlg.Closed += (_, _) => tcs.TrySetResult(!hasReturn);

			// Keyboard handling: Esc -> cancel/close, Enter -> accept (OK or Yes)
			dlg.KeyDown += (_, e) =>
			{
				if (e.Key == Key.Escape)
				{
					tcs.TrySetResult(!hasReturn);
					try { dlg.Close(); } catch { /* 무시 */ }
					e.Handled = true;
				}
				else if (e.Key == Key.Enter)
				{
					// Accept default: first button added (OK or Yes)
					if (buttonPanel.Children.FirstOrDefault() is { } first)
						first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
					e.Handled = true;
				}
			};

			// Set initial focus to the first button when dialog opens so Enter works
			dlg.Opened += (_, _) =>
			{
				var first = buttonPanel.Children.FirstOrDefault();
				first?.Focus();
			};

			// Show dialog; prefer modal if we have an owner
			if (ownerWindow is not null)
				_ = dlg.ShowDialog(ownerWindow);
			else
				dlg.Show();

			return await tcs.Task.ConfigureAwait(false);

			void AddButton(string content, bool result)
			{
				var btn = new Button { Content = content, MinWidth = 80 };
				btn.Click += (_, _) =>
				{
					// Try to set result and close the dialog
					tcs.TrySetResult(result);
					try { dlg.Close(); } catch { /* 무시 */ }
				};
				buttonPanel.Children.Add(btn);
			}
		}
	}

	public static async Task OkAsync(Window? owner, string text, string title = "Information") =>
		_ = await ShowMessageBoxAsync(owner, text, title, false);

	public static async Task<bool> YesNoAsync(Window? owner, string text, string title = "Confirm") =>
		await ShowMessageBoxAsync(owner, text, title, true);
}
