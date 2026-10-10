import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildTiptap } from '../frontend/tiptap-bundle.mjs';
import { sha256 } from '../frontend/editor-bundle.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
const shipped = resolve(root, 'wwwroot/generated/tiptap');
const owned = await mkdtemp(resolve(tmpdir(), 'devcoreblog-tiptap-build-'));
try {
    await buildTiptap(root, owned);
    const receipt = JSON.parse(await readFile(resolve(shipped, 'build.json'), 'utf8'));
    assert.deepEqual(JSON.parse(await readFile(resolve(owned, 'build.json'), 'utf8')), receipt);
    assert.ok(receipt.files['trial.js'] && receipt.files['editor.css']);
    assert.ok(Object.keys(receipt.files).some(name => name.startsWith('chunks/') && name.endsWith('.js')));
    for (const [file, metadata] of Object.entries(receipt.files)) {
        const actual = await readFile(resolve(shipped, file));
        assert.equal(sha256(actual), metadata.sha256);
        assert.equal(actual.length, metadata.bytes);
        assert.deepEqual(await readFile(resolve(owned, file)), actual);
    }
    for (const pkg of receipt.packages) {
        const actual = await readFile(resolve(shipped, pkg.file));
        assert.ok(actual.length && ['MIT', 'ISC', 'BSD-3-Clause'].includes(pkg.license));
        assert.equal(sha256(actual), pkg.sha256);
        if (pkg.name.startsWith('@tiptap/')) assert.equal(pkg.version, receipt.tiptap);
    }
    assert.deepEqual(await readFile(resolve(shipped, 'licenses/tiptap__pm-third-party.txt')),
        await readFile(resolve(root, 'node_modules/@tiptap/pm/THIRD_PARTY_LICENSES.md')));
    assert.ok(receipt.packages.some(pkg => pkg.name === '@tiptap/core'));
    assert.ok(!receipt.packages.some(pkg => /react|vue|@tiptap-pro/.test(pkg.name)));
    console.log(JSON.stringify({ checks: {
        deterministic_rebuild_matches_distribution: true,
        local_entry_chunk_and_css_present: true,
        output_hashes_and_byte_counts_match: true,
        all_bundled_package_licenses_retained: true,
        tiptap_family_versions_match: true,
        no_react_vue_or_paid_package: true,
    } }, null, 2));
} finally {
    await rm(owned, { recursive: true, force: true });
}
