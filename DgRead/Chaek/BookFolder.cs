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

	/// <inheritdoc />
	public override bool SupportsMultiPages => false;

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
	protected override MemoryStream? ReadStream(object entry)
	{
		if (entry is not FileInfo { Exists: true } fi)
			return null;

		using var st = fi.OpenRead();
		var ms = new MemoryStream();
		st.CopyTo(ms);
		ms.Position = 0;
		return ms;
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
		reason = null;
		if (TotalPage <= 0)
		{
			reason = T("[No book opened]");
			return false;
		}

		if (Entries[CurrentPage] is not FileInfo)
		{
			reason = T("Failed to delete file");
			return false;
		}

		return true;
	}

	/// <inheritdoc />
	public override bool DeleteFile(out bool closeBook)
	{
		closeBook = false;
		if (Entries.Count == 0 || CurrentPage < 0 || CurrentPage >= Entries.Count)
			return false;

		if (Entries[CurrentPage] is not FileInfo fi)
			return false;

		var nextPage = CurrentPage < Entries.Count - 1 ? CurrentPage : CurrentPage - 1;
		try
		{
			fi.Delete();
			LoadEntries();
			InvalidateCaches();
			if (Entries.Count == 0)
			{
				closeBook = true;
				return true;
			}

			_ = MovePage(nextPage);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <inheritdoc />
	public override bool RenameFile(string newFilename, out string fullPath)
	{
        fullPath = FullName;
		if (Entries.Count == 0 || CurrentPage < 0 || CurrentPage >= Entries.Count)
			return false;

		if (Entries[CurrentPage] is not FileInfo current)
			return false;

		if (string.IsNullOrWhiteSpace(newFilename))
			return false;

		var target = Path.Combine(_directory.FullName, newFilename.Trim());
		if (current.FullName.Equals(target, StringComparison.OrdinalIgnoreCase))
			return true;

		if (File.Exists(target))
			return false;

		try
		{
			File.Move(current.FullName, target);
			LoadEntries(Path.GetFileName(target));
			InvalidateCaches();
			SetFileName(new FileInfo(target));
			fullPath = target;
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <inheritdoc />
	public override bool MoveFile(string newFilename)
	{
		if (!File.Exists(FullName))
			return false;

		try
		{
			File.Move(FullName, newFilename);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <inheritdoc />
	public override string? FindNextFile(BookDirection direction)
	{
		var parent = _directory.Parent;
		if (parent == null)
			return null;

		var dirs = parent.GetDirectories()
			.OrderBy(x => x, new Doumi.DirectoryInfoComparer())
			.ToList();
		var idx = dirs.FindIndex(x => x.FullName.Equals(_directory.FullName, StringComparison.OrdinalIgnoreCase));
		if (idx < 0)
			return null;

		var next = direction == BookDirection.Next ? idx + 1 : idx - 1;
		if (next < 0)
		{
			// 첫번째였다면 마지막꺼 반환
			next = dirs.Count - 1;
		}
		else if (next >= dirs.Count)
		{
			// 마지막이었다면 첫번째꺼 반환
			next = 0;
		}

		return dirs[next].FullName;
	}

	/// <inheritdoc />
	public override string? FindRandomFile()
	{
		var parent = _directory.Parent;
		if (parent == null)
			return null;

		var candidates = parent.GetDirectories()
			.Where(d => !d.FullName.Equals(_directory.FullName, StringComparison.OrdinalIgnoreCase))
			.ToList();

		var chosen = Doumi.RandomByWeight(candidates, d =>
		{
			var age = DateTime.UtcNow - d.LastWriteTimeUtc;
			var days = Math.Max(0.0, age.TotalDays);
			return Math.Exp(-days / 30.0);
		});

		return chosen?.FullName;
	}

	private void LoadEntries(string? initialName = null)
	{
		var files = _directory.GetFiles()
			.Where(x => PageDecoder.IsSupported(x.Name))
			.OrderBy(x => x, new Doumi.FileInfoComparer())
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
