# TwinCAT Assistant 0.2.0 Beta 11

Editor can now find and edit supported Structured Text across the bound PLC
project and create new ST objects, with native approval for each mutation.

## Changes since Beta 10

- Discover ST POUs, members, DUTs, interfaces and GVLs without first opening
  their files. Filter by moniker to prefer a supported open XML buffer.
- Review exact diff text before applying or rolling back an edit. Automation
  Interface edits may persist directly to disk and refuse unverified unsaved
  editor changes. Supported open XML-buffer edits remain unsaved.
- Create a function block, program, function, method, action, property,
  struct DUT or GVL through a dedicated approval dialog. TwinCAT writes the
  object file or updates the owning POU file immediately. Changes to the
  `.plcproj` remain pending save. Creation has no source journal entry.
- Adopt published Engineering MCP `0.5.9` (`engineering-v0.5.9`,
  `d635d7f6e8e794bb6490561aa4501b8db791cfcd`). The Editor catalog has 16 tools.
  Build retains 9 tools and Debug retains 11.
- Support ST sections up to 65,536 characters, 1 MiB inner requests and
  responses, and 2 MiB authenticated frames. Mutation timeouts accommodate
  a two-minute host approval window. Engineering MCP 0.5.9 tool requests
  carry a 90-second approval expiry.
- Upgrade recognized Beta 6–10 managed profile configurations in place.
  Builds still require separate approval. PLC activation, download, Online
  Change, writes, forcing and runtime control remain blocked.

## Installation and acceptance

Application version `0.2.0-beta.11` uses native version `0.2.0.17`. The supported
target remains TwinCAT 3.1.4024.78 with TcXaeShell 15 x86 on Windows x64. Use
the existing installation directory for a managed upgrade. See the
[installation guide](../INSTALLATION.md) and [profile guide](../EDITOR_PROFILE.md).

Offline verification covers profile migration, catalog isolation, framed
transport and native policy. Installed TwinCAT workflow acceptance remains
pending.
