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
using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using NLog;
using WaveDudeNext.App.Theming;

namespace WaveDudeNext.Browser;

/// <summary>
/// The system's own font in the browser head (2026-11-14, the user's design): Segoe UI where the machine has it, in
/// place of the font the page carries, which stays for every other machine and browser. The application draws its
/// text itself, so it needs the font files' bytes; a page gets those through the browser's Local Font Access API
/// alone and with the user's permission (<c>system-font.js</c>, which says more). Set as
/// <see cref="App.App.SystemFont"/> by <c>Main</c> where the browser has that API, which shows the View menu's
/// "Use the system's font".
/// <para>
/// The faces are read as bytes, made a font collection of their own (<see cref="CollectionKey"/>) and named as the
/// family in the theme's resource from which the page's root and its popups, and through them every control, take
/// their font family (<see cref="ThemeFontKey"/>); the text the application draws itself takes the family its
/// control inherits. So the switch needs no restart: the resource's change lays the text out again. Where the
/// permission was given on an earlier visit the page reads the faces as it loads and the application starts in
/// them (<see cref="CreateAsync"/>, <see cref="UseAtStart"/>).
/// </para>
/// <para>
/// Threads: <see cref="CreateAsync"/> on the scripts' thread (<c>Main</c>); everything else on the UI thread, which
/// hands the calls to the page over without waiting for them: the browser's question may stay open for as long as
/// the user likes.
/// </para>
/// </summary>
internal sealed partial class SystemFont : ISystemFont
{
	private static readonly Logger Logger = LogManager.GetLogger(typeof(SystemFont).FullName!);

	/// <summary>The key of the font collection the system's faces are registered under.</summary>
	private const string CollectionKey = "fonts:System";

	/// <summary>The family taken from the system, by the name its faces carry.</summary>
	private const string Family = "Segoe UI";

	/// <summary>The Fluent theme's resource that names the font family of the root, the popups and with them every control (read at Avalonia 12.1.5).</summary>
	private const string ThemeFontKey = "ContentControlThemeFontFamily";

	private static Task? _module;
	private byte[][] _faces = [];
	private bool _registered;
	private bool _asking;
	private bool _isUsed;

	private SystemFont()
	{
	}

	/// <inheritdoc/>
	public event PropertyChangedEventHandler? PropertyChanged;

	/// <inheritdoc/>
	public bool IsUsed
	{
		get => _isUsed;
		private set
		{
			if (_isUsed != value)
			{
				_isUsed = value;
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUsed)));
			}
		}
	}

	/// <summary>
	/// The system's font where this browser can hand it over, with the faces the page read as it loaded where the
	/// permission was given before; null where the browser has no such thing (Firefox, Safari) or the page's script
	/// fails. The scripts' thread, before the application starts.
	/// </summary>
	public static async Task<SystemFont?> CreateAsync()
	{
		try
		{
			await Module();
			if (!JsSupported())
			{
				Logger.Debug("The system's font is not offered: this browser hands a page no installed fonts");
				return null;
			}
			return new SystemFont { _faces = Take(await JsEarly()) };
		}
		catch (Exception ex)
		{
			Logger.Debug(ex, "The system's font is not offered: the page's script failed");
			return null;
		}
	}

	/// <summary>
	/// The application starts in the system's font where its faces came with the page's load. Once the application
	/// exists and before its view does (the builder's AfterSetup), so the first layout is in them. UI thread.
	/// </summary>
	public void UseAtStart()
	{
		if (_faces.Length > 0 && Register())
		{
			Apply(true);
			Logger.Debug("The text is in the system's font, {Family}, {Count} faces: the browser's permission was given before", Family, _faces.Length);
		}
	}

	/// <inheritdoc/>
	public async Task SetUsedAsync(bool use)
	{
		if (!use)
		{
			Apply(false);
			Logger.Info("The text is in the page's own font again");
			return;
		}
		if (_asking)
		{
			// the browser's question is still open
			return;
		}
		_asking = true;
		try
		{
			if (!_registered)
			{
				try
				{
					_faces = await OnScripts(async () => Take(await JsLoad()));
				}
				catch (Exception ex)
				{
					// the user's refusal (NotAllowedError), a refusal kept from an earlier visit, or a question the browser
					// did not put because the click was too long ago (SecurityError): in the browser's own words
					Logger.Info("The browser did not hand over the system's fonts ({Reason}); the text stays in the page's own font. The permission is the site's \"Fonts\" setting, at the address bar", ex.Message);
					return;
				}
				if (_faces.Length == 0)
				{
					Logger.Info("The system has no {Family}: the text stays in the page's own font", Family);
					return;
				}
				if (!Register())
				{
					Logger.Warn("The system's {Family} could not be read as a font: the text stays in the page's own font", Family);
					return;
				}
			}
			Apply(true);
			Logger.Info("The text is in the system's font now: {Family}, {Count} faces", Family, _faces.Length);
		}
		finally
		{
			_asking = false;
		}
	}

	/// <summary>The faces as a font collection of the font manager's, once; false when none of them is a font it can read.</summary>
	private bool Register()
	{
		if (_registered)
		{
			return true;
		}
		var collection = new Collection();
		int added = 0;
		foreach (byte[] face in _faces)
		{
			// the stream is the typeface's from here on, which reads from it as it needs glyphs: not disposed
			if (collection.TryAddGlyphTypeface(new MemoryStream(face, writable: false), out _))
			{
				added++;
			}
		}
		if (added == 0)
		{
			return false;
		}
		FontManager.Current.AddFontCollection(collection);
		_registered = true;
		return true;
	}

	/// <summary>
	/// Names the system's family in the application's resources, in front of the theme's own entry of that key, or
	/// takes the name away again, which leaves the theme's: its own font, which is not there, and after it the
	/// default family, the font the page carries.
	/// </summary>
	private void Apply(bool use)
	{
		if (Application.Current is not { } application)
		{
			return;
		}
		if (use)
		{
			application.Resources[ThemeFontKey] = new FontFamily(CollectionKey + "#" + Family);
		}
		else
		{
			application.Resources.Remove(ThemeFontKey);
		}
		IsUsed = use;
	}

	/// <summary>The faces the page has read, as arrays of the program's own; the page's copies go. The scripts' thread.</summary>
	private static unsafe byte[][] Take(int count)
	{
		var faces = new byte[count][];
		for (int i = 0; i < count; i++)
		{
			faces[i] = new byte[JsSize(i)];
			fixed (byte* bytes = faces[i])
			{
				JsCopy(i, (nint)bytes);
			}
		}
		JsRelease();
		return faces;
	}

	/// <summary>A call to the page from the UI thread, which goes on meanwhile; the result, or the page's error, comes back to the awaiting thread.</summary>
	private static Task<T> OnScripts<T>(Func<Task<T>> call)
	{
		var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		ScriptThread.Post(async () => done.SetResult(await call()), done.SetException);
		return done.Task;
	}

	private static Task Module() => _module ??= JSHost.ImportAsync("system-font", "../system-font.js");

	/// <summary>The font collection of the system's faces: nothing of its own beyond its key.</summary>
	private sealed class Collection : FontCollectionBase
	{
		public override Uri Key { get; } = new(CollectionKey, UriKind.Absolute);
	}

	[JSImport("supported", "system-font")]
	private static partial bool JsSupported();

	[JSImport("early", "system-font")]
	private static partial Task<int> JsEarly();

	[JSImport("load", "system-font")]
	private static partial Task<int> JsLoad();

	[JSImport("size", "system-font")]
	private static partial int JsSize(int index);

	[JSImport("copy", "system-font")]
	private static partial void JsCopy(int index, nint address);

	[JSImport("release", "system-font")]
	private static partial void JsRelease();
}
