// One local, decorative timeline. No mailbox, subscriber or transport interaction.
(() => {
    const root = document.querySelector('[data-newsletter-demo]');
    if (!root) return;
    const controls = root.querySelector('[data-demo-controls]');
    const toggle = root.querySelector('[data-demo-toggle]');
    const state = root.querySelector('[data-demo-state]');
    const message = root.querySelector('.newsletter-demo-message');
    if (!controls || !toggle || !state || !message) return;
    const preference = window.matchMedia('(prefers-reduced-motion: reduce)');
    let visible = false;
    let started = false;
    let completed = false;
    let userPaused = false;
    let staticOnly = preference.matches;

    function render() {
        toggle.setAttribute('aria-disabled', String(preference.matches));
        if (staticOnly) {
            root.dataset.mode = 'static';
            toggle.textContent = preference.matches ? 'Animasyon kapalı' : 'Animasyonu oynat';
            state.textContent = preference.matches
                ? 'Azaltılmış hareket tercihiniz açık; örnek sabit gösteriliyor.'
                : 'Örnek sabit gösteriliyor. İsterseniz animasyonu oynatabilirsiniz.';
            return;
        }
        const playing = visible && !document.hidden && !userPaused && !completed;
        root.dataset.mode = playing ? 'playing' : 'paused';
        toggle.textContent = completed ? 'Animasyonu yeniden oynat' : userPaused ? 'Animasyonu devam ettir' : 'Animasyonu duraklat';
        state.textContent = completed ? 'Örnek akış tamamlandı.' : playing ? 'Temsilî animasyon oynatılıyor.'
            : userPaused ? 'Animasyon duraklatıldı.' : 'Ekrana döndüğünüzde animasyon devam edecek.';
    }
    function start() {
        started = true;
        root.dataset.mode = 'paused';
        root.dataset.enhanced = '';
        // A blocked stylesheet must leave a usable static example, never a pretend running timeline.
        if (getComputedStyle(message).animationName !== 'newsletter-open') staticOnly = true;
        render();
    }
    function replay() {
        delete root.dataset.enhanced;
        // Restart the two CSS timelines together without scheduling per-frame JavaScript work.
        void root.offsetWidth;
        completed = false;
        userPaused = false;
        staticOnly = false;
        start();
    }
    toggle.addEventListener('click', () => {
        if (preference.matches) return;
        if (completed || staticOnly) replay();
        else { userPaused = !userPaused; render(); }
    });
    root.addEventListener('animationend', event => {
        if (event.target !== message || event.animationName !== 'newsletter-open') return;
        completed = true;
        render();
    });
    preference.addEventListener('change', () => {
        staticOnly = true;
        completed = false;
        render(); // Keep the same focused control when the operating system preference changes.
    });
    document.addEventListener('visibilitychange', render);
    const observer = new IntersectionObserver(entries => {
        visible = entries[0].isIntersecting && entries[0].intersectionRatio >= 0.15;
        if (!started && visible && !staticOnly) start();
        else render();
    }, { threshold: 0.15 });
    controls.hidden = false;
    render();
    observer.observe(root);
})();
