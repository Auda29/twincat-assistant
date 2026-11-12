# Project website assets

The static GitHub Pages site is served from `main:/docs`. Its German entry point
is `index.html`; English is `en.html`. Both use `site.css` and `language.js`.
Update both language files when changing release links or product scope.

`logo.svg` and `logo.png` are unchanged copies of the existing product icon in
`scripts/installer/assets/setup.svg` and `setup.png`.

`twincat-assistant-tcxaeshell.png` is the original product screenshot supplied
by the repository owner on 2026-11-11. Below the feature overview, both language
versions display the full,
uncropped image and link to the original PNG for a full-size view. The captured
interface retains its original German and English text.

The hero is an authored HTML/CSS illustration inspired by the screenshot's
IDE frame, yellow docked-window title, project attachment toolbar, chat and
composer. It uses synthetic `FB_StartDelay` code and an example timer explanation.
Both languages label it as an illustration and link to the real screenshot
section. It is independent of the unchanged original PNG.

Release links and current capability text refer to published
`native-v0.2.0-beta.5`, including Engineering 0.5.3 and Knowledge 0.1.2.
Analysis remains read-only; project-bound build tools require explicit native
approval. The source-editor development is retained but inactive. The original
screenshot remains dated 2026-11-11 and is not presented as Beta 5 acceptance.
The package version is `0.2.0-beta.5`, and the
supported installation target is TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on
Windows x64. Shell64/4026 is preparation only, not an accepted installation.
