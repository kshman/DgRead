using Avalonia.Threading;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System.Threading.Tasks;

namespace DgRead.Dowa;

internal static class SuppUi
{
	private static async Task<ButtonResult> ShowMessageBoxAsync(string text, string title, ButtonEnum buttons)
	{
		var msgWindow = MessageBoxManager.GetMessageBoxStandard(title, text, buttons);
		if (Dispatcher.UIThread.CheckAccess())
			return await msgWindow.ShowAsync();

		return await Dispatcher.UIThread.InvokeAsync(msgWindow.ShowAsync);
	}

	public static async Task OkAsync(string text, string title = "Information") =>
		_ = await ShowMessageBoxAsync(text, title, ButtonEnum.Ok);

	public static async Task<bool> YesNoAsync(string text, string title = "Confirm")
	{
		var res = await ShowMessageBoxAsync(text, title, ButtonEnum.YesNo);
		return res == ButtonResult.Yes;
	}
}
