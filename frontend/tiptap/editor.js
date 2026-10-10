import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';

// This isolated trial has no persistence, upload, recovery or server-render boundary.
export function mountTrial(trial) {
    const buttons = [...trial.querySelectorAll('[data-editor-command]')];
    const editor = new Editor({
        element: trial.querySelector('[data-editor-mount]'),
        injectCSS: false,
        extensions: [StarterKit.configure({
            blockquote: false, bulletList: false, code: false, codeBlock: false,
            dropcursor: false, gapcursor: false, hardBreak: false, heading: false,
            horizontalRule: false, italic: false, link: false, listItem: false,
            listKeymap: false, orderedList: false, strike: false, underline: false,
            trailingNode: false,
        })],
        content: { type: 'doc', content: [{ type: 'paragraph' }] },
        editorProps: { attributes: {
            class: 'editor-trial-document', role: 'textbox', 'aria-label': 'Deneme metni',
            'aria-multiline': 'true', 'aria-describedby': 'editor-trial-description',
            lang: 'tr', spellcheck: 'true',
        } },
    });
    function refreshControls() {
        for (const button of buttons) {
            const command = button.dataset.editorCommand;
            if (command === 'bold') {
                button.disabled = false;
                button.setAttribute('aria-pressed', String(editor.isActive('bold')));
            } else {
                button.disabled = command === 'undo' ? !editor.can().undo() : !editor.can().redo();
            }
        }
    }
    for (const button of buttons) {
        button.addEventListener('click', () => {
            const chain = editor.chain().focus();
            switch (button.dataset.editorCommand) {
                case 'bold': chain.toggleBold().run(); break;
                case 'undo': chain.undo().run(); break;
                case 'redo': chain.redo().run(); break;
            }
        });
    }
    editor.on('transaction', refreshControls);
    refreshControls();
    trial.querySelector('[data-editor-status]').textContent = 'Editör hazır. Bu metin kaydedilmez.';
    window.addEventListener('pagehide', event => {
        if (!event.persisted) editor.destroy();
    }, { once: true });
}
