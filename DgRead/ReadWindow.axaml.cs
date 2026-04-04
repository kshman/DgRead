using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DgRead;

/// <summary>
/// 책/이미지 뷰어의 메인 읽기 창을 제공합니다.
/// </summary>
public partial class ReadWindow : Window
{
	private BookBase? _book;
	private string _openedEntryName = string.Empty;

	private readonly PageWindow _pageWindow;
	private readonly ZpsController _zps;
	private readonly ScrollModeController _scroll;

	private readonly DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
	private readonly DispatcherTimer _keyHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

	private readonly List<AnimationBinding> _animations = [];
	private readonly Dictionary<int, PageImage> _scrollPageCache = [];
	private readonly HashSet<Key> _pressedKeys = [];

	private int _holdTick;
	private bool _virtualizeBusy;
	private bool _virtualizePending;
	private DateTime _lastVirtualizeAtUtc;
	private DateTime _lastAnimationTickAtUtc;

	private const int MinAnimationFrameDurationMs = 10;
	private static readonly TimeSpan VirtualizeCooldown = TimeSpan.FromMilliseconds(120);

	/// <summary>
	/// <see cref="ReadWindow"/> 인스턴스를 초기화합니다.
	/// </summary>
	public ReadWindow()
	{
		InitializeComponent();
		MinWidth = 550;
		MinHeight = 350;

		_zps = new ZpsController(ReaderScrollViewer, LeftPageImage, RightPageImage);
		_scroll = new ScrollModeController(ReaderScrollViewer, ScrollPagesPanel);
		_pageWindow = new PageWindow();

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
		Deactivated += (_, _) => _pressedKeys.Clear();
		PropertyChanged += OnWindowPropertyChanged;
		_animationTimer.Tick += OnAnimationTick;
		_keyHoldTimer.Tick += OnKeyHoldTick;

		AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
		AddHandler(DragDrop.DropEvent, OnWindowDrop);
		AddHandler(KeyDownEvent, OnReadWindowKeyDown, RoutingStrategies.Tunnel, true);
		AddHandler(KeyUpEvent, OnReadWindowKeyUp, RoutingStrategies.Tunnel, true);

		_zps.Attach();
		ReaderScrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, OnReaderPointerWheelPreview, RoutingStrategies.Tunnel, true);
		ReaderScrollViewer.AddHandler(InputElement.PointerPressedEvent, OnReaderPointerPressedPreview, RoutingStrategies.Tunnel, true);
		ReaderScrollViewer.AddHandler(InputElement.PointerReleasedEvent, OnReaderPointerReleasedPreview, RoutingStrategies.Tunnel, true);
		ReaderScrollViewer.AddHandler(InputElement.PointerMovedEvent, OnReaderPointerMovedPreview, RoutingStrategies.Tunnel, true);

		UpdateTitleBarState();
		RenderBook();
		Focus();
	}

	/// <summary>
	/// 로캘 문자열을 UI 텍스트에 적용합니다.
	/// </summary>
	private void ApplyLocalizedTexts()
	{
		ViewDirectionLabel.Header = T("Viewing Direction");
		FitToScreenMenuItem.Header = T("Fit to Screen");
		LeftToRightMenuItem.Header = T("Left to Right");
		RightToLeftMenuItem.Header = T("Right to Left");
		ScrollModeMenuItem.Header = T("Scroll");

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

	/// <summary>
	/// 메뉴의 기본 체크 상태를 적용합니다.
	/// </summary>
	private void ApplyViewMenuDefaults()
	{
		FitToScreenMenuItem.IsChecked = true;
		DefaultQualityMenuItem.IsChecked = true;
		BilinearInterpolationMenuItem.IsChecked = true;
		CenterAlignMenuItem.IsChecked = true;
		MarginNumericUpDown.Value = 100;
		UpdateThemeMenuChecks(WindowTheme.Default);
		UpdateViewModeIcon(ViewMode.Single);
	}

	/// <summary>
	/// 설정값을 메뉴 상태에 반영합니다.
	/// </summary>
	private void ApplyConfigToViewMenu()
	{
		SetSingleChecked(Configs.ViewMode switch
		{
			ViewMode.LeftToRight => LeftToRightMenuItem,
			ViewMode.RightToLeft => RightToLeftMenuItem,
			ViewMode.Scroll => ScrollModeMenuItem,
			_ => FitToScreenMenuItem
		}, FitToScreenMenuItem, LeftToRightMenuItem, RightToLeftMenuItem, ScrollModeMenuItem);

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

	/// <summary>
	/// 저장된 테마 설정을 애플리케이션에 적용합니다.
	/// </summary>
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

	/// <summary>
	/// 저장된 창 크기/위치를 적용합니다.
	/// </summary>
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

	/// <summary>
	/// 윈도우 속성 변경 이벤트를 처리합니다.
	/// </summary>
	private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == WindowStateProperty)
			UpdateTitleBarState();
	}

	/// <summary>
	/// 드래그 오버 시 파일 드롭 가능 상태를 설정합니다.
	/// </summary>
	private void OnWindowDragOver(object? sender, DragEventArgs e)
	{
		e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
	}

	/// <summary>
	/// 파일/폴더 드롭을 처리하여 책을 엽니다.
	/// </summary>
  private async void OnWindowDrop(object? sender, DragEventArgs e)
	{
		try
		{
			var files = e.DataTransfer.TryGetFiles();
			var item = files?.FirstOrDefault();
			if (item == null)
				return;

			var path = item.TryGetLocalPath();
			if (string.IsNullOrWhiteSpace(path))
				return;

			if (Directory.Exists(path))
			{
				Configs.LastFolder = path;
			}
			else if (File.Exists(path))
			{
				var dir = Path.GetDirectoryName(path);
				if (!string.IsNullOrWhiteSpace(dir))
					Configs.LastFolder = dir;
				Configs.LastFileName = path;
			}

			OpenBookByPath(path);
		}
		catch (Exception ex)
		{
         await SuppUi.OkAsync($"{T("Failed to open book/file")}{Environment.NewLine}{ex.Message}", T("Error"));
		}
	}

	private void OnReaderPointerWheelPreview(object? sender, PointerWheelEventArgs e)
	{
		if (_book == null)
			return;

		if (IsScrollMode())
		{
			Dispatcher.UIThread.Post(() =>
			{
				SyncScrollCurrentPage();
				MaybeVirtualizeScroll();
			});
			return;
		}

		if (_zps.HandleWheelAsZoom(e))
			return;

		PageControl(e.Delta.Y < 0 ? BookControl.Next : BookControl.Previous);
		e.Handled = true;
	}

	private void OnReaderPointerPressedPreview(object? sender, PointerPressedEventArgs e)
	{
		if (!IsScrollMode())
			return;
		_scroll.TryBeginDrag(e);
	}

	private void OnReaderPointerReleasedPreview(object? sender, PointerReleasedEventArgs e)
	{
		_scroll.TryEndDrag(e);
	}

	private void OnReaderPointerMovedPreview(object? sender, PointerEventArgs e)
	{
		if (!IsScrollMode())
			return;

		if (_scroll.TryDrag(e))
		{
			SyncScrollCurrentPage();
			MaybeVirtualizeScroll();
		}
	}

	/// <summary>
	/// 키 입력을 명령 흐름에 맞춰 처리합니다.
	/// </summary>
	private void OnReadWindowKeyDown(object? sender, KeyEventArgs e)
	{
		if (IsModifierOnly(e.Key))
			return;

		if (!_pressedKeys.Add(e.Key))
		{
			e.Handled = true;
			return;
		}

		if (ShouldUseContinuousInput())
			EnsureKeyHoldTimer();

		if (e is { KeyModifiers: KeyModifiers.Alt, Key: Key.Enter })
		{
			ToggleFullscreen();
			e.Handled = true;
			return;
		}

		if (IsScrollMode() && _scroll.TryHandleWidthKey(e.Key))
		{
			RenderBook();
			e.Handled = true;
			return;
		}

		if (_zps.HandleZoomHotkeys(e))
		{
			e.Handled = true;
			return;
		}

		if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.O)
		{
			_ = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? OpenFolderSafeAsync() : OpenBookFileSafeAsync();
			e.Handled = true;
			return;
		}

		var handled = true;

		// ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
		switch (e.Key)
		{
			// 끝
			case Key.Escape:
				if (WindowState == WindowState.FullScreen)
					ToggleFullscreen();
				else if (Configs.EscToExit)
					Close();
				else
					handled = false;
				break;

			case Key.W:
				if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
					CloseBook();
				else
					handled = false;
				break;

			// 페이지 + 스크롤 + ZPS
			case Key.Up:
				if (IsScrollMode())
					break;
				if (!_zps.TryPanByKeyboard(0, -80))
					PageControl(BookControl.SeekMinusOne);
				break;

			case Key.Down:
				if (IsScrollMode())
					break;
				if (!_zps.TryPanByKeyboard(0, 80))
					PageControl(BookControl.SeekPlusOne);
				break;

			case Key.Left:
				if (IsScrollMode())
					break;
				if (!_zps.TryPanByKeyboard(-80, 0))
					PageControl(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? BookControl.SeekMinusOne : BookControl.Previous);
				break;

			case Key.Right:
				if (IsScrollMode() && e.Key == Key.Right)
					break;
				if (!_zps.TryPanByKeyboard(80, 0))
					PageControl(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? BookControl.SeekPlusOne : BookControl.Next);
				break;

			// 페이지
			case Key.OemComma:
				PageControl(BookControl.SeekMinusOne);
				break;

			case Key.OemPeriod:
			case Key.Oem2:
				PageControl(BookControl.SeekPlusOne);
				break;

			case Key.NumPad0:
			case Key.Space:
				PageControl(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? BookControl.SeekPlusOne : BookControl.Next);
				break;

			case Key.Home:
				PageControl(BookControl.First);
				break;

			case Key.End:
				PageControl(BookControl.Last);
				break;

			case Key.PageUp:
				if (IsScrollMode())
				{
					ReaderScrollViewer.Offset = _scroll.ClampOffset(new Vector(ReaderScrollViewer.Offset.X, ReaderScrollViewer.Offset.Y - ReaderScrollViewer.Viewport.Height * 0.9));
					SyncScrollCurrentPage();
					MaybeVirtualizeScroll();
					break;
				}
				if (_zps.IsZoomed)
					PageControl(IsTwoPageMode() ? BookControl.Previous : BookControl.SeekMinusOne);
				else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
					OpenPrevBook();
				else
					PageControl(BookControl.SeekPrevious10);
				break;

			case Key.PageDown:
				if (IsScrollMode() && e.Key == Key.PageDown)
				{
					ReaderScrollViewer.Offset = _scroll.ClampOffset(new Vector(ReaderScrollViewer.Offset.X, ReaderScrollViewer.Offset.Y + ReaderScrollViewer.Viewport.Height * 0.9));
					SyncScrollCurrentPage();
					MaybeVirtualizeScroll();
					break;
				}
				if (_zps.IsZoomed && e.Key == Key.PageDown)
					PageControl(IsTwoPageMode() ? BookControl.Next : BookControl.SeekPlusOne);
				else if (e.Key == Key.PageDown && e.KeyModifiers.HasFlag(KeyModifiers.Control))
					OpenNextBook();
				else
					PageControl(BookControl.SeekNext10);
				break;

			case Key.Back:
				PageControl(BookControl.SeekNext10);
				break;

			case Key.Enter:
				_ = OpenPageWindowSafeAsync();
				break;

			// 보기
			case Key.Oem3:
				UpdateViewMode(ViewMode.Scroll);
				break;

			case Key.D1:
				UpdateViewMode(ViewMode.Single);
				break;

			case Key.D2:
				UpdateViewMode(ViewMode.LeftToRight);
				break;

			case Key.D3:
				UpdateViewMode(ViewMode.RightToLeft);
				break;

			case Key.Tab:
				if (HasOpenedBook())
				{
					switch (Configs.ViewMode)
					{
						case ViewMode.LeftToRight:
							UpdateViewMode(ViewMode.RightToLeft);
							break;
						case ViewMode.RightToLeft:
							UpdateViewMode(ViewMode.LeftToRight);
							break;
						default:
							handled = false;
							break;
					}
				}
				else
				{
					handled = false;
				}
				break;

			// 파일이나 디렉토리
			case Key.BrowserBack:
			case Key.OemOpenBrackets:
				OpenPrevBook();
				break;

			case Key.BrowserForward:
			case Key.OemCloseBrackets:
				OpenNextBook();
				break;

			case Key.OemPipe:
			case Key.OemBackslash:  // 이건 뭐지
				OpenRandomBook();
				break;

			case Key.Insert:
				MoveBook();
				break;

			case Key.OemQuotes:
				if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
					SaveRememberBook();
				else
					handled = false;
				break;

			case Key.OemSemicolon:
				if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
					OpenRememberBook();
				else
					handled = false;
				break;

			// 기능
			case Key.F:
				if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
					ToggleFullscreen();
				else
					handled = false;
				break;

			case Key.F11:
#if DEBUG
				Notify("알림 메시지 테스트이와요~");
#else
				handled = false;
#endif
				break;

			default:
#if DEBUG
				Debug.WriteLine($"키코드: {e.Key}, Modifiers: {e.KeyModifiers}");
#endif
				handled = false;
				break;
		}

		e.Handled = handled;
	}

	private void OnReadWindowKeyUp(object? sender, KeyEventArgs e)
	{
		_pressedKeys.Remove(e.Key);
		if (_pressedKeys.Count == 0)
			_keyHoldTimer.Stop();
	}

	private void OnKeyHoldTick(object? sender, EventArgs e)
	{
		if (_book == null || _pressedKeys.Count == 0)
		{
			_keyHoldTimer.Stop();
			return;
		}

		_holdTick++;

		if (IsScrollMode())
		{
			if ((_pressedKeys.Contains(Key.Add) || _pressedKeys.Contains(Key.OemPlus)) && (_holdTick % 3 == 0))
			{
				if (_scroll.TryHandleWidthKey(Key.Add))
					RenderBook();
			}
			if ((_pressedKeys.Contains(Key.Subtract) || _pressedKeys.Contains(Key.OemMinus)) && (_holdTick % 3 == 0))
			{
				if (_scroll.TryHandleWidthKey(Key.Subtract))
					RenderBook();
			}

			var dy = 0.0;
			var dx = 0.0;
			if (_pressedKeys.Contains(Key.Up)) dy -= 24;
			if (_pressedKeys.Contains(Key.Down)) dy += 24;
			if (_pressedKeys.Contains(Key.Left)) dx -= 24;
			if (_pressedKeys.Contains(Key.Right)) dx += 24;
			if (dx != 0 || dy != 0)
			{
				ReaderScrollViewer.Offset = _scroll.ClampOffset(ReaderScrollViewer.Offset + new Vector(dx, dy));
				SyncScrollCurrentPage();
				MaybeVirtualizeScroll();
			}

			return;
		}

		if (_zps.IsZoomed)
		{
			if ((_pressedKeys.Contains(Key.Add) || _pressedKeys.Contains(Key.OemPlus)) && (_holdTick % 3 == 0))
				_zps.ZoomByFactor(1.05);
			if ((_pressedKeys.Contains(Key.Subtract) || _pressedKeys.Contains(Key.OemMinus)) && (_holdTick % 3 == 0))
				_zps.ZoomByFactor(1 / 1.05);

			var dy = 0.0;
			var dx = 0.0;
			if (_pressedKeys.Contains(Key.Up)) dy -= 20;
			if (_pressedKeys.Contains(Key.Down)) dy += 20;
			if (_pressedKeys.Contains(Key.Left)) dx -= 20;
			if (_pressedKeys.Contains(Key.Right)) dx += 20;
			if (dx != 0 || dy != 0)
				_zps.TryPanByKeyboard(dx, dy);
		}
	}

	private void EnsureKeyHoldTimer()
	{
		if (!_keyHoldTimer.IsEnabled)
		{
			_holdTick = 0;
			_keyHoldTimer.Start();
		}
	}

	private static bool IsModifierOnly(Key key) =>
		key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift;

	private bool ShouldUseContinuousInput() =>
		(_book != null && IsScrollMode()) || _zps.IsZoomed;

	private bool IsScrollMode() =>
		_book?.ViewMode == ViewMode.Scroll;

	private async Task OpenPageWindowSafeAsync()
	{
		var openedBook = _book;
		if (openedBook == null)
			return;

		var selected = await _pageWindow.ShowForPageAsync(this, openedBook.CurrentPage);
		if (selected < 0 || _book != openedBook)
			return;

		openedBook.MovePage(selected);
		_zps.ResetZoom();
		openedBook.PrepareImages();
		RenderBook();
		UpdateWindowTitle();
		Configs.SetHistory(openedBook.FullName, openedBook.CurrentPage);
	}

	private void MaybeVirtualizeScroll()
	{
		if (!IsScrollMode() || _book == null)
			return;

		if (_virtualizeBusy || _virtualizePending)
			return;

		var nowUtc = DateTime.UtcNow;
		var elapsed = nowUtc - _lastVirtualizeAtUtc;
		var due = elapsed >= VirtualizeCooldown ? TimeSpan.Zero : VirtualizeCooldown - elapsed;
		_virtualizePending = true;

		DispatcherTimer.RunOnce(() =>
		{
			_virtualizePending = false;
			ExecuteVirtualizeScroll();
		}, due, DispatcherPriority.Background);
	}

	private void ExecuteVirtualizeScroll()
	{
		if (_virtualizeBusy || !IsScrollMode() || _book == null)
			return;

		SyncScrollCurrentPage();

		var dir = _scroll.GetVirtualizeDirection();
		switch (dir)
		{
			case 0:
			case > 0 when _book.CurrentPage >= _book.TotalPage - 1:
			case < 0 when _book.CurrentPage <= 0:
				return;
			default:
				_virtualizeBusy = true;
				try
				{
					if (_book.MovePage(_book.CurrentPage + dir))
					{
						_book.PrepareImages();
						RenderBook();
						UpdateWindowTitle();
						_lastVirtualizeAtUtc = DateTime.UtcNow;
					}
				}
				finally
				{
					_virtualizeBusy = false;
				}

				break;
		}
	}

	private void SyncScrollCurrentPage()
	{
		if (!IsScrollMode() || _book == null)
			return;

		var centerPage = _scroll.GetCenterPageIndex(_book.CurrentPage);
		if (!_book.MovePage(centerPage))
			return;

		UpdateWindowTitle();
	}

	/// <summary>
	/// 타이틀 바에서 마우스 누름 이벤트를 처리합니다.
	/// </summary>
	private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && !HasInteractiveAncestor(e.Source))
			BeginMoveDrag(e);
	}

	/// <summary>
	/// 타이틀 바 더블 클릭 이벤트를 처리합니다.
	/// </summary>
	private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (!HasInteractiveAncestor(e.Source) && WindowState != WindowState.FullScreen)
			ToggleMaximizeRestore();
	}

	/// <summary>상단 리사이즈 드래그를 시작합니다.</summary>
	private void OnResizeTopPressed(object? sender, PointerPressedEventArgs e) => BeginResizeFromPointer(e, WindowEdge.North);
	/// <summary>하단 리사이즈 드래그를 시작합니다.</summary>
	private void OnResizeBottomPressed(object? sender, PointerPressedEventArgs e) => BeginResizeFromPointer(e, WindowEdge.South);
	/// <summary>좌측 리사이즈 드래그를 시작합니다.</summary>
	private void OnResizeLeftPressed(object? sender, PointerPressedEventArgs e) => BeginResizeFromPointer(e, WindowEdge.West);
	/// <summary>우측 리사이즈 드래그를 시작합니다.</summary>
	private void OnResizeRightPressed(object? sender, PointerPressedEventArgs e) => BeginResizeFromPointer(e, WindowEdge.East);
	/// <summary>좌상단 리사이즈 드래그를 시작합니다.</summary>
	private void OnResizeTopLeftPressed(object? sender, PointerPressedEventArgs e) => BeginResizeFromPointer(e, WindowEdge.NorthWest);
	/// <summary>우상단 리사이즈 드래그를 시작합니다.</summary>
	private void OnResizeTopRightPressed(object? sender, PointerPressedEventArgs e) => BeginResizeFromPointer(e, WindowEdge.NorthEast);
	/// <summary>좌하단 리사이즈 드래그를 시작합니다.</summary>
	private void OnResizeBottomLeftPressed(object? sender, PointerPressedEventArgs e) => BeginResizeFromPointer(e, WindowEdge.SouthWest);
	/// <summary>우하단 리사이즈 드래그를 시작합니다.</summary>
	private void OnResizeBottomRightPressed(object? sender, PointerPressedEventArgs e) => BeginResizeFromPointer(e, WindowEdge.SouthEast);

	/// <summary>
	/// 최소화 버튼 클릭을 처리합니다.
	/// </summary>
	private void OnMinimizeClick(object? sender, RoutedEventArgs e) =>
		WindowState = WindowState.Minimized;

	/// <summary>
	/// 최대화/복원 버튼 클릭을 처리합니다.
	/// </summary>
	private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e) =>
		ToggleMaximizeRestore();

	/// <summary>
	/// 닫기 버튼 클릭을 처리합니다.
	/// </summary>
	private void OnCloseClick(object? sender, RoutedEventArgs e) =>
		Close();

	/// <summary>
	/// 전체화면 버튼 클릭을 처리합니다.
	/// </summary>
	private void OnToggleFullscreenClick(object? sender, RoutedEventArgs e) =>
		ToggleFullscreen();

	/// <summary>
	/// 창 닫힘 직전에 창 상태를 저장합니다.
	/// </summary>
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

	/// <summary>
	/// 창이 닫힌 뒤 리소스를 정리합니다.
	/// </summary>
	private void OnClosed(object? sender, EventArgs e)
	{
		try
		{
			_pageWindow.DisposeDialog();
			_animationTimer.Stop();
			_keyHoldTimer.Stop();
			_animationTimer.Tick -= OnAnimationTick;
			_keyHoldTimer.Tick -= OnKeyHoldTick;
			ClearRenderedPages();
			_book?.Dispose();
			_book = null;
			Configs.Close();
		}
		catch
		{
			// 설정 저장 실패는 앱 종료 흐름을 막지 않습니다.
		}
	}

	/// <summary>
	/// 현재 파일 이름 기반으로 타이틀을 갱신합니다.
	/// </summary>
	private void UpdateWindowTitle()
	{
		var appTitle = T("DgRead");
		var title = (_book != null) ?
			$"[{_book.CurrentPage + 1}/{_book.TotalPage}] {_book.DisplayName}" :
			(!string.IsNullOrWhiteSpace(_openedEntryName)) ?
				_openedEntryName :
				T("[No book opened]");
		TitleTextBlock.Text = title;
		Title = $"{title} - {appTitle}";
	}

	/// <summary>
	/// 창 상태에 따라 타이틀바 표시 및 버튼 아이콘을 갱신합니다.
	/// </summary>
	private void UpdateTitleBarState()
	{
		var isFullscreen = WindowState == WindowState.FullScreen;
		TitleBarHost.IsVisible = !isFullscreen;
		FullscreenGlyph.Text = isFullscreen ? "\uE73F" : "\uE740";
		MaximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
	}

	/// <summary>
	/// 입력 소스가 메뉴/버튼 같은 상호작용 컨트롤인지 확인합니다.
	/// </summary>
	private static bool HasInteractiveAncestor(object? source)
	{
		return
			source is Visual visual &&
			visual.GetSelfAndVisualAncestors().Any(v => v is Button or MenuItem or ComboBox or NumericUpDown);
	}

	/// <summary>
	/// 가장자리 리사이즈 드래그를 시작합니다.
	/// </summary>
	private void BeginResizeFromPointer(PointerPressedEventArgs e, WindowEdge edge)
	{
		if (WindowState != WindowState.Normal)
			return;
		if (!CanResize)
			return;

		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			BeginResizeDrag(edge, e);
	}

	/// <summary>
	/// 창 최대화/복원을 전환합니다.
	/// </summary>
	private void ToggleMaximizeRestore()
	{
		WindowState = WindowState switch
		{
			WindowState.Maximized => WindowState.Normal,
			WindowState.Normal => WindowState.Maximized,
			_ => WindowState
		};
	}

	/// <summary>
	/// 창 전체화면을 전환합니다.
	/// </summary>
	private void ToggleFullscreen()
	{
		WindowState = WindowState switch
		{
			WindowState.FullScreen => WindowState.Normal,
			_ => WindowState.FullScreen
		};
	}

	private bool IsTwoPageMode() =>
		_book?.ViewMode is ViewMode.LeftToRight or ViewMode.RightToLeft;

	/// <summary>
	/// 현재 책/모드 상태를 화면에 렌더링합니다.
	/// </summary>
	private void RenderBook()
	{
		var keepScrollAnchor = _book?.ViewMode == ViewMode.Scroll;
		if (keepScrollAnchor)
			_scroll.CaptureAnchorByCenter();

		ClearRenderedPages();

		if (_book == null)
		{
			_zps.ResetZoom();
			_zps.SetTwoPageMode(false);
			LogoImage.IsVisible = true;
			SpreadPanel.IsVisible = false;
			ScrollPagesPanel.IsVisible = false;
			ReaderScrollViewer.Offset = default;
			return;
		}

		var leftPage = _book.PageLeft;
		var rightPage = _book.PageRight;
		var singlePage = leftPage ?? rightPage;
		var actualTwoPage = IsTwoPageMode() && leftPage != null && rightPage != null && !leftPage.HasAnimation && !rightPage.HasAnimation;
		_zps.SetTwoPageMode(actualTwoPage);
		_zps.ApplyLayout();

		LogoImage.IsVisible = false;

		if (_book.ViewMode == ViewMode.Scroll && _book.SupportsMultiPageModes)
		{
			SpreadPanel.IsVisible = false;
			ScrollPagesPanel.IsVisible = true;
			_scroll.RenderWindow(_book, _scrollPageCache, (page, image) => _animations.Add(new AnimationBinding(page, image)));
			_scroll.RestoreAnchorByCenter();
		}
		else
		{
			SpreadPanel.IsVisible = true;
			ScrollPagesPanel.IsVisible = false;

			if (actualTwoPage)
			{
				LeftPageImage.Source = leftPage!.GetBitmap();
				RightPageImage.Source = rightPage!.GetBitmap();
				RightPageImage.IsVisible = true;
			}
			else
			{
				LeftPageImage.Source = singlePage?.GetBitmap();
				RightPageImage.Source = null;
				RightPageImage.IsVisible = false;

				if (singlePage?.HasAnimation == true)
					_animations.Add(new AnimationBinding(singlePage, LeftPageImage));
			}
		}

		if (_book.ViewMode is not ViewMode.Scroll && !_zps.IsZoomed)
			ReaderScrollViewer.Offset = default;

		if (_animations.Count > 0)
		{
			_lastAnimationTickAtUtc = DateTime.UtcNow;
			_animationTimer.Start();
		}
	}

	private void OnAnimationTick(object? sender, EventArgs e)
	{
		if (_animations.Count == 0)
		{
			_animationTimer.Stop();
			return;
		}

		var nowUtc = DateTime.UtcNow;
		var elapsedMs = (int)Math.Max(1, (nowUtc - _lastAnimationTickAtUtc).TotalMilliseconds);
		_lastAnimationTickAtUtc = nowUtc;

		foreach (var anim in _animations)
		{
			anim.Remaining -= elapsedMs;
			var advanced = false;
			while (anim.Remaining <= 0)
			{
				anim.Remaining += Math.Max(MinAnimationFrameDurationMs, anim.Page.Animate());
				advanced = true;
			}

			if (advanced)
				anim.Target.Source = anim.Page.GetBitmap();
		}
	}

	private void ClearRenderedPages()
	{
		_animationTimer.Stop();
		_animations.Clear();

		LeftPageImage.Source = null;
		RightPageImage.Source = null;
		ScrollPagesPanel.Children.Clear();
		_scrollPageCache.Clear();
	}

	/// <summary>
	/// 페이지 제어 명령을 처리합니다.
	/// </summary>
	/// <remarks>
	/// 실제 렌더러/페이지 모델 연결 시 본문을 구현합니다.
	/// </remarks>
	private void PageControl(BookControl control)
	{
		if (_book == null)
			return;

		var moved = control switch
		{
			BookControl.Previous => _book.MovePrev(),
			BookControl.Next => _book.MoveNext(),
			BookControl.First => _book.MovePage(0),
			BookControl.Last => _book.MovePage(_book.TotalPage - 1),
			BookControl.SeekPrevious10 => _book.MovePage(_book.CurrentPage - 10),
			BookControl.SeekNext10 => _book.MovePage(_book.CurrentPage + 10),
			BookControl.SeekMinusOne => _book.MovePage(_book.CurrentPage - 1),
			BookControl.SeekPlusOne => _book.MovePage(_book.CurrentPage + 1),
			BookControl.Select => _book.MovePage(_book.CurrentPage),
			_ => false
		};

		if (!moved && control != BookControl.Select)
			return;

		_zps.ResetZoom();
		_book.PrepareImages();
		RenderBook();
		UpdateWindowTitle();
		Configs.SetHistory(_book.FullName, _book.CurrentPage);
	}

	/// <summary>
	/// 이전 책 열기 흐름을 시작합니다.
	/// </summary>
	private void OpenPrevBook()
	{
		if (_book == null)
			return;

		var next = _book.FindNextFileAny(BookDirection.Previous);
		if (!string.IsNullOrWhiteSpace(next))
			OpenBookByPath(next);
	}

	/// <summary>
	/// 다음 책 열기 흐름을 시작합니다.
	/// </summary>
	private void OpenNextBook()
	{
		if (_book == null)
			return;

		var next = _book.FindNextFileAny(BookDirection.Next);
		if (!string.IsNullOrWhiteSpace(next))
			OpenBookByPath(next);
	}

	private void OpenRandomBook()
	{
		if (_book == null)
			return;

		var next = _book.FindRandomFile();
		if (!string.IsNullOrWhiteSpace(next))
			OpenBookByPath(next);
	}

	/// <summary>
	/// 현재 책을 이동하는 흐름을 시작합니다.
	/// </summary>
	private async void MoveBook()
	{
		try
		{
			var book = _book;
			var fileName = book?.FileName ?? string.Empty;

			var dlg = new MoveDialog();
			var destinationFile = await dlg.ShowForMoveAsync(this, fileName);
			if (string.IsNullOrWhiteSpace(destinationFile))
				return;

			if (book == null)
			{
             await SuppUi.OkAsync(T("[No book opened]"), T("Information"));
				return;
			}

			var next = book.FindNextFileAny(BookDirection.Next);
			if (!book.MoveFile(destinationFile))
			{
               await SuppUi.OkAsync(T("Failed to move book/file"), T("Error"));
				return;
			}

			CloseBook();
			if (!string.IsNullOrWhiteSpace(next))
				OpenBookByPath(next);
		}
		catch (Exception ex)
		{
         await SuppUi.OkAsync($"{T("Failed to move book/file")}{Environment.NewLine}{ex.Message}", T("Error"));
		}
	}

	/// <summary>
	/// 현재 책을 기억 목록에 저장합니다.
	/// </summary>
	private void SaveRememberBook()
	{
		if (_book == null)
			return;

		Configs.LastFileName = _book.FullName;
	}

	/// <summary>
	/// 기억한 책을 엽니다.
	/// </summary>
	private void OpenRememberBook()
	{
		var path = Configs.LastFileName;
		if (!string.IsNullOrWhiteSpace(path))
			OpenBookByPath(path);
	}

	/// <summary>
	/// 보기 모드를 변경하고 UI/설정을 동기화합니다.
	/// </summary>
	private void UpdateViewMode(ViewMode mode)
	{
		if (_book is { SupportsMultiPageModes: false } && mode != ViewMode.Single)
			mode = ViewMode.Single;

		Configs.ViewMode = mode;
		if (_book != null)
		{
			_book.ViewMode = mode;
			_book.PrepareImages();
			RenderBook();
			UpdateWindowTitle();
		}

		SetSingleChecked(mode switch
		{
			ViewMode.LeftToRight => LeftToRightMenuItem,
			ViewMode.RightToLeft => RightToLeftMenuItem,
			ViewMode.Scroll => ScrollModeMenuItem,
			_ => FitToScreenMenuItem
		}, FitToScreenMenuItem, LeftToRightMenuItem, RightToLeftMenuItem, ScrollModeMenuItem);

		UpdateViewModeIcon(mode);
	}

	private void UpdateViewModeIcon(ViewMode mode)
	{
		var iconName = mode switch
		{
			ViewMode.Single => "ViewMenuIcon",
			ViewMode.LeftToRight => "ViewL2RIcon",
			ViewMode.RightToLeft => "ViewR2LIcon",
			ViewMode.Scroll => "ViewScrollIcon",
			_ => "ViewMenuIcon",
		};
		if (this.TryFindResource(iconName, out var icon) && icon is DrawingImage drawingImage)
		{
			// 메뉴 버튼 아이콘을 현재 보기 모드에 맞게 변경합니다.
			ViewMenuButtonIcon.Source = drawingImage;
		}
	}

	/// <summary>
	/// 현재 열려 있는 책이 있는지 확인합니다.
	/// </summary>
	private bool HasOpenedBook() =>
	   _book != null;

	/// <summary>
	/// 디버그 알림 메시지를 출력합니다.
	/// </summary>
	private static void Notify(string message)
	{
		Debug.WriteLine(message);
	}

	/// <summary>
	/// 보기 메뉴 버튼 클릭을 처리합니다.
	/// </summary>
	private void OnViewMenuButtonClick(object? sender, RoutedEventArgs e)
	{
		if (sender is Button button)
			button.ContextMenu?.Open(button);
	}

	/// <summary>
	/// 메인 메뉴 버튼 클릭을 처리합니다.
	/// </summary>
	private void OnMainMenuButtonClick(object? sender, RoutedEventArgs e)
	{
		if (sender is Button button)
			button.ContextMenu?.Open(button);
	}

	/// <summary>
	/// 보기 방향 메뉴 클릭을 처리합니다.
	/// </summary>
	private void OnViewDirectionClick(object? sender, RoutedEventArgs e)
	{
		if (sender is not MenuItem selected)
			return;

		SetSingleChecked(selected, FitToScreenMenuItem, LeftToRightMenuItem, RightToLeftMenuItem, ScrollModeMenuItem);
		var mode = selected == LeftToRightMenuItem
			? ViewMode.LeftToRight
			: selected == RightToLeftMenuItem
				? ViewMode.RightToLeft
			 : selected == ScrollModeMenuItem
					? ViewMode.Scroll
					: ViewMode.Single;

		UpdateViewMode(mode);
	}

	/// <summary>
	/// 이미지 품질 메뉴 클릭을 처리합니다.
	/// </summary>
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

	/// <summary>
	/// 가로 정렬 메뉴 클릭을 처리합니다.
	/// </summary>
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

	/// <summary>
	/// 여백 값 변경을 처리합니다.
	/// </summary>
	private void OnMarginValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
	{
		var margin = (int)Math.Round(Convert.ToDouble(e.NewValue));
		Configs.ViewMargin = Math.Clamp(margin, 0, 9999);
	}

	/// <summary>
	/// 테마 메뉴 클릭을 처리합니다.
	/// </summary>
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

	/// <summary>
	/// 책/파일 열기 메뉴 클릭을 처리합니다.
	/// </summary>
	private async void OnOpenBookClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			await OpenBookFileAsync();
		}
		catch (Exception ex)
		{
            await SuppUi.OkAsync($"{T("Failed to open book/file")}{Environment.NewLine}{ex.Message}", "Error");
		}
	}

	/// <summary>
	/// 폴더 열기 메뉴 클릭을 처리합니다.
	/// </summary>
	private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			await OpenFolderAsync();
		}
		catch (Exception ex)
		{
            await SuppUi.OkAsync($"{T("Failed to open folder")}{Environment.NewLine}{ex.Message}", T("Error"));
		}
	}

	/// <summary>
	/// 닫기 메뉴 클릭을 처리합니다.
	/// </summary>
	private void OnCloseBookClick(object? sender, RoutedEventArgs e) =>
		CloseBook();

	/// <summary>
	/// 설정 메뉴 클릭을 처리합니다.
	/// </summary>
	private void OnSettingsClick(object? sender, RoutedEventArgs e)
	{
	}

	/// <summary>
	/// 종료 메뉴 클릭을 처리합니다.
	/// </summary>
	private void OnExitClick(object? sender, RoutedEventArgs e) =>
		Close();

	/// <summary>
	/// 메뉴 그룹에서 단일 체크를 적용합니다.
	/// </summary>
	private static void SetSingleChecked(MenuItem selected, params MenuItem[] group)
	{
		foreach (var item in group)
			item.IsChecked = item == selected;
	}

	/// <summary>
	/// 현재 테마 선택 상태를 메뉴 체크에 반영합니다.
	/// </summary>
	private void UpdateThemeMenuChecks(WindowTheme theme)
	{
		SetSingleChecked(theme switch
		{
			WindowTheme.Light => ThemeLightMenuItem,
			WindowTheme.Dark => ThemeDarkMenuItem,
			_ => ThemeDefaultMenuItem
		}, ThemeDefaultMenuItem, ThemeLightMenuItem, ThemeDarkMenuItem);
	}

	/// <summary>
	/// 키 처리용 책/파일 열기를 예외 안전하게 수행합니다.
	/// </summary>
	private async Task OpenBookFileSafeAsync()
	{
		try
		{
			await OpenBookFileAsync();
		}
		catch (Exception ex)
		{
         await SuppUi.OkAsync($"{T("Failed to open book/file")}{Environment.NewLine}{ex.Message}", T("Error"));
		}
	}

	/// <summary>
	/// 키 처리용 폴더 열기를 예외 안전하게 수행합니다.
	/// </summary>
	private async Task OpenFolderSafeAsync()
	{
		try
		{
			await OpenFolderAsync();
		}
		catch (Exception ex)
		{
            await SuppUi.OkAsync($"{T("Failed to open folder")}{Environment.NewLine}{ex.Message}", T("Error"));
		}
	}

	/// <summary>
	/// 파일 선택기를 통해 책/파일을 엽니다.
	/// </summary>
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

		var filePath = files[0].TryGetLocalPath();
		if (!string.IsNullOrWhiteSpace(filePath))
		{
			var dir = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrWhiteSpace(dir))
				Configs.LastFolder = dir;
			Configs.LastFileName = filePath;
			OpenBookByPath(filePath);
		}
		else
		{
			_openedEntryName = files[0].Name;
			UpdateWindowTitle();
		}
	}

	/// <summary>
	/// 폴더 선택기를 통해 폴더를 엽니다.
	/// </summary>
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

		var folderPath = folders[0].TryGetLocalPath();
		if (!string.IsNullOrWhiteSpace(folderPath))
		{
			Configs.LastFolder = folderPath;
			OpenBookByPath(folderPath);
		}
		else
		{
			_openedEntryName = folders[0].Name;
			UpdateWindowTitle();
		}
	}

	/// <summary>
	/// 현재 열린 책 상태를 해제합니다.
	/// </summary>
	private void CloseBook()
	{
		if (_book != null)
			Configs.SetHistory(_book.FullName, _book.CurrentPage);

		_book?.Dispose();
		_book = null;
		_pageWindow.ResetBook();
		_openedEntryName = string.Empty;
		UpdateWindowTitle();
		UpdateViewModeIcon(ViewMode.Single);
		RenderBook();
	}

	/// <summary>
	/// 경로로부터 책을 열고 초기 이미지를 준비합니다.
	/// </summary>
	private void OpenBookByPath(string path)
	{
		try
		{
			var book = BookFactory.Open(path);
			if (book == null)
			{
                _ = SuppUi.OkAsync(T("Unsupported book format"), T("Error"));
				return;
			}

			_book?.Dispose();
			_zps.ResetZoom();
			_book = book;
			_book.ViewMode = _book.SupportsMultiPageModes ? Configs.ViewMode : ViewMode.Single;
			_book.MovePage(Configs.GetHistory(path));
			_book.PrepareImages();
			_pageWindow.SetBook(_book);

			_openedEntryName = _book.FileName;
			UpdateWindowTitle();
			RenderBook();

			if (!_book.SupportsMultiPageModes)
				UpdateViewMode(ViewMode.Single);

			UpdateViewModeIcon(Configs.ViewMode);
		}
		catch (Exception ex)
		{
         _ = SuppUi.OkAsync($"{T("Failed to open book/file")}{Environment.NewLine}{ex.Message}", T("Error"));
		}
	}

	private sealed class AnimationBinding
	{
		public AnimationBinding(PageImage page, Image target)
		{
			Page = page;
			Target = target;
			Remaining = page.Frames is { Count: > 0 }
				? Math.Max(MinAnimationFrameDurationMs, page.Frames[page.CurrentFrame].Duration)
				: MinAnimationFrameDurationMs;
		}

		public PageImage Page { get; }
		public Image Target { get; }
		public int Remaining { get; set; }
	}
}
