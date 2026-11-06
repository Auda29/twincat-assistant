# Release handoff, 2026-11-06

Operator departure deadline: 13:50 Europe/Berlin. No installation was performed.
Do not publish an unverified release or force a merge to meet the deadline.

- PR: https://github.com/Auda29/twincat-assistant/pull/15
- Release CI source: `329213737e637639192eafae5301e2f654e1af7f`.
- Actual Windows run: https://github.com/Auda29/twincat-assistant/actions/runs/42220003076
- Input manifest: `6ee4eb9bb5c83de02f60cdea06ecf918e33c50d4baf5a3da2c39a01b2e7da271`.
- Native DLL: `77e03ab221db44cd2a4eb442fa778f22de3da565940272312e540d85bac10ffd`.
- Prerelease draft tag: `native-v0.2.0-preview.2`. No installer published yet.
- Ephemeral runner: `alpha-guard-73a94c62`. Confirm remote deregistration after import.

Earlier runs failed on CI shell availability and the profile fixture under
PowerShell 7. The latest workflow runs that contract in the Windows PowerShell 5
runtime actually used by the EXE bootstrap. No production path guard was relaxed.

Next: verify exact current PR SHA and all checks; download the actual hosted
`windows-package-candidate`, compare its EXE hash against included SHA256.txt,
confirm package provenance, merge PR, then publish the prerelease with CI EXE,
checksum and provenance. Delete only obsolete remote branches whose tips are
ancestors of the merged main. Retain local worktrees and original recovery files.
Keep the recovery branch while the workstation still depends on its old code.

The normal working checkout remains `fix/local-runtime-recovery-20261105` at
`f6e018a`, with runtime source `491ed3a`. Never switch or build this live backend
checkout as part of release housekeeping. Existing unpackaged native prototype
requires a verified one-time migration; the installer deliberately refuses to
overwrite it. Live UI/update/rollback/first-after-logon acceptance remains open.

The previously local-only preparation helper is now tracked at
`scripts/host-tests/Prepare-LocalUiMaintenance.ps1`. It records progress and a
transcript and only creates a plan, not an Enter/Restore or IDE launch. It needs
the local reviewed package; its build output and user data are intentionally
not committed. Personal paths were replaced with the checkout-relative root.

This handoff/helper checkpoint is pushed to the feature branch without changing
the already running CI/PR SHA. Merge those follow-up files separately after the
release run settles; do not accidentally delete their branch before integration.
