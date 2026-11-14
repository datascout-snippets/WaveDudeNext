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

// The service worker that isolates the page where the server does not (main.js installs it and says when): every
// file of the site reaches the page through it, fetched as the page asked for it and handed on with the two headers
// a cross-origin isolated page is served with, Cross-Origin-Opener-Policy: same-origin and
// Cross-Origin-Embedder-Policy: require-corp. It keeps nothing, no cache and no state: without a network the page
// fails as it would without the worker. Everything the page loads is the site's own (the runtime, the scripts, the
// font inside the program), so "require-corp" asks nothing of any other server.
//
// A browser knows an installed worker by this file's address: under another name the old worker would stay in
// charge for every visitor who has it. The name stays.

// A new version of this file takes over at once, from the version before it and for the pages already open: it
// holds nothing a page could be in the middle of.
self.addEventListener('install', () => { self.skipWaiting(); });
self.addEventListener('activate', event => { event.waitUntil(self.clients.claim()); });

self.addEventListener('fetch', event => {
	const request = event.request;
	// another site's file is the browser's business, as without this worker
	if (new URL(request.url).origin !== self.location.origin) { return; }
	// a request for what the cache holds and nothing else (the developer tools make such) is refused when it is
	// fetched from a worker in any mode but same-origin: left to the browser too
	if (request.cache === 'only-if-cached' && request.mode !== 'same-origin') { return; }
	event.respondWith(isolated(request));
});

// the server's answer to a request with the two headers added, its body passed through as it arrives
async function isolated(request) {
	const response = await fetch(request);
	// A redirect the browser is to follow itself (a page's address without its last slash) arrives without a
	// status and cannot be made anew: handed on as it is.
	if (response.status === 0) { return response; }
	const headers = new Headers(response.headers);
	headers.set('Cross-Origin-Opener-Policy', 'same-origin');
	headers.set('Cross-Origin-Embedder-Policy', 'require-corp');
	return new Response(response.body, { status: response.status, statusText: response.statusText, headers });
}
