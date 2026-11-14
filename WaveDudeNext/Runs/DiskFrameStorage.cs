// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Alexander Graef <alex@graef.cc>
//
// This file is part of WaveDude Next, a 2D wave simulator.
//
// Based on the work of
//   Jeroen Vleggaar (Huygens Optics), author of Wave Dude 2D, Copyright (C) 2026
//     <https://www.youtube.com/@HuygensOptics>, <https://www.huygensoptics.com>
//   0x23 (Diffraction Limited), author of WaveSimulator2D
//     <https://www.youtube.com/@DiffractionLimited>, <https://github.com/0x23/WaveSimulator2D>
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using Microsoft.Win32.SafeHandles;
using NLog;

namespace WaveDudeNext.Runs;

/// <summary>
/// The frame cache on a drive, the default <see cref="IFrameStorage"/>: one file per run in a folder, named so that
/// a leftover of a crashed process is recognisable, opened without sharing and deleted when closed
/// (<see cref="FileOptions.DeleteOnClose"/>). Threads as the interface says.
/// </summary>
/// <param name="directory">The folder of the files; created on first use. A relative one counts from the current directory.</param>
internal sealed class DiskFrameStorage(string directory) : IFrameStorage
{
	private static readonly Logger Logger = LogManager.GetLogger(typeof(DiskFrameStorage).FullName!);
	private int _sequence;

	/// <summary>
	/// The drive of the folder's full path. Where the local application data folder has no path, the folder is a
	/// relative one without a root of its own: the empty drive name made <see cref="DriveInfo"/> throw an
	/// <see cref="ArgumentException"/>, an error no start of a run expects, and in the browser head, which ran on this
	/// class until it got a storage of its own, the page's UI thread ended with it (2026-11-13, the user's report: the
	/// page locked up at a run that did not fit the memory).
	/// </summary>
	private DriveInfo Drive
	{
		get
		{
			Directory.CreateDirectory(directory);
			string full = Path.GetFullPath(directory);
			return new DriveInfo(Path.GetPathRoot(full) is { Length: > 0 } root ? root : full);
		}
	}

	/// <inheritdoc/>
	public string Place => "on " + Drive.Name;

	/// <summary>A fresh file path: <c>run-&lt;time&gt;-&lt;pid&gt;-&lt;n&gt;.wdframes</c> in the folder.</summary>
	public string NewPath()
	{
		Directory.CreateDirectory(directory);
		int n = Interlocked.Increment(ref _sequence);
		return Path.Combine(directory, $"run-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{n}{FrameCache.Extension}");
	}

	/// <inheritdoc/>
	public long AvailableBytes() => Drive.AvailableFreeSpace;

	/// <inheritdoc/>
	public IFrameFile Create(string path) => new DiskFrameFile(path);

	/// <summary>
	/// Deletes leftover stores that no running process holds open (a live store is opened without sharing, so deleting
	/// it fails and it is skipped).
	/// </summary>
	public void Sweep()
	{
		if (!Directory.Exists(directory))
		{
			return;
		}
		foreach (string file in Directory.GetFiles(directory, "*" + FrameCache.Extension))
		{
			try
			{
				File.Delete(file);
				Logger.Debug("Deleted leftover frame cache {0}", file);
			}
			catch (IOException)
			{
				// in use by another instance
			}
			catch (UnauthorizedAccessException)
			{
				// in use by another instance
			}
		}
	}

	/// <summary>A store's file on a drive: positional reads and writes on its handle, any thread.</summary>
	private sealed class DiskFrameFile : IFrameFile
	{
		private readonly FileStream _stream;
		private readonly SafeFileHandle _handle;

		public DiskFrameFile(string path)
		{
			_stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
			// a temporary file: the cache manager keeps its pages in memory while it can instead of writing
			// them out eagerly (a hint only); the file is deleted in the end
			try
			{
				File.SetAttributes(path, FileAttributes.Temporary | FileAttributes.NotContentIndexed);
			}
			catch (IOException)
			{
				// a hint only
			}
			catch (UnauthorizedAccessException)
			{
				// a hint only
			}
			catch
			{
				_stream.Dispose();
				throw;
			}
			_handle = _stream.SafeFileHandle;
		}

		public void SetLength(long bytes) => _stream.SetLength(bytes);

		public void Write(ReadOnlySpan<byte> data, long offset) => RandomAccess.Write(_handle, data, offset);

		public int Read(Span<byte> data, long offset) => RandomAccess.Read(_handle, data, offset);

		public void Close() => _stream.Dispose();
	}
}
