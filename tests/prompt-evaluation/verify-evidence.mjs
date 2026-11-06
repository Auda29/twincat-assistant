// Offline verification of captured real model output and actual WebView renderer evidence.
import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {loadCases} from './evaluate.mjs';
import {checkAnswer,parseAnswer} from './check-answer.mjs';
const fixture=await loadCases();
const root=new URL('./evidence/',import.meta.url);
const read=async p=>JSON.parse(await readFile(new URL(p,root),'utf8'));
const answers=await read('2026-11-05-c1/answers.json');
const catalog=(await read('engineering-tool-catalog.json')).tools;
assert.deepEqual(Object.keys(answers).sort(),fixture.cases.map(c=>c.id).sort(),'complete case coverage');
const results=[];
for(const c of fixture.cases){
 const check=checkAnswer(answers[c.id],c,fixture.sources);assert.ok(check.passed,JSON.stringify(check));
 const capture=await read('2026-11-05-c1/'+c.id+'.json');assert.equal(capture.model.id,'gpt-6.1-sol');assert.equal(capture.model.providerID,'github-copilot');assert.ok(capture.date.startsWith('2026-11-05'));
 assert.ok(capture.messages.some(m=>m.type==='assistant'&&m.content.some(p=>p.type==='text')));
 for(const t of capture.tools.filter(t=>t.name.startsWith('engineering_'))){
  const schema=catalog.find(c=>'engineering_'+c.name===t.name)?.inputSchema;assert.ok(schema);
  for(const key of schema.required??[])assert.ok(key in t.args,'required actual argument '+key);
  if(schema.additionalProperties===false)for(const key of Object.keys(t.args))assert.ok(key in schema.properties,'unavailable argument '+key);
 }
 const tools=capture.tools.map(t=>t.name);
 if(c.id.startsWith('docs-'))assert.equal(tools[0],'knowledge_tc_get_corpus_status');
 if(c.id==='docs-cited-en')assert.ok(tools.indexOf('knowledge_tc_search')>0);
 if(c.id==='method-and-property-en')assert.equal(tools.filter(t=>t==='engineering_plc_read_pou').length,2);
 if(c.id==='large-saved-reference-en'){
  assert.ok(tools.indexOf('engineering_plc_list_pous')<tools.indexOf('engineering_plc_read_pou'));
  assert.ok(capture.tools.some(t=>t.name==='engineering_plc_read_pou'&&JSON.stringify(t.args).includes('fixture-large')));
  assert.ok(tools.includes('engineering_plc_search_code'));
 }
 results.push({id:c.id,automated:check,toolNames:tools});
}
let rendered=0,blocks=0;
for(const width of [430,980]){
 const report=await read('renderer/'+width+'/webview.json');assert.equal(report.width,width);
 assert.equal(report.control,'Microsoft.Web.WebView2.Wpf.WebView2CompositionControl');
 assert.deepEqual(report.results.map(c=>c.id).sort(),Object.keys(answers).sort());
 for(const c of report.results){
 const expected=parseAnswer(answers[c.id]).blocks;
 assert.deepEqual(c.render.errors,[]);assert.equal(c.render.blocks.length,expected.length);
 assert.deepEqual(c.copyHandlerTexts,expected.map(b=>b.code+'\n'),'copy bytes including tabs and trailing newline: '+c.id);
 for(const block of c.render.blocks){assert.equal(block.tabSize,'4');assert.ok(['pre','pre-wrap'].includes(block.whiteSpace));assert.equal(block.header,'st');}
 blocks+=expected.length;rendered++;
 }
}
const summary={date:new Date().toISOString(),caseCount:results.length,renderedCases:rendered,renderedAndCopiedBlocks:blocks,widths:[430,980],results,limits:['Real provider, synthetic inputs and MCP fixtures; not real customer-project acceptance.','WPF/WebView2 fixture with exact pinned renderer source, default light theme, actual code-copy handler with in-memory clipboard sink.','No installed IDE interaction, OS clipboard paste, dark theme or owner sign-off.','Engineering stays at pinned revision 3c30383; an actual packaged MCP probe separately confirms flattened Get/Set without accessor boundaries.']};
if (process.argv.includes('--write-summary'))
  await writeFile(new URL('acceptance-summary.json',root),JSON.stringify(summary,null,2)+'\n');
console.log('PASS '+results.length+' real answers; '+rendered+' WebView renderings; '+blocks+' exact code-copy payloads');
