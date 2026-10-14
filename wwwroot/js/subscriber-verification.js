// Keep proof out of server URLs, referrers, persistent browser storage and history entries.
(() => {
    const fragment = window.location.hash;
    window.history.replaceState(null, '', window.location.pathname);
    const input = document.getElementById('verification-token');
    const button = document.getElementById('verification-submit');
    const status = document.getElementById('verification-status');
    if (!input || !button || !status) return;
    const match = /^#t=([A-Za-z0-9_-]{43})$/.exec(fragment);
    if (!match) {
        status.textContent = 'E-postanızdaki doğrulama bağlantısını açın. Geçerli bir bağlantı bulunamadı.';
        return;
    }
    input.value = match[1];
    button.disabled = false;
    status.textContent = 'Hazır. Devam etmek için e-posta adresinizi doğrulayın.';
    // Avoid a browser back/forward cache retaining a proof in a hidden form.
    window.addEventListener('pagehide', () => { input.value = ''; button.disabled = true; });
})();
