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

// The user settings in the browser's local storage (LocalStorageSettings.cs; 2026-11-14, the user's choice): a key
// for each setting, "WaveDudeNext.Settings.PreviewResolution" = "2", as a browser's developer tools list them, the
// few settings that are lists or trees as JSON. Local storage is the page's own thread's alone, which is where the
// program's calls to the page arrive; the program hands all the settings over as one JSON object of name and text
// and gets them the same way, so a save or a load is one call.
const prefix = 'WaveDudeNext.Settings.';

// the one key that held all the settings as an XML text for some hours of 2026-11-14, before a key each
const formerKey = 'WaveDudeNext.Settings';

// every stored setting, as the JSON of an object of name and text ("{}" when none is stored); null where the browser
// gives the page no storage (a setting of the browser's, some private windows), which is a first start every time
export function read() {
	try {
		const storage = globalThis.localStorage;
		const all = {};
		for (let i = 0; i < storage.length; i++) {
			const key = storage.key(i);
			if (key.startsWith(prefix)) {
				all[key.substring(prefix.length)] = storage.getItem(key);
			}
		}
		return JSON.stringify(all);
	} catch {
		return null;
	}
}

// Makes what is stored what the JSON object says: its settings written (those that changed), every other of ours
// removed, a setting back at its default among them. Where the browser refuses (no storage for the page, its room
// used up) its error is the caller's.
export function write(json) {
	const storage = globalThis.localStorage;
	const all = JSON.parse(json);
	for (const [name, text] of Object.entries(all)) {
		if (storage.getItem(prefix + name) !== text) {
			storage.setItem(prefix + name, text);
		}
	}
	for (let i = storage.length - 1; i >= 0; i--) {
		const key = storage.key(i);
		if (key.startsWith(prefix) && !Object.hasOwn(all, key.substring(prefix.length))) {
			storage.removeItem(key);
		}
	}
	storage.removeItem(formerKey);
}
