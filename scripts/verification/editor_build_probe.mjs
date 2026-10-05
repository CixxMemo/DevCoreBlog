import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { buildEditor, replaceSanitizer, sha256 } from '../frontend/editor-bundle.mjs';

const root = new URL('../../', import.meta.url);
const upstream = await readFile(new URL('frontend/vendor/toastui/upstream/toastui-editor-all.js', root), 'utf8');
const purify = await readFile(new URL('node_modules/dompurify/dist/purify.cjs.js', root), 'utf8');
assert.throws(() => replaceSanitizer(upstream + '\n', purify), /integrity changed/);
assert.throws(() => replaceSanitizer(upstream, purify + '\n'), /integrity changed/);
const rebuilt = await buildEditor(root);
const shipped = await readFile(new URL('wwwroot/generated/toastui/toastui-editor-all.min.js', root), 'utf8');
assert.equal(shipped, rebuilt);
assert.ok(!shipped.includes('DOMPurify 2.3.3') && shipped.includes('DOMPurify 3.4.16'));
const receipt = JSON.parse(await readFile(new URL('wwwroot/generated/toastui/build.json', root), 'utf8'));
assert.equal(receipt.sha256, sha256(shipped));
assert.equal(receipt.dompurify, '3.4.16');
console.log(JSON.stringify({ checks: {
    altered_upstream_fails_closed: true, altered_sanitizer_fails_closed: true,
    deterministic_rebuild_matches_distribution: true, old_sanitizer_absent: true,
    distribution_hash_and_version_match: true
} }, null, 2));
