const shared = require('./tailwind.shared.cjs');
// Preserve the public layout's existing class dark mode and square corners.
module.exports = {
    ...shared,
    darkMode: 'class',
    theme: { extend: {
        ...shared.theme.extend,
        colors: {
            primary: { DEFAULT: '#4f46e5', hover: '#4338ca' },
            accent: { DEFAULT: '#4f46e5', light: '#6366f1', dark: '#4338ca' }
        },
        borderRadius: Object.fromEntries(['none', 'sm', 'DEFAULT', 'md', 'lg', 'xl', '2xl', '3xl', 'full'].map(key => [key, '0']))
    } }
};
