# Knowledge startup investigation — 2026-11-02

The main delay is full-corpus validation in Knowledge MCP, before MCP negotiation
can complete. A local Knowledge change reduces this cost, but the connection is
still too slow. The installed Assistant and Knowledge executable were not changed.

## Measurements

Windows, Python 3.12.10, the existing German `.47` corpus: 49,970 topics and
525,278 sections. Each loader run used a new process and an empty private cache.
The OS file cache was not cleared. These are local observations, not percentile
benchmarks or cold-disk measurements.

| Loader phase | Installed 0.1.1 | Local candidate |
| --- | ---: | ---: |
| Copy and verify JSONL hash | 1.155 s | 1.014 s |
| Parse and validate JSONL records | 23.945 s | 18.298 s |
| Validate corpus consistency | 31.504 s | 19.840 s |
| Copy and verify SQLite hash | 1.368 s | 1.395 s |
| Compare SQLite/FTS with validated topics | 20.578 s | 15.078 s |
| Loader ready, including other loader work | **79.320 s** | **56.325 s** |

Both columns used the installed interpreter and dependencies. The candidate
selected the changed Knowledge source through a process-local `PYTHONPATH`;
neither the installed wheel nor operator caches were patched. A first candidate
run overlapped the test suite and took 60.321 s; the table uses the subsequent
run without that competing test workload. The reduction is about 29%, with
normal machine-load variation still possible.

The candidate also passed these separate end-to-end checks:

- Assistant shared broker, fresh isolated profile and already activated corpus:
  first successful corpus-status call at **59.168 s**; exactly **one** backend.
- Three further calls from separate relay clients — search, opening the returned
  topic and corpus status — took **0.978 s** together. Search returned three hits;
  `FB_EcCoeSdoReadEx` opened with its returned ID and five sources. The complete
  structured result was also present in model-visible text. No additional
  backend started.
- Built native host, actual source-runtime WebView2, both real MCPs and synthetic
  PLC/editor fixtures: both MCP connections were confirmed after **69.389 s**
  from runtime launch. The broker again reported exactly **one** backend.
  This excludes IDE startup and is not live-editor, compiler or model acceptance.

The approximately 98-second native connection measured on 2026-10-30 and the
older 108-second baseline used different runs. Do not equate them with today's
loader-only measurements or subtract them to claim a precise UI improvement.

## Cause and local Knowledge fix

Profiling the original loader found 2,300,992 calls to the source-locator
validator. Articles and their sections repeat these locators, and the loader
validates them again during corpus and index checks. Two Python generators
scanned every character for ASCII controls; the profile recorded about
382 million `ord` calls. Profiling itself adds substantial overhead, so its
wall-clock duration is not a performance baseline.

Knowledge branch `fix/startup-validation`, local commit
`5442be0368b91f5e9e40078f4c7b98786af18a8d`, changes only this validation path:

- A compiled expression checks the same ASCII control range before and after
  percent decoding, and in canonical URLs.
- At most 4,096 successful locator checks are retained per process, keyed by
  the exact original immutable string. Encoded variants remain distinct.
  Exceptions are not cached; other source fields are still validated.

Manifest pins, file hashes, record schemas, canonical IDs, source/hash/count
consistency and SQLite/FTS comparison all still run. There is no persistent
validation cache and no package-format, MCP-protocol or tool change.

The local source worktree is `local-data/knowledge-startup-worktree` inside the
backend worktree. Its commit belongs to the separate `twincat-knowledge-mcp`
repository; an Assistant commit alone cannot publish or install that fix.
The Assistant pin remains released Knowledge **0.1.1**, revision `fff24f4`.

## Why the earlier fixture started two backends

The previous `Test-ChatIntegration.ps1 -KnowledgeStateSource` started the runtime
before copying `state/active.json` into its private profile. The broker preloaded
without that selection, then saw a changed managed-corpus revision and replaced
the backend once. This exercised activation, rather than an ordinary cold start.

The fixture now initializes its empty profile and copies the existing selection
before launching the runtime. Its real-corpus path asserts one backend after
both MCPs connect and records the elapsed native connection time. The isolated
native run above passes this assertion. The running operator broker also
reported one start during today's read-only observation.

This explains the two starts in that fixture. It does not establish that every
historical UI reconnect was harmless or rule out a separate production failure.
The existing activation regression still checks one replacement per new corpus;
client disconnects, cancellation and warm calls must retain the backend.

## Historical operator-profile writes

On 2026-10-30, hashes matched immediately after installation. Later database/WAL
and OpenChamber preference timestamps changed while the selected corpus stayed
the same. File timestamps alone do not identify the writing process. A check for
running IDE processes at the end cannot establish that no IDE had run earlier.

Today's audit queried only session metadata on a temporary database/WAL copy.
It found no sessions created or updated in the 14:40–14:48 Berlin window and no
`tc-chat-integration-*` fixture sessions anywhere in that copy. This provides no
evidence of fixture chats in the operator database; it does not identify the
writer or prove that no other state was written.

The runtime environment's private HOME, XDG, OpenChamber and OpenCode roots and
the native fixture's configuration, workspace, browser, credentials and binding
overrides were checked. Their current isolation regressions pass. Do not restore
an older database, copy a test profile over the operator profile or claim the
historical write attribution is resolved. If it recurs, capture process/file
write attribution during an explicitly observed run.

Automatic approval rejected cleanup of one temporary audit copy. It was left
under the system temp directory, outside the operator profile; it contains a
private database copy and must not be committed or uploaded. The refusal was
not bypassed.

## Verification and remaining work

- Knowledge: 157 focused provenance cases pass, including all 33 ASCII controls
  in raw, encoded and nested-encoded locators, URL controls, Unicode and repeated
  source round trips. Ruff, formatting, strict Mypy, dependency compatibility and
  wheel/source-archive build pass.
- The full Knowledge suite reports **604 passed, 9 skipped, 6 failed**. Each of
  those six fails while creating an existing symlink fixture with Windows
  `WinError 1314` before its product assertion. They need a symlink-capable
  environment; this local run must not be described as fully green.
- Assistant: 11 broker/state component cases and PowerShell syntax pass. The
  changed native corpus fixture passes with source contract 1 and both real MCPs.
  No cloud-model call, source edit, reimport or installed-runtime restart occurs.

Local diagnostic evidence is retained under backend `local-data`:
`knowledge-loader-profile.log`, `knowledge-loader-functions.log`,
`knowledge-loader-candidate.log`, `knowledge-loader-candidate-quiet.log`,
`knowledge-broker-candidate.log` and `knowledge-native-candidate.log`.
These private diagnostic files and the corpus are not release contents.

For an independent loader check, run Knowledge's existing read-only command with
the approved package path, its expected manifest SHA-256 and a fresh private cache:

```powershell
Measure-Command {
    & $KnowledgeExe corpus validate $PackagePath `
        --expected-manifest-sha256 $ManifestSha256 --cache-dir $PrivateCache --json
}
```

The startup issue stays open. The next target is a confirmed connection within
**15 seconds of local runtime launch** for an already prepared corpus on this
machine, with the warm search/topic/status sequence remaining below **2 seconds**.
Today's candidate misses the cold-start target. Further work must address record,
corpus and index-validation costs without silently dropping integrity checks.
Any persistent validation reuse needs its own reviewed invalidation/integrity
design.

Before rollout, publish a new Knowledge software version with green CI, update
the Assistant's verified pin, and rerun the combined checks before installation.
Chat-history copy and Markdown export were manually accepted by the operator on
2026-11-02. Project-chat navigation remains a separate implementation task.
