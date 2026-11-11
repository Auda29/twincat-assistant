# Project website assets

The static GitHub Pages site is served from `main:/docs`. Its German entry point
is `index.html`; English is `en.html`. Both use `site.css` and `language.js`.
Update both language files when changing release links or product scope.

`logo.svg` and `logo.png` are unchanged copies of the existing product icon in
`scripts/installer/assets/setup.svg` and `setup.png`.

The code/chat illustration is authored HTML with a synthetic `FB_StartDelay`
and an example answer. It is labelled as an illustration in both languages.
It does not claim an IDE screenshot, a real model call or a compiled PLC sample.
No upstream renderer-test screenshots are presented as product screenshots.

Release links and compatibility text on the initial site refer to the published
`native-v0.2.0-beta.4` release. The package version is `0.2.0-beta.4`, and the
supported installation target is TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on
Windows x64. Shell64/4026 is preparation only, not an accepted installation.
