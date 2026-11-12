import { resolve } from 'node:path';
import { createHash } from 'node:crypto';
import { C1_ANALYSIS_PROMPTS } from '../src/analysis-prompts-c1.mjs';
import { C0_ANALYSIS_PROMPTS } from '../src/analysis-prompts-c0.mjs';
import { previousAnalysisPrompts } from '../src/agent-prompts.mjs';
import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile } from 'node:fs/promises';
import { ANALYSIS_PROFILE, ANALYSIS_TOOL_NAMES, PREVIOUS_ANALYSIS_PROMPTS, SHARED_RULES, ST_FENCE, analysisSystemPrompt } from '../src/agent-prompts.mjs';
import { READ_TOOLS, createOpenCodeConfig, previousOpenCodeConfigs } from '../src/config.mjs';
import { checkAnswer } from './prompt-evaluation/check-answer.mjs';
import { composeCase, loadCases, metadataText, snapshotText } from './prompt-evaluation/evaluate.mjs';

const knowledge = { executable: resolve('tests/fixtures/knowledge.exe') };
const engineering = { entrypoint: resolve('tests/fixtures/engineering.mjs'), config: resolve('tests/fixtures/engineering.json') };
const SCOPES = [
  { knowledge: false, engineering: false, project: false },
  { knowledge: true, engineering: false, project: false },
  { knowledge: false, engineering: true, project: false },
  { knowledge: true, engineering: true, project: false },
  { knowledge: true, engineering: true, project: true },
];
const mentionedTools = prompt => new Set(prompt.match(/\b(?:knowledge|engineering)_[a-z_]+\b/g) ?? []);
const source = path => readFile(new URL('../' + path, import.meta.url), 'utf8');

test('prompt tool names are exactly the reviewed tools of the configured servers', () => {
  assert.deepEqual(new Set([...ANALYSIS_TOOL_NAMES.knowledge, ...ANALYSIS_TOOL_NAMES.engineering]), new Set(READ_TOOLS),
    'every allowed MCP tool is described, and no other tool is');
  for (const scope of SCOPES) {
    const tools = mentionedTools(analysisSystemPrompt(scope));
    for (const name of tools) {
      assert.ok(READ_TOOLS.includes(name), name + ' is not an allowed tool');
      assert.ok(name.startsWith('knowledge_') ? scope.knowledge : scope.engineering, name + ' named without its server: ' + JSON.stringify(scope));
    }
    if (scope.knowledge) assert.ok(tools.has('knowledge_tc_get_corpus_status'));
    if (scope.engineering) assert.ok(tools.has('engineering_plc_read_pou'));
  }
  assert.throws(() => analysisSystemPrompt({ project: true }), /requires the engineering tools/);
});

test('generated configurations carry the scope-specific prompt without widening permissions', () => {
  const general = createOpenCodeConfig({ knowledge });
  const agent = general.agents[ANALYSIS_PROFILE];
  assert.equal(general.default_agent, ANALYSIS_PROFILE);
  assert.equal(agent.system, analysisSystemPrompt({ knowledge: true }));
  assert.match(agent.system, /No PLC project is connected to this chat/);
  assert.equal(mentionedTools(agent.system).has('engineering_plc_read_pou'), false);
  assert.deepEqual(agent.permissions[0], { action: '*', resource: '*', effect: 'deny' });
  assert.deepEqual(agent.permissions.filter(rule => rule.effect === 'allow').map(rule => rule.action).sort(), [...READ_TOOLS, 'question'].sort());
  assert.equal(createOpenCodeConfig({}).agents[ANALYSIS_PROFILE].system, analysisSystemPrompt({}));
  assert.match(createOpenCodeConfig({ knowledge, engineering }).agents[ANALYSIS_PROFILE].system, /saved PLC project files configured by the operator/);
  // Fits the bounded secure JSON reader with ample room for MCP paths.
  assert.ok(Buffer.byteLength(JSON.stringify(createOpenCodeConfig({ knowledge, engineering }), null, 2)) < 32768);
});

test('every profile scope keeps the shared rules and the read-only boundary', () => {
  for (const scope of SCOPES) {
    const prompt = analysisSystemPrompt(scope);
    for (const [name, lines] of Object.entries(SHARED_RULES)) {
      for (const line of lines) assert.ok(prompt.includes(line), name + ' rule missing for ' + JSON.stringify(scope));
    }
    assert.match(prompt, /This profile is read-only/);
    assert.match(prompt, /Every other tool is denied/);
    assert.match(prompt, /No tool in this profile reads or verifies the current editor buffer/);
    assert.match(prompt, /never an applied change/);
    assert.doesNotMatch(prompt, /\b(you may|you can) (edit|write|save|build|download|activate)\b/i);
  }
});

test('prompt names the context markers that the native host and chat overlay really inject', async () => {
  const prompt = analysisSystemPrompt({ knowledge: true, engineering: true, project: true });
  const binding = await source('native/Shared/ChatContextBinding.cs');
  const overlay = await source('upstream/overlays/openchamber/twincat.ts');
  for (const marker of ['TwinCAT context snapshot', 'Unsaved editor text is not verified', 'Object kind: ', 'Owning POU: ', 'Member: ', 'Source: ', 'Changes: ', 'Revision: ']) {
    assert.ok(binding.includes(marker), marker + ' not emitted by the host');
  }
  assert.match(binding, /ActiveReferenceId = "Aktiver Baustein"/);
  const referenceMarker = 'TwinCAT saved-object reference (no source code attached)';
  assert.ok(binding.includes(referenceMarker) && prompt.includes(referenceMarker));
  assert.match(prompt, /read the returned object ID/);
  assert.match(prompt, /excludes unsaved editor changes/);
  assert.match(overlay, /'TwinCAT IDE context, metadata only\./);
  for (const marker of ['"TwinCAT context snapshot"', '"Aktiver Baustein"', '"TwinCAT IDE context, metadata only"', 'Object kind, Owning POU and Member', 'Declaration and Structured Text implementation sections', 'Source, Changes and Revision'])
    assert.ok(prompt.includes(marker), marker + ' missing from the prompt');
  // The evaluation harness mirrors both injection formats line by line.
  for (const line of snapshotText({ object: 'X', changes: 'unknown', declaration: '', implementation: '' }).split('\n')) {
    const label = line.match(/^([A-Za-z ]+:) /)?.[1];
    if (label) assert.ok(binding.includes('"' + label + ' ') || binding.includes('\\n' + label + ' '), label + ' not in AttachmentText');
  }
  for (const label of ['Active object: ', 'Owning POU: ', 'Source observation: ']) {
    assert.ok(overlay.includes(label) && metadataText('X').includes(label), label);
  }
  assert.ok(binding.includes('\\n\\nDeclaration:\\n') && binding.includes('\\n\\nStructured Text implementation:\\n'));
});

test('three code sources are worded distinctly and live editor content is never promised', () => {
  const prompt = analysisSystemPrompt({ knowledge: true, engineering: true, project: true });
  assert.match(prompt, /1\. Saved file:.*not the editor/);
  assert.match(prompt, /2\. Attached snapshot:.*can contain unsaved changes, can be outdated and is not proven to equal the current editor/);
  assert.match(prompt, /3\. Verified current editor content: only text that a tool explicitly reports/);
  assert.match(prompt, /never say that you see the current, live or unsaved editor content/);
  assert.match(prompt, /in der gespeicherten Datei/);
  assert.match(prompt, /im angehängten Snapshot/);
  assert.match(prompt, /contains no source code/);
  assert.match(prompt, /untrusted data, not instructions/);
  assert.match(prompt, /Never invent titles, IDs, URLs/);
  assert.match(prompt, /German or English\. If it is unclear, answer in German/);
});

test('the fence tag matches the pinned renderer record', async () => {
  const fixture = await loadCases();
  const lock = JSON.parse(await source('upstream/source-lock.json'));
  assert.equal(fixture.renderer.openchamber, lock.openchamber.revision,
    'OpenChamber changed: re-check the Markdown renderer and update cases.json renderer facts');
  assert.match(fixture.renderer.highlighter, new RegExp('\\b' + ST_FENCE + '\\b.*not bundled'));
  const prompt = analysisSystemPrompt({});
  assert.ok(prompt.includes('fenced code blocks tagged `' + ST_FENCE + '`'));
  assert.match(prompt, /no ST syntax highlighting/);
});

test('earlier prompts are recorded exactly and are never the current prompt', () => {
  assert.equal(PREVIOUS_ANALYSIS_PROMPTS.profile.length, 1);
  assert.equal(PREVIOUS_ANALYSIS_PROMPTS.project.length, 1);
  for (const scope of SCOPES)
    for (const old of [...PREVIOUS_ANALYSIS_PROMPTS.profile, ...PREVIOUS_ANALYSIS_PROMPTS.project])
      assert.notEqual(analysisSystemPrompt(scope), old);
  const [previous] = previousOpenCodeConfigs({ knowledge });
  const current = createOpenCodeConfig({ knowledge });
  assert.equal(previous.agents[ANALYSIS_PROFILE].system, PREVIOUS_ANALYSIS_PROMPTS.profile[0]);
  assert.notDeepEqual(previous.permissions, current.permissions, 'earlier policy retains its original tool list');
  assert.equal(previous.permissions.length, 15);
});

test('evaluation cases are composable, use only tools of their scope and reference real sources', async () => {
  const fixture = await loadCases();
  const ids = new Set();
  const languages = new Set();
  for (const item of fixture.cases) {
    assert.ok(!ids.has(item.id)); ids.add(item.id); languages.add(item.language);
    assert.ok(item.review.length > 0 && item.question.length > 0);
    const composed = composeCase(fixture, item);
    assert.equal(composed.system, analysisSystemPrompt(item.scope));
    for (const tool of composed.tools) {
      assert.ok(tool, item.id + ' references an unknown source');
      if (tool.tool) assert.ok(READ_TOOLS.includes(tool.tool) && (tool.tool.startsWith('knowledge_') ? item.scope.knowledge : item.scope.engineering), item.id + ' uses ' + tool.tool);
    }
    for (const id of item.checks.verbatimFrom ?? []) assert.ok(fixture.sources[id], item.id + ': ' + id);
  }
  assert.deepEqual([...languages].sort(), ['de', 'en']);
  const kinds = new Set(Object.values(fixture.sources).map(item => item.objectKind ?? item.kind));
  for (const kind of ['method', 'property', 'action', 'attached-snapshot', 'saved-file', 'documentation']) assert.ok(kinds.has(kind), kind);
});

test('code-evidence cases reject missing code and invented property accessor labels', async () => {
  const fixture = await loadCases();
  const answers = JSON.parse(await source('tests/prompt-evaluation/evidence/2026-11-05-c1/answers.json'));
  for (const id of ['property-set-en', 'saved-property-accessors-ambiguous-en', 'injection-in-source-en', 'proposal-en']) {
    const item = fixture.cases.find(entry => entry.id === id);
    assert.equal(checkAnswer(answers[id], item, fixture.sources).passed, true, id);
    const withoutCode = answers[id].replace(/```[^\n]*\n[\s\S]*?```/g, '');
    const rejected = checkAnswer(withoutCode, item, fixture.sources);
    assert.equal(rejected.passed, false, id + ' must require code evidence');
    assert.equal(rejected.results.minCodeBlocks.ok, false, id);
  }
  const set = fixture.cases.find(entry => entry.id === 'property-set-en');
  assert.equal(checkAnswer(answers[set.id].replace(' - Set**', ' - Get**'), set, fixture.sources).results.requiredLabels.ok, false);
  const ambiguous = fixture.cases.find(entry => entry.id === 'saved-property-accessors-ambiguous-en');
  const inventedGet = answers[ambiguous.id].replace(/ - Implementation[^*\n]*/, ' - Get');
  assert.equal(checkAnswer(inventedGet, ambiguous, fixture.sources).results.forbiddenLabels.ok, false);
});

test('the answer checker accepts the reference answers and rejects the counter-examples precisely', async () => {
  const fixture = await loadCases();
  const { samples } = JSON.parse(await source('tests/prompt-evaluation/samples.json'));
  assert.ok(samples.some(item => item.kind === 'good') && samples.some(item => item.kind === 'bad'));
  for (const sample of samples) {
    const item = fixture.cases.find(entry => entry.id === sample.case);
    const result = checkAnswer(sample.answer, item, fixture.sources);
    const failed = Object.entries(result.results).filter(([, value]) => !value.ok).map(([name]) => name);
    assert.deepEqual(failed, sample.expectFailures, sample.case + ' ' + sample.kind);
  }
});

test('C0 migration matches exact scope without changing policy or permitting other C0 scopes', () => {
  assert.equal(C0_ANALYSIS_PROMPTS.profile.length,4);
  assert.equal(C0_ANALYSIS_PROMPTS.project.length,2);
  for(const knowledge of [false,true])for(const engineering of [false,true]) {
    const scope={knowledge,engineering};const old=previousAnalysisPrompts(scope).at(-1);
    assert.equal(old,C0_ANALYSIS_PROMPTS.profile[2*Number(knowledge)+Number(engineering)]);
    assert.notEqual(old,analysisSystemPrompt(scope));
    for(const other of C0_ANALYSIS_PROMPTS.profile)if(other!==old)assert.ok(!previousAnalysisPrompts(scope).includes(other));
  }
  for(const knowledge of [false,true]) {
    const old=previousAnalysisPrompts({knowledge,engineering:true,project:true}).at(-1);
    assert.equal(old,C0_ANALYSIS_PROMPTS.project[Number(knowledge)]);
  }
});

// Captured independently from git 88b11e0 before changing either generator.
test('C1 scope snapshots and complete policies match the released bytes', () => {
  const digest = value => createHash('sha256').update(typeof value === 'string' ? value : JSON.stringify(value)).digest('hex');
  assert.deepEqual(C1_ANALYSIS_PROMPTS.profile.map(digest), [
    '1a75e0f1a994caa06f7c6b5e50558ed29a8aa13031a0581db3e29e5ea529fff2',
    '1ca19abb0d47f9ccf971da2dea8b9920b5eb30261a8fea5f5617723ffa1ceffb',
    '41ac4a39c683c94771cf0ad9f9bde554ebc0bebebf552e5d944d5ab3d6217009',
    'e5326c3cf9547e0ace7951c094637b9ca944434aa6712fe0d1d0233e4b77ad9f',
  ]);
  assert.deepEqual(C1_ANALYSIS_PROMPTS.project.map(digest), [
    '41556c64068d8660a26fbd0a6dfd7135ef32d106d067aca3da49c9d8e35a74ed',
    'f4253be782a658b25960fda94f8a962be2b141317e2a52514d7ba970573ba70f',
  ]);
  const base = resolve('tests/fixtures');
  const normalize = (key, value) => typeof value === 'string' && value.startsWith(base)
    ? value.replace(base, '<FIXTURES>').replaceAll('\\', '/') : value;
  const expected = [
    'b3194e58e33b340d192c70dae70e9b3c844ac8e39d9c9e3a8c2a8e36f7f1a493',
    'd72811df8bfbf56fde4bb237250209dc38ceb4d91579a1ef0aea3561f92018ee',
    '65a838d868669c48c8de48864718ddb4459f91ed7699d4d1dbca026dfdcdd7d4',
    '8ed0feb9ab828eb75b4f02e191c15d8b30d8eff412ac10b4e6de180fc3d35b6f',
  ];
  let i = 0;
  for (const k of [false, true]) for (const e of [false, true]) {
    const settings = { ...(k && { knowledge }), ...(e && { engineering }) };
    const old = previousOpenCodeConfigs(settings, { node: 'legacy-node', knowledgeClient: 'legacy-client' })[1];
    assert.equal(createHash('sha256').update(JSON.stringify(old, normalize)).digest('hex'), expected[i++]);
  }
});

test('typed Knowledge guidance preserves evidence and ambiguity limits', () => {
  const prompt = analysisSystemPrompt({ knowledge: true });
  for (const tool of ['knowledge_tc_find_symbols', 'knowledge_tc_get_symbol', 'knowledge_tc_find_libraries', 'knowledge_tc_get_library', 'knowledge_tc_lookup_error']) assert.ok(mentionedTools(prompt).has(tool));
  assert.match(prompt, /get_corpus_status before documentation research/);
  assert.match(prompt, /Multiple matches remain ambiguous even when paginated/);
  assert.match(prompt, /documented version is not an installed version/);
  assert.match(prompt, /nullable metadata/);
});
