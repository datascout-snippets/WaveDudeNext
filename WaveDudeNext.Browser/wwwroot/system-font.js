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

// The system's own font for the application's text (SystemFont.cs; 2026-11-14, the user's design): Segoe UI where
// the machine has it, in place of the font the page carries. The application draws its text itself, so it needs the
// font files' bytes, which a page gets through the Local Font Access API alone (queryLocalFonts: Chromium's browsers
// on a desktop, neither Firefox nor Safari) and with the user's permission, which the browser asks for once a site
// and only shortly after a click or a key press. Once given, the browser answers without either (Chromium's
// font_access_manager.cc: "Transient User Activation only required before showing permission prompt"), so a later
// visit reads the fonts as the page loads: this script is loaded by main.js for that, and by the program for the
// rest.

// The faces the application asks for (regular, italic, semibold, bold), by their PostScript names, as the font files
// of Windows 10 give them (segoeui.ttf, segoeuii.ttf, seguisb.ttf, segoeuib.ttf).
const faces = ['SegoeUI', 'SegoeUI-Italic', 'SegoeUI-Semibold', 'SegoeUI-Bold'];

// the faces read last, a Uint8Array each, until the program has taken them
let loaded = [];

// whether this browser hands a page the installed fonts at all
export function supported() { return typeof globalThis.queryLocalFonts === 'function'; }

// 'granted', 'prompt' or 'denied'; 'prompt' where the browser does not say
async function permission() {
	try {
		return (await navigator.permissions.query({ name: 'local-fonts' })).state;
	} catch {
		return 'prompt';
	}
}

// Reads the faces and resolves to their number, 0 where the system has none of them. Where the permission has not
// been given yet the browser asks, and rejects when this does not follow a click or a key press closely enough
// (SecurityError). A refusal, the user's or one kept from an earlier visit, Chromium answers with no fonts at all
// and no error (Edge 157, seen 2026-11-14; the specification has a NotAllowedError there), so an empty answer is
// told from a system without the faces by the permission's state, and a refusal is made the error it is.
export async function load() {
	const found = await globalThis.queryLocalFonts({ postscriptNames: faces });
	if (found.length === 0 && await permission() !== 'granted') {
		throw new Error('the permission to use the installed fonts was not given');
	}
	const bytes = [];
	for (const font of found) {
		bytes.push(new Uint8Array(await (await font.blob()).arrayBuffer()));
	}
	loaded = bytes;
	return loaded.length;
}

// At the page's load: the faces, read already where the permission was given on an earlier visit, so that the
// program starts in them; 0 where it was not, or where the read fails. Started here, beside the runtime's own start.
const atLoad = supported() ? permission().then(state => state === 'granted' ? load() : 0).catch(() => 0) : Promise.resolve(0);
export function early() { return atLoad; }

// the length of a face read, and its bytes into the program's memory at an address (which arrives as a signed number)
export function size(index) { return loaded[index].length; }
export function copy(index, address) { globalThis.waveDudeRuntime.localHeapViewU8().set(loaded[index], address >>> 0); }

// the program has its copies: the page's go
export function release() { loaded = []; }
