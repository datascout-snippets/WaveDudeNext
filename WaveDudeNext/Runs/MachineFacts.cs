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

using WaveDudeNext.Engine;

namespace WaveDudeNext.Runs;

/// <summary>
/// What a first start learns about the machine before choosing the preview quality (2026-10-31, the user's design;
/// <see cref="PreviewDefaults"/> makes the choice): the physical memory, the processor's threads, the OpenCL GPU the
/// engine would take and whether the frame cache's drive incurs a seek penalty. <see cref="Read"/> gathers them in a
/// few milliseconds, most of it the OpenCL enumeration; the record itself is immutable and compared by value, so the
/// harness builds the facts of machines it does not run on. In a browser page the program is not on the machine but
/// in the page's memory (<see cref="Browser"/>), which decides there whatever the machine has. Thread-free.
/// </summary>
/// <param name="PhysicalBytes">Bytes of physical memory as the system reports them (<see cref="FrameMemory.PhysicalBytes"/>), a little under the nominal size on Windows (about 15.9 GB on a 16 GB machine).</param>
/// <param name="Threads">Logical processors.</param>
/// <param name="Gpu">The OpenCL GPU the engine would take, by name, "(shared memory)" added for one integrated with the processor; empty when there is none or OpenCL is missing.</param>
/// <param name="CacheDrive">The frame cache's volume for a log line (<see cref="StorageDevice.VolumeOf"/>: "C:" on Windows, the mount point on Linux, else the cache folder's root).</param>
/// <param name="CacheDriveSeekPenalty">Whether the cache drive incurs a seek penalty, a hard disk; null when the system cannot say (<see cref="StorageDevice.IncursSeekPenalty"/>).</param>
/// <param name="Browser">Whether the program runs in a browser page: <see cref="PhysicalBytes"/> is then the ceiling of the page's WebAssembly memory, of which a run's frames get what <see cref="FrameMemory.Budget(int, long)"/> says, the GPU is not OpenCL's to name and the cache is the storage the browser manages for the page.</param>
public sealed record MachineFacts(long PhysicalBytes, int Threads, string Gpu, string CacheDrive, bool? CacheDriveSeekPenalty, bool Browser = false)
{
	/// <summary>Reads the facts of this machine; a missing OpenCL runtime counts as no GPU.</summary>
	public static MachineFacts Read()
	{
		string gpu = "";
		try
		{
			if (GpuDeviceInfo.FindAll() is [var device, ..])
			{
				gpu = device.Discrete ? device.Name : device.Name + " (shared memory)";
			}
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
		{
			// no OpenCL runtime: the CPU engine serves
		}
		return new MachineFacts(FrameMemory.PhysicalBytes(), Environment.ProcessorCount, gpu, StorageDevice.VolumeOf(FrameCache.Directory), StorageDevice.IncursSeekPenalty(FrameCache.Directory),
			OperatingSystem.IsBrowser());
	}
}
