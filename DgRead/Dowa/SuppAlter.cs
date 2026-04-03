using System;
using System.Linq;

namespace DgRead.Dowa;

internal static class SuppAlter
{
	private static readonly string[] sBoolNames = ["TRUE", "YES", "CHAM", "OK", "1"];

	extension(string? value)
	{
		public long AlterLong(long failValue = 0) =>
			long.TryParse(value, out var ret) ? ret : failValue;

		public int AlterInt(int failValue = 0) =>
			int.TryParse(value, out var ret) ? ret : failValue;

		public short AlterShort(short failValue = 0) =>
			short.TryParse(value, out var ret) ? ret : failValue;

		public bool AlterBool(bool failValue = false) =>
			string.IsNullOrEmpty(value) ? failValue : sBoolNames.Any(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));

		public float AlterFloat(float failValue = 0) =>
			float.TryParse(value, out var ret) ? ret : failValue;
	}
}
