# Native chat connection, 0.1.3

Version 0.1.6 adds the selected compact context bar, automatic
local sign-in, project-chat selection and provider enablement, plus optional
automatic POU attachment described in [Chat integration](CHAT_INTEGRATION.md).
Candidate 0.1.8 adds [native MCP setup and project workspaces](NATIVE_MCP.md).
The connection history and manual setup workflow below belong to 0.1.3.

Updated 2026-10-26. This slice fixes `Connect chat` opening an unavailable local
URL and reporting success before navigation completed. It adds a protected local
runtime start; it does not attach native source or configure a model/provider.
The x86 0.1.3 payload is installed with all 13 files verified and 0.1.2 backed up
outside the extension search path. The stored synthetic project was closed only
after confirming that its documents and project were saved. The per-user runtime
paths are configured; the actual local password is chosen in the native window.
The user has completed this first-run setup and confirms that the real
OpenChamber login page appears inside the installed x86 window. This establishes
the connection/login-page flow; an authenticated model conversation is a separate
acceptance step.

## Workspace initialization follow-up

After the `native-v0.1.3` source tag was published, the user reached the
authenticated UI and reported `Could not initialize workspace` in the chats
sidebar. This was reproduced with a separate synthetic first-run profile.
The pinned browser bootstraps the legacy chat directory under the isolated
`home/.config/openchamber/chats` even when `OPENCHAMBER_DATA_DIR` relocates the
managed root. The directory did not exist, so directory-scoped config/location
and pending form/permission reads returned HTTP 404 `LocationNotFoundError`.

The launcher now prepares both that legacy directory and `openchamber/chats`
before starting the runtime, including for existing profiles. Each component
must be a regular directory; files and symbolic links/junctions are rejected.
Existing chats, policy, settings and credentials are preserved. This is a Node
startup change and does not require a new VSIX. The published source tag remains
unchanged; this correction is a follow-up on the PR branch.

The same repair was applied to the running operator profile without restarting
the IDE or reading its credential. On 2026-10-26, the user confirmed that pressing
**try again** in the installed x86 Assistant removed the workspace error. The
synthetic browser also removed the message and its workspace requests succeeded.
The user subsequently reported the same error again while both directories
still existed. That retry therefore established only temporary recovery.
After pressing **Connect chat** to reload the entire embedded page, the user
confirmed on 2026-10-26 that the message remained absent for about a minute.
For an already-running page that first initialized before directory repair,
use **Connect chat** after the repair rather than relying on the sidebar retry.
No restart, password reset or VSIX replacement was needed for this recovery.

Additional local tests exercised the real compiled WPF control and WebView2,
including protected synthetic login and three full page loads. A separate
synthetic runtime outside the repository reproduced missing-directory HTTP
404s before late repair; subsequent full page loads had no required workspace
bootstrap failures. Expected optional Git-repository HTTP 422s were separate.
The exact failed request behind the user's recurrence was not captured, so a
retained browser initialization failure is an inference, not a proven upstream
root cause. Extended lifecycle acceptance and a provider/model conversation
remain open.
The real-runtime integration now checks config, location, forms and permission
reads against both chat roots after login, rather than stopping at login HTTP
200. Root checks pass with 36 Node tests and 8 browser contracts.

## Normal use

1. Press **Connect chat**. The host checks the configured loopback port for the
   protected OpenChamber authentication endpoint.
2. If that service is already running, load it without starting or owning another
   instance. If it is absent, start the configured local runtime.
3. On first start, choose and confirm a local chat password in the native window,
   using 12 to 1024 characters. Press **Start local chat**. This password is entered
   directly into the app, not supplied through the agent or a chat message.
4. Wait for the service to become ready and for WebView2 navigation to succeed.
   Then sign in through OpenChamber's own login using the same local password.

The local password is encrypted with Windows DPAPI `CurrentUser` in the private
`TwinCatAssistant/native-chat` application-data directory. No plaintext password
file is created. Existing credentials are not overwritten or silently reset.
Model/provider credentials remain separate and are not requested by this host.

The compact native POU context remains local. Starting or signing into chat does
not transfer ST, bind a chat session to the IDE or enable provider use. A fresh
operator profile contains no model, MCP servers or source-disclosure acceptance.

## Development setup

The development VSIX uses this repository's pinned Node runtime and Windows
helper, rather than bundling a second copy of OpenChamber/OpenCode. Build native
components and install the pinned `upstream` dependencies first, then run:

```powershell
./scripts/Configure-NativeChat.ps1 -DotNetRoot 'C:\absolute\sdk-root'
```

This creates a current-user configuration at
`%LOCALAPPDATA%/TwinCatAssistant/native-chat.json` containing only absolute Node,
repository, isolated state-root and .NET paths. It does not contain a password.
The script refuses to replace a different existing setup. The default state root
is `%LOCALAPPDATA%/TwinCatAssistant/operator-state`; its workspace is separate
from any PLC project. Missing setup is explained in Diagnostics.

This remains a development setup, not a standalone installer. Do not move the
checkout or remove the configured SDK while that setup is in use.

## Process ownership and failure behavior

`NativeChatSession` launches `src/native-chat.mjs` directly with the configured
Node executable, without a shell or visible console. A private stdin channel
carries the state/port/password request. Credentials are absent from command-line
arguments and from the bounded stdout readiness response. Upstream stderr is
drained without copying its contents into IDE error messages.

The Node entry point reuses production state validation, exact upstream version
gates, the default-deny engineering profile and the existing Windows Job Object
supervisor. It starts only in the isolated workspace. Provider keys and unrelated
IDE environment variables are not inherited by the native launcher.

An occupied port is refused before creating a new state directory. A compatible
already-running protected service is reused by the native host. Its owning IDE
keeps ownership; another IDE connecting to it does not gain permission to stop
it. Closing the owner/control pipe stops its entire owned process tree. Peers
detect that loss and can reconnect/start again using their local setup.

No PID-based stale-lock repair or termination of unrelated processes is added.
After an abnormal owner/helper failure, inspect the existing profile lock using
the documented operator procedure before changing it.

Native status distinguishes setup, startup/readiness, page loading, page loaded,
navigation failure and service loss. `Navigate()` alone never reports success.
Navigation completions are checked for actual success and HTTP status, and stale
completions are ignored. Failed browser error pages are hidden. A browser process
failure causes the WebView to be recreated on the next connect attempt.

Navigation/resources remain restricted to the exact chosen loopback origin.
Same-host/port `ws://` is permitted for chat resources, not for navigation;
cross-host, cross-port and user-info URLs remain blocked. The host exposes no
generic WebView messages or COM objects to the page.

## Verification

- Both 0.1.3 VSIX variants build with no warnings/errors.
- Full `npm run check`: 34 Node tests and 8 production-panel browser contracts.
- Native production state/policy regressions: 49 checks, including refusing false
  success on connection refusal/HTTP failure and ignoring stale callbacks.
- Real compiled WPF view: 20 checks, including visible connection errors, hidden
  failed pages and exact-origin WebSocket restrictions. Synthetic light/dark
  password-setup/error states were rendered for review.
- Real pinned runtime through the native stdin protocol: protected HTTP/login,
  unconfigured provider state, occupied-port refusal, credential non-disclosure,
  graceful owner EOF and removal of its launcher lock. Malformed input is refused.
- Real C# owner/DPAPI checks: encrypted credential round-trip, actual runtime
  start/readiness, actual login with a Unicode password and complete listener
  shutdown on owner stop. These use an
  isolated synthetic profile and synthetic password, not the installed credential.

Both stdin hops use UTF-8 explicitly: the .NET Framework IDE owner writes UTF-8
bytes to Node, and the Windows process supervisor reads Node's launch plan as
strict UTF-8. This avoids Windows ANSI/OEM code-page corruption of passwords and
paths. The regression uses umlauts, a Greek character and an emoji in the real
login, plus a non-ASCII selected environment value in the native job tests.

Commands after building:

```powershell
node --test tests/integration/native-chat.test.mjs
powershell.exe -NoProfile -STA -File scripts/Test-NativeChat.ps1 -DotNetRoot 'C:\absolute\sdk-root'
powershell.exe -NoProfile -STA -File scripts/Test-NativeView.ps1
```

Windows CI includes the native Node transport test with the actual pinned runtime.
Local evidence is recorded under ignored `local-data/chat-fix-*.log`. WPF inputs
are synthetic and do not replace an installed WebView2/login check. amd64 live
host acceptance, real provider conversation and native/session/MCP binding remain
separate gates.

## References

- [WebView2 navigation completion](https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/winrt/microsoft_web_webview2_core/corewebview2navigationcompletedeventargs?view=webview2-winrt-1.0.3856.49)
- [Windows DPAPI CurrentUser protection](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope)
