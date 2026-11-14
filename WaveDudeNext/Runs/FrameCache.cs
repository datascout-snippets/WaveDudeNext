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

using System.Globalization;

namespace WaveDudeNext.Runs;

/// <summary>
/// Where <see cref="FrameStore"/> files live: one per run in the <see cref="Storage"/>, by default a folder under the
/// user's local application data, the files named so that a leftover of a crashed process is recognisable. UI thread.
/// </summary>
public static class FrameCache
{
	/// <summary>File extension of a frame store.</summary>
	public const string Extension = ".wdframes";

	/// <summary><c>%LOCALAPPDATA%\WaveDudeNext\cache</c>, the folder of the default storage; created on first use.</summary>
	public static string Directory { get; } =
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WaveDudeNext", "cache");

	private static IFrameStorage _storage = new DiskFrameStorage(Directory);

	/// <summary>
	/// The place of the stores' files: the folder <see cref="Directory"/> on its drive unless a head has set its own
	/// (2026-11-13: the browser head's is the storage the browser manages for the page, which has no drive). Set once,
	/// at the application's start, before <see cref="Sweep"/> and the first run.
	/// </summary>
	public static IFrameStorage Storage
	{
		get => _storage;
		set => _storage = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <summary>A fresh name for a run's store in the <see cref="Storage"/>; the default's is a file path, <c>run-&lt;time&gt;-&lt;pid&gt;-&lt;n&gt;.wdframes</c> in <see cref="Directory"/>.</summary>
	public static string NewPath() => Storage.NewPath();

	/// <summary>
	/// Checks that the <see cref="Storage"/> has room for a store of <paramref name="bytes"/> bytes (plus a small margin).
	/// </summary>
	/// <exception cref="RunSpecException">Not enough free space; the message states the size needed.</exception>
	public static void CheckSpace(long bytes) => CheckSpace(Storage, bytes);

	/// <summary><see cref="CheckSpace(long)"/> for <paramref name="storage"/>.</summary>
	/// <param name="storage">The place that is asked for its room.</param>
	/// <param name="bytes">The size of the store's file.</param>
	/// <exception cref="RunSpecException">Not enough free space; the message states the size needed.</exception>
	internal static void CheckSpace(IFrameStorage storage, long bytes)
	{
		long available = storage.AvailableBytes();
		long needed = bytes + (64L << 20);
		if (available < needed)
		{
			// the text reaches the log, the status bar and the dialog alike, so its numbers take the decimal point of every
			// other number in the log (2026-11-03, the user's rule: a report's reader must not trip over "1806,2 GB")
			throw new RunSpecException(
				string.Create(CultureInfo.InvariantCulture,
					$"The run needs {bytes / (1024.0 * 1024 * 1024):0.#} GB of frame cache {storage.Place}, but only {available / (1024.0 * 1024 * 1024):0.#} GB is free. ")
				+ "Reduce the canvas, the steps or raise 'Visualize every', or turn the intensity preview off.");
		}
	}

	/// <summary>The file of a store's spilled frames in the <see cref="Storage"/>, empty; it is gone when it is closed.</summary>
	/// <param name="path">The store's path: a name from <see cref="NewPath"/>, or for the default storage any file path.</param>
	internal static IFrameFile CreateFile(string path) => Storage.Create(path);

	/// <summary>
	/// Removes leftover stores that no running process holds open (a live store's file cannot be deleted and is
	/// skipped). Call once at start-up.
	/// </summary>
	public static void Sweep() => Storage.Sweep();
}
