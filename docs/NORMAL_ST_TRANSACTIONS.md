# Normal TwinCAT ST write transaction blocker

Live normal ST discovery, read and preview are implemented. Apply and rollback
remain unavailable for that route. This is a concrete adapter limitation, not a
missing permission toggle or a reason to treat a preview as an applied change.

## Examined host

The bounded metadata/static-IL research used TwinCAT 3.1.4024.78's installed
`TwinCATPlcControl.dll`, SHA-256
`a8d08963ec4ebb728c01166730650d56bfa99269d3f5e2db2adad29fab5057ec`.
No IDE was started, attached or modified. No vendor DLL or decompiled code is
redistributed in this repository. The observations are version-specific and do
not establish behavior on 4026 or Shell64.

## Safe read path

The host resolves the actual attached IPOUEditor/IMethodEditor child slot and
IEditorView. GUID, qualified name, project handle, requested section, exact RDT
cookie/docdata, physical file and bound project must agree and remain stable.
Declaration wrappers must contain exactly one matching concrete editor.
The provider reads the already-attached document using GetTextDocument(false).
It does not open a document, choose the focused/first view or read saved source
as though it were the current buffer.

## Why the apparent MEF bridge is not a write adapter

A genuine MEF adapter exists behind the public TcTextBuffer property, but its
backing text is a concatenation of searchable text blocks. It is neither the
whole TcPOU XML nor a proven exact declaration/implementation document.
Its change handler forwards edits through separate editor selection and
UndoableReplace/object-manager paths, including recovery and refresh handlers.
Other modification paths call global SaveAllEditors, alter AutoDeclare or call
SaveEditor. Those names alone do not prove a disk save, but the calls exceed
an approval scoped to one exact editor section.

Consequently ITextEdit.Apply on this synthetic buffer is not evidence of one
atomic transaction on the attached TwinCAT text document. A post-read could
spot a discrepancy only after an unsafe submission. Calling the exposed setter,
UndoableReplace, or a method named Commit/Reject would not fix that proof gap.
The implementation therefore never promotes this route to writable support.

Static-IL research identifiers retained outside the repository were 060019A6
(adapter), 060019A7 (buffer), 0600196E (concatenation), 0600197F (global calls),
060019AA (change) and 060019AB (recovery). These identify the examined binary,
not a supported public API contract.

## What is needed next

A vendor-supported exact-slot transaction and synchronization contract, or an
independently validated editor adapter with equivalent baseline/version,
conditional edit, cancellation and rollback guarantees, is required before
normal-ST writes can be enabled. Existing XML-buffer transactions do not imply
that contract. Live acceptance is also required after a suitable adapter exists.
The broader P1 write requirement remains open; the PR does not claim completion.
