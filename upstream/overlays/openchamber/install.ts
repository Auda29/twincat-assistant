import { twinCatSource } from './twincat';

declare global {
  interface Window { __twinCatSource?: typeof twinCatSource; __twinCatSourceReady?: Promise<void> }
}

// This source build is embedded through the existing protected web runtime.
// No native/COM bridge or credential is exposed to the page.
window.__twinCatSource = twinCatSource;
