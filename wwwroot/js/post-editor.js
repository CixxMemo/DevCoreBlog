import { initializeRecoveryForm } from './post-recovery-form.js';

// Shared progressive enhancement; all mutable editor state stays on this page.
function create(options) {
    const editorElement = options?.editorElement;
    const contentField = options?.contentField;
    const fallbackElement = options?.fallbackElement;
    const Editor = window.toastui?.Editor;

    if (!editorElement || !contentField || !fallbackElement || !Editor) {
        return null;
    }

    editorElement.hidden = false;

    try {
        const editor = new Editor({
            ...options.editorOptions,
            el: editorElement,
            initialValue: contentField.value || ''
        });

        fallbackElement.hidden = true;
        editor.on('change', function () {
            contentField.value = editor.getMarkdown();
            contentField.dispatchEvent(new Event('input', { bubbles: true }));
        });
        return editor;
    } catch (error) {
        editorElement.hidden = true;
        fallbackElement.hidden = false;
        console.warn('Rich text editor unavailable; using the Markdown textarea.');
        return null;
    }
}

function initializePostEditor() {
    const titleInput = document.getElementById('post-title-input');
    const summaryInput = document.getElementById('post-summary-input');
    const contentHidden = document.getElementById('Content');
    const postForm = document.getElementById('postForm');
    if (!postForm) return;
    const isCreate = postForm.dataset.mode === 'create';
    // jQuery's numeric step method throws for datetime-local. Keep native
    // second precision and server date validation without that inferred rule.
    if (window.jQuery?.fn.rules) {
        window.jQuery(postForm.elements.namedItem('PublishDate')).rules('add', { step: false });
    }
    const antiforgeryToken = postForm?.querySelector(
        'input[name="__RequestVerificationToken"]'
    )?.value;

    // SERP Preview elements
    const serpTitle = document.getElementById('serp-title-preview');
    const serpSlug = document.getElementById('serp-slug-preview');
    const serpSummary = document.getElementById('serp-summary-preview');

    // Metrics bar
    const wordCountEl = document.getElementById('metric-word-count');
    const readingTimeEl = document.getElementById('metric-reading-time');
    const charCountEl = document.getElementById('metric-char-count');
    // Initialize Toast UI Editor
    const editorElement = document.querySelector('#editor');
    const contentFallback = document.getElementById('content-fallback');
    // Error responses have a strict style policy; keep the authored textarea without dynamic editor styles.
    const editor = postForm.dataset.richEditorEnabled === 'true' ? create({
        editorElement: editorElement,
        contentField: contentHidden,
        fallbackElement: contentFallback,
        editorOptions: {
            height: '600px',
            initialEditType: 'markdown',
            previewStyle: 'vertical',
            hooks: {
                // Hook for editor image uploading
                addImageBlobHook: async (blob, callback) => {
                    await uploadFileAndInsert(blob, callback);
                }
            }
        }
    }) : null;

    if (editor) {
        // Update hidden content before form submit
        postForm.addEventListener('submit', function () {
            contentHidden.value = editor.getMarkdown();
        });
    }

    // Upload to Cloudinary helper function
    async function uploadFileAndInsert(file, callback = null) {
        if (!file) return;

        if (!antiforgeryToken) {
            if (window.showToast) {
                window.showToast('Security token unavailable. Refresh the page and try again.', 'error');
            }
            return;
        }

        if (window.showToast) window.showToast('Uploading image...', 'info');

        const formData = new FormData();
        formData.append('file', file);

        try {
            const response = await fetch(postForm.dataset.uploadUrl, {
                method: 'POST',
                headers: {
                    'X-Requested-With': 'XMLHttpRequest',
                    'X-CSRF-TOKEN': antiforgeryToken
                },
                body: formData
            });

            const isJson = response.headers.get('content-type')?.includes('application/json');
            const data = isJson ? await response.json() : null;
            if (!response.ok) {
                const message = typeof data?.message === 'string'
                    ? data.message
                    : response.status === 400
                        ? 'Your security token is invalid or expired. Refresh the page and try again.'
                        : 'Image upload failed.';
                throw new Error(message);
            }

            if (data?.success && typeof data.url === 'string') {
                if (callback) {
                    callback(data.url, file.name || 'Image');
                } else {
                    insertText(`\n![Image](${data.url})\n`);
                }
                if (window.showToast) window.showToast('Image uploaded and inserted.', 'success');
            } else {
                const uploadError = typeof data?.message === 'string'
                    ? data.message
                    : 'Image upload failed.';
                if (window.showToast) window.showToast(uploadError, 'error');
            }
        } catch (err) {
            console.error('Upload error:', err);
            if (window.showToast) {
                const message = err instanceof Error && err.message
                    ? err.message
                    : 'Error uploading image to server.';
                window.showToast(message, 'error');
            }
        }
    }

    // Custom Toolbar Actions: Image & Video Embed
    const imagePicker = document.getElementById('editor-image-picker');
    const btnToolbarImage = document.getElementById('btn-toolbar-image');
    const btnToolbarVideo = document.getElementById('btn-toolbar-video');

    if (btnToolbarImage && imagePicker) {
        btnToolbarImage.addEventListener('click', () => imagePicker.click());
        imagePicker.addEventListener('change', async function () {
            if (this.files && this.files[0]) {
                await uploadFileAndInsert(this.files[0]);
                this.value = '';
            }
        });
    }

    if (btnToolbarVideo) {
        btnToolbarVideo.addEventListener('click', function () {
            const videoUrl = prompt('Enter YouTube Video URL (e.g., https://www.youtube.com/watch?v=...):');
            if (videoUrl && videoUrl.trim()) {
                const tag = `\n[video](${videoUrl.trim()})\n`;
                insertText(tag);
                if (window.showToast) window.showToast('Video embed tag inserted.', 'success');
            }
        });
    }

    // Drag-and-Drop & Clipboard Paste Handlers
    const dropzoneWrapper = document.getElementById('editor-dropzone');
    const dropzoneOverlay = document.getElementById('dropzone-overlay');

    if (dropzoneWrapper && dropzoneOverlay) {
        ['dragenter', 'dragover'].forEach(eventName => {
            dropzoneWrapper.addEventListener(eventName, (e) => {
                e.preventDefault();
                e.stopPropagation();
                dropzoneOverlay.classList.remove('hidden');
            });
        });

        ['dragleave', 'drop'].forEach(eventName => {
            dropzoneWrapper.addEventListener(eventName, (e) => {
                e.preventDefault();
                e.stopPropagation();
                dropzoneOverlay.classList.add('hidden');
            });
        });

        dropzoneWrapper.addEventListener('drop', async (e) => {
            if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length > 0) {
                const file = e.dataTransfer.files[0];
                if (file.type.startsWith('image/')) {
                    await uploadFileAndInsert(file);
                }
            }
        });
    }

    // Global paste listener for image data
    window.addEventListener('paste', async function (e) {
        if (e.clipboardData && e.clipboardData.items) {
            for (let i = 0; i < e.clipboardData.items.length; i++) {
                const item = e.clipboardData.items[i];
                if (item.type.indexOf('image') !== -1) {
                    const blob = item.getAsFile();
                    if (blob) {
                        e.preventDefault();
                        await uploadFileAndInsert(blob);
                        break;
                    }
                }
            }
        }
    });

    // Live SERP, Slug & Metrics Calculation Engine
    function generateSlug(text) {
        return (text || '')
            .toLowerCase()
            .replace(/[^a-z0-9\s-]/g, '')
            .trim()
            .replace(/\s+/g, '-');
    }

    function updatePreviewsAndMetrics() {
        const title = titleInput?.value || '';
        const summary = summaryInput?.value || '';
        const content = editor ? editor.getMarkdown() : contentHidden.value;

        // SERP preview update
        if (serpTitle) serpTitle.textContent = title || 'Article Title Preview';
        if (isCreate && serpSlug) serpSlug.textContent = generateSlug(title) || 'your-slug';
        if (serpSummary) serpSummary.textContent = summary || 'Summary and meta description will appear here as the Google snippet...';

        // Metrics update
        const words = content.trim() ? content.trim().split(/\s+/).length : 0;
        const readingMins = Math.ceil(words / 200);
        const chars = content.length;

        if (wordCountEl) wordCountEl.textContent = words.toLocaleString();
        if (readingTimeEl) readingTimeEl.textContent = `~${readingMins} min`;
        if (charCountEl) charCountEl.textContent = chars.toLocaleString();
    }

    if (titleInput) titleInput.addEventListener('input', updatePreviewsAndMetrics);
    if (summaryInput) summaryInput.addEventListener('input', updatePreviewsAndMetrics);
    contentHidden.addEventListener('input', updatePreviewsAndMetrics);

    initializeRecoveryForm({ form: postForm, setMarkdown, updatePreview: updatePreviewsAndMetrics });

    function insertText(value) {
        if (editor) editor.insertText(value);
        else {
            const start = contentHidden.selectionStart;
            const end = contentHidden.selectionEnd;
            contentHidden.setRangeText(value, start, end, 'end');
            contentHidden.dispatchEvent(new Event('input', { bubbles: true }));
        }
    }

    function setMarkdown(value) {
        if (editor) editor.setMarkdown(value);
        else {
            contentHidden.value = value;
            contentHidden.dispatchEvent(new Event('input', { bubbles: true }));
        }
    }

    // Initial calculation
    updatePreviewsAndMetrics();
}

// Run after the existing unobtrusive validation ready callback has parsed the form.
if (window.jQuery) window.jQuery(initializePostEditor);
else initializePostEditor();
