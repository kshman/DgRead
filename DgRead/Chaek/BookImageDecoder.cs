using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DgRead.Chaek;

/// <summary>
/// 이미지 데이터 디코딩 도우미입니다.
/// </summary>
internal static class BookImageDecoder
{
	private static readonly HashSet<string> sSupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".bmp", ".jpg", ".jpeg", ".png", ".gif", ".webp"
	};

	private static readonly byte[] sFallbackPng =
		Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8Xw8AAoMBgA2MvmUAAAAASUVORK5CYII=");

	/// <summary>
	/// 지원하는 확장자인지 확인합니다.
	/// </summary>
	public static bool IsSupported(string fileName)
	{
		var ext = Path.GetExtension(fileName);
		return !string.IsNullOrWhiteSpace(ext) && sSupportedExtensions.Contains(ext);
	}

	/// <summary>
	/// 바이트 데이터를 <see cref="PageImage"/>로 디코딩합니다.
	/// </summary>
	public static PageImage Decode(byte[] raw)
	{
		try
		{
			using var image = Image.Load<Rgba32>(raw);
			var formatName = image.Metadata.DecodedImageFormat?.Name ?? string.Empty;
			if (image.Frames.Count > 1 && string.Equals(formatName, "GIF", StringComparison.OrdinalIgnoreCase))
				return DecodeGifAnimation(image);
			if (image.Frames.Count > 1 && string.Equals(formatName, "WEBP", StringComparison.OrdinalIgnoreCase))
				return DecodeWebpAnimation(image);

			return new PageImage(ToBitmap(image));
		}
		catch (Exception e)
		{
			Debug.WriteLine($"Decode failed: {e.Message}");
			return new PageImage(CreateFallbackBitmap());
		}
	}

	private static PageImage DecodeGifAnimation(Image<Rgba32> image)
	{
		var frames = new List<AnimatedFrame>(image.Frames.Count);
		for (var i = 0; i < image.Frames.Count; i++)
		{
			using var frameImage = image.Clone();
			for (var remove = frameImage.Frames.Count - 1; remove >= 0; remove--)
			{
				if (remove != i)
					frameImage.Frames.RemoveFrame(remove);
			}

			var delay = image.Frames[i].Metadata.GetGifMetadata().FrameDelay * 10;
			if (delay <= 0)
				delay = 100;
			frames.Add(new AnimatedFrame(ToBitmap(frameImage), delay));
		}

		return new PageImage(frames);
	}

	private static PageImage DecodeWebpAnimation(Image<Rgba32> image)
	{
		var frames = new List<AnimatedFrame>(image.Frames.Count);
		for (var i = 0; i < image.Frames.Count; i++)
		{
			using var frameImage = image.Clone();
			for (var remove = frameImage.Frames.Count - 1; remove >= 0; remove--)
			{
				if (remove != i)
					frameImage.Frames.RemoveFrame(remove);
			}

			frames.Add(new AnimatedFrame(ToBitmap(frameImage), ReadWebpFrameDelay(image, i)));
		}

		return new PageImage(frames);
	}

	private static int ReadWebpFrameDelay(Image<Rgba32> image, int frameIndex)
	{
		try
		{
			var metadata = image.Frames[frameIndex].Metadata.GetFormatMetadata(SixLabors.ImageSharp.Formats.Webp.WebpFormat.Instance);
			var prop = metadata?.GetType().GetProperty("FrameDelay");
			if (prop?.GetValue(metadata) is int delay && delay > 0)
				return delay;
		}
		catch
		{
			// 라이브러리 버전별 메타데이터 차이를 허용한다.
		}

		return 100;
	}

	private static Bitmap ToBitmap(Image<Rgba32> image)
	{
		using var ms = new MemoryStream();
		image.SaveAsPng(ms);
		ms.Position = 0;
		return new Bitmap(ms);
	}

	private static Bitmap CreateFallbackBitmap() =>
		new Bitmap(new MemoryStream(sFallbackPng, writable: false));
}
