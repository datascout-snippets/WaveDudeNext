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
using System.Runtime.InteropServices.JavaScript;
using NLog;
using NLog.Targets;

namespace WaveDudeNext.Browser;

/// <summary>
/// The log in the browser's console, styled as the user's own web projects do it (2026-11-12, his <c>logging.js</c>):
/// each line through the console's method of its level, so the developer tools' level filter works, with the level as
/// a small badge and Trace and Debug in grey and blue (<c>waveDudeLog</c> in main.js does the styling). A log line may
/// come from any thread and the page's scripts are reached from one, so the lines are posted to that thread's
/// context, in the order they were written; without a context (never in the browser) they go to the plain console.
/// </summary>
/// <param name="scripts">The synchronisation context of the thread the page's scripts are called from: the one Main starts on.</param>
internal sealed partial class BrowserConsoleTarget(SynchronizationContext? scripts) : Target
{
	/// <inheritdoc />
	protected override void Write(LogEventInfo logEvent)
	{
		string label = string.Create(CultureInfo.InvariantCulture, $"[{logEvent.TimeStamp:HH:mm:ss.fff}] [{Environment.CurrentManagedThreadId}] ");
		string logger = logEvent.LoggerName is { } name ? name[(name.LastIndexOf('.') + 1)..] : "";
		string message = logger + ": " + logEvent.FormattedMessage + (logEvent.Exception is { } exception ? Environment.NewLine + exception : "");
		int level = logEvent.Level.Ordinal;
		if (scripts == null)
		{
			Console.WriteLine(label + logEvent.Level + " " + message);
			return;
		}
		scripts.Post(static state =>
		{
			(int level, string label, string message) = ((int, string, string))state!;
			WriteLine(level, label, message);
		}, (level, label, message));
	}

	/// <summary>main.js: one styled line; <paramref name="level"/> is NLog's ordinal, 0 Trace to 5 Fatal.</summary>
	[JSImport("globalThis.waveDudeLog")]
	private static partial void WriteLine(int level, string label, string message);
}
