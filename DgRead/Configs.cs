using System;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DgRead;

internal static class Configs
{
	public record MoveInfo(int No, string Alias, string Folder);

	public record BookmarkInfo(int Id, string Path, int Page, DateTime Created, bool Incoming);

	#region 기본 설정
	private static readonly DateTime sLaunched = DateTime.Now;
	private static string sAppPath = string.Empty;
	private static string sDataSource = string.Empty;
	private static bool sOk;
	#endregion

	#region 캐시 데이터
	private static long sRunCount;
	private static long sRunDuration;
	private static bool sRunOnce = true;

	private static int sWindowX = -1;
	private static int sWindowY = -1;
	private static int sWindowWidth = 600;
	private static int sWindowHeight = 400;
	private static WindowTheme sWindowTheme = WindowTheme.Default;

	private static bool sEscToExit = true;
	private static bool sFileConfirmDelete = true;

	private static string sExternalProgram = string.Empty;

	private static int sCacheMaxSize = 230; // 메가바이트

	private static bool sMouseDoubleFullScreen;
	private static bool sMouseClickPaging;

	private static bool sViewZoom = true;
	private static ViewMode sViewMode = ViewMode.Fit;
	private static ViewAlign sViewAlign = ViewAlign.Center;
	private static ViewQuality sViewQuality = ViewQuality.Default;
	private static int sViewMargin = 100;

	private static readonly List<MoveInfo> sMoves = [];
	private static readonly List<BookmarkInfo> sBookmarks = [];

	private static readonly Dictionary<string, string> sStorage = [];
	#endregion

	#region 클래스 자체 처리
	// 쿼리 목록
	private static readonly string[] sQueries =
	[
		"CREATE TABLE IF NOT EXISTS configs (key TEXT PRIMARY KEY, value TEXT);",
		"CREATE TABLE IF NOT EXISTS moves (no INTEGER PRIMARY KEY, alias TEXT, folder TEXT);",
		"CREATE TABLE IF NOT EXISTS history (filename TEXT PRIMARY KEY, page INTEGER, updated TEXT);",
		"CREATE TABLE IF NOT EXISTS bookmarks (id INTEGER PRIMARY KEY AUTOINCREMENT, path TEXT, page INTEGER, created TEXT);",
		"CREATE INDEX IF NOT EXISTS idx_bookmarks_path_page ON bookmarks(path, page);"
	];

	// 설정 초기화
	public static bool Initialize()
	{
		sAppPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ksh");
		if (!Directory.Exists(sAppPath))
			Directory.CreateDirectory(sAppPath);
		sDataSource = $"Data Source={Path.Combine(sAppPath, "DgRead.conf")}";

		using var conn = OpenConnection();

		try
		{
			foreach (var query in sQueries)
			{
				if (conn.ExecuteSql(query))
					continue;
				Debug.WriteLine($"쿼리 실패: {query}");
				SuppUi.Ok("Failed to create config file!", "Error");
				return false;
			}
		}
		catch (Exception e)
		{
			SuppUi.Ok($"Failed to access config file!{Environment.NewLine}{Environment.NewLine}{e.Message}", "Error");
			return false;
		}

		sRunCount = conn.SelectConfigsAsLong("RunCount") + 1;
		sRunDuration = conn.SelectConfigsAsLong("RunDuration");
		sRunOnce = conn.SelectConfigsAsBool("RunOnce", sRunOnce);

		return sOk = true;
	}

	// 설정 저장하고 닫기
	public static void Close()
	{
		if (!sOk)
			return;

		sRunDuration += (long)(DateTime.Now - sLaunched).TotalSeconds;

		try
		{
			using var conn = OpenConnection();
			using var transaction = conn.BeginTransaction();
			conn.IntoConfigs("WindowX", sWindowX);
			conn.IntoConfigs("WindowY", sWindowY);
			conn.IntoConfigs("WindowWidth", sWindowWidth);
			conn.IntoConfigs("WindowHeight", sWindowHeight);
			conn.IntoConfigs("RunCount", sRunCount);
			conn.IntoConfigs("RunDuration", sRunDuration);
			transaction.Commit();
		}
		catch { /* 무시 */ }
	}

	// 모든 캐시 읽기
	public static void LoadAllCache()
	{
		if (!sOk)
			return;

		using var conn = OpenConnection();

		sWindowX = conn.SelectConfigsAsInt("WindowX", sWindowX);
		sWindowY = conn.SelectConfigsAsInt("WindowY", sWindowY);
		sWindowWidth = conn.SelectConfigsAsInt("WindowWidth", sWindowWidth);
		sWindowHeight = conn.SelectConfigsAsInt("WindowHeight", sWindowHeight);
		sWindowTheme = conn.SelectConfigsAsWindowTheme("WindowTheme", sWindowTheme);

		sEscToExit = conn.SelectConfigsAsBool("GeneralEscToExit", sEscToExit);
		sFileConfirmDelete = conn.SelectConfigsAsBool("FileConfirmDelete", sFileConfirmDelete);

		sExternalProgram = conn.SelectConfigs("ExternalProgram") ?? sExternalProgram;

		sCacheMaxSize = (int)conn.SelectConfigsAsLong("CacheMaxSize", sCacheMaxSize);

		sMouseDoubleFullScreen = conn.SelectConfigsAsBool("MouseDoubleFullScreen", sMouseDoubleFullScreen);
		sMouseClickPaging = conn.SelectConfigsAsBool("MouseClickPaging", sMouseClickPaging);

		sViewZoom = conn.SelectConfigsAsBool("ViewZoom", sViewZoom);
		sViewMode = conn.SelectConfigsAsViewMode("ViewMode");
		sViewAlign = conn.SelectConfigsAsViewAlign("ViewAlign");
		sViewQuality = conn.SelectConfigsAsViewQuality("ViewQuality");
		sViewMargin = conn.SelectConfigsAsInt("ViewMargin", sViewMargin);

		// 이동
		using (var moveCmd = conn.CreateCommand())
		{
			moveCmd.CommandText = "SELECT no, alias, folder FROM moves ORDER BY no;";
			using (var moveRdr = moveCmd.ExecuteReader())
			{
				sMoves.Clear();
				while (moveRdr.Read())
				{
					var no = moveRdr.GetInt32(0);
					var alias = moveRdr.GetString(1);
					var folder = moveRdr.GetString(2);
					sMoves.Add(new MoveInfo(no, alias, folder));
				}
			}
		}

		// 북마크
		using (var bmCmd = conn.CreateCommand())
		{
			bmCmd.CommandText = "SELECT id, path, page, created FROM bookmarks ORDER BY path, page;";
			using (var bmRdr = bmCmd.ExecuteReader())
			{
				sBookmarks.Clear();
				while (bmRdr.Read())
				{
					var id = bmRdr.GetInt32(0);
					var path = bmRdr.GetString(1);
					var page = bmRdr.GetInt32(2);
					var created = bmRdr.GetString(3);
					if (!DateTime.TryParse(created, out var cdt))
						cdt = DateTime.MinValue;
					sBookmarks.Add(new BookmarkInfo(id, path, page, cdt, false));
				}
			}
		}

		Debug.WriteLine("설정 읽기 성공");
	}
	#endregion

	#region SQL 도우미
	extension(SqliteConnection conn)
	{
		public bool ExecuteSql(string query)
		{
			using var cmd = conn.CreateCommand();
			cmd.CommandText = query;
			try
			{
				cmd.ExecuteNonQuery();
				return true;
			}
			catch (Exception e)
			{
				Debug.WriteLine($"쿼리 실행 실패: {e.Message}");
				return false;
			}
		}

		public string? SelectConfigs(string key, string? defaultValue = null)
		{
			var cmd = conn.CreateCommand();
			cmd.CommandText = "SELECT value FROM configs WHERE key = @key LIMIT 1;";
			cmd.Parameters.AddWithValue("@key", key);
			using var rdr = cmd.ExecuteReader();
			return rdr.Read() ? rdr.GetString(0) : defaultValue;
		}

		public int SelectConfigsAsInt(string key, int defaultValue = 0) =>
			conn.SelectConfigs(key).AlterInt(defaultValue);

		public long SelectConfigsAsLong(string key, long defaultValue = 0) =>
			conn.SelectConfigs(key).AlterLong(defaultValue);

		public bool SelectConfigsAsBool(string key, bool defaultValue = false) =>
			conn.SelectConfigs(key).AlterBool(defaultValue);

		public ViewMode SelectConfigsAsViewMode(string key, ViewMode defaultValue = ViewMode.Fit) =>
			conn.SelectConfigs(key) is { } s && Enum.TryParse<ViewMode>(s, out var vm) ? vm : defaultValue;

		public ViewAlign SelectConfigsAsViewAlign(string key, ViewAlign defaultValue = ViewAlign.Center) =>
			conn.SelectConfigs(key) is { } s && Enum.TryParse<ViewAlign>(s, out var va) ? va : defaultValue;

		public ViewQuality SelectConfigsAsViewQuality(string key, ViewQuality defaultValue = ViewQuality.Default) =>
			conn.SelectConfigs(key) is { } s && Enum.TryParse<ViewQuality>(s, out var vq) ? vq : defaultValue;

		public WindowTheme SelectConfigsAsWindowTheme(string key, WindowTheme defaultValue = WindowTheme.Default) =>
			conn.SelectConfigs(key) is { } s && Enum.TryParse<WindowTheme>(s, out var wt) ? wt : defaultValue;

		public void IntoConfigs(string key, string value)
		{
			var cmd = conn.CreateCommand();
			cmd.CommandText = "INSERT OR REPLACE INTO configs (key, value) VALUES (@key, @value);";
			cmd.Parameters.AddWithValue("@key", key);
			cmd.Parameters.AddWithValue("@value", value);
			try
			{
				cmd.ExecuteNonQuery();
			}
			catch (Exception e)
			{
				Debug.WriteLine($"설정 쓰기 실패: {key}={value}");
				Debug.WriteLine($" >> {e.Message}");
			}
		}

		public void IntoConfigs(string key, int value) =>
			conn.IntoConfigs(key, value.ToString());

		public void IntoConfigs(string key, long value) =>
			conn.IntoConfigs(key, value.ToString());

		public void IntoConfigs(string key, bool value) =>
			conn.IntoConfigs(key, value ? "true" : "false");
	}

	private static string? GetSql(string key, string? defaultValue = null)
	{
		using var conn = OpenConnection();
		return conn.SelectConfigs(key, defaultValue);
	}

	private static void SetSql(string key, string value)
	{
		using var conn = OpenConnection();
		conn.IntoConfigs(key, value);
	}

	private static void SetSql(string key, int value)
	{
		using var conn = OpenConnection();
		conn.IntoConfigs(key, value);
	}

	private static void SetSql(string key, bool value)
	{
		using var conn = OpenConnection();
		conn.IntoConfigs(key, value);
	}

	private static SqliteConnection OpenConnection()
	{
		var conn = new SqliteConnection(sDataSource);
		conn.Open();
		return conn;
	}
	#endregion

	#region 속성

	public static bool RunOnce
	{
		get => sRunOnce;
		set
		{
			if (value == sRunOnce) return;
			SetSql("RunOnce", sRunOnce = value);
		}
	}

	public static int WindowX
	{
		get => sWindowX;
		set => sWindowX = value;
	}

	public static int WindowY
	{
		get => sWindowY;
		set => sWindowY = value;
	}

	public static int WindowWidth
	{
		get => sWindowWidth;
		set => sWindowWidth = value;
	}

	public static int WindowHeight
	{
		get => sWindowHeight;
		set => sWindowHeight = value;
	}

	public static WindowTheme WindowTheme
	{
		get => sWindowTheme;
		set
		{
			if (value == sWindowTheme) return;
			sWindowTheme = value;
			SetSql("WindowTheme", sWindowTheme.ToString());
		}
	}

	public static bool EscToExit
	{
		get => sEscToExit;
		set
		{
			if (value == sEscToExit) return;
			SetSql("GeneralEscToExit", sEscToExit = value);
		}
	}

	public static bool FileConfirmDelete
	{
		get => sFileConfirmDelete;
		set
		{
			if (value == sFileConfirmDelete) return;
			SetSql("FileConfirmDelete", sFileConfirmDelete = value);
		}
	}

	public static string ExternalProgram
	{
		get => sExternalProgram;
		set
		{
			if (value.Equals(sExternalProgram)) return;
			SetSql("ExternalProgram", sExternalProgram = value);
		}
	}

	public static int CacheMaxSize
	{
		get => sCacheMaxSize;
		set
		{
			if (value == sCacheMaxSize) return;
			SetSql("CacheMaxSize", sCacheMaxSize = value);
		}
	}

	public static long CacheActualMaxSize => sCacheMaxSize * 1024L * 1024L;

	public static bool MouseDoubleFullScreen
	{
		get => sMouseDoubleFullScreen;
		set
		{
			if (value == sMouseDoubleFullScreen) return;
			SetSql("MouseDoubleFullScreen", sMouseDoubleFullScreen = value);
		}
	}

	public static bool MouseClickPaging
	{
		get => sMouseClickPaging;
		set
		{
			if (value == sMouseClickPaging) return;
			SetSql("MouseClickPaging", sMouseClickPaging = value);
		}
	}

	public static bool ViewZoom
	{
		get => sViewZoom;
		set
		{
			if (value == sViewZoom) return;
			SetSql("ViewZoom", sViewZoom = value);
		}
	}

	public static ViewMode ViewMode
	{
		get => sViewMode;
		set
		{
			if (value == sViewMode) return;
			sViewMode = value;
			SetSql("ViewMode", sViewMode.ToString());
		}
	}

	public static ViewAlign ViewAlign
	{
		get => sViewAlign;
		set
		{
			if (value == sViewAlign) return;
			sViewAlign = value;
			SetSql("ViewAlign", sViewAlign.ToString());
		}
	}

	public static ViewQuality ViewQuality
	{
		get => sViewQuality;
		set
		{
			if (value == sViewQuality) return;
			sViewQuality = value;
			SetSql("ViewQuality", sViewQuality.ToString());
		}
	}

	public static int ViewMargin
	{
		get => sViewMargin;
		set
		{
			var v = value < 0 ? 0 : value;
			if (v == sViewMargin) return;
			SetSql("ViewMargin", sViewMargin = v);
		}
	}

	public static string LastFolder
	{
		get
		{
			var s = GetSql("FileLastFolder") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
			return sStorage["FileLastFolder"] = s;
		}
		set
		{
			var s = sStorage.GetValueOrDefault("FileLastFolder");
			if (string.IsNullOrEmpty(s) && value.Equals(s))
				return;
			SetSql("FileLastFolder", sStorage["FileLastFolder"] = value);
		}
	}

	public static string LastFileName
	{
		get
		{
			var s = GetSql("FileLastFileName") ?? string.Empty;
			return sStorage["FileLastFileName"] = s;
		}
		set
		{
			var s = sStorage.GetValueOrDefault("FileLastFileName");
			if (string.IsNullOrEmpty(s) && value.Equals(s))
				return;
			SetSql("FileLastFileName", sStorage["FileLastFileName"] = value);
		}
	}
	#endregion

	#region 최근 파일
	public static int GetHistory(string filename)
	{
		using var conn = OpenConnection();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "SELECT page FROM history WHERE filename = @filename LIMIT 1;";
		cmd.Parameters.AddWithValue("@filename", filename);
		using var rdr = cmd.ExecuteReader();
		return !rdr.Read() ? 0 : rdr.GetInt32(0); //  최근 페이지가 없으면 0을 반환
	}

	public static void SetHistory(string filename, int page)
	{
		using var conn = OpenConnection();
		using var cmd = conn.CreateCommand();
		if (page <= 0)
		{
			// 페이지가 0이면 삭제
			cmd.CommandText = "DELETE FROM history WHERE filename = @filename;";
			cmd.Parameters.AddWithValue("@filename", filename);
		}
		else
		{
			cmd.CommandText = "INSERT OR REPLACE INTO history (filename, page, updated) VALUES (@filename, @page, @updated);";
			cmd.Parameters.AddWithValue("@filename", filename);
			cmd.Parameters.AddWithValue("@page", page);
			cmd.Parameters.AddWithValue("@updated", DateTime.Now.ToString("o"));
		}

		try
		{
			cmd.ExecuteNonQuery();
		}
		catch (Exception e)
		{
			Debug.WriteLine($"최근 파일 쓰기 실패: \"{filename}\":{page}");
			Debug.WriteLine($" >> {e.Message}");
		}
	}
	#endregion

	#region 이동 위치
	public static IReadOnlyList<MoveInfo> Moves => sMoves;

	public static void CommitMoves()
	{
		using var conn = OpenConnection();

		// 먼저 기존 데이터를 삭제
		using var cmd = conn.CreateCommand();
		cmd.CommandText = "DELETE FROM moves;";
		cmd.ExecuteNonQuery();

		// 그리고 새로 저장
		using var transaction = conn.BeginTransaction();
		cmd.Transaction = transaction;
		foreach (var move in sMoves)
		{
			cmd.CommandText = "INSERT INTO moves (no, alias, folder) VALUES (@no, @alias, @folder);";
			cmd.Parameters.Clear();
			cmd.Parameters.AddWithValue("@no", move.No);
			cmd.Parameters.AddWithValue("@alias", move.Alias);
			cmd.Parameters.AddWithValue("@folder", move.Folder);
			cmd.ExecuteNonQuery();
		}
		transaction.Commit();
	}
	#endregion

	#region 즐겨찾기
	public static IReadOnlyList<BookmarkInfo> Bookmarks => sBookmarks;

	public static void CommitBookmarks()
	{
		// 새로 추가된것이 있는가
		var incomes = sBookmarks.Count(bm => bm.Incoming);
		if (incomes == 0)
			return;

		// 있다면 새로 추가된 것만 넣는다
		using var conn = OpenConnection();
		using var cmd = conn.CreateCommand();
		using var transaction = conn.BeginTransaction();
		cmd.Transaction = transaction;
		var count = sBookmarks.Count;
		for (var i = 0; i < count; i++)
		{
			var bm = sBookmarks[i];
			if (!bm.Incoming)
				continue;
			cmd.CommandText = "INSERT INTO bookmarks (path, page, created) VALUES (@path, @page, @created);";
			cmd.Parameters.Clear();
			cmd.Parameters.AddWithValue("@path", bm.Path);
			cmd.Parameters.AddWithValue("@page", bm.Page);
			cmd.Parameters.AddWithValue("@created", bm.Created.ToString("o"));
			cmd.ExecuteNonQuery();
			sBookmarks[i] = bm with { Incoming = false };
		}
		transaction.Commit();
	}
	#endregion
}
