// Toast UI progressively enhances the marked textarea. Keep that hidden
// field in client validation after the rich editor becomes available.
const defaultHighlight = window.jQuery.validator.defaults.highlight;
const defaultUnhighlight = window.jQuery.validator.defaults.unhighlight;
function markField(element, invalid) {
    element.setAttribute('aria-invalid', String(invalid));
    const descriptions = (element.getAttribute('aria-describedby') || '').split(' ');
    for (const surface of document.querySelectorAll('[role="textbox"][aria-describedby]')) {
        if (surface.getAttribute('aria-describedby').split(' ').some(id => descriptions.includes(id)))
            surface.setAttribute('aria-invalid', String(invalid));
    }
}
window.jQuery.validator.setDefaults({
    highlight(element, errorClass, validClass) {
        defaultHighlight.call(this, element, errorClass, validClass);
        markField(element, true);
    },
    unhighlight(element, errorClass, validClass) {
        defaultUnhighlight.call(this, element, errorClass, validClass);
        markField(element, false);
    },
    ignore: ':hidden:not([data-validate-hidden="true"])'
});

// Unobtrusive field rules keep their explicit messages; numeric/default rules use Turkish.
window.jQuery.extend(window.jQuery.validator.messages, {
    required: 'Bu alan gereklidir.', email: 'Geçerli bir e-posta adresi girin.',
    url: 'Geçerli bir adres girin.', date: 'Geçerli bir tarih girin.',
    dateISO: 'Geçerli bir tarih girin.', number: 'Geçerli bir sayı girin.',
    digits: 'Yalnız rakam kullanın.', equalTo: 'Aynı değeri yeniden girin.',
    maxlength: window.jQuery.validator.format('En fazla {0} karakter girin.'),
    minlength: window.jQuery.validator.format('En az {0} karakter girin.'),
    rangelength: window.jQuery.validator.format('{0} ile {1} karakter arasında girin.'),
    range: window.jQuery.validator.format('{0} ile {1} arasında bir değer girin.'),
    max: window.jQuery.validator.format('En fazla {0} girin.'),
    min: window.jQuery.validator.format('En az {0} girin.'),
    step: window.jQuery.validator.format('{0} adımına uygun bir değer girin.'),
    remote: 'Bu değeri kontrol edin.'
});
