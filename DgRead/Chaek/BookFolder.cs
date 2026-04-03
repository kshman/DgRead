using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DgRead.Chaek;

/// <summary>
/// 폴더(또는 폴더 내 이미지 파일 집합)를 책으로 처리합니다.
/// </summary>
public sealed class BookFolder : BookBase
{
	private readonly DirectoryInfo _directory;

	/// <summary>
	/// 폴더 책을 생성합니다.
	/// </summary>
	/// <param name="path">폴더 경로 또는 폴더 내 이미지 파일 경로</param>
	public BookFolder(string path)
	{
		if (File.Exists(path))
		{
			var file = new FileInfo(path);
			_directory = file.Directory ?? throw new DirectoryNotFoundException(path);
			SetFileName(file);
			LoadEntries(file.Name);
		}
		else
		{
			_directory = new DirectoryInfo(path);
			if (!_directory.Exists)
				throw new DirectoryNotFoundException(path);

			SetFileName(_directory);
			LoadEntries();
		}
	}

	/// <inheritdoc />
	protected override Stream? OpenEntryStream(object entry)
	{
		if (entry is not FileInfo fi || !fi.Exists)
			return null;
		return fi.OpenRead();
	}

	/// <inheritdoc />
	public override string? GetEntryName(object entry) =>
		(entry as FileInfo)?.Name;

	/// <inheritdoc />
	public override IEnumerable<BookEntryInfo> GetEntriesInfo()
	{
		for (var i = 0; i < Entries.Count; i++)
		{
			if (Entries[i] is not FileInfo fi)
				continue;
			yield return new BookEntryInfo(i, fi.Name, fi.Length, fi.LastWriteTimeUtc);
		}
	}

	/// <inheritdoc />
	public override bool CanDeleteFile(out string? reason)
	{
		reason = T("Folder mode does not support deleting the source directly");
		return false;
	}

	/// <inheritdoc />
	public override bool DeleteFile(out bool closeBook)
	{
		closeBook = false;
		return false;
	}

	/// <inheritdoc />
	public override bool RenameFile(string newFilename, out string fullPath)
	{
		fullPath = FullName;
		return false;
	}

	/// <inheritdoc />
	public override bool MoveFile(string newFilename) =>
		false;

	/// <inheritdoc />
	public override string? FindNextFile(BookDirection direction)
	{
		var parent = _directory.Parent;
		if (parent == null)
			return null;

		var dirs = parent.GetDirectories()
			.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var idx = dirs.FindIndex(x => x.FullName.Equals(_directory.FullName, StringComparison.OrdinalIgnoreCase));
		if (idx < 0)
			return null;

		var nextIdx = direction == BookDirection.Next ? idx + 1 : idx - 1;
		if (nextIdx < 0 || nextIdx >= dirs.Count)
			return null;

		return dirs[nextIdx].FullName;
	}

	private void LoadEntries(string? initialName = null)
	{
		var files = _directory.GetFiles()
			.Where(x => BookImageDecoder.IsSupported(x.Name))
			.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();

		Entries.Clear();
		Entries.AddRange(files);

		if (initialName == null)
			return;

		var idx = files.FindIndex(x => x.Name.Equals(initialName, StringComparison.OrdinalIgnoreCase));
		if (idx >= 0)
			MovePage(idx);
	}
}
