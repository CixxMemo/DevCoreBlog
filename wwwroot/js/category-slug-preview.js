// Real-Time English Slug Generator & Cleanliness Validator
// Converts Turkish / special characters into clean URL-friendly English slugs.
document.addEventListener('DOMContentLoaded', function () {
    const nameInput = document.getElementById('category-name-input');
    const slugPreview = document.getElementById('slug-preview');
    const cleanBadge = document.getElementById('slug-clean-badge');

    function slugify(text) {
        const trMap = {
            'ç': 'c', 'Ç': 'c',
            'ğ': 'g', 'Ğ': 'g',
            'ı': 'i', 'İ': 'i',
            'ö': 'o', 'Ö': 'o',
            'ş': 's', 'Ş': 's',
            'ü': 'u', 'Ü': 'u'
        };

        let str = text || '';
        // Replace Turkish characters
        str = str.replace(/[çÇğĞıİöÖşŞüÜ]/g, match => trMap[match] || match);

        return str
            .toLowerCase()
            .replace(/[^a-z0-9\s-]/g, '')
            .trim()
            .replace(/\s+/g, '-');
    }

    function updateSlug() {
        const rawName = nameInput?.value || '';
        const generated = slugify(rawName);

        if (slugPreview) {
            slugPreview.textContent = generated || 'your-slug';
        }

        if (cleanBadge) {
            if (generated.length > 0) {
                cleanBadge.textContent = 'CLEAN';
                cleanBadge.className = 'bg-emerald-100 border border-emerald-600 text-emerald-900 px-1.5 py-0.2 font-bold text-[9px] uppercase';
            } else {
                cleanBadge.textContent = 'EMPTY';
                cleanBadge.className = 'bg-neutral-200 border border-neutral-400 text-neutral-700 px-1.5 py-0.2 font-bold text-[9px] uppercase';
            }
        }
    }

    if (nameInput) {
        nameInput.addEventListener('input', updateSlug);
        updateSlug();
    }
});
