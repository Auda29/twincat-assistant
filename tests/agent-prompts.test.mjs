import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile } from 'node:fs/promises';
import { ANALYSIS_PROFILE, ANALYSIS_TOOL_NAMES, PREVIOUS_ANALYSIS_PROMPTS, SHARED_RULES, ST_FENCE, analysisSystemPrompt } from '../src/agent-prompts.mjs';
import { READ_TOOLS, createOpenCodeConfig, previousOpenCodeConfigs } from '../src/config.mjs';
import { checkAnswer } from './prompt-evaluation/check-answer.mjs';
import { composeCase, loadCases, metadataText, snapshotText } from './prompt-evaluation/evaluate.mjs';

const knowledge = { executable: 'C:\\Synthetic\\knowledge.exe' };
const engineering = { entrypoint: 'C:\\Synthetic\\engineering.mjs', config: 'C:\\Synthetic\\engineering.json' };
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
  assert.ok(JSON.stringify(createOpenCodeConfig({ knowledge, engineering })).length < 24000);
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
  previous.agents[ANALYSIS_PROFILE].system = current.agents[ANALYSIS_PROFILE].system;
  assert.deepEqual(previous, current, 'an earlier configuration differs only by its prompt');
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
