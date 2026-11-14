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

using Avalonia;
using Avalonia.Controls;
using WaveDudeNext.App.Theming;
using WaveDudeNext.UI;

namespace WaveDudeNext.App;

/// <summary>
/// The main window of the desktop head: the frame around the <see cref="MainView"/>, which is the application (the
/// menus, the panes, the models; since 2026-11-12 a view of its own, so the browser head, which has no windows, shows
/// the same one). The window keeps what only a window has: the title, the placement in the settings, the closing with
/// its save question, the zoom.
/// </summary>
public partial class MainWindow : Window
{
	private bool _closeConfirmed;

	/// <summary>Creates the window around its view: the placement from the settings, the zoom.</summary>
	public MainWindow()
	{
		InitializeComponent();
		// the title's inputs are the view's document
		DataContext = View;
		RestorePlacement();
		Closing += OnClosing;
		WindowZoom.Attach(this);
	}

	/// <summary>The application's main view, the window's content.</summary>
	public MainView View => view;

	/// <summary>Avalonia's Closing cannot wait for the save question: it is cancelled, the question asked, and the window closed again with the answer in hand.</summary>
	private async void OnClosing(object? sender, WindowClosingEventArgs e)
	{
		if (_closeConfirmed)
		{
			return;
		}
		e.Cancel = true;
		if (!await View.ConfirmDiscardAsync())
		{
			return;
		}
		_closeConfirmed = true;
		View.SaveLayout();
		SavePlacement();
		View.Shutdown();
		Settings.Default.TrySave();
		Close();
	}

	/// <summary>
	/// The window's size, position and state as the last session left them (the size at zoom 1, which the zoom multiplies).
	/// The settings hold the whole window's size, frame included, as the WPF window's Width and Height are; Avalonia's are
	/// the client area, so the frame comes off here and goes back on in <see cref="SavePlacement"/>, which the platform
	/// knows before the window shows (2026-11-11, the user's choice of one settings file for both heads with no
	/// discrepancy: the same numbers had made this window 16 px wider and 39 px taller than the WPF one at 100 %). The
	/// zoom multiplies the client later (<see cref="WindowZoom.Attach"/>) and never the frame, which is the OS's, so what
	/// comes off is the frame at zoom 1. Where the platform does not know its frame before showing (X11), the client stands for the whole.
	/// </summary>
	private void RestorePlacement()
	{
		Settings settings = Settings.Default;
		Size frame = FrameSize is { } outer ? outer - ClientSize : default;
		Width = Math.Max(1, settings.WindowWidth - frame.Width / WindowZoom.Scale);
		Height = Math.Max(1, settings.WindowHeight - frame.Height / WindowZoom.Scale);
		if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
		{
			WindowStartupLocation = WindowStartupLocation.Manual;
			Position = new PixelPoint((int)Math.Round(settings.WindowLeft), (int)Math.Round(settings.WindowTop));
		}
		if (settings.WindowState == WindowMode.Maximized)
		{
			WindowState = WindowState.Maximized;
		}
	}

	/// <summary>Never saved as minimised; a maximised window keeps its normal size for the next start.</summary>
	private void SavePlacement()
	{
		Settings settings = Settings.Default;
		settings.WindowState = WindowState == WindowState.Maximized ? WindowMode.Maximized : WindowMode.Normal;
		if (WindowState == WindowState.Normal)
		{
			// the whole window at zoom 1, as the WPF window writes it (see RestorePlacement)
			Size frame = FrameSize is { } outer ? outer - ClientSize : default;
			settings.WindowWidth = (Width + frame.Width) / WindowZoom.Scale;
			settings.WindowHeight = (Height + frame.Height) / WindowZoom.Scale;
			settings.WindowLeft = Position.X;
			settings.WindowTop = Position.Y;
		}
	}
}
