# TwinCAT Assistant OpenChamber extension

## Install

1. Build the extension from the repository root:

   ```bash
   npm run build
   ```

2. In OpenChamber, open **Settings → Extensions → Add**.
3. Select the local folder `dist/extension`.
4. Approve the extension's empty capability list. Reload the extension after rebuilding a folder install.

The built folder contains `package.json`, `panel/index.html`, `panel/main.js`, `THIRD_PARTY_NOTICES.txt`, and `BUILD.txt`. `panel/main.js` is a classic browser IIFE for OpenChamber's sandboxed iframe; it is not an ES module.

## Behavior and readiness

The panel consumes the OpenChamber SDK host contract (`@openchamber/sdk` 2.1.1). This SDK exposes host context through `onReady`, `onDirectory`, and `onSession`; it does not expose an authoritative `getContext()` API. The panel therefore treats `onReady` as an atomic snapshot and fails closed when an independent directory update arrives: the old session and binding are cleared until a fresh session event arrives. A session or directory switch replaces the displayed binding; no binding is restored from `localStorage` or another local preference.

Engineering readiness is deliberately fail-closed:

- A missing project directory or session disables context attachment.
- Even with verified project and session context, readiness remains **blocked** because the native TwinCAT bridge is not connected.
- The panel never claims a TwinCAT IDE binding, native PLC access, source/corpus availability, or engineering completion.
- The panel is read-only. It does not read or write project files, start services, call a model, or provide IDE write buttons.
- The only side effect is the explicit **Attach verified context** click, which calls the SDK's `host.attach` with the current verified project/session snapshot. A pending attach captures its directory, session, binding, and context revision; duplicate clicks are ignored, and a late result from a switched context is ignored with a prompt to reattach the current context. Attach failures for the current context are shown as host errors and do not become readiness.
- Host lifecycle cleanup calls SDK unsubscribe functions and `host.dispose()` on page teardown.

No synthetic review is shown while disconnected. The tool display manifest contains only the explicit normalized patterns `knowledge_*` and `engineering_*`; it does not grant those tools, files, services, network, model, or session capabilities.

## Feature matrix

| Feature | Implemented behavior | Native engineering claim |
| --- | --- | --- |
| Current project | Displays the verified OpenChamber directory from host context | None |
| Current session | Displays the verified current session and activity/lifecycle | None |
| Session switch/rebind | Replaces context from later host messages; directory-first changes remain unbound until a fresh session event | None |
| Engineering readiness | Always blocked until a future native bridge exists | Fail-closed |
| Native TwinCAT bridge | Explicitly reported unavailable | Not connected |
| Context attachment | User-clicked, read-only SDK `host.attach` | No IDE write |
| Reviews, corpus, sources | Not shown while disconnected | Not asserted |
| File/service/model access | Not declared or called | Not available |

## Verification

Node contract tests cover the fail-closed presentation model, hostile/long values, context-derived attachment, session rebinding, manifest capabilities, SDK usage, safe `textContent` rendering, and cleanup. Browser contract tests additionally exercise independent directory/session transitions, duplicate clicks while an attach is delayed, stale success/error results after a context switch, and disposal. Run:

```bash
npm test
npm run build
npm run test:browser
```

The Playwright tests use a test-only HTML host fixture at `tests/browser/host-fixture.html`. It exercises the actual production bundle and SDK `postMessage` bridge at the iframe boundary. These are host contract tests, not full OpenChamber acceptance tests. Full acceptance still requires installing `dist/extension` in a compatible OpenChamber build.

The native TwinCAT bridge and a real TwinCAT IDE are intentionally not bundled or simulated.
