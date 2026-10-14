// Proof stays in a fragment until an explicit POST; no persistent storage or automatic exit.
(() => {
    const fragment = window.location.hash;
    window.history.replaceState(null, '', window.location.pathname);
    const input = document.getElementById('exit-token');
    const button = document.getElementById('exit-submit');
    const status = document.getElementById('exit-status');
    if (!input || !button || !status) return;
    const match = /^#t=([A-Za-z0-9_-]{100,512})$/.exec(fragment);
    if (!match) {
        status.textContent = 'E-postanızdaki çıkış bağlantısını açın. Geçerli bir bağlantı bulunamadı.';
        return;
    }
    input.value = match[1];
    button.disabled = false;
    status.textContent = 'Hazır. Çıkmak istiyorsanız aşağıdaki düğmeye basın.';
    window.addEventListener('pagehide', () => { input.value = ''; button.disabled = true; });
})();
