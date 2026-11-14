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

using System.ComponentModel;

namespace WaveDudeNext.App.Theming;

/// <summary>
/// The system's own font for the application's text, in a head that carries a font of its own because its platform
/// hands it none (the browser head, 2026-11-14, the user's design): a page gets the fonts installed on the machine
/// only with the user's permission, which the browser asks for at a click or a key press. The head sets
/// <see cref="App.SystemFont"/> where it can offer this; the View menu then shows "Use the system's font" with a
/// check mark that says whether the system's font is in use: set at the start where the permission was given
/// before, cleared where it was not; choosing the entry while it is cleared asks for the permission and, once it is
/// given, switches the text at once; choosing it while it is set goes back to the font the head carries. The
/// desktop heads take the platform's font anyway and leave the hook empty. UI thread.
/// </summary>
public interface ISystemFont : INotifyPropertyChanged
{
	/// <summary>Whether the application's text is in the system's font now; raised through <see cref="INotifyPropertyChanged"/> on the UI thread.</summary>
	bool IsUsed { get; }

	/// <summary>
	/// Turns the system's font on, asking for the permission where it has not been given yet, or off. Call it from a
	/// command the user ran: the browser shows its question only shortly after a click or a key press. A refusal, a
	/// system without the font and an error are said in the log and leave <see cref="IsUsed"/> as it was; the task
	/// does not fail.
	/// </summary>
	Task SetUsedAsync(bool use);
}
