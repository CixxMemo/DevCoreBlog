// Regression checks for recovery ownership, storage failures and save receipts.
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { createRecoverySession, clearConfirmedRecovery, recoveryKey, scratchKey, fieldNames }
    from '../../wwwroot/js/post-recovery.js';

import { initializeRecoveryForm } from '../../wwwroot/js/post-recovery-form.js';

const baseline = { Title: '', Content: '', CategoryId: '', Summary: '', Excerpt: '', ThumbnailAlt: '',
    PublishDate: '2026-09-30T12:00', IsPublished: false, IsActive: true };
const data = new Map();
const storage = { getItem: key => data.get(key) ?? null,
    setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) };
// Serialize the same key like the browser's exclusive Web Lock, including simultaneous writers.
const tails = new Map();
const locks = { request(key, action) {
    const next = (tails.get(key) ?? Promise.resolve()).then(action);
    tails.set(key, next.catch(() => {}));
    return next;
} };
function session(key, initial = baseline, overrides = {}) {
    let fields = { ...initial };
    const notices = [];
    return { engine: createRecoverySession({ key, storage, locks, baseline: { ...initial },
        readFields: () => fields, notify: notice => notices.push(notice),
        newRevision: randomUUID, now: () => '2026-09-30T12:00:00Z', ...overrides }),
        set: value => { fields = { ...fields, ...value }; }, notices };
}
const checks = {};
async function check(name, action) { data.clear(); await action(); checks[name] = true; }
const create = recoveryKey('create');
const edit = recoveryKey('edit', 2002);
await check('isolated_keys_and_whitelisted_fields', async () => {
    assert.notEqual(create, edit); assert.notEqual(edit, recoveryKey('edit', 2003));
    assert.equal(recoveryKey('edit', '../cookie'), null);
    const a = session(create); const b = session(edit);
    a.set({ Title: 'Create text', Content: 'Body', Password: 'not stored', thumbnailFile: 'not stored' });
    b.set({ Title: 'Edit text' }); await a.engine.save(); await b.engine.save();
    assert.deepEqual(Object.keys(JSON.parse(data.get(create)).fields), fieldNames);
    assert.equal(JSON.parse(data.get(edit)).fields.Title, 'Edit text');
});
await check('reload_offers_without_overwriting_and_restores_empty_fields', async () => {
    const a = session(edit); a.set({ Title: 'Unsaved', Content: 'Exact content' }); await a.engine.save();
    const raw = data.get(edit); const b = session(edit, { ...baseline, Summary: 'Server summary' });
    assert.equal(b.notices.at(-1).kind, 'offer'); assert.equal(await b.engine.save(), null);
    assert.equal(data.get(edit), raw);
    const restored = await b.engine.restore(); assert.equal(restored.Content, 'Exact content');
    assert.equal(restored.Summary, ''); b.set(restored); assert.ok(await b.engine.save());
});
await check('legacy_create_copy_requires_explicit_restore', async () => {
    data.set(create, JSON.stringify({ title: 'Legacy', content: 'Legacy body', categoryId: 1001 }));
    const a = session(create); assert.equal(await a.engine.save(), null);
    const fields = await a.engine.restore(); assert.equal(fields.Title, 'Legacy');
    assert.equal(fields.CategoryId, '1001'); a.set(fields); assert.ok(await a.engine.save());
});
await check('malformed_copy_is_not_silently_overwritten', async () => {
    data.set(edit, '{broken'); const a = session(edit); assert.equal(await a.engine.save(), null);
    assert.equal(await a.engine.restore(), null); assert.equal(data.get(edit), '{broken');
    await a.engine.discard(); assert.equal(data.has(edit), false);
});
await check('simultaneous_tabs_preserve_first_copy_and_expose_conflict', async () => {
    const a = session(edit); const b = session(edit); a.set({ Title: 'Tab A' }); b.set({ Title: 'Tab B' });
    const revisions = await Promise.all([a.engine.save(), b.engine.save()]);
    assert.ok(revisions[0]); assert.equal(revisions[1], null);
    assert.equal(JSON.parse(data.get(edit)).fields.Title, 'Tab A');
    assert.equal(b.notices.at(-1).kind, 'conflict');
    assert.equal((await b.engine.restore()).Title, 'Tab A');
});
await check('stale_receipt_keeps_newer_copy_matching_receipt_clears_only_its_key', async () => {
    const a = session(edit); const other = session(create); await other.engine.save();
    a.set({ Title: 'Submitted' }); const old = await a.engine.save();
    // Failure/no receipt leaves this exact submitted copy available.
    assert.equal(JSON.parse(data.get(edit)).revision, old);
    a.set({ Title: 'Newer' }); const latest = await a.engine.save();
    assert.equal(await clearConfirmedRecovery({ key: edit, revision: old, storage, locks }), false);
    assert.equal(JSON.parse(data.get(edit)).fields.Title, 'Newer');
    assert.equal(await clearConfirmedRecovery({ key: edit, revision: latest, storage, locks }), true);
    assert.equal(data.has(edit), false); assert.equal(data.has(create), true);
});
await check('stale_restore_and_discard_cannot_delete_an_unseen_newer_copy', async () => {
    const a = session(edit); const b = session(edit); const c = session(edit);
    a.set({ Title: 'Newer' }); await a.engine.save();
    assert.equal(await c.engine.restore(), null);
    assert.equal(JSON.parse(data.get(edit)).fields.Title, 'Newer');
    await b.engine.discard(); assert.equal(JSON.parse(data.get(edit)).fields.Title, 'Newer');
    assert.equal(b.notices.at(-1).kind, 'conflict');
});
await check('denied_storage_quota_and_missing_locks_are_nonthrowing', async () => {
    for (const overrides of [
        { storage: { getItem() { throw new Error('Denied'); } } },
        { storage: { ...storage, setItem() { throw new Error('Quota'); } } }, { locks: undefined }
    ]) {
        const a = session(edit, baseline, overrides); a.set({ Title: 'Still in form' });
        assert.equal(await a.engine.save(), null); assert.equal(a.notices.at(-1).kind, 'unavailable');
    }
    assert.equal(data.size, 0);
});
await check('scratch_transfer_is_offered_and_only_matching_transfer_is_cleared', async () => {
    data.set(scratchKey, 'Scratch body'); const a = session(create); await a.engine.offerScratch();
    assert.equal(a.notices.at(-1).kind, 'offer');
    const fields = await a.engine.restore(); assert.equal(fields.Content, 'Scratch body'); a.set(fields);
    const revision = await a.engine.save(); data.set(scratchKey, 'New scratch');
    assert.equal(await clearConfirmedRecovery({ key: create, revision, storage, locks }), true);
    assert.equal(data.get(scratchKey), 'New scratch');
    const b = session(create); await b.engine.offerScratch(); await b.engine.discard();
    assert.equal(data.has(scratchKey), false); assert.equal(data.has(create), false);
});
await check('untrusted_receipt_key_and_bad_revision_do_not_clear_storage', async () => {
    data.set('cookie', 'retained');
    assert.equal(await clearConfirmedRecovery({ key: 'cookie', revision: randomUUID(), storage, locks }), false);
    assert.equal(data.get('cookie'), 'retained');
});
await check('storage_failure_does_not_block_native_form_submission', async () => {
    const original = Object.fromEntries(['window', 'document', 'navigator'].map(name =>
        [name, Object.getOwnPropertyDescriptor(globalThis, name)]));
    try {
        for (const denied of [true, false]) {
            const nodes = Object.fromEntries(['draft-recovery-banner', 'draft-recovery-message',
                'draft-timestamp', 'autosave-status', 'btn-restore-draft', 'btn-discard-draft']
                .map(id => [id, { textContent: '', classList: { add() {}, remove() {} },
                    addEventListener() {} }]));
            const inputs = Object.fromEntries(Object.entries(baseline).map(([name, value]) =>
                [name, { value, checked: value }]));
            inputs.recoveryRevision = { value: '' };
            const listeners = {};
            let submitted = 0;
            const form = { dataset: { mode: 'create' }, elements: { namedItem: name => inputs[name] },
                querySelector: selector => selector.includes('IsPublished') ? inputs.IsPublished
                    : selector.includes('IsActive') ? inputs.IsActive : null,
                addEventListener: (name, listener) => { listeners[name] = listener; },
                requestSubmit: submitter => { assert.equal(submitter, 'save-button'); submitted++; } };
            Object.defineProperty(globalThis, 'window', { configurable: true, value: {
                localStorage: { getItem() { if (denied) throw new Error('Denied'); return null; },
                    setItem() { throw new Error('Quota'); }, removeItem() {} }, addEventListener() {} } });
            Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { locks } });
            Object.defineProperty(globalThis, 'document', { configurable: true, value: {
                getElementById: id => nodes[id], addEventListener() {} } });
            initializeRecoveryForm({ form, setMarkdown() {}, updatePreview() {} });
            await listeners.submit({ submitter: 'save-button', defaultPrevented: false, preventDefault() {} });
            assert.equal(submitted, 1);
            assert.equal(inputs.recoveryRevision.value, '');
            assert.match(nodes['autosave-status'].textContent, /UNAVAILABLE/);
        }
    } finally {
        for (const [name, descriptor] of Object.entries(original)) {
            if (descriptor) Object.defineProperty(globalThis, name, descriptor);
            else delete globalThis[name];
        }
    }
});
console.log(JSON.stringify({ checks, count: Object.keys(checks).length }, null, 2));

// A pre-F49 revision must remain recoverable and must not erase the server description.
{
    const key = recoveryKey('edit', 4901);
    const fields = { ...baseline, Content: 'Older local copy' };
    delete fields.ThumbnailAlt;
    const raw = JSON.stringify({ version: 1, revision: randomUUID(), savedAt: '2026-10-04T12:00:00Z', fields });
    data.set(key, raw);
    const copy = session(key, { ...baseline, ThumbnailAlt: 'Saved cover description' });
    assert.equal(data.get(key), raw);
    const restored = await copy.engine.restore();
    assert.equal(restored.ThumbnailAlt, 'Saved cover description');
    copy.set({ ...restored, ThumbnailAlt: 'Recovered description' });
    await copy.engine.save();
    assert.equal(JSON.parse(data.get(key)).fields.ThumbnailAlt, 'Recovered description');
    console.log('f49_description_and_legacy_recovery=true');
}
