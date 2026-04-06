using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
// ReSharper disable SwitchStatementMissingSomeEnumCasesNoDefault
// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault

namespace DgRead.Chaek;

/// <summary>
/// 책(이미지 모음) 공통 기능을 제공하는 추상 클래스입니다.
/// </summary>
public abstract class BookBase : IDisposable
{
	private readonly Dictionary<int, byte[]> _cache = [];
	private readonly Dictionary<int, PageInfo> _pageInfos = [];
	private readonly Dictionary<int, PageImage> _decodedCache = [];
	private readonly LinkedList<int> _decodedLru = [];
	private const int DecodedCacheMaxCount = 8;

	// 페이지 정보 레코드입니다.
	private sealed record PageInfo(int Width, int Height, bool HasAnimation)
	{
		// 가로가 더 길 경우(랜드스케이브) 참입니다.
		public bool IsLandscape => Width > Height;
	}

	/// <summary>
	/// 책의 전체 경로입니다.
	/// </summary>
	public string FullName { get; protected set; } = string.Empty;

	/// <summary>
	/// 경로를 제외한 표시 이름입니다.
	/// </summary>
	public string FileName { get; protected set; } = string.Empty;

	/// <summary>
	/// 확장자를 제외한 표시 이름입니다.
	/// </summary>
	public virtual string DisplayName => FileName;

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
	/// 보기 모드입니다.
	/// </summary>
	public ViewMode ViewMode { get; set; } = ViewMode.Single;

	/// <summary>
	/// 2장/스크롤 모드 지원 여부입니다.
	/// </summary>
	public virtual bool SupportsMultiPages => true;

	/// <summary>
	/// 책 엔트리 저장소입니다.
	/// </summary>
	protected readonly List<object> Entries = [];
	private int _nextStep = 1;
	private int _prevStep = 1;

	/// <summary>
	/// 실제 보기 모드
	/// </summary>
	private ViewMode ActualViewMode => ViewMode;

	/// <summary>
	/// 엔트리 스트림을 엽니다.
	/// </summary>
	protected abstract MemoryStream? ReadStream(object entry);

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
	/// 다음/이전 파일을 찾습니다.
	/// </summary>
	public virtual string? FindNextFile(BookDirection direction) => null;

	/// <summary>
	/// 임의의 파일을 찾습니다.
	/// </summary>
	public virtual string? FindRandomFile() => null;

	/// <summary>
	/// 지정 페이지 엔트리 이름을 반환합니다.
	/// </summary>
	public string? GetEntryName(int pageNo) =>
		pageNo < 0 || pageNo >= TotalPage ? null : GetEntryName(Entries[pageNo]);

	/// <summary>
	/// 이미지 버퍼를 준비합니다.
	/// </summary>
	public void PrepareImages()
	{
		PageLeft = null;
		PageRight = null;
		_nextStep = 1;
		_prevStep = 1;

		switch (ActualViewMode)
		{
			case ViewMode.Single:
			case ViewMode.Scroll:
				PageLeft = ReadPage(CurrentPage);
				PageRight = null;
				break;

			case ViewMode.LeftToRight:
			case ViewMode.RightToLeft:
			{
				var firstPage = ReadPage(CurrentPage);
				var firstInfo = GetPageInfo(CurrentPage, firstPage);
				PageImage? secondPage = null;
				if (firstInfo is { HasAnimation: false, IsLandscape: false } && CurrentPage + 1 < TotalPage)
				{
					secondPage = ReadPage(CurrentPage + 1);
					var secondInfo = GetPageInfo(CurrentPage + 1, secondPage);
					if (secondInfo.HasAnimation || secondInfo.IsLandscape)
					{
						secondPage = null;
					}
				}

				_nextStep = secondPage == null ? 1 : 2;

				if (CurrentPage > 0)
				{
					var prevInfo = GetPageInfo(CurrentPage - 1);
					var isPrevSingle = prevInfo.HasAnimation || prevInfo.IsLandscape || firstInfo.HasAnimation || firstInfo.IsLandscape;
					_prevStep = isPrevSingle ? 1 : Math.Min(2, CurrentPage);
				}

				if (ActualViewMode == ViewMode.LeftToRight)
				{
					PageLeft = firstPage;
					PageRight = secondPage;
				}
				else
				{
					PageLeft = secondPage;
					PageRight = firstPage;
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
			case ViewMode.Single:
			case ViewMode.Scroll:
				if (CurrentPage + 1 < TotalPage)
					CurrentPage++;
				break;
			case ViewMode.LeftToRight:
			case ViewMode.RightToLeft:
				if (CurrentPage + _nextStep < TotalPage)
					CurrentPage += _nextStep;
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
			case ViewMode.Single:
			case ViewMode.Scroll:
				CurrentPage--;
				break;
			case ViewMode.LeftToRight:
			case ViewMode.RightToLeft:
				CurrentPage -= _prevStep;
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

	/// <summary>
	/// 페이지 이미지를 읽어옵니다.
	/// </summary>
	public PageImage ReadPage(int pageNo)
	{
		if (pageNo < 0 || pageNo >= TotalPage)
			return PageDecoder.Decode([]);

		if (_decodedCache.TryGetValue(pageNo, out var cached))
		{
			TouchDecodedLru(pageNo);
			return cached;
		}

		PageImage page;
		if (!TryReadRaw(pageNo, out var raw) || raw == null || raw.Length == 0)
		{
			// 이미지 데이터가 없다.
			page = PageDecoder.Decode([]);
		}
		else
		{
			// 이미지 데이터를 만든다.
			page = PageDecoder.Decode(raw);
		}

		CacheDecoded(pageNo, page);
		CachePageInfo(pageNo, page);

		return page;
	}

	// 페이지의 원본 바이트를 읽어옵니다. 캐시에서 찾지 못하면 스트림에서 읽어서 캐시에 저장합니다.
	private bool TryReadRaw(int pageNo, out byte[]? raw)
	{
		if (_cache.TryGetValue(pageNo, out raw))
			return true;

		using (var ms = ReadStream(Entries[pageNo]))
		{
			if (ms == null)
			{
				raw = null;
				return false;
			}

			raw = ms.ToArray();
		}

		if (_cache.ContainsKey(pageNo))
			return true;

		if (raw.Length + CacheSize > Configs.CacheActualMaxSize && _cache.Count > 0)
		{
			var evictKey = _cache.Keys.First();
			var evictRaw = _cache[evictKey];
			_cache.Remove(evictKey);
			CacheSize -= evictRaw.Length;
		}

		_cache[pageNo] = raw;
		CacheSize += raw.Length;

		return true;
	}

	private PageInfo GetPageInfo(int pageNo, PageImage? loadedPage = null)
	{
		if (_pageInfos.TryGetValue(pageNo, out var info))
			return info;

		var page = loadedPage ?? ReadPage(pageNo);
		return CachePageInfo(pageNo, page);
	}

	// 만든 이미지를 캐시에 저장합니다. 캐시가 가득 찼으면 가장 오래된 것을 제거합니다.
	private void CacheDecoded(int pageNo, PageImage page)
	{
		if (_decodedCache.ContainsKey(pageNo))
		{
			TouchDecodedLru(pageNo);
			return;
		}

		if (_decodedCache.Count >= DecodedCacheMaxCount && _decodedLru.First != null)
		{
			var evictKey = _decodedLru.First.Value;
			_decodedLru.RemoveFirst();
			if (_decodedCache.Remove(evictKey, out var evictPage))
				evictPage.Dispose();
		}

		_decodedCache[pageNo] = page;
		_decodedLru.AddLast(pageNo);
	}

	// 페이지가 이미 디코딩된 경우 LRU 목록에서 해당 페이지를 가장 뒤로 이동시킵니다.
	private void TouchDecodedLru(int pageNo)
	{
		var node = _decodedLru.Find(pageNo);
		if (node == null)
		{
			_decodedLru.AddLast(pageNo);
			return;
		}

		if (node == _decodedLru.Last)
			return;

		_decodedLru.Remove(node);
		_decodedLru.AddLast(node);
	}

	// 페이지 정보를 캐시에 저장합니다.
	private PageInfo CachePageInfo(int pageNo, PageImage page)
	{
		var bmp = page.GetBitmap();
		var width = (int)Math.Round(bmp.Size.Width);
		var height = (int)Math.Round(bmp.Size.Height);
		var info = new PageInfo(width, height, page.HasAnimation);
		_pageInfos[pageNo] = info;
		return info;
	}

	/// <summary>
	/// 엔트리 변경 후 내부 캐시를 초기화합니다.
	/// </summary>
	protected void InvalidateCaches()
	{
		foreach (var (_, value) in _cache)
			value.AsSpan().Clear();
		_cache.Clear();

		foreach (var (_, page) in _decodedCache)
			page.Dispose();
		_decodedCache.Clear();
		_decodedLru.Clear();
		_pageInfos.Clear();

		PageLeft = null;
		PageRight = null;
		CacheSize = 0;
		_nextStep = 1;
		_prevStep = 1;
	}

	/// <summary>
	/// 파일 정보로 표시 이름을 설정합니다.
	/// </summary>
	protected void SetFileName(FileSystemInfo info)
	{
		FullName = info.FullName;
		FileName = info.Name;
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
		foreach (var (_, page) in _decodedCache)
			page.Dispose();
		_decodedCache.Clear();
		_decodedLru.Clear();
		_pageInfos.Clear();

		PageLeft = null;
		PageRight = null;
	}
}

/// <summary>
/// 책 엔트리(페이지 원본 파일)의 메타 정보를 나타냅니다.
/// </summary>
/// <param name="PageNo">0부터 시작하는 페이지 번호입니다.</param>
/// <param name="Name">엔트리 이름입니다.</param>
/// <param name="Size">엔트리 크기(바이트)입니다.</param>
/// <param name="Modified">최종 수정 시각입니다.</param>
public readonly record struct BookEntryInfo(int PageNo, string Name, long Size, DateTimeOffset? Modified);
