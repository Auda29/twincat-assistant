import { appendFile } from 'node:fs/promises';
import { Server } from '@modelcontextprotocol/server';
import { serveStdio } from '@modelcontextprotocol/server/stdio';
if (process.env.TC_TEST_KNOWLEDGE_STARTS) await appendFile(process.env.TC_TEST_KNOWLEDGE_STARTS, process.pid + '\n');
await new Promise(resolve => setTimeout(resolve, 150));
serveStdio(() => {
  const server = new Server({ name: 'synthetic-knowledge', version: '1.0.0' }, { capabilities: { tools: {} } });
  server.setRequestHandler('tools/list', async () => ({ tools: ['tc_browse_topics', 'tc_get_corpus_status', 'tc_get_topic', 'tc_search', 'tc_find_symbols', 'tc_get_symbol', 'tc_find_libraries', 'tc_get_library', 'tc_lookup_error', 'unreviewed_write'].filter(name => name !== process.env.TC_TEST_OMIT_TOOL).map(name => ({ name, description: name, inputSchema: { type: 'object', properties: {}, additionalProperties: true } })) }));
  server.setRequestHandler('tools/call', async (request,ctx) => {
    if(request.params.arguments?.query==='slow')await new Promise((resolve,reject)=>{
      const timer=setTimeout(resolve,2000);
      ctx.mcpReq.signal.addEventListener('abort',()=>{clearTimeout(timer);reject(new Error('Cancelled fixture query'));},{once:true});
    });
    return {
    content: [{ type: 'text', text: request.params.name + ': status=ok; count=1.' }],
    structuredContent: { status: 'ok', backend_pid:process.pid, arguments: request.params.arguments, data: { items: [{ topic_id: 'fixture-topic-1', title: 'Fixture Beckhoff topic', text: request.params.arguments?.query ?? 'Fixture article', source_uri: 'https://example.invalid/fixture-topic-1' }] } },
    isError: false, resultType: 'complete',
  };});
  return server;
});
