import { youtubeId, youtubeCount } from './youtube.js';

// The URL tool owns interaction only; schema admission and persistence are separate boundaries.
export function mountYoutubeInput(form, editor) {
    const tool = form.querySelector('[data-writing-youtube]');
    const input = tool.querySelector('[data-youtube-url]');
    const button = tool.querySelector('[data-youtube-add]');
    const status = tool.querySelector('[data-youtube-status]');
    function insert() {
        if (!youtubeId(input.value)) {
            status.textContent = 'İzinli bir HTTPS YouTube bağlantısı girin. Ek parametre, iframe kodu ve zaman başlangıcı kabul edilmez. Metniniz korunuyor.';
            input.setAttribute('aria-invalid', 'true'); input.focus(); return;
        }
        input.removeAttribute('aria-invalid');
        if (youtubeCount(editor.state.doc) >= 10) {
            status.textContent = 'Belgede en fazla 10 video olabilir. Metniniz korunuyor.';
            input.focus(); return;
        }
        if (!editor.chain().focus().setYoutubeVideo({ src: input.value, width: 640, height: 480, start: 0 }).run()) {
            status.textContent = 'Video bu konuma eklenemedi. Metniniz korunuyor.'; input.focus(); return;
        }
        input.value = ''; status.textContent = 'YouTube videosu eklendi. Değişikliği kaydetmeyi unutmayın.';
    }
    button.addEventListener('click', insert);
    input.addEventListener('keydown', event => {
        if (event.key === 'Enter' && !event.isComposing) { event.preventDefault(); insert(); }
    });
    tool.hidden = false; input.disabled = false; button.disabled = false;
}
