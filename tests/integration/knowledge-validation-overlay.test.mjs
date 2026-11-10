import test from 'node:test';
import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { join, resolve } from 'node:path';
const input = process.env.TC_KNOWLEDGE_INPUT;
test('actual Python validators preserve refusals and cache only successful exact locators', { skip: !input, timeout: 60000 }, async () => {
  const { stdout } = await promisify(execFile)(join(input,'python/python.exe'),
    ['-I','-B',resolve('tests/fixtures/knowledge-validation-equivalence.py'),resolve(input)], { windowsHide:true });
  const value = JSON.parse(stdout);
  assert.ok(value.cases >= 100); assert.equal(value.equivalent,true);
  assert.equal(value.maxsize,4096); assert.equal(value.cachedErrors,0);
  console.log(stdout.trim());
});
