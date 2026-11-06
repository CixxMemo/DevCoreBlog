import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// The families and weights the site used from Google Fonts. As Google does, each weight is a rule on one variable file per subset,
// so a weight that is not listed still resolves to the same neighbour as before.
const families = [
    { name: 'Inter', slug: 'inter', weights: [400, 500, 600, 700, 800] },
    { name: 'JetBrains Mono', slug: 'jetbrains-mono', weights: [400, 500, 700, 800] }
];
// Where ranges overlap the later rule wins, so latin comes last. Turkish letters live in latin and latin-ext.
const subsets = ['greek', 'latin-ext', 'latin'];

// Ships only the needed files with their licenses; each unicode range comes from the package that built that file.
export async function buildFonts(root, output) {
    const destination = resolve(output, 'fonts');
    await mkdir(destination, { recursive: true });
    const rules = [];
    for (const { name, slug, weights } of families) {
        const source = resolve(root, 'node_modules/@fontsource-variable', slug);
        const css = await readFile(resolve(source, 'wght.css'), 'utf8');
        for (const subset of subsets) {
            const file = `${slug}-${subset}-wght-normal.woff2`;
            const range = new RegExp(String.raw`/\* ${slug}-${subset}-wght-normal \*/[^}]*?unicode-range:\s*([^;]+);`).exec(css)?.[1];
            if (!range) throw new Error(`No unicode range for ${file}`);
            await copyFile(resolve(source, 'files', file), resolve(destination, file));
            for (const weight of weights) {
                rules.push(`@font-face{font-family:'${name}';font-style:normal;font-weight:${weight};font-display:swap;src:url(${file}) format('woff2');unicode-range:${range}}`);
            }
        }
        await copyFile(resolve(source, 'LICENSE'), resolve(destination, `${slug.toUpperCase()}-LICENSE`));
    }
    await writeFile(resolve(destination, 'fonts.css'), rules.join('\n') + '\n');
}
