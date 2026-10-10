import { Extension } from '@tiptap/core';
import { Table, TableCell, TableHeader, TableRow } from '@tiptap/extension-table';
import { Plugin } from '@tiptap/pm/state';

// Client limits keep editing predictable; the canonical server validator remains authoritative.
const limits = { rows: 20, columns: 10, cells: 1000 };
const notice = 'Tablo işlemi uygulanmadı. En fazla 20 satır, 10 sütun ve belgede 1.000 hücre kullanın; iç içe, birleştirilmiş veya boyutlandırılmış tablo desteklenmez. Mevcut metniniz korunuyor.';
const cellNames = new Set(['tableCell', 'tableHeader']);

function tableFacts(doc) {
    let cells = 0, valid = true;
    const walk = (node, insideCell = false) => {
        if (node.type.name === 'table') {
            const columns = node.firstChild?.childCount ?? 0;
            if (insideCell || node.childCount < 1 || node.childCount > limits.rows || columns < 1 || columns > limits.columns) valid = false;
            node.forEach(row => { if (row.childCount !== columns) valid = false; });
        }
        if (cellNames.has(node.type.name)) {
            cells++;
            const { colspan, rowspan, colwidth, align } = node.attrs;
            if (colspan !== 1 || rowspan !== 1 || colwidth !== null || ![null, 'left', 'center', 'right'].includes(align)) valid = false;
            insideCell = true;
        }
        node.forEach(child => walk(child, insideCell));
    };
    walk(doc);
    return { valid: valid && cells <= limits.cells, cells };
}

function selectedTable(editor) {
    const position = editor.state.selection.$from;
    for (let depth = position.depth; depth > 0; depth--)
        if (position.node(depth).type.name === 'table') return position.node(depth);
    return null;
}

// Reject raw invalid table structure before ProseMirror can repair or discard it during paste.
function supportedPaste(html) {
    if (new TextEncoder().encode(html).length > 1048576) return false;
    const dom = new DOMParser().parseFromString(html, 'text/html');
    for (const table of dom.querySelectorAll('table')) {
        if (table.parentElement.closest('table') || table.rows.length < 1 || table.rows.length > limits.rows) return false;
        const columns = table.rows[0].cells.length;
        if (columns < 1 || columns > limits.columns || [...table.rows].some(row => row.cells.length !== columns)) return false;
    }
    for (const cell of dom.querySelectorAll('td, th')) {
        if ((cell.hasAttribute('colspan') && cell.getAttribute('colspan') !== '1') ||
            (cell.hasAttribute('rowspan') && cell.getAttribute('rowspan') !== '1') ||
            cell.hasAttribute('colwidth') || cell.hasAttribute('data-colwidth')) return false;
        const align = cell.getAttribute('align') || cell.style.textAlign;
        if (align && !['left', 'center', 'right'].includes(align)) return false;
    }
    return !dom.querySelector('table[width], td[width], th[width], col[width], colgroup[width]') &&
        ![...dom.querySelectorAll('table, td, th, col, colgroup')].some(el => el.style.width || el.style.minWidth || el.style.maxWidth);
}

// Render only static classes. Default TableView/colgroup widths would require inline CSS.
const StaticTable = Table.extend({
    renderHTML() { return ['div', { class: 'writing-table-scroll' }, ['table', {}, ['tbody', 0]]]; },
}).configure({ resizable: false, View: null });
function staticCell(extension, tag) {
    return extension.extend({
        renderHTML({ node }) {
            return [tag, node.attrs.align ? { class: `writing-align-${node.attrs.align}` } : {}, 0];
        },
    });
}

export function writingTableExtensions(report) {
    const guard = Extension.create({
        name: 'boundedWritingTables',
        addProseMirrorPlugins() {
            return [new Plugin({
                filterTransaction: transaction => {
                    if (!transaction.docChanged || tableFacts(transaction.doc).valid) return true;
                    report(notice); return false;
                },
                props: { handlePaste: (_view, event) => {
                    const html = event.clipboardData?.getData('text/html');
                    if (!html || supportedPaste(html)) return false;
                    report(notice); return true;
                } },
            })];
        },
    });
    return [StaticTable, TableRow, staticCell(TableCell, 'td'), staticCell(TableHeader, 'th'), guard];
}

const operations = {
    tableInsert: 'insertTable', tableHeader: 'toggleHeaderCell',
    tableRowBefore: 'addRowBefore', tableRowAfter: 'addRowAfter', tableRowDelete: 'deleteRow',
    tableColumnBefore: 'addColumnBefore', tableColumnAfter: 'addColumnAfter', tableColumnDelete: 'deleteColumn',
    tableDelete: 'deleteTable',
};
export function isTableCommand(command) { return Object.hasOwn(operations, command); }
export function canTableCommand(editor, command) {
    const table = selectedTable(editor), facts = tableFacts(editor.state.doc);
    if (!facts.valid) return false;
    if (command === 'tableInsert') return !table && facts.cells + 9 <= limits.cells && editor.can().insertTable();
    if (!table) return false;
    if (['tableRowBefore', 'tableRowAfter'].includes(command) &&
        (table.childCount >= limits.rows || facts.cells + table.firstChild.childCount > limits.cells)) return false;
    if (['tableColumnBefore', 'tableColumnAfter'].includes(command) &&
        (table.firstChild.childCount >= limits.columns || facts.cells + table.childCount > limits.cells)) return false;
    return editor.can()[operations[command]]();
}
export function runTableCommand(editor, command) {
    if (!canTableCommand(editor, command)) return false;
    return editor.chain().focus()[operations[command]]().run();
}
