using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault
// ReSharper disable SwitchStatementMissingSomeEnumCasesNoDefault

namespace DgRead;

/// <summary>
/// 메인 책/그림 읽기 창을 제공하는 클래스입니다.
/// </summary>
public partial class ReadWindow : Window
{
	private BookBase? _book;

	private readonly PageWindow _pageWindow;
	private readonly ReadZpsController _zps;
	private readonly ReadScrollController _scroll;

	private readonly DispatcherTimer _animationTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
	private readonly DispatcherTimer _keyHoldTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
	private readonly DispatcherTimer _notifyTimer = new();

	private readonly List<AnimationBind> _animations = [];
	private readonly Dictionary<int, PageImage> _scrollPageCache = [];
	private readonly HashSet<Key> _pressedKeys = [];

	private int _keyHoldTick;
	private int _keyHoldCount;

	private bool _virtualizeBusy;
	private bool _virtualizePending;
	private DateTime _lastVirtualize;
	private DateTime _lastAnimationTick;

	private const int MinAnimationFrameDurationMs = 10;
	private static readonly TimeSpan VirtualizeCooldown = TimeSpan.FromMilliseconds(120);

	/// <summary>
	/// 생성자: 창의 UI를 초기화하고 로케일/설정/이벤트를 설정합니다.
	/// </summary>
	public ReadWindow()
	{
		InitializeComponent();
		InitializeWindowAndLocale();
		UpdateTitleText();

		_zps = new ReadZpsController(ReaderScrollViewer, LeftPageImage, RightPageImage);
		_scroll = new ReadScrollController(ReaderScrollViewer, ScrollPagesPanel);
		_pageWindow = new PageWindow();

		if (!Configs.Initialize())
		{
			if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
				desktop.Shutdown();
			return;
		}

		Configs.LoadAllCache();
		ApplyTheme();
		ApplyMenuState();
		ApplyBounds();

		Closing += OnClosing;
		Closed += OnClosed;
		Deactivated += OnDeactivated;
		PropertyChanged += OnPropertyChanged;
		_animationTimer.Tick += OnAnimationTick;
		_keyHoldTimer.Tick += OnKeyHoldTick;
		_notifyTimer.Tick += OnNotifyTick;

		AddHandler(DragDrop.DragOverEvent, OnDragOverEvent);
		AddHandler(DragDrop.DropEvent, OnDropEvent);
		AddHandler(KeyDownEvent, OnKeyDownEvent, RoutingStrategies.Tunnel, true);
		AddHandler(KeyUpEvent, OnKeyUpEvent, RoutingStrategies.Tunnel, true);

		_zps.Attach();
		ReaderScrollViewer.AddHandler(PointerWheelChangedEvent, OnPointerWheelPreview, RoutingStrategies.Tunnel, true);
		ReaderScrollViewer.AddHandler(PointerPressedEvent, OnPointerPressedPreview, RoutingStrategies.Tunnel, true);
		ReaderScrollViewer.AddHandler(PointerReleasedEvent, OnPointerReleasedPreview, RoutingStrategies.Tunnel, true);
		ReaderScrollViewer.AddHandler(PointerMovedEvent, OnPointerMovedPreview, RoutingStrategies.Tunnel, true);

		UpdateTitleState();
		RenderBook();
		Focus();
	}

	/// <summary>
	/// 윈도우 상태를 초기화 하고 로캘 문자열을 UI 텍스트에 적용합니다.
	/// </summary>
	private void InitializeWindowAndLocale()
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
		AddBookmarkMenuItem.Header = T("Add bookmark");
		ManageBookmarksMenuItem.Header = T("Manage bookmarks");
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

		// 메뉴의 기본 상태 설정
		FitToScreenMenuItem.IsChecked = true;
		DefaultQualityMenuItem.IsChecked = true;
		BilinearInterpolationMenuItem.IsChecked = true;
		CenterAlignMenuItem.IsChecked = true;
		MarginNumericUpDown.Value = 100;
		UpdateThemeMenuChecks(WindowTheme.Default);
		SetViewModeIcon(ViewMode.Single);

		// 최소 창 크기 설정
		MinWidth = 550;
		MinHeight = 350;
	}

	/// <summary>
	/// 설정값을 메뉴 상태에 반영합니다.
	/// </summary>
	private void ApplyMenuState()
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
	private static void ApplyTheme()
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
	private void ApplyBounds()
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
	/// 윈도우 속성 변경 이벤트를 처리합니다. 현재는 창 상태(WindowState) 변경 시
	/// 타이틀 상태를 갱신합니다.
	/// </summary>
	/// <param name="sender">이벤트 발신자</param>
	/// <param name="e">속성 변경 이벤트 인자</param>
	private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == WindowStateProperty)
			UpdateTitleState();
	}

	/// <summary>
	/// 드래그 오버 이벤트 핸들러입니다. 드래그 중인 데이터가 파일인 경우
	/// 드롭이 가능하도록 효과를 설정합니다.
	/// </summary>
	/// <param name="sender">이벤트 발신자</param>
	/// <param name="e">드래그 이벤트 인자</param>
	private void OnDragOverEvent(object? sender, DragEventArgs e)
	{
		e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
	}

	/// <summary>
	/// 드롭 이벤트를 처리하여 드롭된 파일/폴더로 책을 엽니다.
	/// </summary>
	/// <param name="sender">이벤트 발신자</param>
	/// <param name="e">드래그 이벤트 인자</param>
	private void OnDropEvent(object? sender, DragEventArgs e)
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

			OpenBook(path);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"드롭 파일 읽기 실패: {ex.Message}");
			Notify(T("Failed to open book/file"), 5000);
		}
	}

	/// <summary>
	/// 마우스 휠 이벤트의 프리뷰 단계에서 처리합니다. 스크롤 모드일 경우 스크롤 동기화를,
	/// 그렇지 않으면 페이지 전환 또는 줌 처리를 시도합니다.
	/// </summary>
	/// <param name="sender">이벤트 발신자</param>
	/// <param name="e">포인터 휠 이벤트 인자</param>
	private void OnPointerWheelPreview(object? sender, PointerWheelEventArgs e)
	{
		if (_book == null)
			return;

		if (_book?.ViewMode == ViewMode.Scroll)
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

	/// <summary>
	/// 포인터(마우스) 누름 프리뷰 이벤트입니다. 스크롤 모드인 경우 스크롤 드래그를 시작합니다.
	/// <param name="sender">이벤트 발신자</param>
	/// <param name="e">포인터 릴리즈 이벤트 인자</param>
	/// </summary>
	private void OnPointerPressedPreview(object? sender, PointerPressedEventArgs e)
	{
		if (_book?.ViewMode == ViewMode.Scroll)
			_scroll.TryBeginDrag(e);
	}

	/// <summary>
	/// 포인터(마우스) 릴리즈 프리뷰 이벤트입니다. 스크롤 모드에서 드래그 동작을 종료합니다.
	/// </summary>
	/// <param name="sender">이벤트 발신자</param>
	/// <param name="e">포인터 릴리즈 이벤트 인자</param>
	private void OnPointerReleasedPreview(object? sender, PointerReleasedEventArgs e)
	{
		_scroll.TryEndDrag(e);
	}

	/// <summary>
	/// 포인터 이동 프리뷰 이벤트입니다. 스크롤 모드에서 드래그 중이면 스크롤 위치를 갱신합니다.
	/// </summary>
	/// <param name="sender">이벤트 발신자</param>
	/// <param name="e">포인터 이벤트 인자</param>
	private void OnPointerMovedPreview(object? sender, PointerEventArgs e)
	{
		if (_book?.ViewMode != ViewMode.Scroll)
			return;

		if (!_scroll.TryDragging(e))
			return;

		SyncScrollCurrentPage();
		MaybeVirtualizeScroll();
	}

	/// <summary>
	/// 전역 키 다운 이벤트를 처리합니다. 페이지 이동, 줌, 파일 열기 등
	/// 다양한 단축키 동작을 중앙에서 관리합니다.
	/// </summary>
	/// <param name="sender">이벤트 발신자</param>
	/// <param name="e">키 이벤트 인자</param>
	private void OnKeyDownEvent(object? sender, KeyEventArgs e)
	{
		if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
			return;

		if (!_pressedKeys.Add(e.Key))
		{
			e.Handled = true;
			return;
		}

		var viewScroll = _book?.ViewMode == ViewMode.Scroll;

		if ((viewScroll || _zps.IsZoomed) && !_keyHoldTimer.IsEnabled)
		{
			_keyHoldTick = 0;
			_keyHoldCount = 0;
			_keyHoldTimer.Start();
		}

		if (viewScroll && _scroll.HandleKeyDown(e.Key))
		{
			RenderBook();
			e.Handled = true;
			return;
		}

		if (_zps.HandleKeyDown(e))
		{
			e.Handled = true;
			return;
		}

		var handled = true;

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
				if (viewScroll)
					break;
				if (!_zps.TryPanByKeyboard(0, -80))
					PageControl(BookControl.SeekMinusOne);
				break;

			case Key.Down:
				if (viewScroll)
					break;
				if (!_zps.TryPanByKeyboard(0, 80))
					PageControl(BookControl.SeekPlusOne);
				break;

			case Key.Left:
				if (viewScroll)
					break;
				if (!_zps.TryPanByKeyboard(-80, 0))
				{
					PageControl(e.KeyModifiers.HasFlag(KeyModifiers.Shift)
						? BookControl.SeekMinusOne
						: BookControl.Previous);
				}
				break;

			case Key.Right:
				if (viewScroll)
					break;
				if (!_zps.TryPanByKeyboard(80, 0))
				{
					PageControl(e.KeyModifiers.HasFlag(KeyModifiers.Shift)
						? BookControl.SeekPlusOne
						: BookControl.Next);
				}
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
				PageControl(BookControl.Next);
				break;

			case Key.Home:
				PageControl(BookControl.First);
				break;

			case Key.End:
				PageControl(BookControl.Last);
				break;

			case Key.PageUp:
				if (viewScroll)
				{
					var offset = new Vector(
						ReaderScrollViewer.Offset.X,
						ReaderScrollViewer.Offset.Y - ReaderScrollViewer.Viewport.Height * 0.9);
					ReaderScrollViewer.Offset = _scroll.ClampOffset(offset);
					SyncScrollCurrentPage();
					MaybeVirtualizeScroll();
				}
				else if (_zps.IsZoomed)
					PageControl(BookControl.SeekMinusOne);
				else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
					OpenPrevBook();
				else
					PageControl(BookControl.SeekPrevious10);
				break;

			case Key.PageDown:
				if (viewScroll)
				{
					var offset = new Vector(
						ReaderScrollViewer.Offset.X,
						ReaderScrollViewer.Offset.Y + ReaderScrollViewer.Viewport.Height * 0.9);
					ReaderScrollViewer.Offset = _scroll.ClampOffset(offset);
					SyncScrollCurrentPage();
					MaybeVirtualizeScroll();
				}
				else if (_zps.IsZoomed)
					PageControl(BookControl.SeekPlusOne);
				else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
					OpenNextBook();
				else
					PageControl(BookControl.SeekNext10);
				break;

			case Key.Back:
				PageControl(BookControl.SeekNext10);
				break;

			case Key.Enter:
				if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
					ToggleFullscreen();
				else
				{
					// 비동기 호출이라 앞에 대입을 넣어줘야 함
					_ = OpenPageWindowAsync();
				}
				break;

			// 보기
			case Key.F:
				ToggleFullscreen();
				break;

			case Key.Oem3: // `~ 키
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
				switch (Configs.ViewMode)
				{
					case ViewMode.LeftToRight:
						UpdateViewMode(ViewMode.RightToLeft);
						break;
					case ViewMode.RightToLeft:
						UpdateViewMode(ViewMode.LeftToRight);
						break;
				}
				break;

			// 파일이나 디렉토리
			case Key.O:
				if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
				{
					// 비동기 호출이라 앞에 대입을 넣어줘야 함
					_ = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? OpenFolderSafeAsync() : OpenBookFileSafeAsync();
				}
				else
					handled = false;
				break;

			case Key.F9:
				_ = AddBookmarkAsync();
				handled = false;
				break;

			case Key.F10:
				_ = OpenBookmarkWindowAsync();
				break;

			case Key.BrowserBack:
			case Key.OemOpenBrackets: // [ 키
				OpenPrevBook();
				break;

			case Key.BrowserForward:
			case Key.OemCloseBrackets: // ] 키
				OpenNextBook();
				break;

			case Key.OemPipe: // | 키
			case Key.OemBackslash: // \ 키
				OpenRandomBook();
				break;

			case Key.Insert:
				MoveBookAsync();
				break;

			case Key.F2:
				_ = RenameBookAsync();
				break;

			case Key.Delete:
				_ = DeleteBookOrFileAsync();
				break;

			case Key.Z:
				if (e.KeyModifiers.HasFlag(KeyModifiers.Shift | KeyModifiers.Control))
					OpenBook(Configs.LastFileName);
				break;

			// 기능
			case Key.F11:
#if DEBUG
				_ = SuppUi.OkAsync(this, "테스트 입니다", "테스트");
				Notify("알림 메시지 테스트이와요~");
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

	/// <summary>
	/// 키가 놓였을 때 호출됩니다. 내부에서 누른 키 집합을 갱신하고
	/// 모든 키가 놓였으면 키 홀드 타이머를 중지합니다.
	/// </summary>
	/// <param name="sender">이벤트 발신자</param>
	/// <param name="e">키 이벤트 인자</param>
	private void OnKeyUpEvent(object? sender, KeyEventArgs e)
	{
		_pressedKeys.Remove(e.Key);
		if (_pressedKeys.Count == 0)
			_keyHoldTimer.Stop();
	}

	/// <summary>
	/// 키를 길게 누르고 있을 때 주기적으로 실행되는 타이머 핸들러입니다.
	/// 스크롤/팬/줌 같은 연속 동작을 처리합니다.
	/// </summary>
	/// <param name="sender">이벤트 발신자</param>
	/// <param name="e">이벤트 인자</param>
	private void OnKeyHoldTick(object? sender, EventArgs e)
	{
		if (_book == null || _pressedKeys.Count == 0)
		{
			_keyHoldTimer.Stop();
			return;
		}

		_keyHoldTick++;

		if (_book?.ViewMode == ViewMode.Scroll)
		{
			if ((_pressedKeys.Contains(Key.Add) || _pressedKeys.Contains(Key.OemPlus)) && (_keyHoldTick % 3 == 0))
			{
				if (_scroll.HandleKeyDown(Key.Add))
					RenderBook();
			}
			if ((_pressedKeys.Contains(Key.Subtract) || _pressedKeys.Contains(Key.OemMinus)) && (_keyHoldTick % 3 == 0))
			{
				if (_scroll.HandleKeyDown(Key.Subtract))
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
		}
		else if (_zps.IsZoomed)
		{
			if ((_pressedKeys.Contains(Key.Add) || _pressedKeys.Contains(Key.OemPlus)) && (_keyHoldTick % 3 == 0))
				_zps.ZoomByFactor(1.05);
			if ((_pressedKeys.Contains(Key.Subtract) || _pressedKeys.Contains(Key.OemMinus)) && (_keyHoldTick % 3 == 0))
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

		_keyHoldCount++;
		if (_keyHoldCount < int.MaxValue - 1)
			return;

		// 허.. 이거 20년 넘게 틱이 돌아도 안넘어갈 숫자긴 한데 혹시 모르니 초기화
		_keyHoldCount = 0;
		Debug.WriteLine("너무 오래 키를 누르고 있었어요");
	}

	/// <summary>
	/// 페이지 선택 창을 열어서 페이지 이동을 처리합니다. 페이지 선택이 완료되면 줌을 초기화하고 해당 페이지로 이동합니다.
	/// </summary>
	private async Task OpenPageWindowAsync()
	{
		var book = _book;
		if (book == null)
			return;

		var selected = await _pageWindow.ShowAsync(this, book.CurrentPage);
		if (selected < 0 || _book != book)
			return;

		_zps.ResetZoom();
		book.MovePage(selected);
		book.PrepareImages();
		RenderBook();
		UpdateTitleText();
	}

	/// <summary>
	/// 가상 스크롤 모드에서 스크롤 위치에 따라 페이지를 미리 로드하는 작업을 실행할지 결정합니다. 과도한 호출을 방지하기 위해 쿨다운 타이머를 사용합니다.
	/// </summary>
	private void MaybeVirtualizeScroll()
	{
		if (_book?.ViewMode != ViewMode.Scroll || _virtualizeBusy || _virtualizePending)
			return;

		var now = DateTime.UtcNow;
		var elapsed = now - _lastVirtualize;
		var due = elapsed >= VirtualizeCooldown ? TimeSpan.Zero : VirtualizeCooldown - elapsed;
		_virtualizePending = true;

		DispatcherTimer.RunOnce(() =>
		{
			_virtualizePending = false;
			ExecuteVirtualizeScroll();
		}, due, DispatcherPriority.Background);
	}

	/// <summary>
	/// 가상 스크롤 모드에서 현재 스크롤 위치에 따라 페이지를 로드하고 렌더링합니다. 이미 로드된 페이지가 있거나 스크롤이 끝에 도달한 경우에는 아무 작업도 하지 않습니다.
	/// </summary>
	private void ExecuteVirtualizeScroll()
	{
		if (_book?.ViewMode != ViewMode.Scroll || _virtualizeBusy)
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
						UpdateTitleText();
						_lastVirtualize = DateTime.UtcNow;
					}
				}
				finally
				{
					_virtualizeBusy = false;
				}

				break;
		}
	}

	/// <summary>
	/// 가상 스크롤 모드에서 현재 페이지를 스크롤 위치에 맞게 동기화합니다. 스크롤 모드가 아닐 경우에는 아무 작업도 하지 않습니다.
	/// </summary>
	private void SyncScrollCurrentPage()
	{
		if (_book?.ViewMode != ViewMode.Scroll)
			return;

		var center = _scroll.GetCenterPageIndex(_book.CurrentPage);
		if (!_book.MovePage(center))
			return;

		UpdateTitleText();
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
			// 굳이 CloseBook까지 호출할 필요는 없고, 현재 페이지 저장 정도만 해주면 될 것 같음
			if (_book != null)
			{
				Configs.SetHistory(_book.FileName, _book.CurrentPage);
				_book.Dispose();
				_book = null;
			}

			ResetPages();
			_pageWindow.DisposeDialog();
			_animationTimer.Stop();
			_keyHoldTimer.Stop();
			_notifyTimer.Stop();
			_animationTimer.Tick -= OnAnimationTick;
			_keyHoldTimer.Tick -= OnKeyHoldTick;
			_notifyTimer.Tick -= OnNotifyTick;
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
	/// 창이 비활성화될 때
	/// </summary>
	private void OnDeactivated(object? sender, EventArgs e)
	{
		// 키보드 상태 초기화 (키가 눌린 채로 창 전환 시 발생할 수 있는 문제 방지)
		_pressedKeys.Clear();
	}

	/// <summary>
	/// 현재 파일 이름 기반으로 타이틀을 갱신합니다.
	/// </summary>
	private void UpdateTitleText()
	{
		var appTitle = T("DgRead");
		var title = _book != null
			? $"[{_book.CurrentPage + 1}/{_book.TotalPage}] {_book.DisplayName}"
			: T("[No book opened]");
		TitleTextBlock.Text = title;
		Title = $"{title} - {appTitle}";
	}

	/// <summary>
	/// 창 상태에 따라 타이틀바 표시 및 버튼 아이콘을 갱신합니다.
	/// </summary>
	private void UpdateTitleState()
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
		if (WindowState != WindowState.Normal || !CanResize)
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

	/// <summary>
	/// 현재 책/모드 상태를 화면에 렌더링합니다.
	/// </summary>
	private void RenderBook()
	{
		var book = _book;
		var viewScroll = book?.ViewMode == ViewMode.Scroll;

		if (viewScroll)
			_scroll.CaptureAnchorByCenter();

		ResetPages();

		if (book == null)
		{
			_zps.ResetZoom();
			_zps.SetViewTwoPage(false);
			LogoImage.IsVisible = true;
			SpreadPanel.IsVisible = false;
			ScrollPagesPanel.IsVisible = false;
			ReaderScrollViewer.Offset = default;
			return;
		}

		var leftPage = book.PageLeft;
		var rightPage = book.PageRight;
		var singlePage = leftPage ?? rightPage;
		var viewTwoPage =
			book.ViewMode is ViewMode.LeftToRight or ViewMode.RightToLeft &&
			leftPage != null && rightPage != null &&
			!leftPage.HasAnimation && !rightPage.HasAnimation;

		_zps.SetViewTwoPage(viewTwoPage);
		_zps.ApplyLayout();

		LogoImage.IsVisible = false;

		if (viewScroll && book.SupportsMultiPages)
		{
			SpreadPanel.IsVisible = false;
			ScrollPagesPanel.IsVisible = true;
			_scroll.RenderWindow(book, _scrollPageCache, (page, image) => _animations.Add(new AnimationBind(page, image)));
			_scroll.RestoreAnchorByCenter();
		}
		else
		{
			SpreadPanel.IsVisible = true;
			ScrollPagesPanel.IsVisible = false;

			if (viewTwoPage)
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
					_animations.Add(new AnimationBind(singlePage, LeftPageImage));
			}
		}

		if (!viewScroll && !_zps.IsZoomed)
			ReaderScrollViewer.Offset = default;

		if (_animations.Count > 0)
		{
			_lastAnimationTick = DateTime.UtcNow;
			_animationTimer.Start();
		}
	}

	/// <summary>
	/// 애니메이션 프레임 타이머 핸들러입니다. 애니메이션이 포함된 페이지의
	/// 다음 프레임을 계산하고 이미지 소스를 갱신합니다.
	/// </summary>
	/// <param name="sender">이벤트 발신자</param>
	/// <param name="e">이벤트 인자</param>
	private void OnAnimationTick(object? sender, EventArgs e)
	{
		if (_animations.Count == 0)
		{
			_animationTimer.Stop();
			return;
		}

		var now = DateTime.UtcNow;
		var elapsed = (int)Math.Max(1, (now - _lastAnimationTick).TotalMilliseconds);
		_lastAnimationTick = now;

		foreach (var anim in _animations)
		{
			anim.Remaining -= elapsed;
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

	/// <summary>
	/// 페이지 이미지와 애니메이션 상태를 초기화하고 화면에서 페이지를 제거합니다. 책을 닫거나 새로 렌더링할 때 호출됩니다.
	/// </summary>
	private void ResetPages()
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
		var book = _book;

		if (book == null)
			return;

		var moved = control switch
		{
			BookControl.Previous => book.MovePrev(),
			BookControl.Next => book.MoveNext(),
			BookControl.First => book.MovePage(0),
			BookControl.Last => book.MovePage(book.TotalPage - 1),
			BookControl.SeekPrevious10 => book.MovePage(book.CurrentPage - 10),
			BookControl.SeekNext10 => book.MovePage(book.CurrentPage + 10),
			BookControl.SeekMinusOne => book.MovePage(book.CurrentPage - 1),
			BookControl.SeekPlusOne => book.MovePage(book.CurrentPage + 1),
			BookControl.Select => book.MovePage(book.CurrentPage),
			_ => false
		};

		if (!moved && control != BookControl.Select)
			return;

		_zps.ResetZoom();
		book.PrepareImages();
		RenderBook();
		UpdateTitleText();
	}

	/// <summary>
	/// 이전 책을 엽니다
	/// </summary>
	private void OpenPrevBook()
	{
		if (_book == null)
			return;

		var next = _book.FindNextFile(BookDirection.Previous);
		OpenBook(next);
	}

	/// <summary>
	/// 다음 책을 엽니다
	/// </summary>
	private void OpenNextBook()
	{
		if (_book == null)
			return;

		var next = _book.FindNextFile(BookDirection.Next);
		OpenBook(next);
	}

	/// <summary>
	/// 무작위 책을 엽니다
	/// </summary>
	private void OpenRandomBook()
	{
		if (_book == null)
			return;

		var next = _book.FindRandomFile();
		OpenBook(next);
	}

	/// <summary>
	/// 현재 책을 옮깁니다
	/// </summary>
	private async void MoveBookAsync()
	{
		try
		{
			_pressedKeys.Clear();
			_keyHoldTimer.Stop();

			var dlg = new MoveDialog();
			var dest = await dlg.ShowAsync(this, _book?.FileName);
			if (_book == null || string.IsNullOrWhiteSpace(dest))
				return;

			var next = _book.FindNextFile(BookDirection.Next);
			if (!_book.MoveFile(dest))
			{
				Notify(T("Failed to move book/file"), 5000);
				return;
			}

			CloseBook();
			OpenBook(next);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"책 이동 실패: {ex.Message}");
			Notify(T("Failed to move book/file"), 5000);
		}
	}

	/// <summary>
	/// 현재 책 또는 현재 페이지 파일의 이름을 바꿉니다.
	/// </summary>
	private async Task RenameBookAsync()
	{
		try
		{
			var book = _book;
			if (book == null)
				return;

			_pressedKeys.Clear();
			_keyHoldTimer.Stop();

			var dlg = new RenExWindow();
			var res = await dlg.ShowAsync(this, book.FileName);
			if (res == null)
				return;

			var dest = res.FileName.Trim();
			if (string.IsNullOrWhiteSpace(dest) || dest.Equals(book.FileName, StringComparison.Ordinal))
				return;

			var next = book.FindNextFile(BookDirection.Next);
			if (!book.RenameFile(dest, out var newName))
			{
				Notify(T("Failed to rename book"), 5000);
				return;
			}

			if (book is BookFolder)
			{
				_pageWindow.SetBook(book);
				_zps.ResetZoom();
				book.PrepareImages();
				RenderBook();
				UpdateTitleText();
				Configs.LastFileName = newName;
				return;
			}

			CloseBook();
			if (res.Reopen || string.IsNullOrWhiteSpace(next))
				OpenBook(newName);
			else
				OpenBook(next);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"책 이름 변경 실패: {ex.Message}");
			Notify(T("Failed to rename book"), 5000);
		}
	}

	/// <summary>
	/// 현재 책 또는 페이지의 파일을 삭제합니다.
	/// </summary>
	private async Task DeleteBookOrFileAsync()
	{
		try
		{
			var book = _book;
			if (book == null)
				return;

			var nextBook = book.FindNextFile(BookDirection.Next);

			if (!book.CanDeleteFile(out var reason))
			{
				if (!string.IsNullOrWhiteSpace(reason))
					Notify(reason);
				return;
			}

			if (Configs.FileConfirmDelete)
			{
				var fileName = book is BookZip ? book.DisplayName : book.GetEntryName(book.CurrentPage) ?? book.DisplayName;
				var ok = await SuppUi.YesNoAsync(this, $"{fileName}{Environment.NewLine}{Environment.NewLine}{T("Delete this file?")}", T("Confirm"));
				if (!ok)
					return;
			}

			if (!book.DeleteFile(out var closeBook))
			{
				Notify(T("Failed to delete file"), 5000);
				return;
			}

			if (closeBook)
			{
				CloseBook();
				OpenBook(nextBook);
				return;
			}

			_pageWindow.SetBook(book);
			_zps.ResetZoom();
			book.PrepareImages();
			RenderBook();
			UpdateTitleText();
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"파일 삭제 실패: {ex.Message}");
			Notify(T("Failed to delete file"), 5000);
		}
	}

	/// <summary>
	/// 보기 모드를 변경하고 UI/설정을 동기화합니다.
	/// </summary>
	private void UpdateViewMode(ViewMode mode)
	{
		if (_book is { SupportsMultiPages: false } && mode != ViewMode.Single)
			mode = ViewMode.Single;

		Configs.ViewMode = mode;
		if (_book != null)
		{
			_book.ViewMode = mode;
			_book.PrepareImages();
			RenderBook();
			UpdateTitleText();
		}

		SetSingleChecked(mode switch
		{
			ViewMode.LeftToRight => LeftToRightMenuItem,
			ViewMode.RightToLeft => RightToLeftMenuItem,
			ViewMode.Scroll => ScrollModeMenuItem,
			_ => FitToScreenMenuItem
		}, FitToScreenMenuItem, LeftToRightMenuItem, RightToLeftMenuItem, ScrollModeMenuItem);

		SetViewModeIcon(mode);
	}

	/// <summary>
	/// 보기 모드 아이콘을 현재 보기 모드에 맞게 변경합니다. 리소스에서 아이콘을 찾아 메뉴 버튼에 적용합니다.
	/// </summary>
	/// <param name="mode">아이콘으로 설정할 보기 모드</param>
	private void SetViewModeIcon(ViewMode mode)
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

	// 알림 메시지 콜백 
	private void OnNotifyTick(object? sender, EventArgs e)
	{
		_notifyTimer.Stop();
		NotifyBorder.IsVisible = false;
	}

	/// <summary>
	/// 알림 메시지를 출력합니다.
	/// </summary>
	/// <param name="message">출력할 메시지</param>
	/// <param name="duration">메시지를 표시할 시간(밀리초)</param>
	private void Notify(string message, int duration = 3000)
	{
		NotifyTextBlock.Text = message;

		if (!NotifyBorder.IsVisible)
			NotifyBorder.IsVisible = true;

		_notifyTimer.Stop();
		_notifyTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, duration));
		_notifyTimer.Start();
	}

	private void ResetNotify()
	{
		NotifyTextBlock.Text = string.Empty;
		NotifyBorder.IsVisible = false;

		_notifyTimer.Stop();
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
		ApplyTheme();
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
			Debug.WriteLine($"책 열기 메뉴 실패: {ex.Message}");
			Notify(T("Failed to open book/file"), 5000);
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
			Debug.WriteLine($"폴더 열기 메뉴 실패: {ex.Message}");
			Notify(T("Failed to open folder"), 5000);
		}
	}

	/// <summary>
	/// 닫기 메뉴 클릭을 처리합니다.
	/// </summary>
	private void OnCloseBookClick(object? sender, RoutedEventArgs e) =>
		CloseBook();

	/// <summary>
	/// 책갈피 추가 메뉴 클릭을 처리합니다.
	/// </summary>
	private async void OnAddBookmarkClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			await AddBookmarkAsync();
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"책갈피 추가 실패: {ex.Message}");
			Notify(T("Failed to add bookmark"));
		}
	}

	/// <summary>
	/// 책갈피 관리 메뉴 클릭을 처리합니다.
	/// </summary>
	private async void OnManageBookmarksClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			await OpenBookmarkWindowAsync();
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"책갈피 관리 창 열기 실패: {ex.Message}");
			Notify(T("Failed to open bookmark manager"));
		}
	}

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
			Debug.WriteLine($"책/파일 열기 메뉴 실패: {ex.Message}");
			Notify($"{T("Failed to open book/file")}{Environment.NewLine}{ex.Message}", 5000);
		}
	}

	/// <summary>
	/// 현재 페이지를 책갈피로 추가하거나, 이미 존재하면 삭제 여부를 확인합니다.
	/// </summary>
	private async Task AddBookmarkAsync()
	{
		var book = _book;
		if (book == null)
			return;

		var path = book.FullName;
		var page = book.CurrentPage;

		if (Configs.TryGetBookmark(path, page, out var exists) && exists != null)
		{
			var remove = await SuppUi.YesNoAsync(this,
				$"{book.DisplayName} ({page + 1}){Environment.NewLine}{Environment.NewLine}{T("Bookmark already exists. Delete it?")}",
				T("Confirm"));
			if (!remove)
				return;

			if (Configs.RemoveBookmark(exists.Id))
				Notify(T("Bookmark removed"), 1000);
			else
				Notify(T("Failed to delete bookmark"));

			return;
		}

		if (Configs.AddBookmark(path, page, out _))
			Notify(T("Bookmark added"), 1000);
		else
			Notify(T("Failed to add bookmark"));
	}

	/// <summary>
	/// 책갈피 관리 창을 열고 선택된 책갈피 위치로 이동합니다.
	/// </summary>
	private async Task OpenBookmarkWindowAsync()
	{
		var dlg = new BookmarkWindow();
		var selected = await dlg.ShowAsync(this);
		if (selected == null)
			return;

		if (_book == null || !_book.FullName.Equals(selected.Path, StringComparison.OrdinalIgnoreCase))
			OpenBook(selected.Path);

		var book = _book;
		if (book == null || !book.FullName.Equals(selected.Path, StringComparison.OrdinalIgnoreCase))
			return;

		_zps.ResetZoom();
		book.MovePage(selected.Page);
		book.PrepareImages();
		RenderBook();
		UpdateTitleText();
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
			Debug.WriteLine($"폴더 열기 메뉴 실패: {ex.Message}");
			Notify(T("Failed to open folder"), 5000);
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

		if (files.Count > 0)
			OpenBook(files[0].TryGetLocalPath());
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

		if (folders.Count > 0)
			OpenBook(folders[0].TryGetLocalPath());
	}

	/// <summary>
	/// 현재 열린 책 상태를 해제합니다.
	/// </summary>
	private void CloseBook()
	{
		if (_book != null)
		{
			Configs.SetHistory(_book.FileName, _book.CurrentPage);
			_book.Dispose();
			_book = null;
		}

		ResetNotify();
		_pageWindow.ResetBook();
		UpdateTitleText();
		SetViewModeIcon(ViewMode.Single);
		RenderBook();
	}

	/// <summary>
	/// 파일 또는 폴더 경로를 감지하여 책을 엽니다. 지원되지 않는 형식이거나 열기에 실패한 경우에는 null을 반환합니다.
	/// </summary>
	private static BookBase? DetectAndOpenBook(string path)
	{
		if (Directory.Exists(path))
			return new BookFolder(path);

		if (!File.Exists(path))
			return null;

		var ext = Path.GetExtension(path);
		if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".cbz", StringComparison.OrdinalIgnoreCase))
			return new BookZip(path);

		if (PageDecoder.IsSupported(path))
			return new BookFolder(path);

		return null;
	}

	/// <summary>
	/// 경로로부터 책을 열고 초기 이미지를 준비합니다.
	/// 파일이든 디렉토리든 일단 엽니다.
	/// </summary>
	/// <param name="path">열고자 하는 책의 경로</param>
	private void OpenBook([NotNullWhen(true)] string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return;

		try
		{
			var book = DetectAndOpenBook(path);
			if (book == null)
			{
				Notify(T("Unsupported book format"), 5000);
				return;
			}

			book.ViewMode = book.SupportsMultiPages ? Configs.ViewMode : ViewMode.Single;
			book.MovePage(Configs.GetHistory(book.FileName)); // 최근 파일은 파일 이름만 조회한다
			book.PrepareImages();

			if (_book != null)
			{
				Configs.SetHistory(_book.FileName, _book.CurrentPage);
				_book.Dispose();
				ResetNotify();
			}
			_book = book;

			_zps.ResetZoom();
			_pageWindow.SetBook(book);

			UpdateTitleText();
			RenderBook();

			if (!book.SupportsMultiPages)
				UpdateViewMode(ViewMode.Single);

			SetViewModeIcon(Configs.ViewMode);

			Configs.LastFileName = path; // 여기서는 전체 경로를 저장해야 한다.
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"경로로 책/파일 열기 실패: {ex.Message}");
			Notify(T("Failed to open book/file"), 5000);
		}
	}

	/// <summary>
	/// 페이지의 애니메이션 상태를 관리하는 클래스입니다. 페이지 이미지와 해당 페이지가 남은 프레임 시간을 추적하여 애니메이션 타이머에서 업데이트할 때 다음 프레임으로 넘어갈지 결정합니다.
	/// </summary>
	private sealed class AnimationBind
	{
		public AnimationBind(PageImage page, Image target)
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
