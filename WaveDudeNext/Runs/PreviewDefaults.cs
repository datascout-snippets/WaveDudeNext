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

/// <summary>The classes a first start sorts the machine into; <see cref="PreviewDefaults"/> decides and says what each gets.</summary>
public enum PreviewTier
{
	/// <summary>Little memory, a hard disk under the frame cache, no OpenCL GPU or few threads: half resolution, every second frame, 8-bit samples.</summary>
	Low,

	/// <summary>Everything else: full resolution, every frame, 8-bit samples, the fixed defaults.</summary>
	Middle,

	/// <summary>Plenty of memory: full resolution, every frame, 16-bit samples, dithered to 16 bits.</summary>
	High,

	/// <summary>A browser page, whatever the machine: half resolution, every frame, 8-bit samples.</summary>
	Browser,
}

/// <summary>
/// The preview quality a first start chooses for the machine (2026-10-31, the user's design: one decision, made only
/// when no settings file exists, visible and changeable in the View menu). The levers buy memory and readback, never
/// step time: 8-bit samples halve a frame against 16-bit, half resolution divides it by four, every second frame
/// halves the store again; a Full HD run of 1250 frames with both layers takes 9.7 GB at 16-bit, 4.8 GB at 8-bit and
/// 0.6 GB at half resolution with every second frame. The weakest link decides, memory first: 32 GB or more is
/// <see cref="PreviewTier.High"/> whatever else the machine has, since little spills at that size; below it any one
/// of under 16 GB, a cache drive with a seek penalty, no OpenCL GPU or four threads or fewer makes
/// <see cref="PreviewTier.Low"/>; the rest is <see cref="PreviewTier.Middle"/>, the fixed defaults. The thresholds are
/// tested at 30 and 15 GiB because Windows reports the physical memory a little under the nominal size (15.9 GB on a
/// 16 GB machine). An unknown drive kind counts as no seek penalty. Pure functions; the harness proves the table.
/// <para>
/// A browser page is <see cref="PreviewTier.Browser"/> whatever the machine (2026-11-14, the user's choice): the
/// program there has 3 GB for a run's frames and no more (<see cref="FrameMemory.Budget(int, long)"/>), and the
/// settings are not kept, so every start of the page makes this choice. Half resolution with every frame in 8-bit
/// keeps the frames of every one of the 33 examples in that memory, by <see cref="RunSpec.StoreBytesAt"/>'s
/// arithmetic on their canvases and steps with both planes: the largest 2.90 GB (Examples 3, 7, 21 and 23), the
/// default project 1.21 GB. Examples 7 and 16 were shortened for it the same day (10,000 steps each until then,
/// 4.83 and 3.14 GB; now 6000 and 9000, 2.90 and 2.83 GB). The harness reckons it and names an example that does
/// not fit; such a run's rest goes to the frame cache, as everywhere.
/// </para>
/// </summary>
public static class PreviewDefaults
{
	/// <summary>Physical memory from which a machine is <see cref="PreviewTier.High"/>: a nominal 32 GB as Windows reports it.</summary>
	public const long HighBytes = 30L << 30;

	/// <summary>Physical memory below which a machine is <see cref="PreviewTier.Low"/>: a nominal 16 GB as Windows reports it.</summary>
	public const long LowBytes = 15L << 30;

	/// <summary>Logical processors at or below which a machine is <see cref="PreviewTier.Low"/>.</summary>
	public const int LowThreads = 4;

	/// <summary>The bits per channel the <see cref="PreviewTier.High"/> preview dithers to.</summary>
	public const int HighDitherBits = 16;

	/// <summary>The tier of a machine with these facts.</summary>
	public static PreviewTier TierOf(MachineFacts facts)
	{
		if (facts.Browser)
		{
			return PreviewTier.Browser;
		}
		if (facts.PhysicalBytes >= HighBytes)
		{
			return PreviewTier.High;
		}
		bool weak = facts.PhysicalBytes < LowBytes || facts.CacheDriveSeekPenalty == true || facts.Gpu.Length == 0 || facts.Threads <= LowThreads;
		return weak ? PreviewTier.Low : PreviewTier.Middle;
	}

	/// <summary>The preview quality of a tier, the intensity as its root as every preview keeps it.</summary>
	public static PreviewQuality QualityOf(PreviewTier tier) => tier switch
	{
		PreviewTier.High => new PreviewQuality(1, 1, FrameFormat.Half),
		PreviewTier.Low => new PreviewQuality(2, 2, FrameFormat.Index),
		PreviewTier.Browser => new PreviewQuality(2, 1, FrameFormat.Index),
		_ => new PreviewQuality(1, 1, FrameFormat.Index),
	};

	/// <summary>The preview dithering of a tier: <see cref="HighDitherBits"/> for <see cref="PreviewTier.High"/>, off (0) otherwise.</summary>
	public static int DitherBitsOf(PreviewTier tier) => tier == PreviewTier.High ? HighDitherBits : 0;

	/// <summary>The tier's settings in words for the log ("half resolution, every second frame, 8-bit samples"); not localised.</summary>
	public static string Describe(PreviewTier tier) => tier switch
	{
		PreviewTier.High => "full resolution, every frame, 16-bit samples dithered to 16 bits",
		PreviewTier.Low => "half resolution, every second frame, 8-bit samples",
		PreviewTier.Browser => "half resolution, every frame, 8-bit samples",
		_ => "full resolution, every frame, 8-bit samples",
	};
}
