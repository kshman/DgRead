global using DgRead.Chaek;
global using DgRead.Dowa;
global using static DgRead.Dowa.Intl;
global using Debug = System.Diagnostics.Debug;

using System;
using System.Globalization;
using Avalonia;

namespace DgRead;

internal class Program
{
	// Initialization code. Don't use any Avalonia, third-party APIs or any
	// SynchronizationContext-reliant code before AppMain is called: things aren't initialized
	// yet and stuff might break.
	[STAThread]
	public static void Main(string[] args)
	{
		try
		{
			var cultureName = CultureInfo.CurrentCulture?.Name ?? string.Empty;
			var locale = cultureName;
			if (!string.IsNullOrEmpty(cultureName))
			{
				var parts = cultureName.Split(['-', '_'], StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length > 0 && parts[0].Length >= 1)
					locale = parts[0];
			}

			LoadLocale(locale);
		}
		catch (Exception e)
		{
			Console.WriteLine(e);
		}

		BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
	}

	// Avalonia configuration, don't remove; also used by visual designer.
	public static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.WithInterFont()
			.LogToTrace();
}
