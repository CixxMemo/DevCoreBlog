import Image from '@tiptap/extension-image';
import { Extension } from '@tiptap/core';
import { Plugin } from '@tiptap/pm/state';

// Client admission protects the editing DOM; server v1 validation is authoritative.
export function supportedImage(attrs) {
    if (!attrs || attrs.width != null || attrs.height != null || !safeImageUrl(attrs.src)) return false;
    return [attrs.alt, attrs.title].every(value => value == null || typeof value === 'string' &&
        [...value].length <= 300 && !/[\u0000-\u001f\u007f-\u009f]/u.test(value));
}

export function safeImageUrl(value) {
    if (typeof value !== 'string' || !value || [...value].length > 2048 ||
        /[\s\\<>"'\u0000-\u001f\u007f]/u.test(value) || /%(?![\da-f]{2})/iu.test(value) ||
        !/^https:\/\//iu.test(value)) return false;
    for (const escape of value.matchAll(/%([\da-f]{2})/giu)) {
        const byte = Number.parseInt(escape[1], 16);
        if (byte <= 32 || [37, 92, 127].includes(byte)) return false;
    }
    try { const url = new URL(value); return url.protocol === 'https:' && !!url.hostname && !url.username && !url.password; }
    catch { return false; }
}

const StaticImage = Image.extend({
    addAttributes() {
        // v1 preserves nullable legacy dimensions but never renders a resizing attribute.
        return { src: { default: null }, alt: { default: null }, title: { default: null },
            width: { default: null, rendered: false }, height: { default: null, rendered: false } };
    },
    renderHTML({ node }) {
        if (!supportedImage(node.attrs)) return ['span', {}, 'Geçersiz görsel'];
        return ['img', { src: node.attrs.src, alt: node.attrs.alt ?? '',
            ...(node.attrs.title == null ? {} : { title: node.attrs.title }) }];
    },
}).configure({ inline: false, allowBase64: false, resize: false });

export function imageCount(doc) {
    let count = 0;
    doc.descendants(node => { if (node.type.name === 'image') count++; });
    return count;
}

export function writingImageExtensions(report) {
    const guard = Extension.create({
        name: 'boundedWritingImages',
        addProseMirrorPlugins() {
            return [new Plugin({
                filterTransaction: transaction => {
                    if (!transaction.docChanged) return true;
                    let valid = imageCount(transaction.doc) <= 50;
                    transaction.doc.descendants(node => {
                        if (node.type.name === 'image' && !supportedImage(node.attrs)) valid = false;
                    });
                    if (!valid) report('Görsel işlemi uygulanmadı. Güvenli HTTPS adresi, en fazla 300 karakter açıklama ve belgede 50 görsel kullanın. Metniniz korunuyor.');
                    return valid;
                },
                props: {
                    handlePaste: (_view, event) => {
                        const html = event.clipboardData?.getData('text/html');
                        const files = event.clipboardData?.files;
                        const invalid = html && [...new DOMParser().parseFromString(html, 'text/html').querySelectorAll('img')]
                            .some(img => !supportedImage({ src: img.getAttribute('src'), alt: img.getAttribute('alt'),
                                title: img.getAttribute('title'), width: img.getAttribute('width'), height: img.getAttribute('height') }));
                        if (!files?.length && !invalid) return false;
                        report('Dosya için Görsel ve GIF yükleme aracını kullanın. Güvensiz veya boyutlandırılmış görsel yapıştırılmadı; metniniz korunuyor.');
                        return true;
                    },
                    handleDrop: (_view, event) => {
                        if (!event.dataTransfer?.files.length) return false;
                        report('Dosya için Görsel ve GIF yükleme aracını kullanın. Metniniz korunuyor.');
                        return true;
                    },
                },
            })];
        },
    });
    return [StaticImage, guard];
}
