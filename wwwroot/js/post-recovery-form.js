import { createRecoverySession, recoveryKey, fieldNames } from './post-recovery.js';

// Bind the persistence contract to one MVC form, including the textarea fallback.
export function initializeRecoveryForm({ form, setMarkdown, updatePreview }) {
    const key = recoveryKey(form.dataset.mode, form.dataset.postId);
    if (!key) return;
    const banner = document.getElementById('draft-recovery-banner');
    const message = document.getElementById('draft-recovery-message');
    const timestamp = document.getElementById('draft-timestamp');
    const status = document.getElementById('autosave-status');
    const restore = document.getElementById('btn-restore-draft');
    const discard = document.getElementById('btn-discard-draft');
    const revisionField = form.elements.namedItem('recoveryRevision');
    const storage = {
        getItem: key => window.localStorage.getItem(key),
        setItem: (key, value) => window.localStorage.setItem(key, value),
        removeItem: key => window.localStorage.removeItem(key)
    };

    function readFields() {
        return Object.fromEntries(fieldNames.map(name => {
            const field = form.elements.namedItem(name);
            // MVC checkbox helpers also emit a hidden false input.
            const checkbox = form.querySelector(`input[type="checkbox"][name="${name}"]`);
            return [name, name === 'IsPublished' || name === 'IsActive'
                ? (checkbox ? checkbox.checked : field.value === 'true') : field.value];
        }));
    }

    function notify({ kind, record }) {
        if (kind === 'offer' || kind === 'conflict') {
            banner.classList.remove('hidden');
            message.textContent = kind === 'conflict'
                ? 'Another tab changed the recovery copy. Your current text is unchanged. Choose Restore or Discard before local saving resumes.'
                : 'A different recovery copy is available. Your current text is unchanged.';
            timestamp.textContent = record?.savedAt && Number.isFinite(Date.parse(record.savedAt))
                ? new Date(record.savedAt).toLocaleString() : 'unknown time';
            restore.disabled = !record;
            status.textContent = 'LOCAL SAVE PAUSED';
            return;
        }
        if (kind === 'unavailable') {
            status.textContent = 'LOCAL RECOVERY UNAVAILABLE — normal server saving still works.';
            return;
        }
        banner.classList.add('hidden');
        status.textContent = kind === 'saved' ? 'LOCAL RECOVERY SAVED'
            : kind === 'restored' ? 'RECOVERY RESTORED' : 'LOCAL RECOVERY READY';
    }

    const session = createRecoverySession({ key, storage, locks: navigator.locks,
        baseline: readFields(), readFields, notify,
        newRevision: () => crypto.randomUUID(), now: () => new Date().toISOString() });
    let timer;
    let dirty = false;
    let submitting = false;
    function schedule() {
        dirty = true;
        clearTimeout(timer);
        timer = setTimeout(() => session.save(), 300);
    }
    form.addEventListener('input', schedule);
    form.addEventListener('change', schedule);
    window.addEventListener('storage', event => { if (event.key === key) session.refresh(); });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden' && dirty) {
            clearTimeout(timer);
            session.save();
        }
    });
    restore.addEventListener('click', async () => {
        const fields = await session.restore();
        if (!fields) return;
        for (const name of fieldNames) {
            if (name === 'Content') setMarkdown(fields[name]);
            else if (name === 'IsPublished' || name === 'IsActive') {
                const checkbox = form.querySelector(`input[type="checkbox"][name="${name}"]`);
                if (checkbox) checkbox.checked = fields[name];
                else form.elements.namedItem(name).value = String(fields[name]);
            } else form.elements.namedItem(name).value = fields[name];
        }
        updatePreview();
    });
    discard.addEventListener('click', async () => {
        await session.discard();
        if (dirty) schedule();
    });
    form.addEventListener('submit', async event => {
        if (submitting || event.defaultPrevented) return;
        event.preventDefault();
        clearTimeout(timer);
        const revision = await session.save();
        revisionField.value = revision || '';
        // Resume the same native POST and submitter; server failures leave the copy intact.
        submitting = true;
        try { form.requestSubmit(event.submitter); }
        finally { submitting = false; }
    });
    session.offerScratch();
}
