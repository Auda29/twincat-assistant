# TwinCAT Assistant 0.2.0 Beta 11

Editor can now find and edit supported Structured Text across the bound PLC
project and create new ST objects, with native approval for each mutation.

## Changes since Beta 10

- Keep the chat open through brief local service interruptions. The native
  health check reuses HTTP connections, allows four seconds per check, and
  starts reconnecting after three consecutive failures. An owned chat service
  exit triggers recovery immediately.
- Preserve chat sign-in through temporary authentication timeouts, network
  errors, and server errors while marking MCP status as stale. An HTTP 401 or
  403 still triggers sign-in recovery.
- Keep the embedded chat in desktop mode when resizing the tool window or
  toggling the Project Chat sidebar on touch-capable systems.
- Discover ST POUs, members, DUTs, interfaces and GVLs without first opening
  their files. Filter by moniker to prefer a supported open XML buffer.
- Review exact diff text before applying or rolling back an edit. Automation
  Interface edits may persist directly to disk and refuse unverified unsaved
  editor changes. Supported open XML-buffer edits remain unsaved.
- Create a function block, program, function, method, action, property,
  struct DUT or GVL through a dedicated approval dialog. TwinCAT writes the
  object file or updates the owning POU file immediately. Changes to the
  `.plcproj` remain pending save. Creation has no source journal entry.
- Adopt published Engineering MCP `0.5.11` (`engineering-v0.5.11`,
  `8193e35986aceede26e11ea1f2d5ca7790bc557e`). The Editor catalog has 16 tools.
  Build retains 9 tools and Debug retains 11.
- Support ST sections up to 65,536 characters, 1 MiB inner requests and
  responses, and 2 MiB authenticated frames. Engineering MCP tool approvals
  expire after 90 seconds, within the host's 120-second ceiling. Mutation
  timeouts are 125 seconds in both the Engineering MCP client and the native
  host. The pipe and broker exchange timeouts are 130 seconds, and the broker
  HTTP timeout is 135 seconds.
- Upgrade recognized Beta 6–10 managed profile configurations in place.
  Builds still require separate approval. PLC activation, download, Online
  Change, writes, forcing and runtime control remain blocked.

## Installation and acceptance

Application version `0.2.0-beta.11` uses native version `0.2.0.17`. The supported
target remains TwinCAT 3.1.4024.78 with TcXaeShell 15 x86 on Windows x64. Use
the existing installation directory for a managed upgrade. See the
[installation guide](../INSTALLATION.md) and [profile guide](../EDITOR_PROFILE.md).

Offline verification covers profile migration, catalog isolation, framed
transport and native policy. Against a simulated native pipe, the published
Engineering runtime returned 500 source targets in a roughly 2.1 MiB MCP
response and completed a source creation request after a 32-second approval
delay.

An earlier isolated live COM run on TwinCAT 3.1.4024.78 x86 passed six
synthetic cases. `EngineeringDirtyState` observed clean closed `.TcPOU`,
`.TcDUT` and `.TcGVL` files through the DTE fallback after `E_POINTER`. It
detected unsaved `ImplementationText` edits on closed and open source files
and observed a clean state after saving. With `EngineeringProvider` 0.5.10,
`source.read`, `source.preview` and `source.apply` completed for a closed
`MAIN.TcPOU`. The native provider source tree is identical in Engineering
0.5.10 and 0.5.11. End-to-end manual chat acceptance in the installed VSIX remains
pending.
