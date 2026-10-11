import Youtube from '@tiptap/extension-youtube';
import { Extension } from '@tiptap/core';
import { Plugin } from '@tiptap/pm/state';

// Match the server v1 grammar exactly; URL normalization must not broaden admission.
export function youtubeId(source) {
    if (typeof source !== 'string') return null;
    const match = /^(?:https:\/\/youtu\.be\/|https:\/\/(?:www\.)?youtube\.com\/(?:watch\?v=|embed\/)|https:\/\/www\.youtube-nocookie\.com\/embed\/)([A-Za-z0-9_-]{11})$/u.exec(source);
    return match && match[0] === source ? match[1] : null;
}

export function supportedYoutube(attrs) {
    return !!attrs && !!youtubeId(attrs.src) &&
        Object.keys(attrs).every(key => ['src', 'width', 'height', 'start'].includes(key)) &&
        (attrs.width === undefined || attrs.width === 640) &&
        (attrs.height === undefined || attrs.height === 480) &&
        (attrs.start === undefined || attrs.start === 0);
}

export function youtubeCount(doc) {
    let count = 0;
    doc.descendants(node => { if (node.type.name === 'youtube') count++; });
    return count;
}

const SafeYoutube = Youtube.extend({
    parseHTML() { return []; },
    addCommands() {
        return { setYoutubeVideo: attrs => ({ commands, state }) => {
            if (!supportedYoutube(attrs) || youtubeCount(state.doc) >= 10) return false;
            // Insert at the cursor without replacing selected writing.
            return commands.insertContentAt(state.selection.from, { type: this.name, attrs });
        } };
    },
    renderHTML({ node }) {
        if (!supportedYoutube(node.attrs)) return ['span', {}, 'Geçersiz YouTube videosu'];
        return ['div', { class: 'writing-video', 'data-youtube-video': '', contenteditable: 'false' },
            ['iframe', { src: `https://www.youtube-nocookie.com/embed/${youtubeId(node.attrs.src)}`,
                title: 'YouTube videosu', loading: 'lazy', referrerpolicy: 'strict-origin-when-cross-origin',
                allow: 'fullscreen', allowfullscreen: '' }]];
    },
}).configure({ inline: false, addPasteHandler: false, nocookie: true });

export function writingYoutubeExtensions(report) {
    const guard = Extension.create({
        name: 'boundedWritingYoutube',
        addProseMirrorPlugins() {
            return [new Plugin({
                filterTransaction: transaction => {
                    if (!transaction.docChanged) return true;
                    let valid = youtubeCount(transaction.doc) <= 10;
                    transaction.doc.descendants(node => {
                        if (node.type.name === 'youtube' && !supportedYoutube(node.attrs)) valid = false;
                    });
                    if (!valid) report('Video işlemi uygulanmadı. İzinli YouTube bağlantısı ve belgede en fazla 10 video kullanın. Metniniz korunuyor.');
                    return valid;
                },
                props: { handlePaste: (_view, event) => {
                    const html = event.clipboardData?.getData('text/html');
                    if (!html || !new DOMParser().parseFromString(html, 'text/html').querySelector('iframe, embed, object, video')) return false;
                    report('Embed kodu yapıştırılmadı. YouTube bağlantısı aracını kullanın. Metniniz korunuyor.');
                    return true;
                } },
            })];
        },
    });
    return [SafeYoutube, guard];
}
