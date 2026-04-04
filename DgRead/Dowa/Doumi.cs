using System.Collections.Generic;
using System.IO;

namespace DgRead.Dowa;

internal static class Doumi
{
	/// <summary>
	/// 바이트 크기를 사람이 읽기 쉬운 문자열로 변환합니다.
	/// </summary>
	/// <param name="size">바이트 단위의 크기입니다.</param>
	/// <returns>GB, MB, KB, B 단위의 문자열을 반환합니다.</returns>
	public static string SizeToString(long size)
	{
		const long giga = 1024 * 1024 * 1024;
		const long mega = 1024 * 1024;
		const long kilo = 1024;

		double v;
		switch (size)
		{
			// 0.5 기가
			case > giga:
				v = size / (double)giga;
				return $"{v:0.0}GB";

			// 0.5 메가
			case > mega:
				v = size / (double)mega;
				return $"{v:0.0}MB";

			// 0.5 킬로
			case > kilo:
				v = size / (double)kilo;
				return $"{v:0.0}KB";

			default:
				return $"{size}B";
		}
	}

	/// <summary>
	/// 문자열 비교
	/// </summary>
	/// <param name="s1">비교할 첫 번째 문자열입니다.</param>
	/// <param name="s2">비교할 두 번째 문자열입니다.</param>
	/// <returns>숫자 및 문자 순서에 따라 비교 결과를 반환합니다. s1이 s2보다 작으면 음수, 같으면 0, 크면 양수를 반환합니다.</returns>
	public static int StringAsNumericCompare(string? s1, string? s2)
	{
		//get rid of special cases
		if (s1 == null) return s2 == null ? 0 : -1;
		if (s2 == null) return 1;

		if ((s1.Equals(string.Empty) && (s2.Equals(string.Empty)))) return 0;
		if (s1.Equals(string.Empty)) return -1;
		if (s2.Equals(string.Empty)) return -1;

		//WE style, special case
		var sp1 = char.IsLetterOrDigit(s1, 0);
		var sp2 = char.IsLetterOrDigit(s2, 0);
		switch (sp1)
		{
			case true when !sp2:
				return 1;
			case false when sp2:
				return -1;
		}

		int i1 = 0, i2 = 0; //current index
		while (true)
		{
			var c1 = char.IsDigit(s1, i1);
			var c2 = char.IsDigit(s2, i2);
			int r;
			switch (c1)
			{
				case false when !c2:
				{
					var letter1 = char.IsLetter(s1, i1);
					var letter2 = char.IsLetter(s2, i2);
					switch (letter1)
					{
						case true when letter2:
						case false when !letter2:
							r = letter1 && letter2
								? char.ToLower(s1[i1]).CompareTo(char.ToLower(s2[i2]))
								: s1[i1].CompareTo(s2[i2]);
							if (r != 0) return r;
							break;
						case false when letter2:
							return -1;
						case true when !letter2:
							return 1;
					}
				}
				break;
				case true when c2:
					r = InternalNumberCompare(s1, ref i1, s2, ref i2);
					if (r != 0) return r;
					break;
				case true:
					return -1;
				default:
					if (c2) return 1;
					break;
			}

			i1++;
			i2++;
			if ((i1 >= s1.Length) && (i2 >= s2.Length))
				return 0;
			if (i1 >= s1.Length)
				return -1;
			if (i2 >= s2.Length)
				return -1;
		}
	}

	private static int InternalNumberCompare(string s1, ref int i1, string s2, ref int i2)
	{
		var (start1, end1) = InternalNumberScanEnd(s1, i1);
		var (start2, end2) = InternalNumberScanEnd(s2, i2);
		var pos1 = i1;
		i1 = end1 - 1;
		var pos2 = i2;
		i2 = end2 - 1;

		var nzLength1 = end1 - start1;
		var nzLength2 = end2 - start2;

		if (nzLength1 < nzLength2) return -1;
		if (nzLength1 > nzLength2) return 1;

		for (int j1 = start1, j2 = start2; j1 <= i1; j1++, j2++)
		{
			var r = s1[j1].CompareTo(s2[j2]);
			if (r != 0) return r;
		}

		// the nz parts are equal
		var length1 = end1 - pos1;
		var length2 = end2 - pos2;
		if (length1 == length2) return 0;
		if (length1 > length2) return -1;
		return 1;
	}

	private static (int start, int end) InternalNumberScanEnd(string s, int startPosition)
	{
		var start = startPosition;
		var end = startPosition;
		var zero = true;
		while (char.IsDigit(s, end))
		{
			if (zero && s[end].Equals('0'))
				start++;
			else zero = false;
			end++;
			if (end >= s.Length) break;
		}

		return (start, end);
	}

	/// <summary>
	/// 파일 정보 이름으로 비교
	/// </summary>
	internal class FileInfoComparer : IComparer<FileInfo>
	{
		/// <summary>
		/// 비교 메소드
		/// </summary>
		/// <param name="x">비교할 첫 번째 FileInfo 객체입니다.</param>
		/// <param name="y">비교할 두 번째 FileInfo 객체입니다.</param>
		/// <returns>비교 결과를 반환합니다. x가 y보다 작으면 음수, 같으면 0, 크면 양수를 반환합니다.</returns>
		public int Compare(FileInfo? x, FileInfo? y) =>
			StringAsNumericCompare(x?.Name, y?.Name);
	}

	/// <summary>
	/// 디렉토리 정보 이름으로 비교
	/// </summary>
	internal class DirectoryInfoComparer : IComparer<DirectoryInfo>
	{
		/// <summary>
		/// 비교 메소드
		/// </summary>
		/// <param name="x">비교할 첫 번째 DirectoryInfo 객체입니다.</param>
		/// <param name="y">비교할 두 번째 DirectoryInfo 객체입니다.</param>
		/// <returns>비교 결과를 반환합니다. x가 y보다 작으면 음수, 같으면 0, 크면 양수를 반환합니다.</returns>
		public int Compare(DirectoryInfo? x, DirectoryInfo? y) =>
			StringAsNumericCompare(x?.Name, y?.Name);
	}
}
