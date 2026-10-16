(() => {
// Copy Code Snippet to Clipboard Helper
function copySnippet(elementId, buttonElement) {
    const pre = document.getElementById(elementId);
    if (!pre) return;

    const text = pre.innerText || pre.textContent;
    navigator.clipboard.writeText(text).then(() => {
        const originalText = buttonElement.textContent;
        buttonElement.textContent = 'KOPYALANDI!';
        buttonElement.classList.add('bg-black', 'text-white');

        if (window.showToast) {
            window.showToast('Kod panoya kopyalandı.', 'success');
        }

        setTimeout(() => {
            buttonElement.textContent = originalText;
            buttonElement.classList.remove('bg-black', 'text-white');
        }, 2000);
    }).catch(err => {
        console.error('Copy error:', err);
        if (window.showToast) {
            window.showToast('Kod kopyalanamadı.', 'error');
        }
    });
}

for (const button of document.querySelectorAll('[data-copy-snippet]')) {
    button.addEventListener('click', () => copySnippet(button.dataset.copySnippet, button));
}
})();
