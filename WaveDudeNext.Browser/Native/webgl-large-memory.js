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

// Texture uploads in a program whose memory may exceed 2 GB (2026-11-13). Linked into the runtime's script (--pre-js
// in the project file), so it runs once in every thread of the page, before anything draws.
//
// This head is built with its memory's ceiling at 4 GB. For such a build the C library's WebGL layer (emscripten's,
// of the version .NET 10 links) no longer hands WebGL "the whole memory and an offset" but a view of exactly the
// bytes the call reads, and it reckons those as width x height, by the row alignment alone: the other pixel store
// settings are left out. The drawing library (Skia) fills a part of its glyph sheet with UNPACK_ROW_LENGTH set, the
// rows of its source being wider than what it uploads, so the view came out too short and the browser refused the
// upload: "texSubImage2D: ArrayBufferView not big enough for request", 56 times in a session, and the header
// strips' texts, the toolbar's glyphs, the check marks and some digits did not appear.
//
// So the pixel store settings are noted here as they are set, and while one of those the layer leaves out is in
// force, an upload or a read gets a view that begins at the same byte and is long enough for any call (the browser
// asks only that it be long enough; a view copies nothing). Every other call passes through untouched.
(function () {
	if (typeof WebGL2RenderingContext === 'undefined') { return; }
	const prototype = WebGL2RenderingContext.prototype;
	// UNPACK_ROW_LENGTH, _SKIP_ROWS, _SKIP_PIXELS, _SKIP_IMAGES, _IMAGE_HEIGHT; PACK_ROW_LENGTH, _SKIP_ROWS, _SKIP_PIXELS
	const unpack = new Set([0x0cf2, 0x0cf3, 0x0cf4, 0x806d, 0x806e]);
	const pack = new Set([0x0d02, 0x0d03, 0x0d04]);
	// per context, the settings of either kind that are not at their default of 0
	const settings = new WeakMap();
	const pixelStorei = prototype.pixelStorei;
	prototype.pixelStorei = function (name, value) {
		const kind = unpack.has(name) ? 'unpack' : pack.has(name) ? 'pack' : null;
		if (kind) {
			let set = settings.get(this);
			if (!set) {
				set = { unpack: new Set(), pack: new Set() };
				settings.set(this, set);
			}
			if (value) { set[kind].add(name); } else { set[kind].delete(name); }
		}
		return pixelStorei.call(this, name, value);
	};
	// far more than any one call reads, and what is left of the memory from the view's start at the most
	const pieceBytes = 0x20000000;
	const longer = view => new view.constructor(view.buffer, view.byteOffset,
		Math.floor(Math.min(view.buffer.byteLength - view.byteOffset, pieceBytes) / view.BYTES_PER_ELEMENT));
	// method: [the number of arguments of the form with a view as its last, which settings count]
	const forms = { texImage2D: [9, 'unpack'], texSubImage2D: [9, 'unpack'], texImage3D: [10, 'unpack'], texSubImage3D: [11, 'unpack'], readPixels: [7, 'pack'] };
	for (const [name, [count, kind]] of Object.entries(forms)) {
		const original = prototype[name];
		prototype[name] = function (...args) {
			if (args.length === count && ArrayBuffer.isView(args[count - 1])) {
				const set = settings.get(this);
				if (set && set[kind].size > 0) { args[count - 1] = longer(args[count - 1]); }
			}
			return original.apply(this, args);
		};
	}
})();
