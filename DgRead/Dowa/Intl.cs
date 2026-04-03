using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DgRead.Dowa;

// 국제화 지원 클래스. 간단한 키-값 형태의 문자열을 로드하여 Get/Set으로 접근할 수 있게 한다.
internal static class Intl
{
	private static readonly Dictionary<string, string> sHash = new();

	public static string T(string key) =>
		sHash.GetValueOrDefault(key, key);

	public static string GetLocaleMessage(string key) =>
		T(key);

	public static void SetLocaleMessage(string key, string value) =>
		sHash[key] = value;

	// 로캘 설정
	public static void LoadLocale(string locale)
	{
		sHash.Clear(); // 기존 언어는 사용할 수 없게

		if (string.IsNullOrWhiteSpace(locale))
			return;

		// 리소스에서 있나 찾는다. 리소스 이름은 "locale-{locale}.txt" 형식이어야 한다.
		var asm = Assembly.GetExecutingAssembly();
		var resName = $"locale-{locale}.txt";
		var resFound = asm.GetManifestResourceNames().
			FirstOrDefault(name => name.EndsWith(resName, StringComparison.OrdinalIgnoreCase));
		var stream = resFound != null ? asm.GetManifestResourceStream(resFound) : null;

		// 리스소에 없으면 파일로 찾는다. 파일 이름은 "DgRead.{locale}.txt" 형식이어야 한다.
		if (stream == null)
		{
			var filePath = Path.Combine(AppContext.BaseDirectory, $"DgRead.{locale}.txt");
			if (File.Exists(filePath))
				stream = File.OpenRead(filePath);
		}

		if (stream == null)
			return;

		using (stream)
		{
			using var reader = new StreamReader(stream, Encoding.UTF8, true);
			while (reader.ReadLine() is { } line)
			{
				line = line.Trim();
				if (line.Length == 0)
					continue;
				if (line.StartsWith('#'))
					continue;

				// Find first '=' that is not inside double quotes
				var sep = -1;
				var inQuotes = false;
				for (var i = 0; i < line.Length; i++)
				{
					var c = line[i];
					if (c == '"')
					{
						inQuotes = !inQuotes;
						continue;
					}
					if (c == '=' && !inQuotes)
					{
						sep = i;
						break;
					}
				}

				if (sep < 0)
					continue;

				var rawKey = line[..sep].Trim();
				var rawVal = line[(sep + 1)..].Trim();

				var key = Unwrap(rawKey);
				var val = Unwrap(rawVal);

				if (key.Length == 0)
					continue;

				sHash[key] = val;
			}
		}

		return;

		static string Unwrap(string s)
		{
			if (s.Length < 2)
				return s.Trim();
			if (s[0] == '"' && s[^1] == '"')
				return s.Substring(1, s.Length - 2).Trim();
			return s.Trim();
		}
	}
}
