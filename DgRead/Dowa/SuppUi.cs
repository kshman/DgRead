using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace DgRead.Dowa;

internal static class SuppUi
{
	private static ButtonResult ShowMessageBox(string text, string title, ButtonEnum buttons)
	{
		var msgWindow = MessageBoxManager.GetMessageBoxStandard(T(title), T(text), buttons);

		return Dispatcher.UIThread.CheckAccess()
		 ? msgWindow.ShowAsync().GetAwaiter().GetResult()
		   : Dispatcher.UIThread.InvokeAsync(msgWindow.ShowAsync).GetAwaiter().GetResult();
	}

	public static void Ok(string text, string title = "Information") =>
		ShowMessageBox(text, title, ButtonEnum.Ok);

	public static bool YesNo(string text, string title = "Confirm")
	{
		var res = ShowMessageBox(text, title, ButtonEnum.YesNo);
		return res == ButtonResult.Yes;
	}
}
