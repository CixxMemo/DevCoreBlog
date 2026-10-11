// Local storage is untrusted. This format is distinct from all Markdown/scratch copies.
export const recoveryFields = Object.freeze(['DocumentJson', 'Title', 'CategoryId', 'Summary', 'Excerpt',
    'ThumbnailAlt', 'PublishDate', 'IsActive', 'EditVersion']);
export const recoveryBytes = 2_162_688;
const uuid = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/iu;
const sizes = { DocumentJson: 1_048_576, Title: 200, CategoryId: 10, Summary: 500, Excerpt: 1000,
    ThumbnailAlt: 300, PublishDate: 32, EditVersion: 19 };
const bytes = value => new TextEncoder().encode(value).length;
const plain = value => !!value && typeof value === 'object' && !Array.isArray(value);
const exactKeys = (value, keys) => plain(value) && Object.keys(value).length === keys.length && keys.every(k => Object.hasOwn(value, k));

export function recoveryKey(id) {
    return /^(0|[1-9]\d{0,9})$/u.test(String(id)) && Number(id) <= 2147483647
        ? `devcore_document_draft_v1_${Number(id) === 0 ? 'create' : 'edit_' + id}` : null;
}

export function validRecoveryFields(fields) {
    if (!exactKeys(fields, recoveryFields) || typeof fields.IsActive !== 'boolean') return false;
    for (const [key, limit] of Object.entries(sizes)) {
        if (typeof fields[key] !== 'string' || fields[key].length > limit ||
            key === 'DocumentJson' && bytes(fields[key]) > limit) return false;
    }
    if (!/^\d{1,19}$/u.test(fields.EditVersion) || fields.CategoryId && !/^\d{1,10}$/u.test(fields.CategoryId)) return false;
    try {
        const envelope = JSON.parse(fields.DocumentJson);
        return exactKeys(envelope, ['version', 'document']) && envelope.version === 1 && plain(envelope.document);
    } catch { return false; }
}

export function decodeRecovery(raw, id) {
    if (typeof raw !== 'string' || raw.length > recoveryBytes || bytes(raw) > recoveryBytes) return null;
    try {
        const value = JSON.parse(raw);
        if (!exactKeys(value, ['kind', 'version', 'documentVersion', 'postId', 'revision', 'savedAt', 'fields']) ||
            value.kind !== 'aether-notes-json-draft' || value.version !== 1 || value.documentVersion !== 1 ||
            value.postId !== Number(id) || !recoveryKey(value.postId) || typeof value.revision !== 'string' || !uuid.test(value.revision) ||
            typeof value.savedAt !== 'string' || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/u.test(value.savedAt) ||
            !Number.isFinite(Date.parse(value.savedAt)) || !validRecoveryFields(value.fields)) return null;
        return value;
    } catch { return null; }
}

export async function withRecoveryLock(locks, key, action) {
    if (!locks?.request || !key) throw new Error('Recovery coordination unavailable');
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 12000);
    try { return await locks.request('devcore-document-recovery:' + key, { signal: controller.signal }, action); }
    finally { clearTimeout(timeout); }
}

const sameFields = (left, right) => recoveryFields.every(key => left?.[key] === right?.[key]);

// Every writer, restore choice and commit cleanup compares under the same origin lock.
export function createDocumentRecovery({ id, storage, locks, readFields, notify, revision, now }) {
    const key = recoveryKey(id);
    let expectedRaw = null, pending = false;
    function observe(raw, conflict = false) {
        expectedRaw = raw;
        const record = decodeRecovery(raw, id);
        pending = raw !== null && (!record || !sameFields(record.fields, readFields()));
        notify({ kind: pending ? (record ? (conflict ? 'conflict' : 'offer') : 'unknown') : 'ready', record });
        return record;
    }
    try { observe(storage.getItem(key)); }
    catch { notify({ kind: 'unavailable' }); }
    async function transact(action) {
        try { return await withRecoveryLock(locks, key, action); }
        catch { notify({ kind: 'unavailable' }); return null; }
    }
    function current() {
        const raw = storage.getItem(key);
        if (raw !== expectedRaw) { observe(raw, true); return { changed: true, raw }; }
        return { changed: false, raw };
    }
    async function save() {
        return transact(() => {
            const { changed, raw } = current();
            if (changed || pending) return null;
            const fields = readFields();
            if (!validRecoveryFields(fields)) { notify({ kind: 'limit' }); return null; }
            const previous = decodeRecovery(raw, id);
            if (previous && sameFields(previous.fields, fields)) return previous.revision;
            const record = { kind: 'aether-notes-json-draft', version: 1, documentVersion: 1, postId: Number(id),
                revision: revision(), savedAt: now(), fields };
            const encoded = JSON.stringify(record);
            if (!decodeRecovery(encoded, id)) { notify({ kind: 'limit' }); return null; }
            storage.setItem(key, encoded); expectedRaw = encoded;
            notify({ kind: 'saved', record }); return record.revision;
        });
    }
    async function restore(validateAndApply) {
        return transact(async () => {
            const { changed, raw } = current();
            const record = decodeRecovery(raw, id);
            if (changed || !record) return false;
            if (!await validateAndApply(record.fields, () => storage.getItem(key) === raw)) return false;
            pending = false; notify({ kind: 'restored', record }); return true;
        });
    }
    async function discard() {
        return transact(() => {
            if (current().changed) return false;
            storage.removeItem(key); expectedRaw = null; pending = false;
            notify({ kind: 'discarded' }); return true;
        });
    }
    function refresh() {
        try { if (storage.getItem(key) !== expectedRaw) observe(storage.getItem(key), true); }
        catch { notify({ kind: 'unavailable' }); }
    }
    return Object.freeze({ save, restore, discard, refresh, key });
}

export async function clearDocumentRecovery({ id, revision, storage, locks }) {
    const key = recoveryKey(id);
    if (typeof revision !== 'string' || !uuid.test(revision)) return false;
    try {
        return await withRecoveryLock(locks, key, () => {
            const record = decodeRecovery(storage.getItem(key), id);
            if (!record || record.revision !== revision) return false;
            storage.removeItem(key); return true;
        });
    } catch { return false; }
}
