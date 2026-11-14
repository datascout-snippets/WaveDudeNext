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

namespace WaveDudeNext.Runs;

/// <summary>
/// Where the memory of a <see cref="FrameStore"/>'s frames comes from (<see cref="FrameMemory.Allocator"/>). By default
/// the process's native memory, bounded by the machine alone. A head sets its own where the frames need a place of
/// their own (2026-11-13: the browser head keeps them in the upper part of its program's 4 GB, apart from everything
/// the runtime allocates, which must stay in the lower part there). Any thread; a store allocates on its producer
/// thread and frees where it is disposed.
/// </summary>
public interface IFrameAllocator
{
	/// <summary>Bytes it can hold in all, which bounds the frame budget; <see cref="long.MaxValue"/> where only the machine does.</summary>
	long Capacity { get; }

	/// <summary>A block of <paramref name="bytes"/> bytes, its content undefined.</summary>
	/// <exception cref="OutOfMemoryException">There is none to be had; the store writes its remaining frames to its file then.</exception>
	nint Allocate(long bytes);

	/// <summary>Gives a block of <see cref="Allocate"/> back.</summary>
	void Free(nint block);
}

/// <summary>The default <see cref="IFrameAllocator"/>: the process's native memory.</summary>
internal sealed unsafe class NativeFrameAllocator : IFrameAllocator
{
	public long Capacity => long.MaxValue;

	public nint Allocate(long bytes) => (nint)NativeMemory.Alloc((nuint)bytes);

	public void Free(nint block) => NativeMemory.Free((void*)block);
}
