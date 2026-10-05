import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { parse } from 'acorn';
import { minify } from 'terser';

export const upstreamHash = '0d8c202706e3ea8d91c28307c445b55b746cf985d0994d70951b257ba207c8d9';
export const purifyHash = '1144c3ba99465d58ff93ab2419ebd99b7d7e12525b1ca4f45308771acf34cfec';
export const sha256 = value => createHash('sha256').update(value).digest('hex');

// Replace a complete, verified webpack module; never rewrite sanitizer internals.
export function replaceSanitizer(upstream, purify) {
    if (sha256(upstream) !== upstreamHash || sha256(purify) !== purifyHash) {
        throw new Error('Editor source integrity changed; review the upstream and sanitizer before building.');
    }
    const tree = parse(upstream, { ecmaVersion: 'latest' });
    const moduleTables = [];
    function visit(node) {
        if (!node || typeof node !== 'object') return;
        if (node.type === 'VariableDeclarator' && node.id.name === '__webpack_modules__') {
            moduleTables.push(node.init);
        }
        for (const value of Object.values(node)) {
            if (Array.isArray(value)) value.forEach(visit);
            else if (value && typeof value === 'object') visit(value);
        }
    }
    visit(tree);
    if (moduleTables.length !== 1 || moduleTables[0].type !== 'ObjectExpression') {
        throw new Error('Unexpected upstream webpack module table.');
    }
    const candidates = moduleTables[0].properties.filter(property => property.key.value === 368);
    const module = candidates[0]?.value;
    if (candidates.length !== 1 || module?.type !== 'FunctionExpression' ||
        module.params.length !== 1 || module.params[0].name !== 'module' ||
        !upstream.slice(module.start, module.end).includes('@license DOMPurify 2.3.3')) {
        throw new Error('Unexpected upstream DOMPurify module.');
    }
    const code = upstream.slice(0, module.start) + 'function(module) {\n' + purify +
        '\n}' + upstream.slice(module.end);
    parse(code, { ecmaVersion: 'latest' });
    if (code.includes('DOMPurify 2.3.3') || !code.includes('DOMPurify 3.4.16')) {
        throw new Error('Sanitizer replacement did not produce the reviewed version.');
    }
    return code;
}

export async function buildEditor(root) {
    const upstream = await readFile(new URL('frontend/vendor/toastui/upstream/toastui-editor-all.js', root), 'utf8');
    const purify = await readFile(new URL('node_modules/dompurify/dist/purify.cjs.js', root), 'utf8');
    const rebuilt = replaceSanitizer(upstream, purify);
    const result = await minify(rebuilt, {
        format: { comments: /^!|@license|Copyright/ },
        compress: true,
        mangle: true
    });
    if (!result.code) throw new Error('Editor minification produced no code.');
    return result.code + '\n';
}
