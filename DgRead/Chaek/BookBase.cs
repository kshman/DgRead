using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
// ReSharper disable SwitchStatementMissingSomeEnumCasesNoDefault

namespace DgRead.Chaek;

/// <summary>
/// 책(이미지 모음) 공통 기능을 제공하는 추상 클래스입니다.
/// </summary>
public abstract class BookBase : IDisposable
{
	private readonly Dictionary<int, byte[]> _cache = [];

	/// <summary>
	/// 책의 전체 경로입니다.
	/// </summary>
	public string FullName { get; protected set; } = string.Empty;

	/// <summary>
	/// 경로를 제외한 표시 이름입니다.
	/// </summary>
	public string FileName { get; protected set; } = string.Empty;

	/// <summary>
	/// 현재 페이지(0 기반)입니다.
	/// </summary>
	public int CurrentPage { get; protected set; }

	/// <summary>
	/// 전체 페이지 수입니다.
	/// </summary>
	public int TotalPage => Entries.Count;

	/// <summary>
	/// 왼쪽(또는 단일) 페이지 이미지입니다.
	/// </summary>
	public PageImage? PageLeft { get; protected set; }

	/// <summary>
	/// 오른쪽 페이지 이미지입니다.
	/// </summary>
	public PageImage? PageRight { get; protected set; }

	/// <summary>
	/// 현재 캐시 사용 바이트입니다.
	/// </summary>
	public long CacheSize { get; private set; }

	/// <summary>
	/// 보기 모드입니다. <see cref="ViewMode.Follow"/>면 전역 설정을 따릅니다.
	/// </summary>
	public ViewMode ViewMode { get; set; } = ViewMode.Follow;

	/// <summary>
	/// 책 엔트리 저장소입니다.
	/// </summary>
	protected readonly List<object> Entries = [];

	private ViewMode ActualViewMode => ViewMode == ViewMode.Follow ? Configs.ViewMode : ViewMode;

	/// <summary>
	/// 엔트리 스트림을 엽니다.
	/// </summary>
	protected abstract Stream? OpenEntryStream(object entry);

	/// <summary>
	/// 엔트리 이름을 반환합니다.
	/// </summary>
	public abstract string? GetEntryName(object entry);

	/// <summary>
	/// 엔트리 정보를 열거합니다.
	/// </summary>
	public abstract IEnumerable<BookEntryInfo> GetEntriesInfo();

	/// <summary>
	/// 파일 삭제 가능 여부를 확인합니다.
	/// </summary>
	public virtual bool CanDeleteFile(out string? reason)
	{
		reason = string.Empty;
		return true;
	}

	/// <summary>
	/// 파일을 삭제합니다.
	/// </summary>
	public abstract bool DeleteFile(out bool closeBook);

	/// <summary>
	/// 파일 이름을 변경합니다.
	/// </summary>
	public abstract bool RenameFile(string newFilename, out string fullPath);

	/// <summary>
	/// 파일을 이동합니다.
	/// </summary>
	public abstract bool MoveFile(string newFilename);

	/// <summary>
	/// 엔트리 타이틀 표기 여부입니다.
	/// </summary>
	public virtual bool DisplayEntryTitle => false;

	/// <summary>
	/// 다음/이전 파일을 찾습니다.
	/// </summary>
	public virtual string? FindNextFile(BookDirection direction) =>
		null;

	/// <summary>
	/// 우선 방향으로 인접 파일을 찾고 실패 시 반대 방향도 시도합니다.
	/// </summary>
	public string? FindNextFileAny(BookDirection firstDirection) => firstDirection switch
	{
		BookDirection.Next => FindNextFile(BookDirection.Next) ?? FindNextFile(BookDirection.Previous),
		BookDirection.Previous => FindNextFile(BookDirection.Previous) ?? FindNextFile(BookDirection.Next),
		_ => null
	};

	/// <summary>
	/// 지정 페이지 엔트리 이름을 반환합니다.
	/// </summary>
	public string? GetEntryName(int pageNo)
	{
		if (pageNo < 0 || pageNo >= TotalPage)
			return null;

		return GetEntryName(Entries[pageNo]);
	}

	/// <summary>
	/// 이미지 버퍼를 준비합니다.
	/// </summary>
	public void PrepareImages()
	{
		PageLeft?.Dispose();
		PageRight?.Dispose();

		switch (ActualViewMode)
		{
			case ViewMode.Fit:
				PageLeft = ReadPage(CurrentPage);
				PageRight = null;
				break;

			case ViewMode.LeftToRight:
			case ViewMode.RightToLeft:
			{
				var first = ReadPage(CurrentPage);
				PageImage? second = null;
				if (!first.HasAnimation && CurrentPage + 1 < TotalPage)
				{
					second = ReadPage(CurrentPage + 1);
					if (second.HasAnimation)
						second = null;
				}

				if (ActualViewMode == ViewMode.LeftToRight)
				{
					PageLeft = first;
					PageRight = second;
				}
				else
				{
					PageLeft = second;
					PageRight = first;
				}

				break;
			}
		}
	}

	/// <summary>
	/// 다음 페이지로 이동합니다.
	/// </summary>
	public bool MoveNext()
	{
		var prev = CurrentPage;
		switch (ActualViewMode)
		{
			case ViewMode.Fit:
				if (CurrentPage + 1 < TotalPage)
					CurrentPage++;
				break;
			case ViewMode.LeftToRight:
			case ViewMode.RightToLeft:
				if (CurrentPage + 2 < TotalPage)
					CurrentPage += 2;
				break;
		}

		return prev != CurrentPage;
	}

	/// <summary>
	/// 이전 페이지로 이동합니다.
	/// </summary>
	public bool MovePrev()
	{
		var prev = CurrentPage;
		switch (ActualViewMode)
		{
			case ViewMode.Fit:
				CurrentPage--;
				break;
			case ViewMode.LeftToRight:
			case ViewMode.RightToLeft:
				CurrentPage -= 2;
				break;
		}

		if (CurrentPage < 0)
			CurrentPage = 0;
		return prev != CurrentPage;
	}

	/// <summary>
	/// 지정 페이지로 이동합니다.
	/// </summary>
	public bool MovePage(int page)
	{
		if (TotalPage <= 0)
			return false;

		var prev = CurrentPage;
		CurrentPage = Math.Clamp(page, 0, TotalPage - 1);
		return prev != CurrentPage;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		DisposeCore();
		GC.SuppressFinalize(this);
	}

	/// <summary>
	/// 관리 리소스를 해제합니다.
	/// </summary>
	protected virtual void DisposeCore()
	{
		Entries.Clear();

		foreach (var (_, value) in _cache)
			value.AsSpan().Clear();
		_cache.Clear();

		PageLeft?.Dispose();
		PageRight?.Dispose();
	}

	/// <summary>
	/// 페이지 이미지를 읽어옵니다.
	/// </summary>
	protected PageImage ReadPage(int pageNo)
	{
		if (pageNo < 0 || pageNo >= TotalPage)
			return BookImageDecoder.Decode([]);

		if (!TryReadRaw(pageNo, out var raw) || raw == null || raw.Length == 0)
			return BookImageDecoder.Decode([]);

		return BookImageDecoder.Decode(raw);
	}

	private bool TryReadRaw(int pageNo, out byte[]? raw)
	{
		if (_cache.TryGetValue(pageNo, out raw))
			return true;

		using var st = OpenEntryStream(Entries[pageNo]);
		if (st == null)
		{
			raw = null;
			return false;
		}

		using var ms = new MemoryStream();
		st.CopyTo(ms);
		raw = ms.ToArray();
		CacheRaw(pageNo, raw);
		return true;
	}

	private void CacheRaw(int pageNo, byte[] raw)
	{
		if (_cache.ContainsKey(pageNo))
			return;

		if (raw.Length + CacheSize > Configs.CacheActualMaxSize && _cache.Count > 0)
		{
			var evictKey = _cache.Keys.First();
			var evictRaw = _cache[evictKey];
			_cache.Remove(evictKey);
			CacheSize -= evictRaw.Length;
		}

		_cache[pageNo] = raw;
		CacheSize += raw.Length;
	}

	/// <summary>
	/// 파일 정보로 표시 이름을 설정합니다.
	/// </summary>
	protected void SetFileName(FileSystemInfo info)
	{
		FullName = info.FullName;
		FileName = info.Name;
	}
}
