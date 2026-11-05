// Reproducible evaluation of Analysis-profile answers without a model call.
//
//   node tests/prompt-evaluation/evaluate.mjs --list
//   node tests/prompt-evaluation/evaluate.mjs --print CASE_ID
//   node tests/prompt-evaluation/evaluate.mjs ANSWERS.json
//
// --print shows the production system prompt for the case scope, the context
// parts in the format the native host and chat overlay inject, and the
// synthetic tool results to serve. ANSWERS.json maps case IDs to Markdown
// answers (for example copied from a manual chat run) and is checked with the
// machine checks; the review criteria are printed for a human reviewer.
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { analysisSystemPrompt } from '../../src/agent-prompts.mjs';
import { checkAnswer } from './check-answer.mjs';

export async function loadCases() {
  return JSON.parse(await readFile(new URL('./cases.json', import.meta.url), 'utf8'));
}

// Mirrors native/Shared/ChatContextBinding.cs AttachmentText with synthetic identity values.
export function snapshotText(source) {
  return 'TwinCAT context snapshot\nObject: ' + source.object + '\nObject kind: POU\nOwning POU: ' + source.object
    + '\nMember: POU body\nIDE PID: 4242\nSolution: C:\\Synthetic\\Station.sln\nPLC project: C:\\Synthetic\\PLC\\PLC.plcproj'
    + '\nTree path: PLC/POUs/' + source.object + '\nSource: Automation Interface snapshot\nChanges: ' + source.changes
    + '\nUnsaved editor text is not verified. This snapshot is read-only and may be outdated.'
    + '\nRevision: ' + '0'.repeat(64) + '\n\nDeclaration:\n' + source.declaration
    + '\n\nStructured Text implementation:\n' + source.implementation;
}

// Mirrors upstream/overlays/openchamber/twincat.ts twinCatContextTexts.
export function metadataText(object) {
  return 'TwinCAT IDE context, metadata only. Observed when this message was composed.\n'
    + 'PLC project: PLC\nActive object: ' + object + '\nObject kind: POU\nOwning POU: ' + object
    + '\nMember: POU body\nTree path: PLC/POUs/' + object + '\nSource observation: Automation Interface snapshot'
    + '\nThis metadata contains no source code and does not verify the unsaved editor buffer. '
    + 'Attached snapshots and saved files returned by engineering tools are separate sources.';
}

export function composeCase(fixture, testCase) {
  const parts = testCase.context.map(entry => {
    const [kind, value] = entry.split(':');
    if (kind === 'metadata') return metadataText(value);
    if (kind === 'snapshot') return snapshotText(fixture.sources[value]);
    throw new Error('Unknown context part ' + entry);
  });
  return { system: analysisSystemPrompt(testCase.scope), parts, question: testCase.question,
    tools: testCase.tools.map(id => fixture.sources[id]), corpus: testCase.corpus ?? 'ok' };
}

async function main(args) {
  const fixture = await loadCases();
  if (args[0] === '--list') { for (const item of fixture.cases) console.log(item.id + '\t' + item.question); return 0; }
  if (args[0] === '--print') {
    const testCase = fixture.cases.find(item => item.id === args[1]);
    if (!testCase) throw new Error('Unknown case');
    const composed = composeCase(fixture, testCase);
    console.log('=== system\n' + composed.system);
    for (const part of composed.parts) console.log('=== context part\n' + part);
    console.log('=== user\n' + composed.question);
    console.log('=== tool results to serve (corpus: ' + composed.corpus + ')\n' + JSON.stringify(composed.tools, null, 2));
    console.log('=== review\n' + testCase.review.map(item => '- ' + item).join('\n'));
    return 0;
  }
  if (args.length !== 1) { console.log('Usage: --list | --print CASE_ID | ANSWERS.json'); return 2; }
  const answers = JSON.parse(await readFile(resolve(args[0]), 'utf8'));
  let failed = 0;
  for (const [id, markdown] of Object.entries(answers)) {
    const testCase = fixture.cases.find(item => item.id === id);
    if (!testCase) throw new Error('Unknown case ' + id);
    const result = checkAnswer(markdown, testCase, fixture.sources);
    if (!result.passed) failed++;
    console.log((result.passed ? 'PASS ' : 'FAIL ') + id);
    for (const [name, value] of Object.entries(result.results)) if (!value.ok) console.log('  ' + name + ': ' + value.problems.join('; '));
    for (const item of testCase.review) console.log('  review: ' + item);
  }
  return failed ? 1 : 0;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  process.exitCode = await main(process.argv.slice(2));
}
