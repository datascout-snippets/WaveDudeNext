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

using System.Runtime.InteropServices;
using WaveDudeNext.Runs;

namespace LibraryCheck;

/// <summary>
/// A frame allocator of the check's own: the process's native memory, counted and bounded, standing in for a head's
/// (the browser head keeps the frames in a part of its program's memory of their own). Any thread.
/// </summary>
internal sealed unsafe class CountingFrameAllocator(long capacity) : IFrameAllocator
{
	private long _live;
	private int _blocks;
	private int _allocations;
	private int _refused;

	/// <summary>Blocks held now.</summary>
	public int Blocks => Volatile.Read(ref _blocks);

	/// <summary>Blocks handed out so far.</summary>
	public int Allocations => Volatile.Read(ref _allocations);

	/// <summary>Requests refused for want of room.</summary>
	public int Refused => Volatile.Read(ref _refused);

	public long Capacity => capacity;

	public nint Allocate(long bytes)
	{
		if (Interlocked.Add(ref _live, bytes) > capacity)
		{
			Interlocked.Add(ref _live, -bytes);
			Interlocked.Increment(ref _refused);
			throw new OutOfMemoryException("the check's allocator is full");
		}
		Interlocked.Increment(ref _blocks);
		Interlocked.Increment(ref _allocations);
		// the size before the block, for Free
		long* block = (long*)NativeMemory.Alloc((nuint)(bytes + 16));
		*block = bytes;
		return (nint)(block + 2);
	}

	public void Free(nint block)
	{
		long* start = (long*)block - 2;
		Interlocked.Add(ref _live, -*start);
		Interlocked.Decrement(ref _blocks);
		NativeMemory.Free(start);
	}
}

/// <summary>
/// A frame cache storage of the check's own, in memory: stands in for a head's (the browser head's is the storage the
/// browser manages for the page), so the check proves the stores go where <see cref="FrameCache.Storage"/> says.
/// Counts what was asked of it. Single-threaded use.
/// </summary>
internal sealed class MemoryFrameStorage(long available) : IFrameStorage
{
	private int _names;

	/// <summary>The files that exist now, by name.</summary>
	public Dictionary<string, MemoryFrameFile> Files { get; } = [];

	/// <summary>How often <see cref="Sweep"/> was called.</summary>
	public int Sweeps { get; private set; }

	/// <summary>Files created so far.</summary>
	public int Created { get; private set; }

	public string Place => "in a test's memory";

	public string NewPath() => "memory-" + ++_names + FrameCache.Extension;

	public long AvailableBytes() => available;

	public IFrameFile Create(string path)
	{
		if (Files.ContainsKey(path))
		{
			throw new IOException("The file exists: " + path);
		}
		Created++;
		var file = new MemoryFrameFile(() => Files.Remove(path));
		Files[path] = file;
		return file;
	}

	public void Sweep() => Sweeps++;

	/// <summary>A file of the storage: bytes in an array, gone from the storage when closed.</summary>
	internal sealed class MemoryFrameFile(Action closed) : IFrameFile
	{
		private byte[] _bytes = [];
		private bool _closed;

		/// <summary>The file's size.</summary>
		public long Length => _bytes.Length;

		/// <summary>Writes and reads made.</summary>
		public int Writes { get; private set; }

		/// <inheritdoc cref="Writes"/>
		public int Reads { get; private set; }

		public void SetLength(long bytes) => Array.Resize(ref _bytes, checked((int)bytes));

		public void Write(ReadOnlySpan<byte> data, long offset)
		{
			ObjectDisposedException.ThrowIf(_closed, this);
			data.CopyTo(_bytes.AsSpan(checked((int)offset)));
			Writes++;
		}

		public int Read(Span<byte> data, long offset)
		{
			ObjectDisposedException.ThrowIf(_closed, this);
			int count = (int)Math.Min(data.Length, _bytes.Length - offset);
			_bytes.AsSpan(checked((int)offset), count).CopyTo(data);
			Reads++;
			return count;
		}

		public void Close()
		{
			if (!_closed)
			{
				_closed = true;
				closed();
			}
		}
	}
}
