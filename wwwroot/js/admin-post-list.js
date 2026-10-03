// Confirmation never embeds user content into executable script.
for (const form of document.querySelectorAll('form[data-confirm-delete]')) {
    form.addEventListener('submit', event => {
        if (!window.confirm('Permanently delete this post?')) event.preventDefault();
    });
}
