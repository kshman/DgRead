using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DgRead.Dowa;

/// <summary>
/// Reader 화면의 Zoom/Pan/Scroll(ZPS) 입력과 레이아웃을 관리합니다.
/// </summary>
internal sealed class ZpsController
{
	private readonly ScrollViewer _viewer;
	private readonly Image _leftImage;
	private readonly Image _rightImage;
	private bool _twoPageMode;
	private bool _zoomModeActive;

	private bool _isPanning;
	private bool _isRightMouseDown;
	private Point _panStartPoint;
	private Vector _panStartOffset;

	public double ZoomRatio { get; private set; } = 1.0;
	public bool IsZoomed => _zoomModeActive;

	public ZpsController(ScrollViewer viewer, Image leftImage, Image rightImage)
	{
		_viewer = viewer;
		_leftImage = leftImage;
		_rightImage = rightImage;
	}

	public void Attach()
	{
		_viewer.SizeChanged += (_, _) => ApplyLayout();
		_viewer.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, true);
		_viewer.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, true);
		_viewer.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, true);
	}

	public void SetTwoPageMode(bool value)
	{
		_twoPageMode = value;
		ApplyLayout();
	}

	public bool HandleZoomHotkeys(KeyEventArgs e)
	{
		if (e.Key is Key.Add or Key.OemPlus)
		{
			_zoomModeActive = true;
			SetZoom(ZoomRatio * 1.1);
			return true;
		}

		if (e.Key is Key.Subtract or Key.OemMinus)
		{
			_zoomModeActive = true;
			SetZoom(ZoomRatio / 1.1);
			return true;
		}

		if (IsZoomed && e.Key is Key.Divide or Key.Oem2)
		{
			SetZoom(1.0, fitToViewport: true);
			_zoomModeActive = false;
			return true;
		}

		if (IsZoomed && e.Key == Key.D0)
		{
			SetZoom(1.0, fitToViewport: false);
			return true;
		}

		return false;
	}

	public bool TryPanByKeyboard(double dx, double dy)
	{
		if (!IsZoomed)
			return false;

		var next = _viewer.Offset + new Vector(dx, dy);
		_viewer.Offset = ClampOffset(next);
		return true;
	}

	public void ApplyLayout(bool fitToViewport = true)
	{
		if (_viewer.Bounds.Width <= 0 || _viewer.Bounds.Height <= 0)
			return;

		if (fitToViewport && !IsZoomed)
			_viewer.Offset = default;

		var viewW = Math.Max(100, _viewer.Bounds.Width - 8);
		var viewH = Math.Max(100, _viewer.Bounds.Height - 8);
		var scale = IsZoomed ? ZoomRatio : 1.0;

		if (_twoPageMode)
		{
			var eachWidth = Math.Max(50, (viewW - 6) / 2d) * scale;
			_leftImage.MaxWidth = eachWidth;
			_rightImage.MaxWidth = eachWidth;
		}
		else
		{
			var oneWidth = viewW * scale;
			_leftImage.MaxWidth = oneWidth;
			_rightImage.MaxWidth = oneWidth;
		}

		var maxHeight = viewH * scale;
		_leftImage.MaxHeight = maxHeight;
		_rightImage.MaxHeight = maxHeight;
	}

	public void ResetZoom()
	{
		_zoomModeActive = false;
		ZoomRatio = 1.0;
		_viewer.Offset = default;
		ApplyLayout();
	}

	public void SetZoom(double value, bool fitToViewport = true)
	{
		ZoomRatio = Math.Clamp(value, 0.25, 8.0);
		ApplyLayout(fitToViewport);
	}

	public bool HandleWheelAsZoom(PointerWheelEventArgs e)
	{
		var zoomWheel = IsZoomed || e.KeyModifiers.HasFlag(KeyModifiers.Shift) || _isRightMouseDown;
		if (!zoomWheel)
			return false;

		_zoomModeActive = true;
		var factor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
		SetZoom(ZoomRatio * factor);
		e.Handled = true;
		return true;
	}

	private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		var point = e.GetCurrentPoint(_viewer);
		if (point.Properties.IsRightButtonPressed)
			_isRightMouseDown = true;

		if (!IsZoomed || !point.Properties.IsLeftButtonPressed)
			return;

		_isPanning = true;
		_panStartPoint = point.Position;
		_panStartOffset = _viewer.Offset;
		e.Pointer.Capture(_viewer);
		e.Handled = true;
	}

	private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		_isRightMouseDown = false;
		if (!_isPanning)
			return;

		_isPanning = false;
		e.Pointer.Capture(null);
		e.Handled = true;
	}

	private void OnPointerMoved(object? sender, PointerEventArgs e)
	{
		if (!_isPanning)
			return;

		var point = e.GetCurrentPoint(_viewer).Position;
		var delta = point - _panStartPoint;
		var next = _panStartOffset - new Vector(delta.X, delta.Y);
		_viewer.Offset = ClampOffset(next);
		e.Handled = true;
	}

	private Vector ClampOffset(Vector offset)
	{
		var maxX = Math.Max(0, _viewer.Extent.Width - _viewer.Viewport.Width);
		var maxY = Math.Max(0, _viewer.Extent.Height - _viewer.Viewport.Height);
		return new Vector(Math.Clamp(offset.X, 0, maxX), Math.Clamp(offset.Y, 0, maxY));
	}
}
