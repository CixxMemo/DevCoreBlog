// Scan complete Razor and JavaScript literals, including inline toast states.
module.exports = {
    content: ['./Views/**/*.cshtml', './wwwroot/js/**/*.js'],
    theme: { extend: { fontFamily: {
        sans: ['Inter', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'sans-serif'],
        mono: ['JetBrains Mono', 'ui-monospace', 'SFMono-Regular', 'Menlo', 'Monaco', 'Consolas', 'monospace']
    } } }
};
