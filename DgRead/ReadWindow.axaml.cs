using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace DgRead;

public partial class ReadWindow : Window
{
	private string _openedEntryName = string.Empty;

	public ReadWindow()
	{
		InitializeComponent();
		MinWidth = 550;
		MinHeight = 350;

		ApplyLocalizedTexts();
		ApplyViewMenuDefaults();
		UpdateWindowTitle();

		if (!Configs.Initialize())
		{
			if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
				desktop.Shutdown();
			return;
		}

		Configs.LoadAllCache();
		ApplyConfigToViewMenu();
		ApplyWindowTheme();
		ApplySavedWindowBounds();

		Closing += OnClosing;
		Closed += OnClosed;
		PropertyChanged += OnWindowPropertyChanged;

		UpdateTitleBarState();
	}

	private void ApplyLocalizedTexts()
	{
		ViewMenuButtonText.Text = T(" ⧉ ");
		MainMenuButtonText.Text = T(" ≡ ");

		StretchViewMenuItem.Header = T("Stretch View");
		ViewDirectionLabel.Header = T("Viewing Direction");
		FitToScreenMenuItem.Header = T("Fit to Screen");
		LeftToRightMenuItem.Header = T("Left to Right");
		RightToLeftMenuItem.Header = T("Right to Left");

		ImageQualityLabel.Header = T("Image Quality");
		FastQualityMenuItem.Header = T("Fast Quality");
		DefaultQualityMenuItem.Header = T("Default Quality");
		HighQualityMenuItem.Header = T("High Quality");
		NearestInterpolationMenuItem.Header = T("Nearest");
		BilinearInterpolationMenuItem.Header = T("Bilinear");

		HorizontalAlignLabel.Header = T("Horizontal Alignment");
		LeftAlignMenuItem.Header = T("Align Left");
		CenterAlignMenuItem.Header = T("Align Center");
		RightAlignMenuItem.Header = T("Align Right");
		MarginLabel.Header = T("Margin");

		OpenBookMenuItem.Header = T("Open Book/File");
		OpenFolderMenuItem.Header = T("Open Folder");
		CloseBookMenuItem.Header = T("Close Book");
		SettingsMenuItem.Header = T("Settings");
		ThemeMenuItem.Header = T("Theme");
		ThemeDefaultMenuItem.Header = T("System default");
		ThemeLightMenuItem.Header = T("Light");
		ThemeDarkMenuItem.Header = T("Dark");
		ExitMenuItem.Header = T("Exit");

		ToolTip.SetTip(FullscreenButton, T("Toggle Fullscreen"));
		ToolTip.SetTip(MinimizeButton, T("Minimize"));
		ToolTip.SetTip(MaximizeButton, T("Maximize / Restore"));
		ToolTip.SetTip(CloseButton, T("Close"));
	}

	private void ApplyViewMenuDefaults()
	{
		FitToScreenMenuItem.IsChecked = true;
		DefaultQualityMenuItem.IsChecked = true;
		BilinearInterpolationMenuItem.IsChecked = true;
		CenterAlignMenuItem.IsChecked = true;
		MarginNumericUpDown.Value = 100;
		UpdateThemeMenuChecks(WindowTheme.Default);
	}

	private void ApplyConfigToViewMenu()
	{
		StretchViewMenuItem.IsChecked = Configs.ViewZoom;

		SetSingleChecked(Configs.ViewMode switch
		{
			ViewMode.LeftToRight => LeftToRightMenuItem,
			ViewMode.RightToLeft => RightToLeftMenuItem,
			_ => FitToScreenMenuItem
		}, FitToScreenMenuItem, LeftToRightMenuItem, RightToLeftMenuItem);

		SetSingleChecked(Configs.ViewQuality switch
		{
			ViewQuality.Fast => FastQualityMenuItem,
			ViewQuality.High => HighQualityMenuItem,
			ViewQuality.Nearest => NearestInterpolationMenuItem,
			ViewQuality.Bilinear => BilinearInterpolationMenuItem,
			_ => DefaultQualityMenuItem
		}, FastQualityMenuItem, DefaultQualityMenuItem, HighQualityMenuItem, NearestInterpolationMenuItem, BilinearInterpolationMenuItem);

		SetSingleChecked(Configs.ViewAlign switch
		{
			ViewAlign.Left => LeftAlignMenuItem,
			ViewAlign.Right => RightAlignMenuItem,
			_ => CenterAlignMenuItem
		}, LeftAlignMenuItem, CenterAlignMenuItem, RightAlignMenuItem);

		MarginNumericUpDown.Value = Math.Clamp(Configs.ViewMargin, 0, 9999);
		UpdateThemeMenuChecks(Configs.WindowTheme);
	}

	private static void ApplyWindowTheme()
	{
		if (Application.Current == null)
			return;

		Application.Current.RequestedThemeVariant = Configs.WindowTheme switch
		{
			WindowTheme.Light => ThemeVariant.Light,
			WindowTheme.Dark => ThemeVariant.Dark,
			_ => ThemeVariant.Default,
		};
	}

	private void ApplySavedWindowBounds()
	{
		const int savedWindowMinWidth = 550;
		const int savedWindowMinHeight = 350;
		const int savedWindowMaxDimension = 10000;
		if (Configs.WindowWidth >= savedWindowMinWidth && Configs.WindowWidth <= savedWindowMaxDimension)
			Width = Configs.WindowWidth;
		if (Configs.WindowHeight >= savedWindowMinHeight && Configs.WindowHeight <= savedWindowMaxDimension)
			Height = Configs.WindowHeight;

		const int savedWindowMinPos = -20000;
		const int savedWindowMaxPos = 20000;
		if (Configs.WindowX >= savedWindowMinPos && Configs.WindowX <= savedWindowMaxPos && Configs.WindowY >= savedWindowMinPos && Configs.WindowY <= savedWindowMaxPos)
			Position = new PixelPoint(Configs.WindowX, Configs.WindowY);
	}

	private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == WindowStateProperty)
			UpdateTitleBarState();
	}

	private void OnReadWindowKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Enter)
		{
			ToggleFullscreen();
			e.Handled = true;
			return;
		}

		if (e.Key == Key.F)
		{
			ToggleFullscreen();
			e.Handled = true;
			return;
		}

		if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
		{
			ToggleFullscreen();
			e.Handled = true;
			return;
		}

		if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.O)
		{
			_ = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? OpenFolderSafeAsync() : OpenBookFileSafeAsync();
			e.Handled = true;
			return;
		}

		if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.W)
		{
			CloseBook();
			e.Handled = true;
		}
	}

	private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !HasInteractiveAncestor(e.Source))
			BeginMoveDrag(e);
	}

	private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (!HasInteractiveAncestor(e.Source) && WindowState != WindowState.FullScreen)
			ToggleMaximizeRestore();
	}

	private void OnResizeTopPressed(object? sender, PointerPressedEventArgs e) =>
		BeginResizeFromPointer(e, WindowEdge.North);
	private void OnResizeBottomPressed(object? sender, PointerPressedEventArgs e) =>
		BeginResizeFromPointer(e, WindowEdge.South);
	private void OnResizeLeftPressed(object? sender, PointerPressedEventArgs e) =>
		BeginResizeFromPointer(e, WindowEdge.West);
	private void OnResizeRightPressed(object? sender, PointerPressedEventArgs e) =>
		BeginResizeFromPointer(e, WindowEdge.East);
	private void OnResizeTopLeftPressed(object? sender, PointerPressedEventArgs e) =>
		BeginResizeFromPointer(e, WindowEdge.NorthWest);
	private void OnResizeTopRightPressed(object? sender, PointerPressedEventArgs e) =>
		BeginResizeFromPointer(e, WindowEdge.NorthEast);
	private void OnResizeBottomLeftPressed(object? sender, PointerPressedEventArgs e) =>
		BeginResizeFromPointer(e, WindowEdge.SouthWest);
	private void OnResizeBottomRightPressed(object? sender, PointerPressedEventArgs e) =>
		BeginResizeFromPointer(e, WindowEdge.SouthEast);

	private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
		WindowState = WindowState.Minimized;

	private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e) =>
		ToggleMaximizeRestore();

	private void OnCloseClick(object? sender, RoutedEventArgs e) =>
		Close();

	private void OnToggleFullscreenClick(object? sender, RoutedEventArgs e) =>
		ToggleFullscreen();

	private void OnClosing(object? sender, WindowClosingEventArgs e)
	{
		if (Position.X is >= int.MinValue / 2 and <= int.MaxValue / 2)
			Configs.WindowX = Position.X;
		if (Position.Y is >= int.MinValue / 2 and <= int.MaxValue / 2)
			Configs.WindowY = Position.Y;

		if (!double.IsNaN(Bounds.Width) && !double.IsInfinity(Bounds.Width))
		{
			var w = (int)Math.Round(Bounds.Width);
			if (w is >= 0 and <= 100000) Configs.WindowWidth = w;
		}
		if (!double.IsNaN(Bounds.Height) && !double.IsInfinity(Bounds.Height))
		{
			var h = (int)Math.Round(Bounds.Height);
			if (h is >= 0 and <= 100000) Configs.WindowHeight = h;
		}
	}

	private void OnClosed(object? sender, EventArgs e)
	{
		try
		{
			Configs.Close();
		}
		catch { /* 무시 */ }
	}

	private void UpdateWindowTitle()
	{

		var appTitle = T("DgRead");
		var noBook = T("[No book opened]");
		var title = string.IsNullOrWhiteSpace(_openedEntryName)
			? noBook
			: $"{_openedEntryName} - {appTitle}";
		TitleTextBlock.Text = title;
		Title = title;
	}

	private void UpdateTitleBarState()
	{
		var isFullscreen = WindowState == WindowState.FullScreen;
		TitleBarHost.IsVisible = !isFullscreen;
		FullscreenGlyph.Text = isFullscreen ? "\uE73F" : "\uE740";
		MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
	}

	private static bool HasInteractiveAncestor(object? source)
	{
		return
			source is Visual visual &&
			visual.GetSelfAndVisualAncestors().Any(v => v is Button or MenuItem or ComboBox or NumericUpDown);
	}

	private void BeginResizeFromPointer(PointerPressedEventArgs e, WindowEdge edge)
	{
		if (WindowState != WindowState.Normal)
			return;
		if (!CanResize)
			return;

		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			BeginResizeDrag(edge, e);
	}

	private void ToggleMaximizeRestore()
	{
		WindowState = WindowState switch
		{
			WindowState.Maximized => WindowState.Normal,
			WindowState.Normal => WindowState.Maximized,
			_ => WindowState
		};
	}

	private void ToggleFullscreen()
	{
		WindowState = WindowState switch
		{
			WindowState.FullScreen => WindowState.Normal,
			_ => WindowState.FullScreen
		};
	}

	private void OnViewMenuButtonClick(object? sender, RoutedEventArgs e)
	{
		if (sender is Button button)
			button.ContextMenu?.Open(button);
	}

	private void OnMainMenuButtonClick(object? sender, RoutedEventArgs e)
	{
		if (sender is Button button)
			button.ContextMenu?.Open(button);
	}

	private void OnStretchViewClick(object? sender, RoutedEventArgs e)
	{
		Configs.ViewZoom = StretchViewMenuItem.IsChecked;
	}

	private void OnViewDirectionClick(object? sender, RoutedEventArgs e)
	{
		if (sender is not MenuItem selected)
			return;

		SetSingleChecked(selected, FitToScreenMenuItem, LeftToRightMenuItem, RightToLeftMenuItem);
		Configs.ViewMode = selected == LeftToRightMenuItem
			? ViewMode.LeftToRight
			: selected == RightToLeftMenuItem
				? ViewMode.RightToLeft
				: ViewMode.Fit;
	}

	private void OnImageQualityClick(object? sender, RoutedEventArgs e)
	{
		if (sender is not MenuItem selected)
			return;

		SetSingleChecked(selected, FastQualityMenuItem, DefaultQualityMenuItem, HighQualityMenuItem, NearestInterpolationMenuItem, BilinearInterpolationMenuItem);
		Configs.ViewQuality = selected == FastQualityMenuItem
			? ViewQuality.Fast
			: selected == HighQualityMenuItem
				? ViewQuality.High
				: selected == NearestInterpolationMenuItem
					? ViewQuality.Nearest
					: selected == BilinearInterpolationMenuItem
						? ViewQuality.Bilinear
						: ViewQuality.Default;
	}

	private void OnHorizontalAlignClick(object? sender, RoutedEventArgs e)
	{
		if (sender is not MenuItem selected)
			return;

		SetSingleChecked(selected, LeftAlignMenuItem, CenterAlignMenuItem, RightAlignMenuItem);
		Configs.ViewAlign = selected == LeftAlignMenuItem
			? ViewAlign.Left
			: selected == RightAlignMenuItem
				? ViewAlign.Right
				: ViewAlign.Center;
	}

	private void OnMarginValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
	{
		var margin = (int)Math.Round(Convert.ToDouble(e.NewValue));
		Configs.ViewMargin = Math.Clamp(margin, 0, 9999);
	}

	private void OnThemeMenuClick(object? sender, RoutedEventArgs e)
	{
		if (sender is not MenuItem selected)
			return;

		var theme = selected == ThemeLightMenuItem
			? WindowTheme.Light
			: selected == ThemeDarkMenuItem
				? WindowTheme.Dark
				: WindowTheme.Default;

		Configs.WindowTheme = theme;
		UpdateThemeMenuChecks(theme);
		ApplyWindowTheme();
	}

	private async void OnOpenBookClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			await OpenBookFileAsync();
		}
		catch (Exception ex)
		{
			SuppUi.Ok(this, $"{T("Failed to open book/file")}{Environment.NewLine}{ex.Message}", "Error");
		}
	}

	private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			await OpenFolderAsync();
		}
		catch (Exception ex)
		{
			SuppUi.Ok(this, $"{T("Failed to open folder")}{Environment.NewLine}{ex.Message}", "Error");
		}
	}

	private void OnCloseBookClick(object? sender, RoutedEventArgs e) =>
		CloseBook();

	private void OnSettingsClick(object? sender, RoutedEventArgs e)
	{
	}

	private void OnExitClick(object? sender, RoutedEventArgs e) =>
		Close();

	private static void SetSingleChecked(MenuItem selected, params MenuItem[] group)
	{
		foreach (var item in group)
			item.IsChecked = item == selected;
	}

	private void UpdateThemeMenuChecks(WindowTheme theme)
	{
		SetSingleChecked(theme switch
		{
			WindowTheme.Light => ThemeLightMenuItem,
			WindowTheme.Dark => ThemeDarkMenuItem,
			_ => ThemeDefaultMenuItem
		}, ThemeDefaultMenuItem, ThemeLightMenuItem, ThemeDarkMenuItem);
	}

	private async Task OpenBookFileSafeAsync()
	{
		try
		{
			await OpenBookFileAsync();
		}
		catch (Exception ex)
		{
			SuppUi.Ok(this, $"{T("Failed to open book/file")}{Environment.NewLine}{ex.Message}", "Error");
		}
	}

	private async Task OpenFolderSafeAsync()
	{
		try
		{
			await OpenFolderAsync();
		}
		catch (Exception ex)
		{
			SuppUi.Ok(this, $"{T("Failed to open folder")}{Environment.NewLine}{ex.Message}", "Error");
		}
	}

	private async Task OpenBookFileAsync()
	{
		if (!StorageProvider.CanOpen)
			return;

		var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			AllowMultiple = false,
			Title = T("Open Book/File")
		});

		if (files.Count == 0)
			return;

		_openedEntryName = files[0].Name;
		var filePath = files[0].TryGetLocalPath();
		if (!string.IsNullOrWhiteSpace(filePath))
		{
			var dir = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrWhiteSpace(dir))
				Configs.LastFolder = dir;
			Configs.LastFileName = filePath;
		}
		UpdateWindowTitle();
	}

	private async Task OpenFolderAsync()
	{
		if (!StorageProvider.CanOpen)
			return;

		var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
		{
			AllowMultiple = false,
			Title = T("Open Folder")
		});

		if (folders.Count == 0)
			return;

		_openedEntryName = folders[0].Name;
		var folderPath = folders[0].TryGetLocalPath();
		if (!string.IsNullOrWhiteSpace(folderPath))
			Configs.LastFolder = folderPath;
		UpdateWindowTitle();
	}

	private void CloseBook()
	{
		_openedEntryName = string.Empty;
		UpdateWindowTitle();
	}
}
