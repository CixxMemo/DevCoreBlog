// Submit an isolated snapshot to the existing trusted renderer; the writing form never submits.
export function mountDocumentPreview(form, { readJson, blocked }) {
    const button = form.querySelector('[data-writing-preview]');
    const status = form.querySelector('[data-preview-status]');
    button.addEventListener('click', () => {
        if (blocked()) { status.textContent = 'Editör hazır değil veya görsel yükleniyor. Yazınız korunuyor; önizlemeyi daha sonra açın.'; return; }
        let request;
        try {
            const url = new URL(form.dataset.documentPreviewUrl, location.origin);
            if (url.origin !== location.origin || url.search || url.hash) throw new Error('Preview endpoint');
            request = document.createElement('form');
            request.method = 'post'; request.action = url.href;
            request.target = '_blank'; request.rel = 'noopener noreferrer'; request.hidden = true;
            const fields = { 'Input.DocumentJson': readJson(), 'Input.FromEditor': 'true',
                __RequestVerificationToken: form.querySelector('[name=__RequestVerificationToken]').value };
            for (const [name, value] of Object.entries(fields)) {
                const input = document.createElement('input'); input.type = 'hidden'; input.name = name; input.value = value;
                request.append(input);
            }
            document.body.append(request);
            HTMLFormElement.prototype.submit.call(request);
            status.textContent = 'Önizleme ayrı sekmede açılıyor. Yazınız kaydedilmedi; düzenlemeye burada devam edebilirsiniz.';
        } catch {
            status.textContent = 'Önizleme açılamadı. Metniniz ve yerel taslağınız korunuyor; yeniden deneyin.';
        } finally { if (request) setTimeout(() => request.remove(), 0); }
    });
}
