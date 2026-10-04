// Toast UI progressively enhances the marked textarea. Keep that hidden
// field in client validation after the rich editor becomes available.
window.jQuery.validator.setDefaults({
    ignore: ':hidden:not([data-validate-hidden="true"])'
});
