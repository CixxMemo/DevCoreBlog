import { Editor, Extension } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import Code from '@tiptap/extension-code';

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
    blockquote: [], bulletList: [], orderedList: ['start', 'type'], listItem: [], codeBlock: ['language'],
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
        for (const mark of node.marks ?? []) {
            if (!Object.hasOwn(marks, mark.type) || Object.keys(mark).some(k => !['type', 'attrs'].includes(k)) ||
                Object.keys(mark.attrs ?? {}).some(k => !marks[mark.type].includes(k))) return false;
        }
        pending.push(...(node.content ?? []));
    }
    return true;
}

export function mountWritingEditor(form) {
    const input = form.querySelector('[name="DocumentJson"]');
    const original = JSON.parse(input.value);
    if (!supportedDocument(original)) throw new Error('Unsupported document; retain original');
    const mount = form.querySelector('[data-editor-mount]');
    let contentError = false;
    const editor = new Editor({
        element: mount, injectCSS: false, enableContentCheck: true,
        extensions: [StarterKit.configure({
            code: false, heading: { levels: [2, 3, 4] }, hardBreak: { keepMarks: false },
            horizontalRule: false, strike: false, trailingNode: false, dropcursor: false,
            link: { openOnClick: false, autolink: false, linkOnPaste: false,
                HTMLAttributes: { target: null, rel: 'noopener noreferrer', class: null } },
        }), Code.extend({ excludes: '' }), Alignment],
        content: original.document,
        onContentError: () => { contentError = true; },
        editorProps: { attributes: { class: 'writing-document', role: 'textbox',
            'aria-label': 'Yazı metni', 'aria-multiline': 'true', lang: 'tr', spellcheck: 'true' } },
    });
    if (contentError) { editor.destroy(); throw new Error('Invalid content'); }
    const status = form.querySelector('[data-writing-status]');
    const saves = [...form.querySelectorAll('[data-writing-save]')];
    const buttons = [...form.querySelectorAll('[data-writing-command]')];
    let dirty = false;
    function synchronize() {
        input.value = JSON.stringify({ version: 1, document: editor.getJSON() });
    }
    function controls() {
        for (const button of buttons) {
            const command = button.dataset.writingCommand;
            button.disabled = command === 'undo' ? !editor.can().undo() : command === 'redo' ? !editor.can().redo() : false;
            const active = command.startsWith('h') ? editor.isActive('heading', { level: Number(command[1]) }) : editor.isActive(command);
            if (!['undo', 'redo', 'unlink', 'link'].includes(command)) button.setAttribute('aria-pressed', String(active));
        }
    }
    for (const button of buttons) button.addEventListener('click', () => {
        const command = button.dataset.writingCommand;
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
    editor.on('update', () => { synchronize(); dirty = true; status.textContent = 'Kaydedilmemiş değişiklikler var.'; });
    editor.on('transaction', controls);
    form.addEventListener('input', event => {
        if (event.target !== input) { dirty = true; status.textContent = 'Kaydedilmemiş değişiklikler var.'; }
    });
    form.addEventListener('submit', event => {
        if (contentError || saves.every(button => button.disabled)) { event.preventDefault(); return; }
        synchronize();
        dirty = false;
        status.textContent = 'Kaydediliyor…';
    });
    window.addEventListener('beforeunload', event => { if (dirty) { event.preventDefault(); event.returnValue = ''; } });
    window.addEventListener('pagehide', event => { if (!event.persisted) editor.destroy(); });
    // Expose neither a mutable global editor nor a client storage recovery format.
    mount.hidden = false;
    form.querySelector('[data-document-fallback]').hidden = true;
    saves.forEach(button => { button.disabled = false; });
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
