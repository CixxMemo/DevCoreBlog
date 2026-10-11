import { Editor, Extension } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Code from '@tiptap/extension-code';
import { writingTableExtensions, isTableCommand, canTableCommand, runTableCommand } from './tables.js';
import { writingImageExtensions, supportedImage } from './images.js';
import { mountImageUpload } from './image-upload.js';
import { writingYoutubeExtensions, supportedYoutube } from './youtube.js';
import { mountYoutubeInput } from './youtube-input.js';
import { mountDocumentRecovery } from './recovery-form.js';
import { recoveryFields } from './recovery-store.js';
import { mountDocumentPreview } from './preview.js';

// Preserve supported stored attributes without dynamic style attributes or new alignment tools.
const Alignment = Extension.create({
    name: 'documentAlignment',
    addGlobalAttributes() {
        return [{ types: ['paragraph', 'heading'], attributes: { textAlign: {
            default: null,
            parseHTML: () => null,
            renderHTML: attrs => attrs.textAlign ? { class: `writing-align-${attrs.textAlign}` } : {},
        } } }];
    },
});
const attributes = {
    doc: [], paragraph: ['textAlign'], heading: ['level', 'textAlign'], text: [], hardBreak: [],
    table: [], tableRow: [], tableCell: ['colspan', 'rowspan', 'colwidth', 'align'],
    tableHeader: ['colspan', 'rowspan', 'colwidth', 'align'],
    blockquote: [], bulletList: [], orderedList: ['start', 'type'], listItem: [], codeBlock: ['language'],
    image: ['src', 'alt', 'title', 'width', 'height'],
    youtube: ['src', 'width', 'height', 'start'],
};
const marks = { bold: [], italic: [], underline: [], code: [], link: ['href', 'title', 'target', 'rel', 'class'] };

// This is a loss-prevention check, not the server's security validator. Unknown data is never dropped on open.
function supportedDocument(envelope) {
    if (envelope.version !== 1 || Object.keys(envelope).some(k => !['version', 'document'].includes(k))) return false;
    const pending = [envelope.document];
    let count = 0;
    while (pending.length) {
        const node = pending.pop();
        if (++count > 10000 || !node || !Object.hasOwn(attributes, node.type) ||
            Object.keys(node).some(k => !['type', 'attrs', 'content', 'text', 'marks'].includes(k)) ||
            Object.keys(node.attrs ?? {}).some(k => !attributes[node.type].includes(k))) return false;
        if (node.type === 'image' && !supportedImage(node.attrs)) return false;
        if (node.type === 'youtube' && !supportedYoutube(node.attrs)) return false;
        for (const mark of node.marks ?? []) {
            if (!Object.hasOwn(marks, mark.type) || Object.keys(mark).some(k => !['type', 'attrs'].includes(k)) ||
                Object.keys(mark.attrs ?? {}).some(k => !marks[mark.type].includes(k))) return false;
        }
        pending.push(...(node.content ?? []));
    }
    return true;
}

export async function mountWritingEditor(form) {
    const input = form.querySelector('[name="DocumentJson"]');
    const original = JSON.parse(input.value);
    if (!supportedDocument(original)) throw new Error('Unsupported document; retain original');
    const mount = form.querySelector('[data-editor-mount]');
    const status = form.querySelector('[data-writing-status]');
    let contentError = false;
    const editor = new Editor({
        element: mount, injectCSS: false, enableContentCheck: true,
        extensions: [StarterKit.configure({
            code: false, heading: { levels: [2, 3, 4] }, hardBreak: { keepMarks: false },
            horizontalRule: false, strike: false, trailingNode: false, dropcursor: false,
            link: { openOnClick: false, autolink: false, linkOnPaste: false,
                HTMLAttributes: { target: null, rel: 'noopener noreferrer', class: null } },
        }), Code.extend({ excludes: '' }), Alignment, ...writingTableExtensions(message => { status.textContent = message; }),
        ...writingImageExtensions(message => { status.textContent = message; }),
        ...writingYoutubeExtensions(message => { status.textContent = message; })],
        content: original.document,
        onContentError: () => { contentError = true; },
        editorProps: { attributes: { class: 'writing-document', role: 'textbox',
            'aria-label': 'Yazı metni', 'aria-multiline': 'true', lang: 'tr', spellcheck: 'true' } },
    });
    if (contentError) { editor.destroy(); throw new Error('Invalid content'); }
    const saves = [...form.querySelectorAll('[data-writing-save]')];
    const preview = form.querySelector('[data-writing-preview]');
    const buttons = [...form.querySelectorAll('[data-writing-command]')];
    let dirty = false;
    const media = mountImageUpload(form, editor, busy => {
        saves.forEach(button => { button.disabled = busy; });
        preview.disabled = busy;
    });
    mountYoutubeInput(form, editor);
    function synchronize() {
        input.value = JSON.stringify({ version: 1, document: editor.getJSON() });
    }
    mountDocumentPreview(form, { readJson: () => { synchronize(); return input.value; },
        blocked: () => contentError || media.busy() || saves.every(button => button.disabled) });
    const recovery = await mountDocumentRecovery(form, { synchronize,
        blocked: () => contentError || media.busy() || saves.every(button => button.disabled),
        markDirty: () => { dirty = true; },
        apply: fields => {
            try {
                const envelope = JSON.parse(fields.DocumentJson);
                if (!supportedDocument(envelope)) return false;
                const document = editor.schema.nodeFromJSON(envelope.document);
                document.check();
                // Check browser normalization before touching any existing document or metadata.
                const fieldsToApply = recoveryFields.filter(key => key !== 'DocumentJson').map(key => {
                    const field = key === 'IsActive' ? form.querySelector('[name=IsActive][type=checkbox]') : form.elements.namedItem(key);
                    const copy = field.cloneNode(true);
                    if (key !== 'IsActive') { copy.value = fields[key]; if (copy.value !== fields[key]) throw new Error('Unrepresentable recovery field'); }
                    return [key, field];
                });
                if (!editor.commands.setContent(document, { emitUpdate: false, errorOnInvalidContent: true })) return false;
                for (const [key, field] of fieldsToApply) {
                    if (key === 'IsActive') field.checked = fields[key]; else field.value = fields[key];
                }
                synchronize(); controls(); editor.commands.focus(); return true;
            } catch { return false; }
        },
    });
    function controls() {
        for (const button of buttons) {
            const command = button.dataset.writingCommand;
            button.disabled = isTableCommand(command) ? !canTableCommand(editor, command) : command === 'undo' ? !editor.can().undo() : command === 'redo' ? !editor.can().redo() : false;
            const active = command === 'tableHeader' ? editor.isActive('tableHeader') : command.startsWith('h') ? editor.isActive('heading', { level: Number(command[1]) }) : editor.isActive(command);
            if ((!isTableCommand(command) || command === 'tableHeader') && !['undo', 'redo', 'unlink', 'link'].includes(command)) button.setAttribute('aria-pressed', String(active));
        }
    }
    for (const button of buttons) button.addEventListener('click', () => {
        const command = button.dataset.writingCommand;
        if (isTableCommand(command)) { runTableCommand(editor, command); return; }
        const chain = editor.chain().focus();
        switch (command) {
            case 'bold': chain.toggleBold().run(); break;
            case 'italic': chain.toggleItalic().run(); break;
            case 'underline': chain.toggleUnderline().run(); break;
            case 'code': chain.toggleCode().run(); break;
            case 'paragraph': chain.setParagraph().run(); break;
            case 'h2': case 'h3': case 'h4': chain.toggleHeading({ level: Number(command[1]) }).run(); break;
            case 'bulletList': chain.toggleBulletList().run(); break;
            case 'orderedList': chain.toggleOrderedList().run(); break;
            case 'blockquote': chain.toggleBlockquote().run(); break;
            case 'codeBlock': chain.toggleCodeBlock({ language: 'plaintext' }).run(); break;
            case 'undo': chain.undo().run(); break;
            case 'redo': chain.redo().run(); break;
            case 'unlink': chain.extendMarkRange('link').unsetLink().run(); break;
            case 'link': {
                const href = window.prompt('Bağlantı adresi (HTTPS, /yerel veya #başlık):', editor.getAttributes('link').href ?? '');
                if (href === null) break;
                if (!safeLink(href)) { status.textContent = 'Geçerli bir HTTPS, yerel veya başlık bağlantısı girin.'; break; }
                chain.extendMarkRange('link').setLink({ href }).run(); break;
            }
        }
    });
    editor.on('update', () => { synchronize(); dirty = true; recovery.changed(); status.textContent = 'Kaydedilmemiş değişiklikler var.'; });
    editor.on('transaction', controls);
    form.addEventListener('input', event => {
        if (event.target !== input && recoveryFields.includes(event.target.name)) {
            dirty = true; recovery.changed(); status.textContent = 'Kaydedilmemiş değişiklikler var.';
        }
    });
    form.addEventListener('change', event => { if (recoveryFields.includes(event.target.name)) { dirty = true; recovery.changed(); } });
    form.addEventListener('submit', event => {
        if (event.defaultPrevented) return;
        if (contentError || media.busy() || saves.every(button => button.disabled)) { event.preventDefault(); return; }
        synchronize();
        dirty = false;
        status.textContent = 'Kaydediliyor…';
    });
    window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
    window.addEventListener('pagehide', event => { media.cancel(); if (!event.persisted) editor.destroy(); });
    // Keep the editor private; local recovery uses its own bounded, versioned format.
    mount.hidden = false;
    form.querySelector('[data-document-fallback]').hidden = true;
    saves.forEach(button => { button.disabled = false; });
    preview.disabled = false;
    controls();
    status.textContent = 'Editör hazır. Değişiklikler kaydettiğinizde sunucuya yazılır.';
}

function safeLink(value) {
    if (!value || value.length > 2048 || /[\s\\<>"'\u0000-\u001f\u007f]/u.test(value)) return false;
    if (value.startsWith('#')) return value.length > 1;
    if (value.startsWith('/') && !value.startsWith('//')) return true;
    try { const url = new URL(value); return url.protocol === 'https:' && !url.username && !url.password; }
    catch { return false; }
}
