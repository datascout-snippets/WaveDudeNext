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

using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Text.Json;
using System.Xml.Serialization;
using NLog;
using WaveDudeNext.UI;
using WaveDudeNext.UI.Docking;

namespace WaveDudeNext.Browser;

/// <summary>
/// The user settings of the browser head, in the browser's local storage (2026-11-14, the user's choices): a key
/// for each setting, <c>WaveDudeNext.Settings.PreviewResolution</c> = <c>2</c>, as a browser's developer tools
/// list them and as a page's settings are usually kept, where the desktop heads have one XML file. A page has no
/// file that outlives it, so until this the settings were gone at every reload and every start was a first start.
/// Set as <see cref="SettingsStore.Current"/> by <c>Main</c>, before anything asks for a setting.
/// <para>
/// <b>What is stored.</b> Every setting the settings file writes as an attribute is a key of its name, with the
/// text the file would hold (a number with a decimal point, <c>true</c> or <c>false</c>, an enumeration's member),
/// and, as in the file, only where it differs from its default, so a setting back at its default is a key removed.
/// They are found by reflection, so a new setting is stored without a line here. The settings that are a tree or a
/// list are one key each, as JSON: <c>Layout</c>, the dock panes' arrangement, a tree of any depth that is replaced
/// as a whole (<see cref="DockLayoutJson"/>), and the lists <c>RecentFiles</c> and <c>ExportHints</c>. The key
/// <c>Version</c> is always there once anything was saved: it tells a first start from settings that are all at
/// their defaults. <b>Not stored</b>: the main window's place, size and state (<see cref="WindowOnly"/>), which
/// mean nothing in a page: the browser's window is the browser's.
/// </para>
/// <para>
/// <b>What is read</b> is taken setting by setting: a text that is not its setting's (edited by hand in the
/// developer tools) is said in the log and leaves that one setting at its default, the others as they are stored.
/// </para>
/// <para>
/// Local storage belongs to the page's own thread. All of it is read once, as the store is made on the scripts'
/// thread, since the settings are read once, at their first use, on a thread that would have to wait for that one;
/// a save is handed to the scripts' thread and not waited for, so no thread ever waits for the page here, and
/// saves arrive in the order they were made. A save the browser refuses (no storage for the page, its room used
/// up) is therefore said in the log, not to the caller.
/// </para>
/// </summary>
internal sealed partial class LocalStorageSettings : ISettingsStore
{
	private static readonly Logger Logger = LogManager.GetLogger(typeof(LocalStorageSettings).FullName!);

	/// <summary>The key that says settings were saved, and in which form: this one is 1.</summary>
	private const string VersionKey = "Version";

	private const string FormatVersion = "1";

	/// <summary>The settings that are the main window's alone and are neither stored nor read here.</summary>
	private static readonly HashSet<string> WindowOnly =
	[
		nameof(Settings.WindowWidth), nameof(Settings.WindowHeight), nameof(Settings.WindowLeft), nameof(Settings.WindowTop), nameof(Settings.WindowState),
	];

	/// <summary>The settings that are one text each: those the settings file holds as attributes, less the window's.</summary>
	private static readonly PropertyInfo[] Scalars =
	[
		.. typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.GetCustomAttribute<XmlAttributeAttribute>() != null && !WindowOnly.Contains(property.Name)),
	];

	/// <summary>The stored settings as the page found them, name and text; null where the browser gives the page no storage.</summary>
	private readonly Dictionary<string, string>? _atLoad;

	private LocalStorageSettings(Dictionary<string, string>? atLoad)
	{
		_atLoad = atLoad;
		// a setting that is a list or a tree needs its form here: one added to the settings later is not kept until then
		string[] unknown =
		[
			.. typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(property => property.GetCustomAttribute<XmlElementAttribute>() != null
					&& property.Name is not (nameof(Settings.Layout) or nameof(Settings.RecentFiles) or nameof(Settings.ExportHints)))
				.Select(property => property.Name),
		];
		if (unknown.Length > 0)
		{
			Logger.Warn("These settings are not kept in a browser, whose store does not know their form: {Names}", string.Join(", ", unknown));
		}
	}

	/// <summary>The store, with the settings as the page found them; the scripts' thread, before the application starts.</summary>
	public static async Task<LocalStorageSettings> CreateAsync()
	{
		await JSHost.ImportAsync("settings-storage", "../settings-storage.js");
		if (JsRead() is not { } json)
		{
			return new LocalStorageSettings(null);
		}
		var stored = new Dictionary<string, string>();
		using (JsonDocument document = JsonDocument.Parse(json))
		{
			foreach (JsonProperty setting in document.RootElement.EnumerateObject())
			{
				if (setting.Value.GetString() is { } text)
				{
					stored[setting.Name] = text;
				}
			}
		}
		return new LocalStorageSettings(stored);
	}

	/// <inheritdoc/>
	public string Place => "the browser's local storage";

	/// <summary>The settings as they were stored when the page loaded; null for none, a first start.</summary>
	public Settings? Load()
	{
		if (_atLoad is not { } stored || !stored.ContainsKey(VersionKey))
		{
			return null;
		}
		var settings = new Settings();
		foreach (PropertyInfo property in Scalars)
		{
			if (!stored.TryGetValue(property.Name, out string? text))
			{
				continue;
			}
			try
			{
				property.SetValue(settings, Parse(text, property.PropertyType));
			}
			catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
			{
				Logger.Warn("The stored setting {Name} is '{Text}', which is not a {Type}: its default stays", property.Name, text, property.PropertyType.Name);
			}
		}
		Structured(nameof(Settings.Layout), text => settings.Layout = DockLayoutJson.Read(text));
		Structured(nameof(Settings.RecentFiles), text =>
		{
			using JsonDocument document = JsonDocument.Parse(text);
			settings.RecentFiles = [.. document.RootElement.EnumerateArray().Select(file => file.GetString() ?? "")];
		});
		Structured(nameof(Settings.ExportHints), text =>
		{
			using JsonDocument document = JsonDocument.Parse(text);
			settings.ExportHints =
			[
				.. document.RootElement.EnumerateArray().Select(hint => new ExportHint
				{
					Project = Member(hint, "project"),
					Run = Member(hint, "run"),
					Still = Member(hint, "still"),
					Probe = Member(hint, "probe"),
				}),
			];
		});
		return settings;

		void Structured(string name, Action<string> read)
		{
			if (!stored.TryGetValue(name, out string? text))
			{
				return;
			}
			try
			{
				read(text);
			}
			catch (Exception ex) when (ex is JsonException or InvalidOperationException)
			{
				Logger.Warn(ex, "The stored setting {Name} cannot be read, its default stays: {Text}", name, text);
			}
		}

		static string Member(JsonElement hint, string name) => hint.TryGetProperty(name, out JsonElement value) ? value.GetString() ?? "" : "";
	}

	/// <inheritdoc/>
	public void Save(Settings settings)
	{
		var stored = new Dictionary<string, string> { [VersionKey] = FormatVersion };
		foreach (PropertyInfo property in Scalars)
		{
			object? value = property.GetValue(settings);
			if (value != null && !(property.GetCustomAttribute<DefaultValueAttribute>() is { } standard && Equals(value, standard.Value)))
			{
				stored[property.Name] = Format(value);
			}
		}
		if (settings.Layout is { Root: not null } layout)
		{
			stored[nameof(Settings.Layout)] = DockLayoutJson.Write(layout);
		}
		if (settings.RecentFiles.Count > 0)
		{
			stored[nameof(Settings.RecentFiles)] = Json(writer =>
			{
				writer.WriteStartArray();
				foreach (string file in settings.RecentFiles)
				{
					writer.WriteStringValue(file);
				}
				writer.WriteEndArray();
			});
		}
		if (settings.ExportHints.Count > 0)
		{
			stored[nameof(Settings.ExportHints)] = Json(writer =>
			{
				writer.WriteStartArray();
				foreach (ExportHint hint in settings.ExportHints)
				{
					writer.WriteStartObject();
					foreach ((string name, string text) in new[] { ("project", hint.Project), ("run", hint.Run), ("still", hint.Still), ("probe", hint.Probe) })
					{
						if (text.Length > 0)
						{
							writer.WriteString(name, text);
						}
					}
					writer.WriteEndObject();
				}
				writer.WriteEndArray();
			});
		}
		string all = Json(writer =>
		{
			writer.WriteStartObject();
			foreach ((string name, string text) in stored)
			{
				writer.WriteString(name, text);
			}
			writer.WriteEndObject();
		});
		ScriptThread.Post(
			() =>
			{
				JsWrite(all);
				return Task.CompletedTask;
			},
			error => Logger.Warn(error, "The user settings could not be written to the browser's local storage"));
	}

	/// <summary>A setting's text, as the settings file's attribute would hold it.</summary>
	private static string Format(object value) => value switch
	{
		bool flag => flag ? "true" : "false",
		double number => number.ToString("R", CultureInfo.InvariantCulture),
		IFormattable formattable and not Enum => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? "",
	};

	/// <summary>The value a setting's text stands for.</summary>
	/// <exception cref="FormatException">The text is not a value of <paramref name="type"/> (also <see cref="ArgumentException"/> for an enumeration, <see cref="OverflowException"/>).</exception>
	private static object Parse(string text, Type type)
	{
		if (type == typeof(string))
		{
			return text;
		}
		if (type == typeof(bool))
		{
			return bool.Parse(text);
		}
		if (type == typeof(int))
		{
			return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
		}
		if (type == typeof(double))
		{
			return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
		}
		if (type.IsEnum)
		{
			return Enum.Parse(type, text);
		}
		throw new FormatException($"No text form is known for a {type.Name}.");
	}

	/// <summary>What <paramref name="write"/> writes, as one line of JSON.</summary>
	private static string Json(Action<Utf8JsonWriter> write)
	{
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream))
		{
			write(writer);
		}
		return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
	}

	[JSImport("read", "settings-storage")]
	private static partial string? JsRead();

	[JSImport("write", "settings-storage")]
	private static partial void JsWrite(string json);
}
