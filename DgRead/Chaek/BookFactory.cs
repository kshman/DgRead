using System;
using System.IO;

namespace DgRead.Chaek;

/// <summary>
/// 경로에 맞는 책 구현을 생성합니다.
/// </summary>
public static class BookFactory
{
	/// <summary>
	/// 파일/폴더 경로로부터 책 인스턴스를 엽니다.
	/// </summary>
	public static BookBase? Open(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return null;

		if (Directory.Exists(path))
			return new BookFolder(path);

		if (!File.Exists(path))
			return null;

		var ext = Path.GetExtension(path);
		if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase) || string.Equals(ext, ".cbz", StringComparison.OrdinalIgnoreCase))
			return new BookZip(path);

		if (BookImageDecoder.IsSupported(path))
			return new BookFolder(path);

		return null;
	}
}
