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
//   Mark <mark@pab-ai.com>, co-author of the AvaloniaUI port
//
// WaveDude Next is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later
// version. It is distributed WITHOUT ANY WARRANTY. See the LICENSE.md file or
// <https://www.gnu.org/licenses/>.

using Avalonia.Controls;
using WaveDudeNext.App.Dialogs;
using WaveDudeNext.Runs;
using WaveDudeNext.UI;

namespace WaveDudeNext.App.Platform;

/// <summary>
/// The memory warning for the models of <c>WaveDudeNext.UI</c> (<see cref="IMemoryWarning"/>): <see cref="MemoryWarningWindow"/>,
/// modal over the window <paramref name="anchor"/> is in. Avalonia's windows answer later, so the task completes when the window closes
/// and the models go on from the UI thread then (the dialog step of 2026-11-09 made the interface answer as a task for this).
/// Where there is no window (the browser head, whose dialogs are still to come), the question cannot be put: the run
/// goes on as it is, the frames beyond the memory budget in the frame cache (the browser's own storage for the page
/// there), as after a Run in the window with nothing ticked, and the log says so and names the View menu's levers.
/// </summary>
/// <param name="anchor">A control of the view the dialog belongs to; its window is looked up when the question is asked.</param>
public sealed class AvaloniaMemoryWarning(Control anchor) : IMemoryWarning
{
	private static readonly NLog.Logger Logger = NLog.LogManager.GetLogger(typeof(AvaloniaMemoryWarning).FullName!);

	/// <inheritdoc/>
	public Task<MemoryWarningChoice?> AskAsync(RunSpec spec, long budget, long autoBudget, PreviewQuality current)
	{
		if (TopLevel.GetTopLevel(anchor) is Window owner)
		{
			return MemoryWarningWindow.Show(owner, spec, budget, autoBudget, current);
		}
		Logger.Info("The run's frames do not fit the memory budget ({Budget:0.0} GB) and this head cannot ask yet: the run goes on as it is, the rest of its frames in the frame cache (a lower preview resolution, fewer frames or 8-bit samples are in the View menu)",
			FrameMemory.ToGigabytes(budget));
		return Task.FromResult<MemoryWarningChoice?>(new MemoryWarningChoice(current, CacheAuto: false, Mute: false));
	}
}
