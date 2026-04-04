using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace DgRead.Dowa;

/// <summary>
/// 스크롤 모드 전용 입력/가상화/너비 조절을 관리합니다.
/// </summary>
internal sealed class ScrollModeController
{
	public const int MinWidthPercent = 50;
	public const int MaxWidthPercent = 100;
	public const int WidthStepPercent = 5;
	public const int PreloadRadius = 2;

	private readonly ScrollViewer _viewer;
	private readonly StackPanel _panel;

	private bool _isDragging;
	private Point _dragStart;
	private Vector _offsetStart;
	private double _verticalAnchor;
	private int _anchorPage = -1;
	private double _anchorWithin;

	public int WidthPercent { get; private set; } = 80;

	public ScrollModeController(ScrollViewer viewer, StackPanel panel)
	{
		_viewer = viewer;
		_panel = panel;
	}

	public bool TryHandleWidthKey(Key key)
	{
		if (key is Key.Add or Key.OemPlus)
			return AdjustWidth(+WidthStepPercent);

		if (key is Key.Subtract or Key.OemMinus)
			return AdjustWidth(-WidthStepPercent);

		return false;
	}

	public void CaptureAnchorByCenter()
	{
		var scrollable = Math.Max(1.0, _viewer.Extent.Height - _viewer.Viewport.Height);
		_verticalAnchor = _viewer.Offset.Y / scrollable;

		_anchorPage = -1;
		_anchorWithin = 0;
		if (_panel.Children.Count == 0)
			return;

		var centerY = _viewer.Offset.Y + _viewer.Viewport.Height * 0.5;
		var spacing = _panel.Spacing;
		var y = 0.0;
		foreach (var child in _panel.Children)
		{
			if (child is not Image image || image.Tag is not int page)
				continue;

			var h = Math.Max(1.0, image.Bounds.Height);
			var end = y + h;
			if (centerY >= y && centerY <= end)
			{
				_anchorPage = page;
				_anchorWithin = centerY - y;
				return;
			}

			y = end + spacing;
		}
	}

	public int GetCenterPageIndex(int fallback)
	{
		if (_panel.Children.Count == 0)
			return fallback;

		var centerY = _viewer.Offset.Y + _viewer.Viewport.Height * 0.5;
		var spacing = _panel.Spacing;
		var y = 0.0;
		var nearest = fallback;
		var nearestDist = double.MaxValue;

		foreach (var child in _panel.Children)
		{
			if (child is not Image image || image.Tag is not int page)
				continue;

			var h = Math.Max(1.0, image.Bounds.Height);
			var mid = y + h * 0.5;
			var dist = Math.Abs(centerY - mid);
			if (dist < nearestDist)
			{
				nearestDist = dist;
				nearest = page;
			}

			if (centerY >= y && centerY <= y + h)
				return page;

			y += h + spacing;
		}

		return nearest;
	}

	public void RestoreAnchorByCenter()
	{
		Dispatcher.UIThread.Post(() =>
		{
			if (_anchorPage >= 0)
			{
				var spacing = _panel.Spacing;
				var y = 0.0;
				Image? best = null;
				var bestDistance = int.MaxValue;

				foreach (var child in _panel.Children)
				{
					if (child is not Image image || image.Tag is not int page)
						continue;

					var distance = Math.Abs(page - _anchorPage);
					if (distance < bestDistance)
					{
						best = image;
						bestDistance = distance;
					}

					if (page == _anchorPage)
					{
						var h = Math.Max(1.0, image.Bounds.Height);
						var center = y + Math.Min(_anchorWithin, h);
						var target = center - _viewer.Viewport.Height * 0.5;
						_viewer.Offset = ClampOffset(new Vector(_viewer.Offset.X, target));
						return;
					}

					y += Math.Max(1.0, image.Bounds.Height) + spacing;
				}

				if (best != null)
				{
					y = 0;
					foreach (var child in _panel.Children)
					{
						if (!ReferenceEquals(child, best))
						{
							y += Math.Max(1.0, child.Bounds.Height) + spacing;
							continue;
						}

						var h = Math.Max(1.0, best.Bounds.Height);
						var center = y + Math.Min(_anchorWithin, h);
						var target = center - _viewer.Viewport.Height * 0.5;
						_viewer.Offset = ClampOffset(new Vector(_viewer.Offset.X, target));
						return;
					}
				}
			}

			var scrollable = Math.Max(0.0, _viewer.Extent.Height - _viewer.Viewport.Height);
			var targetY = scrollable * _verticalAnchor;
			_viewer.Offset = ClampOffset(new Vector(_viewer.Offset.X, targetY));
		}, DispatcherPriority.Render);
	}

 public void RenderWindow(BookBase book, Dictionary<int, PageImage> pageCache, Action<PageImage, Image> registerAnimation)
	{
		var first = Math.Max(0, book.CurrentPage - PreloadRadius);
		var last = Math.Min(book.TotalPage - 1, book.CurrentPage + PreloadRadius);
		var viewWidth = Math.Max(400, _viewer.Bounds.Width - 10);
		var targetWidth = viewWidth * (WidthPercent / 100.0);

		for (var i = first; i <= last; i++)
		{
           if (!pageCache.TryGetValue(i, out var page))
			{
				page = book.GetPageImage(i);
				pageCache[i] = page;
			}

			var image = new Image
			{
				Source = page.GetBitmap(),
				Stretch = Avalonia.Media.Stretch.Uniform,
				HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
				MaxWidth = targetWidth,
				MaxHeight = 10000,
				Tag = i
			};
			_panel.Children.Add(image);

			if (page.HasAnimation)
				registerAnimation(page, image);
		}

		if (pageCache.Count == 0)
			return;

		var removeKeys = new List<int>();
		foreach (var key in pageCache.Keys)
		{
			if (key < first || key > last)
				removeKeys.Add(key);
		}

		foreach (var key in removeKeys)
           pageCache.Remove(key);
	}

	public bool TryBeginDrag(PointerPressedEventArgs e)
	{
		var point = e.GetCurrentPoint(_viewer);
		if (!point.Properties.IsLeftButtonPressed)
			return false;

		_isDragging = true;
		_dragStart = point.Position;
		_offsetStart = _viewer.Offset;
		e.Pointer.Capture(_viewer);
		e.Handled = true;
		return true;
	}

	public bool TryEndDrag(PointerReleasedEventArgs e)
	{
		if (!_isDragging)
			return false;

		_isDragging = false;
		e.Pointer.Capture(null);
		e.Handled = true;
		return true;
	}

	public bool TryDrag(PointerEventArgs e)
	{
		if (!_isDragging)
			return false;

		var point = e.GetCurrentPoint(_viewer).Position;
		var delta = point - _dragStart;
		var target = _offsetStart - new Vector(delta.X, delta.Y);
		_viewer.Offset = ClampOffset(target);
		e.Handled = true;
		return true;
	}

	public int GetVirtualizeDirection()
	{
		if (_viewer.Extent.Height <= _viewer.Viewport.Height)
			return 0;

		var nearTop = _viewer.Offset.Y <= 80;
		var nearBottom = _viewer.Offset.Y + _viewer.Viewport.Height >= _viewer.Extent.Height - 80;
		if (nearBottom)
			return +1;
		if (nearTop)
			return -1;
		return 0;
	}

	public Vector ClampOffset(Vector offset)
	{
		var maxX = Math.Max(0, _viewer.Extent.Width - _viewer.Viewport.Width);
		var maxY = Math.Max(0, _viewer.Extent.Height - _viewer.Viewport.Height);
		return new Vector(Math.Clamp(offset.X, 0, maxX), Math.Clamp(offset.Y, 0, maxY));
	}

	private bool AdjustWidth(int deltaPercent)
	{
		var before = WidthPercent;
		WidthPercent = Math.Clamp(WidthPercent + deltaPercent, MinWidthPercent, MaxWidthPercent);
		return before != WidthPercent;
	}
}
