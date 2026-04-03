using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Chrome;
using Avalonia.Threading;
using MessageBox.Avalonia;
using MessageBox.Avalonia.Enums;

namespace DgRead.Dowa;

internal static class SuppUi
{
	private static ButtonResult ShowMessageBox(Window? owner, string text, string title, ButtonEnum buttons)
	{
		var msgWindow = MessageBoxManager.GetMessageBoxStandardWindow(T(title), T(text), buttons);

		var ownerWindow = owner;
		if (ownerWindow == null && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
			ownerWindow = desktop.MainWindow;

		return Dispatcher.UIThread.CheckAccess()
			? msgWindow.ShowDialog(ownerWindow).GetAwaiter().GetResult()
			: Dispatcher.UIThread.InvokeAsync(() => msgWindow.ShowDialog(ownerWindow)).GetAwaiter().GetResult();
	}

	public static void Ok(Window? owner, string text, string title = "Information") =>
		ShowMessageBox(owner, text, title, ButtonEnum.Ok);

	public static void Ok(string text, string title = "Information") =>
		Ok(null, text, title);

	public static bool YesNo(Window? owner, string text, string title = "Confirm")
	{
		var res = ShowMessageBox(owner, text, title, ButtonEnum.YesNo);
		return res == ButtonResult.Yes;
	}

	public static bool YesNo(string text, string title = "Confirm") =>
		YesNo(null, text, title);
}
