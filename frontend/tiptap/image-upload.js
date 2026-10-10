import { imageCount, safeImageUrl, supportedImage } from './images.js';

// One in-flight upload maps its insertion bookmark through subsequent editor changes.
export function mountImageUpload(form, editor, setBusy) {
    const panel = form.querySelector('[data-writing-media]');
    const fileInput = panel.querySelector('[data-image-file]');
    const altInput = panel.querySelector('[data-image-alt]');
    const upload = panel.querySelector('[data-image-upload]');
    const cancelButton = panel.querySelector('[data-image-cancel]');
    const update = panel.querySelector('[data-image-update]');
    const status = panel.querySelector('[data-image-status]');
    const endpoint = new URL(form.dataset.imageUploadUrl, location.href);
    if (endpoint.origin !== location.origin || endpoint.search || endpoint.hash) throw new Error('Invalid upload route');
    let pending = null;
    function controls() {
        fileInput.disabled = altInput.disabled = !!pending;
        upload.disabled = !!pending || !fileInput.files.length || imageCount(editor.state.doc) >= 50;
        cancelButton.disabled = !pending;
        update.disabled = !!pending || !editor.isActive('image');
    }
    editor.on('transaction', ({ transaction }) => {
        if (pending) pending.bookmark = pending.bookmark.map(transaction.mapping);
        controls();
    });
    editor.on('selectionUpdate', () => {
        if (!pending && editor.isActive('image')) altInput.value = editor.getAttributes('image').alt ?? '';
        controls();
    });
    fileInput.addEventListener('change', controls);
    update.addEventListener('click', () => {
        const attrs = { ...editor.getAttributes('image'), alt: altInput.value };
        if (!supportedImage(attrs)) { status.textContent = 'Açıklama en fazla 300 karakter olabilir. Metniniz korunuyor.'; return; }
        if (editor.chain().focus().updateAttributes('image', { alt: attrs.alt }).run())
            status.textContent = 'Görsel açıklaması değişti. Yazıyı kaydetmeyi unutmayın.';
    });
    const cancel = () => {
        if (!pending) return;
        pending.cancelled = true;
        pending.controller.abort();
    };
    cancelButton.addEventListener('click', cancel);
    upload.addEventListener('click', async () => {
        if (pending || editor.isDestroyed) return;
        const file = fileInput.files[0], alt = altInput.value;
        if (!file || file.size < 1 || file.size > 8388608 ||
            !['image/jpeg', 'image/png', 'image/webp', 'image/gif'].includes(file.type)) {
            status.textContent = 'En fazla 8 MiB boyutunda JPEG, PNG, WebP veya GIF seçin. Metniniz korunuyor.'; return;
        }
        if ([...alt].length > 300 || imageCount(editor.state.doc) >= 50) {
            status.textContent = 'Açıklama en fazla 300 karakter; belgede en fazla 50 görsel olabilir. Metniniz korunuyor.'; return;
        }
        const token = form.querySelector('[name="__RequestVerificationToken"]')?.value;
        if (!token) { status.textContent = 'Güvenlik doğrulaması eksik. Metniniz korunuyor; sayfayı yeniden açın.'; return; }
        const operation = { controller: new AbortController(), bookmark: editor.state.selection.getBookmark(), cancelled: false, timedOut: false };
        pending = operation;
        controls(); setBusy(true); panel.setAttribute('aria-busy', 'true');
        status.textContent = 'Görsel yükleniyor… Yazmaya devam edebilirsiniz. Kaydetmeden önce yüklemenin bitmesini bekleyin.';
        const deadline = setTimeout(() => { operation.timedOut = true; operation.controller.abort(); }, 30000);
        try {
            const data = new FormData(); data.append('file', file); data.append('__RequestVerificationToken', token);
            const response = await fetch(endpoint, { method: 'POST', body: data, credentials: 'same-origin',
                redirect: 'error', signal: operation.controller.signal });
            if (!response.ok) throw new Error(String(response.status));
            const payload = await response.json();
            if (operation.cancelled || editor.isDestroyed) return;
            if (!payload.success || !safeImageUrl(payload.url) || typeof payload.publicId !== 'string' ||
                !payload.publicId || payload.publicId.length > 255 || /[\u0000-\u001f\u007f]/u.test(payload.publicId) ||
                ![payload.width, payload.height].every(n => Number.isInteger(n) && n > 0 && n <= 4096) ||
                !supportedImage({ src: payload.url, alt })) throw new Error('Invalid upload response');
            // Insert only; never replace the selection or restore a stale document snapshot.
            const position = operation.bookmark.resolve(editor.state.doc).from;
            const inserted = imageCount(editor.state.doc) < 50 && editor.chain().focus()
                .insertContentAt(position, { type: 'image', attrs: { src: payload.url, alt, title: null } }).run();
            if (!inserted) throw new Error('Insertion unavailable');
            fileInput.value = '';
            status.textContent = 'Görsel eklendi. Yazıyı kaydetmeyi unutmayın.';
        } catch (error) {
            const message = operation.cancelled ? 'Yükleme iptal edildi.' : operation.timedOut ? 'Yükleme zaman aşımına uğradı.' :
                error.message === '413' ? 'Dosya boyutu sınırı aşıldı.' : error.message === '503' ? 'Görsel depolama şu anda kullanılamıyor.' :
                error.message === '400' ? 'Dosya veya güvenlik doğrulaması başarısız.' : error.message === '429' ? 'Çok fazla istek. Biraz sonra yeniden deneyin.' :
                'Görsel eklenemedi. Dosyayı ve bağlantıyı kontrol edin.';
            status.textContent = `${message} Metniniz korunuyor. Uzak yükleme tamamlandıysa dosya hesapta kalabilir; otomatik silinmez.`;
        } finally {
            clearTimeout(deadline);
            if (pending === operation) pending = null;
            panel.removeAttribute('aria-busy');
            if (!editor.isDestroyed) { controls(); setBusy(false); (upload.disabled ? fileInput : upload).focus(); }
        }
    });
    panel.hidden = false; controls();
    return { busy: () => !!pending, cancel };
}

