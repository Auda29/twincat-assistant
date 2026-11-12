'use strict';
const readline = require('node:readline');
const input = readline.createInterface({input: process.stdin, crlfDelay: Infinity});
let stage = 0;
input.on('line', line => {
  const msg = JSON.parse(line);
  if (stage === 0) {
    if (msg.kind !== 'twincat-assistant-engineering-bootstrap' || msg.protocolVersion !== 1 || !/^\\\\\.\\pipe\\/.test(msg.pipeName) || !/^[a-f0-9]{64}$/.test(msg.capability) || !msg.expected || msg.expected.idePid !== 123) process.exit(2);
    process.stdout.write(JSON.stringify({kind:'twincat-assistant-engineering-ready', protocolVersion:1, sessionId:msg.sessionId}) + '\n'); stage++; return;
  }
  if (msg.method === 'initialize') {
    process.stdout.write(JSON.stringify({jsonrpc:'2.0', id:msg.id, result:{protocolVersion:'2024-11-05', capabilities:{tools:{}}}}) + '\n'); return;
  }
  if (msg.method === 'notifications/initialized') return;
  if (msg.method === 'tools/call' && msg.params.name === 'probe') process.stdout.write(JSON.stringify({jsonrpc:'2.0',id:msg.id,result:{content:[{type:'text',text:String(msg.params.arguments.number)}],pid:process.pid}}) + '\n');
});
