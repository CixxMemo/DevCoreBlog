import { readFile, writeFile, copyFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import postcss from 'postcss';
import tailwind from '@tailwindcss/postcss';

// Compile the two existing themes through the official, scripts-free PostCSS API.
export async function buildTailwind(root, output) {
    for (const theme of ['public', 'admin']) {
        const from = resolve(root, `frontend/tailwind.${theme}.css`);
        const to = resolve(output, `tailwind-${theme}.css`);
        const result = await postcss([tailwind({ base: root, optimize: { minify: true } })])
            .process(await readFile(from, 'utf8'), { from, to });
        if (result.warnings().length) throw new Error(result.warnings().join('\n'));
        await writeFile(to, result.css);
    }
    await copyFile(resolve(root, 'node_modules/tailwindcss/LICENSE'), resolve(output, 'TAILWIND-LICENSE'));
}
