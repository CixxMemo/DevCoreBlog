// Bootstrap is separate so a dependency-chunk failure leaves encoded input and disabled saves visible.
const form = document.querySelector('[data-document-form]');
if (form) {
    try {
        if (form.dataset.editorEnabled !== 'true') throw new Error('Document unavailable');
        const { mountWritingEditor } = await import('./writing-editor.js');
        mountWritingEditor(form);
    } catch {
        form.querySelector('[data-writing-status]').textContent =
            'Editör güvenle açılamadı. Kaydetme kapalı; içerik belgesi aşağıda korunuyor. Güncel yazıyı yeniden açın.';
    }
}
