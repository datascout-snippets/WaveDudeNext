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

// The program's 4 GB in two parts (2026-11-13, the user's word that the browser head is to use its memory to the
// full): the lower one for everything the runtime and the libraries allocate, the upper one for a run's frames alone
// (FrameArena.cs hands that part out).
//
// Why two parts: .NET 10's threaded runtime cannot start a thread whose own structure lies above 2 GB. Its script
// (dotnet.runtime.js) compares the thread's address as the program hands it over, a signed 32-bit number, with the
// unsigned one it noted when the worker began: "expected monoThreadInfo to be set already when registering", in
// every worker, and the page hangs. With the frames in the general heap, 3 GB of them would push whatever is
// allocated next up there, and this application starts threads at any time. The frames themselves may lie anywhere:
// nothing but the program's own code reads and writes them.
//
// How: everything in the program that allocates does so through malloc (dlmalloc in this runtime), and malloc gets
// its memory from sbrk, which moves the end of the heap, the "break", upwards and fails once the break would pass
// the memory's ceiling. So the heap is first made to reach just below the limit, by one large allocation, the room;
// then the room is shrunk to a small block, which leaves the rest of it malloc's to hand out again; then the break
// is put at the ceiling, after which every request of malloc's for more fails; then the small block goes too.
// Nothing malloc ever hands out lies above the limit from then on, and a program that needed more than its part
// would be out of memory rather than up there.
//
// Why shrunk before the break moves: while the room is whole, the heap has nothing left to hand out, and from the
// break's move on it cannot get more, so a thread that allocated between the two would be refused, which ends the
// runtime; shrunk first, the rest of the room is malloc's again at once. This malloc never gives memory back to the
// system (read in the SDK's dlmalloc.c on 2026-11-14: it is built with MORECORE_CANNOT_TRIM; the note that stood
// here said it did, wrongly, from forms of this file whose allocation the compiler had left out, see below), so
// what is freed stays the heap's. Should another thread get in the way (it would have to move the break within
// these few lines), the heap's end is not where it must be, nothing is changed and the whole is tried again.
//
// What the break at the ceiling does besides (the same reading): the C library's sbrk makes the memory reach the
// break before it answers, a request for nothing included, and malloc asks for nothing once after a request of its
// own was refused. So the first allocation the heap cannot serve grows the program's memory to its ceiling, all
// 4 GB of it at once. Nothing is lost by that (the pages are the frames' part, untouched until a frame is written),
// but a memory at its ceiling does not by itself say that the frames filled it.
//
// The memory above the limit is never malloc's; it is made usable piece by piece (wavedude_commit), as the frames
// need it.
//
// (Linking an sbrk of our own in the C library's place, -Wl,--wrap=sbrk, would have been the plainer way; the
// linker then no longer finds the sbrk the runtime's scripts ask to have exported.)
#include <emscripten/heap.h>
#include <stdint.h>
#include <stdlib.h>

// between the heap's end and the limit: the allocator's own bookkeeping and rounding, and whatever another thread
// allocates beyond the large allocation while it exists
#define SLACK ((uintptr_t)16 << 20)

// what the room is shrunk to until the break has moved
#define KEPT ((size_t)64 << 10)

// Where the room's address is put down. A compiler may leave out an allocation whose block nothing ever looks at,
// with its free, and did: the heap was then never made to reach the limit (2026-11-13: three forms of this file
// failed for it, and the one that printed the address worked). An address written where anything might read it is
// one the compiler must really obtain.
static void *volatile seen;

static uintptr_t heap_end; // 0 until the two parts are made

// The end of the memory the program can have, in bytes (one 64 KiB page short of 4 GB at the most).
uint32_t wavedude_heap_ceiling(void)
{
	return (uint32_t)emscripten_get_heap_max();
}

// The heap's end: where it stays once the two parts are made, its present end before.
uint32_t wavedude_heap_end(void)
{
	return (uint32_t)(heap_end != 0 ? heap_end : __atomic_load_n(emscripten_get_sbrk_ptr(), __ATOMIC_SEQ_CST));
}

// Makes the two parts: from now on the heap ends below limit. 1 when that is so, 0 when it could not be done (the
// heap is past the limit or too close to it already, or the memory for it was refused); nothing is changed then.
int wavedude_partition(uint32_t limit)
{
	uintptr_t *brk = emscripten_get_sbrk_ptr();
	uintptr_t ceiling = emscripten_get_heap_max();
	uintptr_t now = __atomic_load_n(brk, __ATOMIC_SEQ_CST);
	if (heap_end != 0 || ceiling <= limit || limit <= now + 2 * SLACK)
	{
		return 0;
	}
	for (int attempt = 0; attempt < 4; attempt++)
	{
		now = __atomic_load_n(brk, __ATOMIC_SEQ_CST);
		if (limit <= now + 2 * SLACK)
		{
			return 0;
		}
		void *room = malloc(limit - now - SLACK);
		seen = room;
		if (room == 0)
		{
			return 0;
		}
		void *kept = realloc(room, KEPT);
		seen = kept;
		if (kept == 0)
		{
			free(room);
			return 0;
		}
		// the break as it was at this very moment is the heap's end for good
		uintptr_t end = __atomic_exchange_n(brk, ceiling, __ATOMIC_SEQ_CST);
		if (end <= limit && end >= limit - 2 * SLACK)
		{
			heap_end = end;
			free(kept);
			return 1;
		}
		// past the limit after all, or the room was given back to the system all the same: as it was, and again
		__atomic_store_n(brk, end, __ATOMIC_SEQ_CST);
		free(kept);
	}
	return 0;
}

// Makes the program's memory at least end bytes long, for a block above the limit that ends there. 1 when it is.
// The library refuses to grow a memory that is that long already when it looks ("another thread might increase the
// size in between, so return a failure, and let the caller retry", its library.js), and another thread can: a
// malloc that was refused grows the memory to its ceiling (see above). So the size is looked at again after a
// refusal, which had otherwise ended the run's frames in memory there and sent the rest to the frame cache.
int wavedude_commit(uint32_t end)
{
	if (emscripten_get_heap_size() >= end)
	{
		return 1;
	}
	return emscripten_resize_heap(end) || emscripten_get_heap_size() >= end;
}
