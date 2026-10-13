import { clearDocumentRecovery, createDocumentRecovery, recoveryFields } from './recovery-store.js';

// Bind local persistence to one form. Only a server-admitted document can replace current writing.
export async function mountDocumentRecovery(form, { synchronize, apply, blocked, markDirty }) {
    const id = Number(form.dataset.recoveryId);
    const scope = form.dataset.recoveryScope || 'post';
    const panel = form.querySelector('[data-recovery-panel]');
    const message = form.querySelector('[data-recovery-message]');
    const date = form.querySelector('[data-recovery-date]');
    const status = form.querySelector('[data-recovery-status]');
    const restore = form.querySelector('[data-recovery-restore]');
    const discard = form.querySelector('[data-recovery-discard]');
    const revisionInput = form.querySelector('[name=RecoveryRevision]');
    const storage = { getItem: key => localStorage.getItem(key), setItem: (key, value) => localStorage.setItem(key, value),
        removeItem: key => localStorage.removeItem(key) };
    const receipt = form.querySelector('[data-document-receipt]');
    if (receipt) {
        const cleared = await clearDocumentRecovery({ id: receipt.dataset.sourceId, scope, revision: receipt.dataset.revision, storage, locks: navigator.locks });
        receipt.textContent = cleared ? 'Yazı kaydedildi. Bu kayda ait yerel taslak temizlendi.' : 'Yazı kaydedildi. Daha yeni veya okunamayan yerel taslak korundu.';
    }
    function readFields() {
        synchronize();
        return Object.fromEntries(recoveryFields.map(key => [key, key === 'IsActive'
            ? form.querySelector('[name=IsActive][type=checkbox]').checked : form.elements.namedItem(key).value]));
    }
    function notify({ kind, record }) {
        panel.hidden = !['offer', 'conflict', 'unknown'].includes(kind);
        restore.disabled = !record;
        discard.disabled = panel.hidden;
        date.textContent = record ? recoveryDate(record.savedAt, form.dataset.recoveryTimeZone) : '';
        message.textContent = kind === 'unknown' ? 'Eski, başka yazıya ait veya okunamayan yerel taslak var. Üzerine yazılmadı; otomatik geri yükleme kapalı.'
            : kind === 'conflict' ? 'Başka sekme yerel taslağı değiştirdi. Mevcut metniniz korunuyor. Geri yükle veya sil seçimini yapana kadar yerel kayıt duraklatıldı.'
            : 'Bu yazıya ait yerel taslak bulundu. Mevcut metniniz değişmedi. Geri yüklemeyi seçebilirsiniz.';
        const messages = { ready: 'Yerel kurtarma hazır. Sunucuya kaydetmek ayrı işlemdir.',
            saved: 'Yerel taslak kaydedildi; henüz sunucuya kaydedilmedi.', restored: 'Taslak geri yüklendi. Sunucuya kaydetmeyi unutmayın.',
            discarded: 'Yerel taslak silindi. Mevcut yazınız değişmedi.', unavailable: 'Yerel kurtarma kullanılamıyor. Metninizi sunucuya kaydedin.',
            limit: 'Yerel taslak sınırı aşıldı. Önceki kopya korundu; metninizi sunucuya kaydedin.',
            offer: 'Yerel kayıt, seçiminizi bekliyor.', conflict: 'Sekme çakışması: yerel kayıt duraklatıldı.', unknown: 'Okunamayan taslak korundu; yerel kayıt duraklatıldı.' };
        status.textContent = messages[kind];
    }
    const session = createDocumentRecovery({ id, scope, storage, locks: navigator.locks, readFields, notify,
        revision: () => crypto.randomUUID(), now: () => new Date().toISOString() });
    let timer, dirty = false, preparing = false, submitting = false, closed = false, validationController;
    function changed() {
        dirty = true; clearTimeout(timer);
        timer = setTimeout(() => { if (!closed) void session.save(); }, 300);
    }
    restore.addEventListener('click', async () => {
        if (blocked()) { status.textContent = 'Görsel yüklemesi bitmeden taslak geri yüklenemez. Metniniz korunuyor.'; return; }
        restore.disabled = true;
        try {
            await session.restore(async (fields, unchanged) => {
                const before = JSON.stringify(readFields());
                const url = new URL(form.dataset.recoveryValidationUrl, location.origin);
                if (url.origin !== location.origin || url.search || url.hash) throw new Error('Recovery endpoint');
                validationController = new AbortController();
                const timeout = setTimeout(() => validationController.abort(), 8000);
                let admitted;
                try {
                    const response = await fetch(url, { method: 'POST', credentials: 'same-origin', cache: 'no-store',
                        signal: validationController.signal, body: new URLSearchParams({ 'Input.DocumentJson': fields.DocumentJson,
                            __RequestVerificationToken: form.querySelector('[name=__RequestVerificationToken]').value }) });
                    if (!response.ok || response.redirected) { status.textContent = 'Taslak doğrulanamadı. Mevcut yazınız ve yerel kopya korundu.'; return false; }
                    admitted = await response.json();
                } catch {
                    status.textContent = 'Taslak doğrulanamadı. Mevcut yazınız ve yerel kopya korundu.'; return false;
                } finally { clearTimeout(timeout); }
                if (admitted.valid !== true || admitted.version !== 1 || closed || blocked() || !unchanged() || before !== JSON.stringify(readFields())) {
                    status.textContent = 'Doğrulama sırasında yazı değişti. Üzerine yazılmadı; taslak korundu.'; return false;
                }
                if (!apply(fields)) { status.textContent = 'Taslak bu editörde kayıpsız açılamadı. Mevcut metniniz korundu.'; return false; }
                markDirty(); dirty = true; return true;
            });
        } catch { status.textContent = 'Taslak geri yüklenemedi. Mevcut metniniz ve yerel kopya korundu.'; }
        finally { if (!panel.hidden) restore.disabled = false; }
    });
    discard.addEventListener('click', async () => {
        clearTimeout(timer);
        if (await session.discard() && dirty) changed();
    });
    // Capture runs before the editor's submit guard. Resume the same native action and submitter.
    form.addEventListener('submit', async event => {
        if (submitting || event.defaultPrevented) return;
        event.preventDefault();
        if (preparing || blocked()) return;
        clearTimeout(timer);
        preparing = true;
        try {
            const revision = await session.save();
            revisionInput.value = revision ?? '';
            // Immediate storage/coordination failures can settle between native event listeners.
            // Resume in a new task, after the browser has exited its original submission algorithm.
            await new Promise(resolve => setTimeout(resolve, 0));
            if (closed || !form.isConnected || blocked()) return;
            submitting = true;
            form.requestSubmit(event.submitter);
        } finally { submitting = false; preparing = false; }
    }, { capture: true });
    const refresh = event => { if (event.key === session.key || event.key === null) session.refresh(); };
    window.addEventListener('storage', refresh);
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden' && dirty && !closed) { clearTimeout(timer); void session.save(); }
    });
    window.addEventListener('pagehide', event => {
        validationController?.abort(); clearTimeout(timer);
        if (!event.persisted) { closed = true; window.removeEventListener('storage', refresh); }
    });
    window.addEventListener('pageshow', event => { if (event.persisted) session.refresh(); });
    return Object.freeze({ changed });
}

function recoveryDate(value, timeZone) {
    try {
        const parts = new Intl.DateTimeFormat('tr-TR', { timeZone, day: '2-digit', month: 'short', year: 'numeric',
            hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).formatToParts(new Date(value));
        const part = type => parts.find(p => p.type === type).value;
        return `${part('day')} ${part('month')} ${part('year')} ${part('hour')}:${part('minute')} (${timeZone})`;
    } catch { return 'Taslak zamanı gösterilemedi.'; }
}
