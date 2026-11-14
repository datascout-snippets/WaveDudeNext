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

// A thread's identity for System.Threading.Lock on .NET 10's threaded browser runtime (2026-11-13).
//
// That lock knows its owner by the operating system's thread id, which the runtime asks its native part for
// (SystemNative_TryGetUInt32OSThreadId), and on the threaded browser runtime that function answers 1 on every thread
// (dotnet/runtime#129900, open, for .NET 12). So the lock takes every thread for its owner: a second thread enters a
// held lock as if re-entering it, and the one that leaves last finds the lock not its own. Found here when the GPU
// tables' gathering of a scene's distinct values, which merges its threads' sets under such a lock, failed once in ten
// full-HD scenes; measured apart, eight threads adding to a counter under the lock threw
// SynchronizationLockException or hung, under a monitor they counted 2,400,000 of 2,400,000. The runtime's own timer
// queue uses the lock too, which is the fatal error that issue reports.
//
// The project file links this in the function's place (-Wl,--wrap), the way out the issue names: the thread's own
// pthread handle, which no two living threads share and which is never 0 or the "unknown" the runtime would take for
// a reason to ask the managed thread id. With it the same test counts 2,400,000 of 2,400,000 under the lock. To go
// when a runtime with the fault mended is the one this head builds with.
#include <pthread.h>
#include <stdint.h>

uint32_t __wrap_SystemNative_TryGetUInt32OSThreadId(void)
{
	return (uint32_t)(uintptr_t)pthread_self();
}
