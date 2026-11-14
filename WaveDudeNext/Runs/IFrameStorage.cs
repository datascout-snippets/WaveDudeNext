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

namespace WaveDudeNext.Runs;

/// <summary>
/// Where the part of a run's frames that does not fit the memory goes: the place <see cref="FrameCache"/> asks for
/// room and for a store's file (2026-11-13, the user's choice when the browser head came: a page has no drive, its
/// disk is the storage the browser manages for it, so the head sets its own here, <see cref="FrameCache.Storage"/>,
/// as it sets its GPU engine at the engine factory). The default is the folder on a drive (<see cref="DiskFrameStorage"/>).
/// Threads: <see cref="NewPath"/>, <see cref="AvailableBytes"/> and <see cref="Create"/> on the thread that starts a run
/// (the UI thread), <see cref="Sweep"/> once at the application's start.
/// </summary>
public interface IFrameStorage
{
	/// <summary>Where this is, as the message of a run that finds no room says it after "of frame cache": "on C:\", "in the browser's storage".</summary>
	string Place { get; }

	/// <summary>A fresh name for a run's file, one that <see cref="Create"/> takes and a leftover of a crashed session is recognised by.</summary>
	string NewPath();

	/// <summary>Bytes a new file may take here now.</summary>
	long AvailableBytes();

	/// <summary>
	/// Creates the empty file <paramref name="path"/>, which must not exist; it is gone again when it is closed.
	/// </summary>
	/// <param name="path">A name from <see cref="NewPath"/>; the default storage takes any file path.</param>
	/// <exception cref="IOException">The file cannot be created.</exception>
	IFrameFile Create(string path);

	/// <summary>Removes the files earlier sessions left behind and no running one holds open. May return before they are gone.</summary>
	void Sweep();
}

/// <summary>
/// The file of a <see cref="FrameStore"/>'s spilled frames, as <see cref="IFrameStorage.Create"/> made it: sized once
/// for what it will hold, written and read at byte offsets, deleted when closed. Threads: one writer at a time, readers
/// on any thread and while it writes; <see cref="Close"/> when none of them is inside a call (the store's lock sees to it).
/// </summary>
public interface IFrameFile
{
	/// <summary>Sizes the file to <paramref name="bytes"/> bytes, so a lack of room shows now and not in the middle of a run.</summary>
	/// <exception cref="IOException">There is no room for it.</exception>
	void SetLength(long bytes);

	/// <summary>Writes all of <paramref name="data"/> at <paramref name="offset"/> bytes from the file's start.</summary>
	/// <exception cref="IOException">The write failed.</exception>
	/// <exception cref="ObjectDisposedException">The file is closed.</exception>
	void Write(ReadOnlySpan<byte> data, long offset);

	/// <summary>Reads into <paramref name="data"/> from <paramref name="offset"/> bytes from the file's start; returns the bytes read, fewer than asked only at the file's end.</summary>
	/// <exception cref="IOException">The read failed.</exception>
	/// <exception cref="ObjectDisposedException">The file is closed.</exception>
	int Read(Span<byte> data, long offset);

	/// <summary>Closes the file, which deletes it. Safe to call more than once.</summary>
	void Close();
}
