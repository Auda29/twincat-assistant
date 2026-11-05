// Synthetic read-only MCP for real-provider prompt evaluation. No engineering backend.
import { createInterface } from 'node:readline';
import { appendFile, readFile } from 'node:fs/promises';
const [caseFile, scope, trace] = process.argv.slice(2);
const data = JSON.parse(await readFile(caseFile, 'utf8'));
const catalog=scope==='engineering'?JSON.parse(await readFile(new URL('./evidence/engineering-tool-catalog.json',import.meta.url),'utf8')).tools:null;
const names = scope === 'knowledge' ? ['tc_get_corpus_status','tc_search','tc_get_topic','tc_browse_topics'] : ['plc_list_pous','plc_read_pou','plc_search_code','plc_describe_pou','plc_list_libraries','plc_describe_library','tc_list_projects','tc_project_state','tc_list_workbenches'];
for await (const line of createInterface({ input: process.stdin })) {
 let requestId;
 try {
 const q = JSON.parse(line);requestId=q.id; if(q.id === undefined) continue;
 let result;
 if(q.method === 'initialize') result = {protocolVersion:'2024-11-05',capabilities:{tools:{}},serverInfo:{name:'synthetic-'+scope,version:'1.0'}};
 else if(q.method === 'tools/list') result = {tools:catalog??names.map(name=>({name,description:'Read synthetic evaluation data: '+name,inputSchema:{type:'object',properties:{id:{type:'string'},query:{type:'string'},name:{type:'string'},pou_id:{type:'string'},topic_id:{type:'string'}},additionalProperties:true}}))};
 else if(q.method === 'tools/call') {
 const name=q.params.name, args=q.params.arguments??{};
 const schema=catalog?.find(t=>t.name===name)?.inputSchema;const required=schema?.required??[];if(required.some(k=>args[k]===undefined)||schema?.additionalProperties===false&&Object.keys(args).some(k=>!(k in schema.properties)))throw new Error('Invalid tool argument');
 let value;
 if(name==='tc_get_corpus_status') value={status:data.corpus,topic_count:data.corpus==='ok'?1:0};
 else if(name==='plc_list_pous') value={pous:data.tools.filter(x=>x.object).map((x,i)=>({id:x.id??'fixture-'+i,name:x.object,qualifiedName:x.object,kind:x.objectKind??'functionBlock',sourceFile:'C:\\Synthetic\\PLC\\'+x.object.split('.')[0]+'.TcPOU',project:'C:\\Synthetic\\PLC\\PLC.plcproj'}))};
 else if(name==='plc_read_pou'||name==='plc_describe_pou') {const list=data.tools.filter(x=>x.object);const needle=JSON.stringify(args.pou??''); const found=list.find((x,i)=>needle.includes(x.id??'fixture-'+i)||needle.includes(x.object));value=found?{pou:{...found,kind:found.objectKind??'functionBlock',id:found.id??'fixture-'+list.indexOf(found),qualifiedName:found.object}}: {error:'Object ID required; call plc_list_pous first'};}
 else if(name==='plc_search_code'&&data.tools.some(x=>x.id==='fixture-large')) value={matches:[{pou:'FB_Large',section:'implementation',line:3501,snippet:'nCounter := nCounter + 1;'}]};
 else value=data.tools.find(x=>x.tool===scope+'_'+name)??{status:data.corpus,items:[]};
 await appendFile(trace,JSON.stringify({at:new Date().toISOString(),name:scope+'_'+name,args,result:value})+'\n');
 result={content:[{type:'text',text:JSON.stringify(value)}]};
 } else if(q.method==='ping') result={}; else throw new Error('Unsupported request');
 process.stdout.write(JSON.stringify({jsonrpc:'2.0',id:q.id,result})+'\n');
 } catch { if(requestId!==undefined)process.stdout.write(JSON.stringify({jsonrpc:'2.0',id:requestId,error:{code:-32602,message:'Invalid synthetic fixture request'}})+'\n'); }
}
