// Read the Razor-encoded name as data, never as executable JavaScript.
for (const form of document.querySelectorAll('form[data-confirm-category]')) {
    form.addEventListener('submit', event => {
        if (!window.confirm(`'${form.dataset.confirmCategory}' kategorisini silmek istiyor musunuz?`)) {
            event.preventDefault();
        }
    });
}
