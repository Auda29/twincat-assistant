# Windows installer wizard

Newly built installers open a German Windows Forms setup window when launched
without arguments. Windows requests administrator rights automatically before
opening setup. Confirm UAC with the same Windows account that uses the Assistant;
signing in as another administrator selects that administrator's user profile.

The window offers installation/update or rollback, a default destination of
`C:\Program Files\TwinCatAssistant`, a folder picker, progress activity, live
transaction output and a final success/error result. Logs remain in
`%LOCALAPPDATA%\TwinCatAssistant\installer-logs`. Package extraction and full
integrity checks can take minutes; the activity bar does not invent percentages.
Closing setup during the transaction is blocked rather than terminating the
installer in the middle of a file swap. Setup does not start or close TwinCAT.

The existing PowerShell transaction still checks host compatibility, package
integrity, prerequisites, Windows identity, active hosts, ownership and recovery
state. Prerequisites are checked, not downloaded or installed by the wizard.
Rollback requires a retained compatible managed version; the original unpacked
prototype is not a rollback target.

Explicit command-line arguments retain the automation entry point, now
noninteractive. Missing required arguments fail instead of opening a hidden
PowerShell prompt. Use `Start-Process -Wait -PassThru` when a calling script needs
to wait for a GUI-subsystem executable and inspect its exit code.

`scripts/Test-InstallerWizard.ps1` builds a tiny synthetic embedded script and
checks argument roundtrips, Install/Rollback forwarding, stderr/exit codes,
missing-argument failure, initial UI defaults and the UAC manifest. It never
installs, discovers or operates a real host. Existing published EXEs remain
unchanged; a new build is required to obtain this UI.
