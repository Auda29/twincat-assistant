# TwinCAT Assistant 0.2.0 Beta 1

This beta combines the product cleanup from PR #16 and the saved German/English
language selection from PR #17 with all fixes from Preview 6.

- Host and Chat share a language selection. It is available under
  **… → Language** in the Host menu and in Chat settings, and survives a restart.
  Source code, messages and drafts retain their original content. The model's
  response language follows the user's message.
- Product text, the logo and version display use TwinCAT Assistant branding.
  Unused Chat server services, routes and their dependencies have been removed.
  Required data contracts and third-party notices are retained. Shared helpers
  and remaining client code are documented in the
  [cleanup scope](../PRODUCT_CLEANUP_20261109.md).
- Includes the corrected first tool-window invocation, UTF-8 project setup,
  explicit error reporting and chat binding before the composer mounts.
  The operator confirmed Preview 6 chat startup in the installed IDE.

## Installation

Close TcXaeShell normally. Download the setup EXE and `SHA256.txt` from this
release, compare the checksum and choose **Install / update** in the existing
installation directory. Confirm UAC with the same Windows account. Uninstalling
the previous version is unnecessary. Existing chats, sign-ins and project
bindings use the existing data paths. The application version is `0.2.0-beta.1`;
the native file, assembly and VSIX version is `0.2.0.7`.

The target remains TwinCAT 3.1.4024.78 / TcXaeShell 15 x86 on Windows x64 with
.NET 8, .NET Framework and WebView2. Other hosts remain unaccepted. The installer
is unsigned. Existing locks are not deleted automatically. The editor remains
read-only; the editor profile and inline completion are outside this beta.

## Evidence and limits

The local Windows build produces the complete application package and setup EXE
from the integrated source. `BUILD_PROVENANCE.json` records the source revision,
components, checksums and checks that actually passed. Installer verification
reads the embedded payload without executing setup. The package smoke test uses
both real MCPs exclusively on synthetic files. WPF/WebView startup and language
checks use private test profiles, no operator profiles and no model requests.

Visible language acceptance of this beta in the installed IDE, real model
responses, member/paste/first-after-logon acceptance and a complete real
update/rollback sequence remain open. At the user's request, the beta is listed
as GitHub **Latest**. It therefore does not carry GitHub's “Pre-release” flag;
the product name and version still explicitly identify it as a beta.
