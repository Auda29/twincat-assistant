# Normal local runtime restored, 2026-11-05

This supersedes the machine-state section of the paused WIP checkpoint
`10ac145`. The larger roadmap goal remains paused.

After the operator closed TcXaeShell and confirmed same-user UAC, the original
13-file native prototype, original native-chat.json bytes and original
extension marker bytes/timestamp were restored. The managed C0 extension was
retained outside the extension search path; its package and synthetic state
were retained. No whole operator profile or chat database was copied back.

The original native configuration points to this development checkout.
Its runtime source was therefore switched from untested WIP to verified
commit `491ed3aa687248a46021830d93e9c0e09e209a99` on local branch
`fix/local-runtime-recovery-20261105`. Source-runtime receipts and inputs were
verified. This recovery branch adds documentation only to that runtime source.
All unfinished work remains pushed on `feat/alpha-delivery-foundations` and
Draft PR15; it is not the source used by the restored local backend.

The actual restored x86 NativeChatSession was tested with the original
operator StateRoot and existing locally protected UI credential. It reached
protected-ready in 6370 ms including stop, with no model call. Credential
values were neither printed nor copied. Policy/settings were privately backed
up before this start; chats and credential stores were not restored wholesale.

Private evidence is under
`local-data/alpha-host-baseline-20261105-140304/`:
`prototype-restore-uac-result.json` (exit0),
`restored-operator-runtime-result.json` and the original byte/hash backups.
The UAC restore completed at 15:17:55Z; the restored backend check completed at
15:27:13Z. The IDE can be opened normally after this check.

Before resuming alpha installation work, inspect the actual native config
again. It now selects the original operator profile, not the synthetic alpha
profile. Prepare a separate acceptance configuration before any C1 installation
or migration; do not apply the paused test plan to operator data.
