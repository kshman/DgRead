using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace DgRead.Chaek;

/// <summary>
/// 이미지 데이터 디코딩 도우미입니다.
/// </summary>
internal static class BookImageDecoder
{
	private enum DetectedType
	{
		Unknown,
		Bmp,
		Jpeg,
		Png,
		Gif,
		Webp,
	}

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
			if (!TryDetectImageType(raw, out var type, out var hasAnimation) || !hasAnimation)
				return DecodeStatic(raw);

			using var image = Image.Load<Rgba32>(raw);
			if (image.Frames.Count <= 1)
				return DecodeStatic(raw);

			return type switch
			{
				DetectedType.Gif => DecodeGifAnimation(image),
				DetectedType.Webp => DecodeWebpAnimation(image),
				_ => DecodeStatic(raw)
			};
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
			var prop = metadata.GetType().GetProperty("FrameDelay");
			if (prop?.GetValue(metadata) is int delay and > 0)
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
		var size = new PixelSize(image.Width, image.Height);
		var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);

		var pixelBytes = new byte[image.Width * image.Height * 4];
		image.CopyPixelDataTo(pixelBytes);

		using var locked = bitmap.Lock();
		var rowBytes = image.Width * 4;
		if (locked.RowBytes == rowBytes)
		{
			Marshal.Copy(pixelBytes, 0, locked.Address, pixelBytes.Length);
		}
		else
		{
			for (var y = 0; y < image.Height; y++)
				Marshal.Copy(pixelBytes, y * rowBytes, IntPtr.Add(locked.Address, y * locked.RowBytes), rowBytes);
		}

		return bitmap;
	}

	private static PageImage DecodeStatic(byte[] raw)
	{
		using var ms = new MemoryStream(raw, writable: false);
		return new PageImage(new Bitmap(ms));
	}

	private static bool TryDetectImageType(byte[] raw, out DetectedType type, out bool hasAnimation)
	{
		switch (raw.Length)
		{
			case < 4:
				type = DetectedType.Unknown;
				hasAnimation = false;
				return false;
			case >= 8 when raw is [0xFF, 0xD8, _, ..] && raw[^2] == 0xFF && raw[^1] == 0xD9:
				type = DetectedType.Jpeg;
				hasAnimation = false;
				return true;
			case >= 8 when raw[0] == 0x89 && raw[1] == 0x50 && raw[2] == 0x4E && raw[3] == 0x47:
				type = DetectedType.Png;
				hasAnimation = false;
				return true;
			case >= 16 when
				raw[0] == 'R' && raw[1] == 'I' && raw[2] == 'F' && raw[3] == 'F' &&
				raw[8] == 'W' && raw[9] == 'E' && raw[10] == 'B' && raw[11] == 'P':
				type = DetectedType.Webp;
				hasAnimation = HasWebpAnimation(raw);
				return true;
			case >= 6 when raw[0] == 'G' && raw[1] == 'I' && raw[2] == 'F':
				type = DetectedType.Gif;
				hasAnimation = HasGifAnimation(raw);
				return true;
			case >= 26 when raw[0] == 'B' && raw[1] == 'M':
				type = DetectedType.Bmp;
				hasAnimation = false;
				return true;
			default:
				type = DetectedType.Unknown;
				hasAnimation = false;
				return false;
		}
	}

	private static bool HasGifAnimation(byte[] raw)
	{
		if (raw.Length < 13)
			return false;

		var pos = 13;
		if ((raw[10] & 0x80) != 0)
		{
			var gctSize = 3 * (1 << ((raw[10] & 0x07) + 1));
			pos += gctSize;
		}

		var imageCount = 0;
		while (pos < raw.Length - 1)
		{
			var b = raw[pos];
			if (b == 0x21)
			{
				if (pos + 1 >= raw.Length) break;
				pos += 2;
				while (pos < raw.Length && raw[pos] != 0x00)
				{
					var blockSize = raw[pos];
					pos += blockSize + 1;
				}
				if (pos < raw.Length) pos++;
			}
			else if (b == 0x2C)
			{
				imageCount++;
				if (imageCount > 1)
					return true;

				pos += 10;
				if (pos >= raw.Length) break;

				if ((raw[pos - 1] & 0x80) != 0)
				{
					var lctSize = 3 * (1 << ((raw[pos - 1] & 0x07) + 1));
					pos += lctSize;
				}

				if (pos < raw.Length) pos++; // LZW min code size
				while (pos < raw.Length && raw[pos] != 0x00)
				{
					var blockSize = raw[pos];
					pos += blockSize + 1;
				}
				if (pos < raw.Length) pos++;
			}
			else if (b == 0x3B)
			{
				break;
			}
			else
			{
				pos++;
			}
		}

		return false;
	}

	private static bool HasWebpAnimation(byte[] raw)
	{
		if (raw.Length >= 30 && raw[12] == 'V' && raw[13] == 'P' && raw[14] == '8' && raw[15] == 'X')
			return (raw[20] & 0x02) != 0;

		for (var i = 12; i + 8 < raw.Length;)
		{
			if (raw[i] == 'A' && raw[i + 1] == 'N' && raw[i + 2] == 'M' && raw[i + 3] == 'F')
				return true;

			var chunkSize = raw[i + 4] | (raw[i + 5] << 8) | (raw[i + 6] << 16) | (raw[i + 7] << 24);
			if (chunkSize < 0)
				break;

			i += chunkSize + 8 + (chunkSize & 1);
		}

		return false;
	}

	private static Bitmap CreateFallbackBitmap() => new(new MemoryStream(sFallbackPng, writable: false));
}
