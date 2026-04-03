using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace DgRead.Chaek;

/// <summary>
/// ZIP 파일 기반 책입니다.
/// </summary>
public sealed class BookZip : BookBase
{
	private readonly FileInfo _zipFile;
	private readonly FileStream _stream;
	private readonly ZipArchive _zip;

	/// <summary>
	/// ZIP 책을 생성합니다.
	/// </summary>
	public BookZip(string fullPath)
	{
		_zipFile = new FileInfo(fullPath);
		if (!_zipFile.Exists)
			throw new FileNotFoundException(fullPath);

		SetFileName(_zipFile);
		_stream = _zipFile.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
		_zip = new ZipArchive(_stream, ZipArchiveMode.Read);

		var entries = _zip.Entries
			.Where(x => !string.IsNullOrEmpty(x.Name) && BookImageDecoder.IsSupported(x.Name))
			.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase)
			.Cast<object>()
			.ToList();

		Entries.AddRange(entries);
	}

	/// <inheritdoc />
	protected override Stream? OpenEntryStream(object entry)
	{
		if (entry is not ZipArchiveEntry ze)
			return null;

		using var src = ze.Open();
		var ms = new MemoryStream();
		src.CopyTo(ms);
		ms.Position = 0;
		return ms;
	}

	/// <inheritdoc />
	public override string? GetEntryName(object entry) =>
		(entry as ZipArchiveEntry)?.FullName;

	/// <inheritdoc />
	public override IEnumerable<BookEntryInfo> GetEntriesInfo()
	{
		for (var i = 0; i < Entries.Count; i++)
		{
			if (Entries[i] is not ZipArchiveEntry ze)
				continue;
			yield return new BookEntryInfo(i, ze.FullName, ze.Length, ze.LastWriteTime);
		}
	}

	/// <inheritdoc />
	public override bool DeleteFile(out bool closeBook)
	{
		closeBook = false;
		try
		{
			Dispose();
			_zipFile.Delete();
			closeBook = true;
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
		try
		{
			var target = Path.Combine(_zipFile.DirectoryName ?? string.Empty, newFilename);
			if (!target.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
				target += ".zip";

			Dispose();
			File.Move(FullName, target);
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
		try
		{
			Dispose();
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
		var dir = _zipFile.Directory;
		if (dir == null)
			return null;

		var files = dir.GetFiles("*.zip")
			.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		var idx = files.FindIndex(x => x.FullName.Equals(_zipFile.FullName, StringComparison.OrdinalIgnoreCase));
		if (idx < 0)
			return null;

		var nextIdx = direction == BookDirection.Next ? idx + 1 : idx - 1;
		if (nextIdx < 0 || nextIdx >= files.Count)
			return null;

		return files[nextIdx].FullName;
	}

	/// <inheritdoc />
	protected override void DisposeCore()
	{
		base.DisposeCore();
		_zip.Dispose();
		_stream.Dispose();
	}
}
