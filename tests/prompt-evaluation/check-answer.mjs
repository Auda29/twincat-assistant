// Deterministic checks for Analysis-profile answers to the cases in cases.json.
// They cover what can be verified mechanically (code fences, labels, part
// separation, balanced constructs, verbatim quotes, wording, URLs, language).
// Each case's `review` criteria still need a human reviewer.

const LABEL = /^\*\*([^*]+?)\s[-–—]\s(Declaration|Implementation|Deklaration|Implementierung|Get|Set)\*\*:?$/;
const HEADER = /^\s*(FUNCTION_BLOCK|PROGRAM|FUNCTION|METHOD|PROPERTY|INTERFACE|TYPE)\b/;
const OMISSION = /^\s*\/\/\s*\.\.\./;
const PAIRS = [['IF', 'END_IF'], ['CASE', 'END_CASE'], ['FOR', 'END_FOR'], ['WHILE', 'END_WHILE'], ['REPEAT', 'END_REPEAT'],
  ['(?:VAR|VAR_INPUT|VAR_OUTPUT|VAR_IN_OUT|VAR_GLOBAL|VAR_TEMP|VAR_STAT|VAR_INST|VAR_EXTERNAL)', 'END_VAR']];

export function regex(pattern) {
  return pattern.startsWith('(?i)') ? new RegExp(pattern.slice(4), 'iu') : new RegExp(pattern, 'u');
}

// CommonMark-style fences; enough for answers, not a general Markdown parser.
export function parseAnswer(markdown) {
  const lines = markdown.replace(/\r\n?/g, '\n').split('\n');
  const blocks = [], prose = [];
  let open = null, label = null;
  for (const line of lines) {
    if (open) {
      const close = line.match(/^ {0,3}(`{3,}|~{3,})\s*$/);
      if (close && close[1][0] === open.marker[0] && close[1].length >= open.marker.length) { blocks.push(open.block); open = null; }
      else open.block.lines.push(line);
      continue;
    }
    const start = line.match(/^ {0,3}(`{3,}|~{3,})\s*([^`\s]*)/);
    if (start) { open = { marker: start[1], block: { lang: start[2].toLowerCase(), label, lines: [] } }; continue; }
    prose.push(line);
    if (line.trim()) label = line.trim();
  }
  if (open) blocks.push({ ...open.block, unclosed: true });
  return { blocks: blocks.map(block => ({ ...block, code: block.lines.join('\n') })), prose: prose.join('\n') };
}

function stripComments(code) {
  return code.replace(/\(\*[\s\S]*?\*\)/g, ' ').replace(/\/\/.*$/gm, ' ').replace(/'(?:\$.|[^'$])*'/g, "''").replace(/"(?:\$.|[^"$])*"/g, '""');
}
function looksLikeSt(code) { return /:=|\bEND_(IF|CASE|FOR|WHILE|REPEAT|VAR)\b|\bVAR(_\w+)?\b/.test(code); }

export function separatePartsProblem(code) {
  const lines = stripComments(code).split('\n').map(line => line.trim()).filter(Boolean);
  const header = lines.findIndex(line => HEADER.test(line));
  if (header < 0) return null;
  const lastEndVar = lines.findLastIndex(line => /^END_VAR\b/.test(line));
  const end = Math.max(header, lastEndVar);
  const rest = lines.slice(end + 1).filter(line => !/^END_(TYPE|STRUCT)\b|^\{/.test(line));
  return rest.length ? 'declaration and implementation in one block: ' + rest[0] : null;
}

export function balanceProblem(code) {
  const text = stripComments(code);
  for (const [open, close] of PAIRS) {
    const opened = (text.match(new RegExp('\\b' + open + '\\b', 'g')) ?? []).length;
    const closed = (text.match(new RegExp('\\b' + close + '\\b', 'g')) ?? []).length;
    if (opened !== closed) return close + ' count ' + closed + ' differs from ' + open.replace(/\(\?:|\)/g, '') + ' count ' + opened;
  }
  return null;
}

function sourceLines(source) {
  if (source.matches) return source.matches.map(match => match.snippet);
  return [source.declaration, source.implementation].filter(Boolean).flatMap(text => text.split('\n'));
}

function language(prose) {
  const words = prose.toLowerCase().match(/\p{L}+/gu) ?? [];
  const de = new Set(['der', 'die', 'das', 'und', 'ist', 'nicht', 'ein', 'eine', 'wird', 'mit', 'im', 'den', 'dem', 'auf', 'ich', 'sie']);
  const en = new Set(['the', 'and', 'is', 'not', 'with', 'this', 'of', 'to', 'it', 'in', 'when', 'are', 'you', 'that']);
  const score = set => words.filter(word => set.has(word)).length;
  const [g, e] = [score(de), score(en)];
  return g === e ? 'unknown' : g > e ? 'de' : 'en';
}

export function checkAnswer(markdown, testCase, sources = {}) {
  const { blocks, prose } = parseAnswer(markdown);
  const checks = testCase.checks ?? {};
  // Label, separation and balance checks apply to ST however it was tagged.
  const st = blocks.filter(block => block.lang === 'st' || (!['text', 'xml'].includes(block.lang) && looksLikeSt(block.code)));
  const results = {};
  const record = (name, problems) => { results[name] = { ok: problems.length === 0, problems }; };

  record('language', language(prose) === testCase.language ? [] : ['expected ' + testCase.language + ', detected ' + language(prose)]);
  record('closedFences', blocks.filter(block => block.unclosed).map(() => 'unclosed code fence'));
  if (checks.stFencesOnly) record('stFencesOnly', blocks.flatMap(block =>
    !block.lang ? ['code block without language tag'] : block.lang !== 'st' && looksLikeSt(block.code) ? ['ST code tagged ' + block.lang] : []));
  if (checks.labelsBeforeBlocks) record('labelsBeforeBlocks', st.flatMap(block =>
    LABEL.test(block.label ?? '') ? [] : ['no part label above block: ' + (block.label ?? '(none)')]));
  if (checks.separateParts) record('separateParts', st.map(block => separatePartsProblem(block.code)).filter(Boolean));
  if (checks.balancedBlocks) record('balancedBlocks', st.map(block => balanceProblem(block.code)).filter(Boolean));
  if (checks.verbatimFrom) {
    const allowed = new Set(checks.verbatimFrom.flatMap(id => {
      if (!sources[id]) throw new Error('Unknown source ' + id);
      return sourceLines(sources[id]);
    }));
    record('verbatimFrom', st.flatMap(block => block.lines.filter(line => line.trim() && !OMISSION.test(line) && !allowed.has(line))
      .map(line => 'not verbatim: ' + JSON.stringify(line))));
  }
  if (checks.requiredLabels) {
    const labels = st.map(block => block.label ?? '');
    record('requiredLabels', checks.requiredLabels.filter(pattern => !labels.some(label => regex(pattern).test(label))).map(pattern => 'missing label ' + pattern));
  }
  if (checks.mustMention) record('mustMention', checks.mustMention.filter(pattern => !regex(pattern).test(prose)).map(pattern => 'missing ' + pattern));
  if (checks.mustNotMention) record('mustNotMention', checks.mustNotMention.filter(pattern => regex(pattern).test(prose)).map(pattern => 'forbidden ' + pattern));
  if (checks.maxCodeBlocks !== undefined) record('maxCodeBlocks', blocks.length > checks.maxCodeBlocks ? [blocks.length + ' code blocks'] : []);
  if (checks.allowedUrls) {
    const urls = markdown.match(/https?:\/\/[^\s)>\]]+/g) ?? [];
    record('allowedUrls', urls.filter(url => !checks.allowedUrls.includes(url)).map(url => 'unreturned URL ' + url));
  }
  return { id: testCase.id, passed: Object.values(results).every(result => result.ok), results };
}
