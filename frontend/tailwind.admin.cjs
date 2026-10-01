const shared = require('./tailwind.shared.cjs');
// Admin retains its existing default palette and radius scale.
module.exports = {
    ...shared,
    theme: { extend: { ...shared.theme.extend, borderRadius: { none: '0px' } } }
};
