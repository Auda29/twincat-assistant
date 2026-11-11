# Project website assets

The static GitHub Pages site is served from `main:/docs`. Its German entry point
is `index.html`; English is `en.html`. Both use `site.css` and `language.js`.
Update both language files when changing release links or product scope.

`logo.svg` and `logo.png` are unchanged copies of the existing product icon in
`scripts/installer/assets/setup.svg` and `setup.png`.

`twincat-assistant-tcxaeshell.png` is the original product screenshot supplied
by the repository owner on 2026-11-11. Both language versions display the full,
uncropped image and link to the original PNG for a full-size view. The captured
interface retains its original German and English text.

Release links and compatibility text on the initial site refer to the published
`native-v0.2.0-beta.4` release. The package version is `0.2.0-beta.4`, and the
supported installation target is TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on
Windows x64. Shell64/4026 is preparation only, not an accepted installation.
