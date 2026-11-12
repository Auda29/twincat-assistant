import test from 'node:test';
import assert from 'node:assert/strict';
import { isReviewedKnowledgeOverlay, originalSha256, patchedSha256, lfOriginalSha256, lfPatchedSha256, optimizeKnowledgeValidation } from '../scripts/knowledge-validation-overlay.mjs';

test('overlay receipts cannot mix the CRLF and canonical LF reviewed variants', () => {
  const old = {version:1, originalSha256, patchedSha256};
  const canonical = {version:1,originalSha256:lfOriginalSha256,patchedSha256:lfPatchedSha256};
  assert.equal(isReviewedKnowledgeOverlay(old,patchedSha256),true);
  assert.equal(isReviewedKnowledgeOverlay(canonical,lfPatchedSha256),true);
  assert.equal(isReviewedKnowledgeOverlay({...old,patchedSha256:lfPatchedSha256},lfPatchedSha256),false);
  assert.equal(isReviewedKnowledgeOverlay(canonical,patchedSha256),false);
  assert.equal(isReviewedKnowledgeOverlay({...canonical,originalSha256:'a'.repeat(64)},lfPatchedSha256),false);
  assert.throws(()=>optimizeKnowledgeValidation(Buffer.from('unreviewed source')),/overlay review required/);
});
