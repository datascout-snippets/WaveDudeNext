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
using System.Runtime.Versioning;

namespace WaveDudeNext.Runs;

/// <summary>
/// How much memory a run's frames may take before the rest goes to disk (2026-10-14). The budget is decided
/// when a run starts, from what the machine has to spare at that moment: a share of the physical memory (the
/// user's setting), but never more than what is available now minus a margin for the rest of the application
/// and the system, so a run never pushes the machine into its page file. Windows answers through
/// <c>GlobalMemoryStatusEx</c>; Linux through <c>/proc/meminfo</c> (<c>MemTotal</c> and <c>MemAvailable</c>, the
/// kernel's own estimate of what new allocations may take without swapping, the page cache's reclaimable part
/// counted as Windows counts its standby list; and under strict overcommit, <c>vm.overcommit_memory</c> 2, the
/// commit left as well, <c>CommitLimit</c> less <c>Committed_AS</c>, the lesser of the two taken as on Windows);
/// macOS through Mach's <c>host_statistics64</c> (the free, inactive, speculative and purgeable pages, what the
/// system hands out without compressing or swapping; the total is the runtime's <c>hw.memsize</c>); elsewhere the
/// runtime's view of the total serves, with half of it taken as available (2026-11-12, the Linux and macOS ports).
/// Thread-free.
/// </summary>
public static class FrameMemory
{
	/// <summary>The share value meaning "no cap: whatever is free now minus the margin" (2026-10-15, the user's choice: the application decides, a percentage is only an upper bound).</summary>
	public const int Automatic = 0;

	/// <summary>Share of the physical memory a run may take when the user has not chosen one: <see cref="Automatic"/>.</summary>
	public const int DefaultPercent = Automatic;

	/// <summary>Memory left untouched below what is available: the application, the engines' own buffers and the system need room too.</summary>
	public const long Margin = 2L << 30;

	/// <summary>Bytes of physical memory in the machine.</summary>
	public static long PhysicalBytes()
	{
		if (OperatingSystem.IsWindows() && TryStatus(out MemoryStatus status))
		{
			return (long)Math.Min(status.TotalPhys, long.MaxValue);
		}
		if (OperatingSystem.IsLinux() && TryMemInfo(out MemInfo info))
		{
			return info.TotalBytes;
		}
		// macOS and the rest: the runtime reads hw.memsize there (a cgroup limit on Linux, where this is the fallback)
		return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
	}

	/// <summary>
	/// Bytes free for new allocations right now: the lesser of the free physical memory (the standby cache counts as
	/// free, as Windows reports it) and the commit still available (physical memory plus page file minus what every
	/// process has committed). Windows refuses any allocation at the commit limit, which with a small page file comes
	/// before the physical memory is used up: a full-HD run of 12,500 frames filled the budget by the physical
	/// measure alone and the preview's next bitmap then failed to allocate (2026-10-19).
	/// </summary>
	public static long AvailableBytes()
	{
		if (OperatingSystem.IsWindows() && TryStatus(out MemoryStatus status))
		{
			return (long)Math.Min(Math.Min(status.AvailPhys, status.AvailPageFile), long.MaxValue);
		}
		if (OperatingSystem.IsLinux() && TryMemInfo(out MemInfo info))
		{
			return info.AvailableBytes;
		}
		if (OperatingSystem.IsMacOS() && MacMemory.TryAvailable(out long available))
		{
			return available;
		}
		return PhysicalBytes() / 2;
	}

	/// <summary>
	/// The frame budget for a run started now: what is available minus <see cref="Margin"/>, and no more than
	/// <paramref name="percentOfPhysical"/> of the physical memory when that is not <see cref="Automatic"/>; never negative.
	/// </summary>
	/// <param name="percentOfPhysical">The user's upper bound, 1 to 100 (clamped), or <see cref="Automatic"/> for none.</param>
	/// <param name="reclaimedBytes">Memory the caller gives back before the new frames are allocated (the outgoing
	/// run's frames), counted as available: Windows still reports it as in use when asked now. Without it a re-run
	/// measured next to its predecessor's store under-budgets by that store's size and spills a run that fits
	/// (found on a 16 GB laptop, 2026-10-16).</param>
	public static long Budget(int percentOfPhysical, long reclaimedBytes = 0)
		=> Math.Min(Allocator.Capacity, OperatingSystem.IsBrowser()
			? BrowserBudget(PhysicalBytes(), percentOfPhysical)
			: Budget(AvailableBytes(), PhysicalBytes(), percentOfPhysical, reclaimedBytes));

	private static IFrameAllocator _allocator = new NativeFrameAllocator();

	/// <summary>
	/// Where the stores' frames are allocated: the process's native memory unless a head has set its own
	/// (<see cref="IFrameAllocator"/>), whose capacity then bounds the <see cref="Budget(int, long)"/>. Set once, at the
	/// application's start, before the first run; a store keeps the one it was made with.
	/// </summary>
	public static IFrameAllocator Allocator
	{
		get => _allocator;
		set => _allocator = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <summary>What the program in a browser page keeps of its WebAssembly memory for itself: 1 GB (see <see cref="BrowserBudget"/>).</summary>
	public const long BrowserReserve = 1L << 30;

	/// <summary>
	/// The frame budget in a browser. A page's program has its WebAssembly memory and no more: of a fixed ceiling that
	/// nothing else shares and no free figure is reported for, so what a desktop measures ("available" less
	/// <see cref="Margin"/>) came to nothing there. The frames get all of it but <see cref="BrowserReserve"/>, which
	/// stays for the runtime, the engines' host arrays and the pictures (measured in the browser head on 2026-11-13:
	/// the program itself took 0.29 GB at its start and 0.43 GB beside a run's frames). The browser head is built with
	/// the ceiling at 4 GB, the most a 32-bit program addresses, so the frames have 3 GB (2026-11-13, the user's word
	/// that the memory is to be used more boldly; half of the 2 GB a runtime has by default, 1 GB, since 2026-11-12),
	/// and it keeps them in a part of the memory of their own (its <see cref="Allocator"/>, which says why). Of a
	/// memory too small to keep the reserve, half. The user's percentage bounds it as on a desktop, as a share of the ceiling. The
	/// outgoing run's frames are released before the new ones are allocated, so nothing is added for them; and should
	/// the memory not be had after all, the store writes the rest to its file, as everywhere.
	/// </summary>
	/// <param name="heapBytes">The memory's ceiling (<see cref="PhysicalBytes"/> there).</param>
	/// <param name="percentOfPhysical">The user's upper bound, 1 to 100 (clamped), or <see cref="Automatic"/> for none.</param>
	internal static long BrowserBudget(long heapBytes, int percentOfPhysical)
	{
		long share = Math.Max(heapBytes - BrowserReserve, heapBytes / 2);
		return percentOfPhysical <= Automatic ? share : Math.Min(share, (long)(heapBytes * (Math.Clamp(percentOfPhysical, 1, 100) / 100.0)));
	}

	/// <summary>The arithmetic of <see cref="Budget(int, long)"/> on given measurements, exact for the harness.</summary>
	internal static long Budget(long availableBytes, long physicalBytes, int percentOfPhysical, long reclaimedBytes)
	{
		long byAvailable = availableBytes + reclaimedBytes - Margin;
		if (percentOfPhysical <= Automatic)
		{
			return Math.Max(0, byAvailable);
		}
		double share = Math.Clamp(percentOfPhysical, 1, 100) / 100.0;
		long byShare = (long)(physicalBytes * share);
		return Math.Max(0, Math.Min(byShare, byAvailable));
	}

	/// <summary>A byte count as gigabytes with one decimal and the unit, in the current culture, for the UI.</summary>
	public static string Gigabytes(long bytes) => ToGigabytes(bytes).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture) + " GB";

	/// <summary>A byte count in gigabytes (2^30 bytes), for a log line's <c>{Name:0.0} GB</c>, which NLog formats invariantly.</summary>
	public static double ToGigabytes(long bytes) => bytes / (double)(1L << 30);

	private static bool TryStatus(out MemoryStatus status)
	{
		status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
		return GlobalMemoryStatusEx(ref status);
	}

	/// <summary>The two numbers of Linux's <c>/proc/meminfo</c> this class needs, in bytes.</summary>
	/// <param name="TotalBytes"><c>MemTotal</c>.</param>
	/// <param name="AvailableBytes"><c>MemAvailable</c> (<c>MemFree</c> on a kernel before 3.14), and under strict overcommit no more than the commit left.</param>
	internal readonly record struct MemInfo(long TotalBytes, long AvailableBytes);

	/// <summary>Reads <c>/proc/meminfo</c> and the overcommit mode; false where either cannot be read or the file names no total.</summary>
	[SupportedOSPlatform("linux")]
	private static bool TryMemInfo(out MemInfo info)
	{
		try
		{
			bool strict = File.Exists(OvercommitPath) && File.ReadAllText(OvercommitPath).Trim() == "2";
			return ParseMemInfo(File.ReadAllText(MemInfoPath), strict, out info);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			info = default;
			return false;
		}
	}

	private const string MemInfoPath = "/proc/meminfo";

	private const string OvercommitPath = "/proc/sys/vm/overcommit_memory";

	/// <summary>
	/// Reads the numbers out of the text of <c>/proc/meminfo</c> (lines of <c>Name:   12345 kB</c>): the total from
	/// <c>MemTotal</c>, the available from <c>MemAvailable</c>, or <c>MemFree</c> where a kernel before 3.14 has none,
	/// and with <paramref name="strictOvercommit"/> no more than <c>CommitLimit</c> less <c>Committed_AS</c>, never
	/// negative. False without a total. Exact for the harness.
	/// </summary>
	internal static bool ParseMemInfo(string text, bool strictOvercommit, out MemInfo info)
	{
		var values = new Dictionary<string, long>(StringComparer.Ordinal);
		foreach (string line in text.Split('\n'))
		{
			int colon = line.IndexOf(':');
			if (colon <= 0)
			{
				continue;
			}
			ReadOnlySpan<char> rest = line.AsSpan(colon + 1).Trim();
			int end = rest.IndexOf(' ');
			ReadOnlySpan<char> number = end < 0 ? rest : rest[..end];
			if (long.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long kilobytes))
			{
				// every line of the file is in kB but the counts (HugePages_*), which name no unit and are not asked for
				values[line[..colon]] = kilobytes << 10;
			}
		}
		if (!values.TryGetValue("MemTotal", out long total))
		{
			info = default;
			return false;
		}
		long available = values.TryGetValue("MemAvailable", out long memAvailable) ? memAvailable : values.GetValueOrDefault("MemFree");
		if (strictOvercommit && values.TryGetValue("CommitLimit", out long limit) && values.TryGetValue("Committed_AS", out long committed))
		{
			available = Math.Min(available, Math.Max(0, limit - committed));
		}
		info = new MemInfo(total, available);
		return true;
	}

	/// <summary>
	/// Bytes the system hands out without compressing or swapping, from Mach's page counts: the free pages, the
	/// inactive ones (the file cache and what has not been touched for a while, reclaimed first), the speculative
	/// ones (read ahead, as good as free) and the purgeable ones, times the page size. Exact for the harness.
	/// </summary>
	internal static long AvailableFromPages(long free, long inactive, long speculative, long purgeable, long pageSize)
		=> (free + inactive + speculative + purgeable) * pageSize;

	/// <summary>Mach's virtual memory statistics on macOS: <c>host_statistics64</c> with <c>HOST_VM_INFO64</c>, as <c>vm_stat</c> reads them.</summary>
	[SupportedOSPlatform("macos")]
	private static class MacMemory
	{
		private const int HostVmInfo64 = 4;

		/// <summary><c>HOST_VM_INFO64_COUNT</c>: the structure's size in 32-bit integers.</summary>
		private const uint HostVmInfo64Count = 38;

		public static bool TryAvailable(out long bytes)
		{
			var statistics = new VmStatistics64();
			uint count = HostVmInfo64Count;
			if (host_statistics64(mach_host_self(), HostVmInfo64, ref statistics, ref count) != 0)
			{
				bytes = 0;
				return false;
			}
			bytes = AvailableFromPages(statistics.FreeCount, statistics.InactiveCount, statistics.SpeculativeCount, statistics.PurgeableCount, getpagesize());
			return true;
		}

		/// <summary>The <c>vm_statistics64</c> structure of <c>mach/vm_statistics.h</c>, 152 bytes: counts of pages as 32-bit integers, event counts as 64-bit ones.</summary>
		[StructLayout(LayoutKind.Sequential)]
		private struct VmStatistics64
		{
			public uint FreeCount;
			public uint ActiveCount;
			public uint InactiveCount;
			public uint WireCount;
			public ulong ZeroFillCount;
			public ulong Reactivations;
			public ulong PageIns;
			public ulong PageOuts;
			public ulong Faults;
			public ulong CowFaults;
			public ulong Lookups;
			public ulong Hits;
			public ulong Purges;
			public uint PurgeableCount;
			public uint SpeculativeCount;
			public ulong Decompressions;
			public ulong Compressions;
			public ulong SwapIns;
			public ulong SwapOuts;
			public uint CompressorPageCount;
			public uint ThrottledCount;
			public uint ExternalPageCount;
			public uint InternalPageCount;
			public ulong TotalUncompressedPagesInCompressor;
		}

		[DllImport("libc", ExactSpelling = true)]
		private static extern uint mach_host_self();

		[DllImport("libc", ExactSpelling = true)]
		private static extern int host_statistics64(uint host, int flavor, ref VmStatistics64 info, ref uint count);

		[DllImport("libc", ExactSpelling = true)]
		private static extern int getpagesize();
	}

	/// <summary>The <c>MEMORYSTATUSEX</c> structure of <c>kernel32</c>.</summary>
	[StructLayout(LayoutKind.Sequential)]
	private struct MemoryStatus
	{
		public uint Length;
		public uint MemoryLoad;
		public ulong TotalPhys;
		public ulong AvailPhys;
		public ulong TotalPageFile;
		public ulong AvailPageFile;
		public ulong TotalVirtual;
		public ulong AvailVirtual;
		public ulong AvailExtendedVirtual;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);
}
