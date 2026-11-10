# German and English

Implemented on 2026-11-09 in `feat/de-en-localization`, based on the product
cleanup in `feat/lean-product-runtime`. The implementation has been included in
[Beta 1](releases/native-v0.2.0-beta.1.md) since 2026-11-10. The original checks
below remain dated evidence; integration added the latest project-error messages
in both languages and was verified again.

## Selection and persistence

The Host menu offers **Deutsch** and **English** under **… → Sprache / Language**.
This works without a connected Chat. The same selection is available in Chat
under **Settings → Appearance → Language**. Host and Chat synchronize it without
recreating the conversation, draft or attachments. An earlier language
observation must not undo a more recent native selection.

The Host stores `language: "de" | "en"` beside `autoAttach` in the existing
`preferences.json`. The existing credential-directory resolver determines the
path; the process test override still uses only its private profile. Language
is not written to credentials or project configurations. Existing atomic
preference persistence is retained.

On the next opening, Chat receives the saved native selection. It therefore
survives changes to the local backend port and browser origin. Chat also retains
its existing `openchamber.i18n.v1` LocalStorage entry. Without a native
preference, an existing German or English Chat selection is adopted; otherwise
German is used. Old preference files need no migration. Unknown values are
ignored on read; reading alone does not rewrite the file.

## Scope

- Native menus, pickers, dialogs, status, source-state indicators, notifications,
  known Host diagnostics and window titles use central `.resx` catalogs.
- Chat uses its existing complete DE/EN dictionaries and reactive locale store.
  The product language picker exposes these two languages. The new native empty
  state is also a catalog entry, supplied for all 13 dictionaries still present
  in the source.
- The project-chat picker formats relative times and dates in the selected
  language. The culture of the entire IDE process is not changed.
- Technical protocol values, provider/model names, paths, error codes and
  unchanged external-component messages remain in their original form. Installer
  UI is outside this Host/Chat change.
- Source code, object/chat titles and existing messages retain their original
  content. The analysis prompt still selects the model's response language from
  the latest user message, independently of UI language.

The original local verification package contains 21,538 files; its manifest SHA-256
is `85d1f27549932bf4406324aa576ceb20e5860b4d1567a919c559a558a2953c41`.
The English satellite assembly SHA-256 is
`7b4b4238263a121538ada6e1cff3048bc67d53ba8f254b030a3e90a6f7ae56ea`.
That verification copy retained the existing version number and was not
published as a release.

Native WPF bindings recognize catalog-produced strings by object identity. A
user-assigned title `Gespeichert` therefore does not become `Saved` when switching
to English. Text boxes containing source or user input are not translation targets.

The x86 Host generates an English satellite assembly. The package builder
requires `host/x86/en/TwinCatAssistant.IdeHost.resources.dll` and includes it in
the full file manifest. A build without this file fails.

## Verification

Checks use synthetic files and private test profiles. No installed IDE, operator
profiles, real provider credentials or PLC actions were used.

| Check | Result |
| --- | --- |
| Chat catalogs | 7,239 keys per DE/EN catalog; no missing keys or differing placeholders |
| Native catalogs | Full DE/EN key and placeholder parity; English resources loaded without German fallback |
| Original native catalog size | 384 entries per language |
| Native build/regressions | x86 Release build passed; 932 checks including catalog checks passed; five existing threading warnings |
| Node tests | 162/162 passed, no skips |
| Embedded adapter and package-builder fixture | 29/29 passed; fixture includes the English satellite assembly |
| Project-chat picker in the actual WPF fixture | 110 checks; DE/EN menus/times, unchanged titles and round trip passed |
| Actual AssistantControl outside the IDE | 11 checks: switching, recreation with saved language, retained auto-attach, unchanged source/object name, old/invalid preferences |
| Built Chat in Chrome | Picker exposes only DE/EN; settings/adapter switching preserves session, generation and draft; English retained after reload |
| UI/Web typechecks | Both passed |
| Complete application package | Built with English Host satellite; protected startup, both real MCPs on synthetic files, restart, data preservation and unchanged package passed |

The standalone WPF test invokes the shared synchronous transition; it does not
establish menu dispatch through the IDE-owned JoinableTaskContext. The browser
test sent no prompt. Existing generic-client endpoint errors in the reduced
backend remain visible; a clean browser-console result is not claimed.

Reproduce with:

```powershell
powershell.exe -NoProfile -File scripts/Build-Native.ps1 -DotNetRoot <SDK-8.0.425> -Architecture X86
powershell.exe -NoProfile -STA -File scripts/Test-HostLanguage.ps1
powershell.exe -NoProfile -STA -File scripts/Test-ProjectChatPicker.ps1
node --test --test-concurrency=1 tests/*.test.mjs
$env:TC_TEST_DOTNET_ROOT = 'C:\Program Files\dotnet'
node --test tests/integration/application-package-builder.test.mjs
```

Visible acceptance in the installed TcXaeShell remains open: language menu,
window title, popups, embedded Chat and persistence after an IDE restart. This
PR performed no installation and does not close other roadmap Host gates.
