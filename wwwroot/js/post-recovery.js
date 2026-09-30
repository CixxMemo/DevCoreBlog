// Local recovery persistence only: never store files, cookies, tokens or passwords.
const prefix = 'devcore_editor_draft_';
export const scratchKey = 'devcore_editor_scratch_transfer';
export const fieldNames = Object.freeze([
    'Title', 'Content', 'CategoryId', 'Summary', 'Excerpt', 'PublishDate', 'IsPublished', 'IsActive'
]);
const booleanFields = new Set(['IsPublished', 'IsActive']);
const revisionPattern = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;

export function recoveryKey(mode, id) {
    if (mode === 'create') return prefix + 'create';
    if (mode === 'edit' && /^[1-9]\d*$/.test(String(id))) return prefix + 'edit_' + id;
    return null;
}

function decode(raw, baseline) {
    if (!raw) return null;
    try {
        const record = JSON.parse(raw);
        if (record?.version === 1 && revisionPattern.test(record.revision) &&
            typeof record.savedAt === 'string' && record.fields &&
            fieldNames.every(name => typeof record.fields[name] ===
                (booleanFields.has(name) ? 'boolean' : 'string'))) return record;
        // Preserve the old Create copy until an explicit choice or confirmed save.
        if (baseline && typeof record?.content === 'string' &&
            (record.title === undefined || typeof record.title === 'string') &&
            (record.summary === undefined || typeof record.summary === 'string') &&
            (record.categoryId === undefined || typeof record.categoryId === 'string' ||
                typeof record.categoryId === 'number')) {
            return { fields: { ...baseline, Title: record.title || '', Content: record.content,
                Summary: record.summary || '', CategoryId: String(record.categoryId || '') },
                savedAt: record.savedAt || '' };
        }
    } catch { /* An unreadable copy must not be overwritten automatically. */ }
    return null;
}

function sameFields(left, right) {
    return fieldNames.every(name => left?.[name] === right?.[name]);
}

async function locked(locks, key, action) {
    // No unsafe read/check/write fallback when cross-tab coordination is unavailable.
    if (!locks?.request) throw new Error('Recovery coordination unavailable.');
    return locks.request('devcore-post-recovery:' + key, action);
}

// Every writer compares the exact revision under the same origin-scoped lock.
export function createRecoverySession({ key, storage, locks, baseline, readFields,
    notify, newRevision, now }) {
    let expectedRaw = null;
    let pending = false;
    let available = true;
    let scratchContent = null;

    function observe(raw, conflict = false) {
        expectedRaw = raw;
        const record = decode(raw, baseline);
        pending = !!raw && !sameFields(record?.fields, readFields());
        notify({ kind: pending ? (conflict ? 'conflict' : 'offer') : 'ready', record });
        return record;
    }

    try { observe(storage.getItem(key)); }
    catch { available = false; notify({ kind: 'unavailable' }); }

    async function transact(action) {
        if (!available) return null;
        try { return await locked(locks, key, action); }
        catch { notify({ kind: 'unavailable' }); return null; }
    }

    async function save() {
        return transact(() => {
            const raw = storage.getItem(key);
            if (raw !== expectedRaw) { observe(raw, true); return null; }
            if (pending) return null;
            const fields = readFields();
            const current = decode(raw, baseline);
            if (current?.revision && sameFields(current.fields, fields)) return current.revision;
            const record = { version: 1, revision: newRevision(), savedAt: now(),
                fields: Object.fromEntries(fieldNames.map(name => [name, fields[name]])) };
            if (scratchContent !== null) record.scratchContent = scratchContent;
            const encoded = JSON.stringify(record);
            storage.setItem(key, encoded);
            expectedRaw = encoded;
            notify({ kind: 'saved', record });
            return record.revision;
        });
    }

    async function choose(restore) {
        return transact(() => {
            const raw = storage.getItem(key);
            if (raw !== expectedRaw) { observe(raw, true); return null; }
            const record = decode(raw, baseline);
            if (restore) {
                if (!record) return null;
                pending = false;
                scratchContent = typeof record.scratchContent === 'string' ? record.scratchContent : null;
                notify({ kind: 'restored', record });
                return record.fields;
            }
            // Discard is an explicit user action, never a submit side effect.
            if (typeof record?.scratchContent === 'string' &&
                storage.getItem(scratchKey) === record.scratchContent) storage.removeItem(scratchKey);
            storage.removeItem(key);
            expectedRaw = null;
            pending = false;
            scratchContent = null;
            notify({ kind: 'discarded' });
            return null;
        });
    }

    // Scratchpad transfer is offered, never silently placed over form/server text.
    async function offerScratch() {
        if (key !== recoveryKey('create')) return;
        await transact(() => {
            if (storage.getItem(key) !== null) return;
            const note = storage.getItem(scratchKey);
            if (!note) return;
            const record = { version: 1, revision: newRevision(), savedAt: now(),
                fields: { ...baseline, Content: note }, scratchContent: note };
            const raw = JSON.stringify(record);
            storage.setItem(key, raw);
            observe(raw);
        });
    }

    function refresh() {
        if (!available) return;
        try {
            const raw = storage.getItem(key);
            if (raw !== expectedRaw) observe(raw, true);
        } catch { notify({ kind: 'unavailable' }); }
    }

    return Object.freeze({ save, restore: () => choose(true), discard: () => choose(false),
        refresh, offerScratch });
}

// The receipt comes only from the successful MVC redirect, never from query input.
export async function clearConfirmedRecovery({ key, revision, storage, locks }) {
    if (!/^devcore_editor_draft_(create|edit_[1-9]\d*)$/.test(key) ||
        !revisionPattern.test(revision)) return false;
    try {
        return await locked(locks, key, () => {
            const record = decode(storage.getItem(key));
            if (!record || record.revision !== revision) return false;
            // Preserve a newer scratchpad transfer too.
            if (typeof record.scratchContent === 'string' &&
                storage.getItem(scratchKey) === record.scratchContent) storage.removeItem(scratchKey);
            storage.removeItem(key);
            return true;
        });
    } catch { return false; }
}
