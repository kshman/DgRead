using System;

namespace DgRead.Chaek;

/// <summary>
/// 책 엔트리(페이지 원본 파일)의 메타 정보를 나타냅니다.
/// </summary>
/// <param name="PageNo">0부터 시작하는 페이지 번호입니다.</param>
/// <param name="Name">엔트리 이름입니다.</param>
/// <param name="Size">엔트리 크기(바이트)입니다.</param>
/// <param name="Modified">최종 수정 시각입니다.</param>
public readonly record struct BookEntryInfo(int PageNo, string Name, long Size, DateTimeOffset? Modified);
