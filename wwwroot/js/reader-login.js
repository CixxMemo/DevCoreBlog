// A scanner-safe GET never consumes a proof. Erase the fragment before any explicit confirmation.
(() => {
    // A new mail link may reuse a tab currently showing a POST error at this same path.
    window.addEventListener('hashchange', () => { if (window.location.hash) window.location.reload(); });
    const fragment = window.location.hash;
    window.history.replaceState(null, '', window.location.pathname + window.location.search);
    const input = document.getElementById('reader-proof-token');
    const button = document.getElementById('reader-proof-submit');
    const status = document.getElementById('reader-proof-status');
    if (!input || !button || !status) return;
    const match = /^#t=([A-Za-z0-9_-]{43})$/.exec(fragment);
    if (!match) {
        status.textContent = 'Geçerli bir giriş bağlantısı bulunamadı. E-postanızdaki bağlantıyı yeniden açın.';
        return;
    }
    input.value = match[1];
    button.disabled = false;
    status.textContent = 'Hazır. Talep size aitse aşağıdaki düğmeyle giriş yapın.';
    window.addEventListener('pagehide', () => { input.value = ''; button.disabled = true; });
})();
