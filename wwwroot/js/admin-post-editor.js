(function (window) {
    'use strict';

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

    Object.defineProperty(window, 'DevCoreBlogPostEditor', {
        configurable: false,
        enumerable: false,
        value: Object.freeze({ create }),
        writable: false
    });
})(window);
