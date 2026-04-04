using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;

namespace DgRead.Chaek;

/// <summary>
/// 단일 이미지 또는 애니메이션 프레임 집합을 나타냅니다.
/// </summary>
public sealed class PageImage : IDisposable
{
	/// <summary>
	/// 기준 비트맵입니다. 애니메이션이 아닌 경우 표시 비트맵 자체입니다.
	/// </summary>
	public Bitmap Bitmap { get; }

	/// <summary>
	/// 애니메이션 프레임 목록입니다. 정적 이미지면 <see langword="null"/> 입니다.
	/// </summary>
	public IReadOnlyList<AnimatedFrame>? Frames { get; }

	/// <summary>
	/// 현재 프레임 인덱스입니다.
	/// </summary>
	public int CurrentFrame { get; private set; }

	/// <summary>
	/// 애니메이션 여부를 반환합니다.
	/// </summary>
	public bool HasAnimation => Frames is { Count: > 1 };

	/// <summary>
	/// 정적 이미지를 생성합니다.
	/// </summary>
	public PageImage(Bitmap bitmap)
	{
		Bitmap = bitmap;
	}

	/// <summary>
	/// 애니메이션 이미지를 생성합니다.
	/// </summary>
	public PageImage(List<AnimatedFrame> frames)
	{
		if (frames.Count == 0)
			throw new ArgumentException("frames is empty", nameof(frames));

		Frames = frames;
		Bitmap = frames[0].Bitmap;
	}

	/// <summary>
	/// 현재 프레임 비트맵을 가져옵니다.
	/// </summary>
	public Bitmap GetBitmap() => Frames?[CurrentFrame].Bitmap ?? Bitmap;

	/// <summary>
	/// 다음 프레임으로 이동하고 해당 프레임의 표시 시간을 반환합니다.
	/// </summary>
	/// <returns>프레임 지연시간(밀리초), 애니메이션이 아니면 -1</returns>
	public int Animate()
	{
		if (Frames is not { Count: > 0 })
			return -1;

		CurrentFrame++;
		if (CurrentFrame >= Frames.Count)
			CurrentFrame = 0;

		return Frames[CurrentFrame].Duration;
	}

	/// <summary>
	/// 애니메이션 인덱스를 초기화합니다.
	/// </summary>
	public void ResetAnimation() =>
		CurrentFrame = 0;

	/// <inheritdoc />
	public void Dispose()
	{
		if (Frames != null)
		{
			foreach (var frame in Frames)
				frame.Bitmap.Dispose();
		}
		else
		{
			Bitmap.Dispose();
		}
	}
}
