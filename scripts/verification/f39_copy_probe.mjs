// Exercise the production handler with a fake DOM and clipboard; no real browser is controlled.
import { readFile } from 'node:fs/promises';
import { runInNewContext } from 'node:vm';
import assert from 'node:assert/strict';
const source = await readFile(new URL('../../wwwroot/js/article-reading.js', import.meta.url), 'utf8');
const checks = {};
for (const succeeds of [true, false]) {
    const code = { textContent: 'Console.WriteLine("safe text");\n// exact whitespace\n' };
    let controls, written;
    const pre = { querySelector: () => code, setAttribute() {}, closest: () => null,
        before: element => { controls = element; } };
    const element = () => ({ children: [], disabled: false, textContent: '',
        setAttribute() {}, addEventListener: function (_, handler) { this.handler = handler; },
        append: function (...items) { this.children.push(...items); } });
    runInNewContext(source, {
        document: { querySelectorAll: selector => selector.endsWith(' pre') ? [pre] : [], createElement: element },
        navigator: { clipboard: { writeText: async text => {
            written = text;
            if (!succeeds) throw new Error('Synthetic permission rejection');
        } } },
    });
    const [button, status] = controls.children;
    await button.handler();
    checks[`${succeeds ? 'success' : 'failure'}_passes_exact_code_text`] = written === code.textContent;
    checks[`${succeeds ? 'success' : 'failure'}_re_enables_button`] = !button.disabled;
    checks[`${succeeds ? 'success' : 'failure'}_status`] = status.textContent === (succeeds
        ? 'Copied.' : 'Could not copy. Select the code and copy it manually.');
}
assert.ok(Object.values(checks).every(Boolean));
console.log(JSON.stringify({ checks }, null, 2));
