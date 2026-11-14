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

namespace WaveDudeNext.Browser;

/// <summary>
/// The way to the page's scripts from any thread of the program. On the threaded runtime the scripts can be called
/// from one thread alone, the runtime's main thread, the one <c>Main</c> ran on; what the other threads need of the
/// page (the graphics card through WebGPU, the browser's storage) is posted to that thread's context and, where the
/// caller needs the outcome, waited for, as a blocking call of a driver is. A hand-over and its answer take 0.3 to
/// 0.6 ms (measured 2026-11-13). Any thread but the scripts' own, which would wait for itself.
/// </summary>
internal static class ScriptThread
{
	private static SynchronizationContext? _context;

	/// <summary>The scripts' thread hands its context over, once, at the program's start (<c>Main</c>).</summary>
	public static void Use(SynchronizationContext context) => _context = context;

	/// <summary>
	/// Runs <paramref name="call"/> on the scripts' thread and waits for its result; an error there is thrown here,
	/// as an <see cref="InvalidOperationException"/> with the error's words and the error inside it.
	/// </summary>
	/// <exception cref="InvalidOperationException">The call failed, the scripts' thread is not known, or this is that thread.</exception>
	public static T Await<T>(Func<Task<T>> call)
	{
		SynchronizationContext scripts = _context ?? throw new InvalidOperationException("the page's script thread is not known");
		if (SynchronizationContext.Current == scripts)
		{
			throw new InvalidOperationException("the page's script thread cannot wait for a call it would have to make itself");
		}
		using var done = new ManualResetEventSlim();
		T result = default!;
		Exception? error = null;
		scripts.Post(async _ =>
		{
			try
			{
				result = await call();
			}
			catch (Exception ex)
			{
				error = ex;
			}
			finally
			{
				done.Set();
			}
		}, null);
		// the analyser knows the browser as a platform without threads; this head runs on the threaded runtime
		// (WasmEnableThreads), and the one thread that must not block, the scripts', is refused above
#pragma warning disable CA1416
		done.Wait();
#pragma warning restore CA1416
		return error == null ? result : throw new InvalidOperationException(error.Message, error);
	}

	/// <summary>A call the page answers later, run on the scripts' thread and waited for.</summary>
	public static void Await(Func<Task> call) => Await(async () =>
	{
		await call();
		return 0;
	});

	/// <summary>A call with a result, run on the scripts' thread and waited for.</summary>
	public static T Invoke<T>(Func<T> call) => Await(() => Task.FromResult(call()));

	/// <summary>A call without a result, run on the scripts' thread and waited for.</summary>
	public static void Invoke(Action call) => Await(() =>
	{
		call();
		return Task.FromResult(0);
	});

	/// <summary>Runs <paramref name="call"/> on the scripts' thread without waiting for it; an error there goes to <paramref name="failed"/>, on that thread.</summary>
	public static void Post(Func<Task> call, Action<Exception> failed)
	{
		SynchronizationContext scripts = _context ?? throw new InvalidOperationException("the page's script thread is not known");
		scripts.Post(async _ =>
		{
			try
			{
				await call();
			}
			catch (Exception ex)
			{
				failed(ex);
			}
		}, null);
	}
}
