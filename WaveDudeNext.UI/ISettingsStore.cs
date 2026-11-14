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

using System.Text;
using System.Xml;
using WaveDudeNext.Model;

namespace WaveDudeNext.UI;

/// <summary>
/// Where the user settings are kept, and in what form. On a desktop one XML file (<see cref="Settings.FilePath"/>,
/// the default); a head whose platform has no such file sets its own as <see cref="SettingsStore.Current"/>, in
/// the form its platform is used to (2026-11-14, the user's choice for the browser head: the browser's local
/// storage, a key for each setting). The store is handed the settings themselves, not a text of them, since the
/// forms differ. Called by <see cref="Settings"/> alone, on the UI thread.
/// </summary>
public interface ISettingsStore
{
	/// <summary>What the store is, for a log line: the file's path, or the name of what stands for it.</summary>
	string Place { get; }

	/// <summary>The stored settings, or null when nothing is stored yet, which makes the start a first start.</summary>
	/// <exception cref="UnreadableFileException">What is stored cannot be read as settings; the exception carries its text for the log.</exception>
	/// <exception cref="IOException">The store could not be read (also <see cref="UnauthorizedAccessException"/>).</exception>
	Settings? Load();

	/// <summary>Replaces what is stored with <paramref name="settings"/>.</summary>
	/// <exception cref="IOException">The store could not be written (also <see cref="UnauthorizedAccessException"/>).</exception>
	void Save(Settings settings);
}

/// <summary>
/// The store the user settings are read from and written to (<see cref="ISettingsStore"/>). A class of its own and
/// not a member of <see cref="Settings"/>: the settings are read at that class's first use, which setting a member
/// of it would be. A head sets <see cref="Current"/> once, at its start, before anything asks for a setting.
/// </summary>
public static class SettingsStore
{
	private static ISettingsStore _current = new SettingsFile();

	/// <summary>The store in use: the settings file unless a head has set its own.</summary>
	public static ISettingsStore Current
	{
		get => _current;
		set => _current = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <summary>The default store: the XML file at <see cref="Settings.FilePath"/>, UTF-8, its folder made at the first write.</summary>
	private sealed class SettingsFile : ISettingsStore
	{
		public string Place => Settings.FilePath;

		public Settings? Load()
		{
			if (!File.Exists(Settings.FilePath))
			{
				return null;
			}
			string text = File.ReadAllText(Settings.FilePath, Encoding.UTF8);
			try
			{
				return Settings.FromXml(text);
			}
			catch (Exception ex) when (ex is InvalidOperationException or XmlException)
			{
				throw new UnreadableFileException(Settings.FilePath, text, ex);
			}
		}

		public void Save(Settings settings)
		{
			string text = settings.ToXml();
			Directory.CreateDirectory(Path.GetDirectoryName(Settings.FilePath)!);
			File.WriteAllText(Settings.FilePath, text, new UTF8Encoding(false));
		}
	}
}
