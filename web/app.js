'use strict';
// ---------------------------------------------------------------------------
//  Веб-версия «Расчет сметы»: логика страницы.
//  Данные прайс-листа встраиваются в HTML как window.SEED_PRICES.
// ---------------------------------------------------------------------------

var UNITS = ['шт.', 'услуга', 'литр', 'комплект'];

var state = {
    items: [],          // [{ group, article, name, unit, price }]
    chosen: {},         // ключ позиции -> { quantity, price } (отмечено)
    templates: [],      // сохранённые наборы услуг
    document: {         // реквизиты документа и скидка
        number: '',
        customer: '',
        discount: 0
    },
    logo: null,         // логотип компании: { data: 'data:image/png;base64,...', name: 'logo.png' }
    theme: 'light',
    query: '',
    collapsing: {}      // свёрнутые разделы
};

var STORAGE_KEY = 'raschet-smeta-v2';
var pendingBackup = null;   // копия прайса, снятого кнопкой очистки
var CLEAR_WORD = '\u041f\u041e\u041b\u041d\u041e\u0421\u0422\u042c\u042e';   // слово подтверждения очистки

var SERVER_KEY = 'raschet-smeta-server';    // адрес сервиса настольной программы

// Работа через сервис: если адрес задан и сервис отвечает, данные читаются
// и записываются через него, а память браузера служит запасным вариантом.
var server = { url: '', online: false, stamp: '', timer: null, pending: {}, quiet: false };
var EXCHANGE_FORMAT = 'raschet-smeta';
// --------------------------------------------------------------- утилиты

function key(item) {
    return item.group + '\u0001' + item.article + '\u0001' + item.name;
}

function money(value) {
    var text = (Math.round(value * 100) / 100).toFixed(2);
    var parts = text.split('.');
    parts[0] = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, '\u00A0');
    return parts[0] + ',' + parts[1];
}

function number(value) {
    var text = String(Math.round(value * 1000) / 1000);
    return text.replace('.', ',');
}

function parseNumber(text) {
    if (text === null || text === undefined) return NaN;
    var cleaned = String(text).replace(/\u00A0/g, '').replace(/\s/g, '').replace(',', '.');
    if (cleaned === '') return NaN;
    var value = Number(cleaned);
    return isNaN(value) ? NaN : value;
}

function clampDiscount(value) {
    var discount = parseNumber(value);
    if (isNaN(discount) || discount < 0) return 0;
    if (discount > 90) return 90;
    return discount;
}

function escapeHtml(text) {
    return String(text === null || text === undefined ? '' : text)
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

// ---------------------------------------------------------- вычисления

/** Все позиции прайса с отметками: [{ item, quantity, price, sum }] */
function chosenRows() {
    var rows = [];
    for (var i = 0; i < state.items.length; i++) {
        var item = state.items[i];
        var mark = state.chosen[key(item)];
        if (!mark || !mark.quantity || mark.quantity <= 0) continue;

        var price = (mark.price === undefined || mark.price === null) ? item.price : mark.price;
        rows.push({
            item: item,
            quantity: mark.quantity,
            price: price,
            changed: price !== item.price,
            sum: price * mark.quantity
        });
    }
    return rows;
}

/** Подытог, скидка и сумма к оплате. */
function totals() {
    var rows = chosenRows();
    var subtotal = 0;
    for (var i = 0; i < rows.length; i++) subtotal += rows[i].sum;

    var percent = clampDiscount(state.document.discount);
    var discount = (percent > 0 && subtotal > 0)
        ? Math.round(subtotal * percent) / 100
        : 0;

    return {
        subtotal: subtotal,
        percent: percent,
        discount: discount,
        payable: subtotal - discount,
        count: rows.length
    };
}

/** Итог по разделу. */
function groupTotal(group) {
    var rows = chosenRows();
    var sum = 0;
    for (var i = 0; i < rows.length; i++) if (rows[i].item.group === group) sum += rows[i].sum;
    return sum;
}

/** Разделы строго по порядку появления позиций. */
function orderedGroups() {
    var seen = {};
    var list = [];
    for (var i = 0; i < state.items.length; i++) {
        var group = state.items[i].group;
        if (seen[group]) continue;
        seen[group] = true;
        list.push(group);
    }
    return list;
}

function matches(item, query) {
    if (!query) return true;
    var needle = query.toLowerCase();
    return item.name.toLowerCase().indexOf(needle) >= 0 ||
           item.group.toLowerCase().indexOf(needle) >= 0 ||
           (item.article || '').toLowerCase().indexOf(needle) >= 0;
}

function matchedGroups(query) {
    var seen = {};
    var list = [];
    for (var i = 0; i < state.items.length; i++) {
        var item = state.items[i];
        if (!matches(item, query)) continue;
        if (seen[item.group]) continue;
        seen[item.group] = true;
        list.push(item.group);
    }
    return list;
}

function findByKey(rowKey) {
    for (var i = 0; i < state.items.length; i++) {
        if (key(state.items[i]) === rowKey) return state.items[i];
    }
    return null;
}

function findTemplate(name) {
    if (!name) return null;
    for (var i = 0; i < state.templates.length; i++) {
        if (state.templates[i].name.toLowerCase() === name.trim().toLowerCase()) return state.templates[i];
    }
    return null;
}

/** Наборы, подходящие под строку поиска. */
function findTemplates(query) {
    if (!query) return state.templates.slice(0);
    var needle = query.trim().toLowerCase();
    var found = [];
    for (var i = 0; i < state.templates.length; i++) {
        var template = state.templates[i];
        var text = template.name.toLowerCase();
        for (var k = 0; k < template.items.length; k++) {
            text += ' ' + template.items[k].name.toLowerCase();
        }
        if (text.indexOf(needle) >= 0) found.push(template);
    }
    return found;
}
// ------------------------------------------------------------- отрисовка

function render() {
    var query = state.query.trim();
    var visibleGroups = query ? matchedGroups(query) : orderedGroups();

    var host = document.getElementById('list');
    var html = '';
    var shownItems = 0;
    var shownGroups = 0;
    for (var g = 0; g < visibleGroups.length; g++) {
        var group = visibleGroups[g];
        var inGroup = [];
        for (var i = 0; i < state.items.length; i++) {
            if (state.items[i].group !== group) continue;
            if (query && !matches(state.items[i], query)) continue;
            inGroup.push(state.items[i]);
        }
        if (inGroup.length === 0) continue;
        shownGroups++;

        var collapsed = !!state.collapsing[group];
        var groupSum = groupTotal(group);

        html += '<div class="group-head' + (collapsed ? ' collapsed' : '') + '" data-group="' + escapeHtml(group) + '">' +
                '<span class="marker">' + (collapsed ? '+' : '\u2013') + '</span>' +
                '<span class="group-name">' + escapeHtml(group) + '</span>' +
                '<span class="group-total">' + (groupSum > 0 ? money(groupSum) + ' \u20BD' : '') + '</span>' +
                '</div>';

        if (collapsed) continue;

        html += '<table class="items"><thead><tr>' +
                '<th class="pick"></th><th class="num">#</th><th class="article">Артикул</th>' +
                '<th class="name">Наименование</th><th class="unit">Ед. изм.</th>' +
                '<th class="qty">Кол-во</th><th class="price">Цена</th><th class="sum">Всего</th>' +
                '</tr></thead><tbody>';

        for (var k = 0; k < inGroup.length; k++) {
            var item = inGroup[k];
            shownItems++;
            var rowKey = key(item);
            var mark = state.chosen[rowKey];
            var picked = mark && mark.quantity > 0;
            var price = (mark && mark.price !== undefined && mark.price !== null) ? mark.price : item.price;
            var changed = picked && price !== item.price;
            var rowSum = picked ? money(price * mark.quantity) : '';

            html += '<tr class="row' + (picked ? ' picked' : '') + '" data-key="' + escapeHtml(rowKey) + '">' +
                '<td class="pick"><input type="checkbox" data-act="pick"' + (picked ? ' checked' : '') + '></td>' +
                '<td class="num">' + (k + 1) + '</td>' +
                '<td class="article">' + escapeHtml(item.article) + '</td>' +
                '<td class="name">' + escapeHtml(item.name) + '</td>' +
                '<td class="unit">' + escapeHtml(item.unit) + '</td>' +
                '<td class="qty"><input type="text" inputmode="decimal" data-act="qty" value="' +
                    (picked ? number(mark.quantity) : '') + '" placeholder="1"></td>' +
                '<td class="price"><input type="text" inputmode="decimal" data-act="price" value="' +
                    money(price) + '" class="right' + (changed ? ' changed' : '') + '" title="Цена для этой сметы"></td>' +
                '<td class="sum">' + rowSum + '</td>' +
                '</tr>';
        }

        html += '</tbody></table>';
    }

    if (shownGroups === 0) {
        html = '<p class="empty">Ничего не найдено. Измените строку поиска.</p>';
    }

    host.innerHTML = html;
    renderSummary();
    renderTemplateList();
}

function renderSummary() {
    var summary = totals();

    document.getElementById('total').textContent = 'ИТОГО: ' + money(summary.payable) + ' \u20BD';

    var text = 'Отмечено позиций: ' + summary.count;
    if (summary.discount > 0) {
        text += '   •   подытог: ' + money(summary.subtotal) +
                '   •   скидка ' + number(summary.percent) + ' %: \u2212' + money(summary.discount);
    }
    text += '   •   позиций в прайсе: ' + state.items.length;
    document.getElementById('counter').textContent = text;
}

function renderTemplateList() {
    var box = document.getElementById('templateBox');
    if (!box) return;

    var query = document.getElementById('templateSearch').value;
    var found = findTemplates(query);
    var current = box.value;

    var html = '<option value=""></option>';
    for (var i = 0; i < found.length; i++) {
        html += '<option value="' + escapeHtml(found[i].name) + '">' +
                escapeHtml(found[i].name) + ' (' + found[i].items.length + ')</option>';
    }
    box.innerHTML = html;

    if (current && findTemplate(current)) box.value = current;

    var hasTemplates = state.templates.length > 0;
    document.getElementById('templateApply').disabled = !hasTemplates;
    document.getElementById('templateDelete').disabled = !hasTemplates;
}

function renderEditor() {
    var host = document.getElementById('editor');
    var html = '<table class="items editor"><thead><tr>' +
        '<th class="num">#</th><th class="group">Раздел</th><th class="article">Артикул</th>' +
        '<th class="name">Наименование</th><th class="unit">Ед. изм.</th><th class="price">Цена</th>' +
        '<th class="act"></th></tr></thead><tbody>';

    for (var i = 0; i < state.items.length; i++) {
        var item = state.items[i];
        var units = '';
        for (var u = 0; u < UNITS.length; u++) {
            units += '<option value="' + escapeHtml(UNITS[u]) + '"' +
                     (item.unit === UNITS[u] ? ' selected' : '') + '>' + escapeHtml(UNITS[u]) + '</option>';
        }
        if (UNITS.indexOf(item.unit) < 0) {
            units += '<option value="' + escapeHtml(item.unit) + '" selected>' + escapeHtml(item.unit) + '</option>';
        }

        html += '<tr data-index="' + i + '">' +
            '<td class="num">' + (i + 1) + '</td>' +
            '<td><input type="text" data-field="group" value="' + escapeHtml(item.group) + '"></td>' +
            '<td><input type="text" data-field="article" value="' + escapeHtml(item.article) + '"></td>' +
            '<td><input type="text" data-field="name" value="' + escapeHtml(item.name) + '"></td>' +
            '<td><select data-field="unit">' + units + '</select></td>' +
            '<td><input type="text" inputmode="decimal" data-field="price" value="' + money(item.price) + '" class="right"></td>' +
            '<td class="act"><button type="button" class="mini danger" data-act="drop" title="Удалить">\u2715</button></td>' +
            '</tr>';
    }

    html += '</tbody></table>';
    host.innerHTML = html;
}
// ------------------------------------------------------------- смета

function documentTitle() {
    var text = 'СМЕТА';
    return text;
}

function documentSubtitle() {
    var text = 'от ' + new Date().toLocaleString('ru-RU');
    if (state.document.number) text += '   \u2022   № ' + state.document.number;
    if (state.document.customer) text += '   \u2022   Заказчик: ' + state.document.customer;
    return text;
}

function buildEstimateText() {
    var rows = chosenRows();
    var summary = totals();
    var lines = [];

    lines.push('СМЕТА');
    lines.push(documentSubtitle());
    lines.push(new Array(87).join('-'));

    var group = null;
    for (var i = 0; i < rows.length; i++) {
        var row = rows[i];
        if (row.item.group !== group) {
            group = row.item.group;
            lines.push('');
            lines.push('[' + group.toUpperCase() + ']');
        }

        lines.push('  ' + row.item.name +
            (row.item.article ? ' (арт. ' + row.item.article + ')' : '') +
            '  ' + money(row.price) + ' x ' + number(row.quantity) + ' ' + row.item.unit +
            ' = ' + money(row.sum));
    }

    lines.push(new Array(87).join('-'));
    if (summary.discount > 0) {
        lines.push('Подытог: ' + money(summary.subtotal));
        lines.push('Скидка ' + number(summary.percent) + ' %: \u2212' + money(summary.discount));
    }
    lines.push('ИТОГО К ОПЛАТЕ: ' + money(summary.payable) + ' \u20BD');

    lines.push('');
    lines.push('Исполнитель: ____________________        Заказчик: ' +
        (state.document.customer ? state.document.customer : '____________________'));
    return lines.join('\n');
}

function buildEstimateHtml() {
    var rows = chosenRows();
    var summary = totals();

    var logo = state.logo && state.logo.data
        ? '<img class="logo" src="' + escapeHtml(state.logo.data) + '" alt="Логотип компании">'
        : '';

    var html = '<div class="head">' + logo + '<div class="head-text">' +
        '<h1>СМЕТА</h1>' +
        '<p class="meta">' + escapeHtml(documentSubtitle()) +
        '   \u2022   позиций: ' + rows.length + '</p></div></div>' +
        '<table class="items estimate"><thead><tr>' +
        '<th class="num">#</th><th class="article">Артикул</th><th class="name">Наименование</th>' +
        '<th class="unit">Ед. изм.</th><th class="qty">Кол-во</th><th class="price">Цена</th>' +
        '<th class="sum">Всего</th></tr></thead><tbody>';

    var group = null;
    var position = 0;

    for (var i = 0; i < rows.length; i++) {
        var row = rows[i];
        if (row.item.group !== group) {
            group = row.item.group;
            html += '<tr class="group-row"><td colspan="7">' + escapeHtml(group) + '</td></tr>';
        }
        position++;
        html += '<tr>' +
            '<td class="num">' + position + '</td>' +
            '<td class="article">' + escapeHtml(row.item.article) + '</td>' +
            '<td class="name">' + escapeHtml(row.item.name) + '</td>' +
            '<td class="unit">' + escapeHtml(row.item.unit) + '</td>' +
            '<td class="qty">' + number(row.quantity) + '</td>' +
            '<td class="price">' + money(row.price) + (row.changed ? ' *' : '') + '</td>' +
            '<td class="sum">' + money(row.sum) + '</td>' +
            '</tr>';
    }

    if (summary.discount > 0) {
        html += '<tr><td colspan="6">Подытог</td><td class="sum">' + money(summary.subtotal) + '</td></tr>';
        html += '<tr><td colspan="6">Скидка ' + number(summary.percent) + ' %</td><td class="sum">\u2212' +
                money(summary.discount) + '</td></tr>';
    }

    html += '<tr class="total-row"><td colspan="6">ИТОГО К ОПЛАТЕ, \u20BD</td><td class="sum">' +
            money(summary.payable) + '</td></tr>';
    html += '</tbody></table>';
    html += '<table class="sign"><tr>' +
            '<td>Исполнитель: ____________________</td>' +
            '<td>Заказчик: ' + escapeHtml(state.document.customer || '____________________') + '</td>' +
            '</tr></table>';

    return html;
}

// --------------------------------------------- сохранение сметы в Word

function utf8(text) {
    var source = unescape(encodeURIComponent(String(text)));
    var bytes = new Uint8Array(source.length);
    for (var i = 0; i < source.length; i++) bytes[i] = source.charCodeAt(i);
    return bytes;
}

var CRC_TABLE = (function () {
    var table = new Uint32Array(256);
    for (var n = 0; n < 256; n++) {
        var c = n;
        for (var k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
        table[n] = c >>> 0;
    }
    return table;
})();

function crc32(bytes) {
    var crc = 0xFFFFFFFF;
    for (var i = 0; i < bytes.length; i++) crc = CRC_TABLE[(crc ^ bytes[i]) & 0xFF] ^ (crc >>> 8);
    return (crc ^ 0xFFFFFFFF) >>> 0;
}

function xmlEscape(text) {
    return escapeHtml(text);
}

/** Разметка логотипа в документе Word. */
function logoParagraph(logo) {
    if (!logo) return '';

    var cx = Math.round(logo.width * 9525);      // точки в английские пункты (EMU)
    var cy = Math.round(logo.height * 9525);
    var id = 'Logo1';

    return '<w:p><w:pPr><w:jc w:val="center"/><w:spacing w:after="120"/></w:pPr><w:r><w:drawing>' +
        '<wp:inline distT="0" distB="0" distL="0" distR="0" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing">' +
        '<wp:extent cx="' + cx + '" cy="' + cy + '"/>' +
        '<wp:docPr id="1" name="Логотип компании"/>' +
        '<a:graphic xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">' +
        '<a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">' +
        '<pic:pic xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture">' +
        '<pic:nvPicPr><pic:cNvPr id="1" name="Логотип"/><pic:cNvPicPr/></pic:nvPicPr>' +
        '<pic:blipFill><a:blip r:embed="rId2" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"/>' +
        '<a:stretch><a:fillRect/></a:stretch></pic:blipFill>' +
        '<pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="' + cx + '" cy="' + cy + '"/></a:xfrm>' +
        '<a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr>' +
        '</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>';
}

function buildDocxParts() {
    var rows = chosenRows();
    var logo = logoForDocx();

    var summary = totals();

    var contentTypes = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">' +
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>' +
        '<Default Extension="xml" ContentType="application/xml"/>' +
        '<Default Extension="png" ContentType="image/png"/>' +
        '<Default Extension="jpeg" ContentType="image/jpeg"/>' +
        '<Default Extension="gif" ContentType="image/gif"/>' +
        '<Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>' +
        '<Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>' +
        '</Types>';

    var rootRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>' +
        '</Relationships>';

    var documentRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>' +
        (logo ? '<Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/logo.' + logo.extension + '"/>' : '') +
        '</Relationships>';
    var font = '<w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/>';
    var styles = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">' +
        '<w:docDefaults><w:rPrDefault><w:rPr>' + font +
        '<w:sz w:val="22"/><w:szCs w:val="22"/></w:rPr></w:rPrDefault>' +
        '<w:pPrDefault><w:pPr><w:spacing w:after="0"/></w:pPr></w:pPrDefault></w:docDefaults>' +
        '<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/><w:qFormat/>' +
        '<w:pPr><w:spacing w:after="0"/></w:pPr><w:rPr>' + font + '</w:rPr></w:style>' +
        '<w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:qFormat/>' +
        '<w:pPr><w:jc w:val="center"/><w:spacing w:after="120"/></w:pPr>' +
        '<w:rPr><w:b/><w:sz w:val="32"/><w:szCs w:val="32"/></w:rPr></w:style>' +
        '<w:style w:type="paragraph" w:styleId="Subtitle"><w:name w:val="Subtitle"/><w:basedOn w:val="Normal"/>' +
        '<w:pPr><w:jc w:val="center"/><w:spacing w:after="240"/></w:pPr>' +
        '<w:rPr><w:color w:val="595959"/><w:sz w:val="20"/></w:rPr></w:style>' +
        '<w:style w:type="paragraph" w:styleId="TableHeader"><w:name w:val="Table Header"/><w:basedOn w:val="Normal"/>' +
        '<w:rPr><w:b/></w:rPr></w:style>' +
        '<w:style w:type="paragraph" w:styleId="GroupRow"><w:name w:val="Group Row"/><w:basedOn w:val="Normal"/>' +
        '<w:rPr><w:b/><w:caps/></w:rPr></w:style>' +
        '<w:style w:type="paragraph" w:styleId="TotalRow"><w:name w:val="Total Row"/><w:basedOn w:val="Normal"/>' +
        '<w:rPr><w:b/><w:sz w:val="24"/></w:rPr></w:style>' +
        '<w:style w:type="paragraph" w:styleId="FootLine"><w:name w:val="Foot Line"/><w:basedOn w:val="Normal"/>' +
        '<w:pPr><w:jc w:val="right"/></w:pPr><w:rPr><w:b/></w:rPr></w:style>' +
        '</w:styles>';

    function paragraph(text, style, alignment) {
        return '<w:p><w:pPr><w:pStyle w:val="' + style + '"/>' +
               (alignment ? '<w:jc w:val="' + alignment + '"/>' : '') + '</w:pPr>' +
               (text ? '<w:r><w:t xml:space="preserve">' + xmlEscape(text) + '</w:t></w:r>' : '') +
               '</w:p>';
    }

    function cell(text, style, alignment) {
        return '<w:tc><w:tcPr><w:vAlign w:val="center"/></w:tcPr>' +
               paragraph(text, style, alignment) + '</w:tc>';
    }

    function row(style, cells) {
        var html = '<w:tr>';
        for (var i = 0; i < cells.length; i++) {
            var numeric = i >= 4;
            var center = (i <= 1 || i === 3);
            html += cell(cells[i], style, numeric ? 'right' : (center ? 'center' : 'left'));
        }
        return html + '</w:tr>';
    }

    var grid = '<w:tblGrid><w:gridCol w:w="500"/><w:gridCol w:w="1100"/><w:gridCol w:w="4400"/>' +
               '<w:gridCol w:w="1000"/><w:gridCol w:w="800"/><w:gridCol w:w="1200"/><w:gridCol w:w="1300"/></w:tblGrid>';

    var borders = '<w:tblBorders><w:top w:val="single" w:sz="4" w:color="808080"/>' +
        '<w:left w:val="none"/><w:bottom w:val="single" w:sz="4" w:color="808080"/><w:right w:val="none"/>' +
        '<w:insideH w:val="single" w:sz="4" w:color="BFBFBF"/><w:insideV w:val="none"/></w:tblBorders>';

    var table = '<w:tbl><w:tblPr><w:tblW w:w="5000" w:type="pct"/>' + borders + '</w:tblPr>' + grid;
    table += row('TableHeader', ['#', 'Артикул', 'Наименование', 'Ед. изм.', 'Кол-во', 'Цена', 'Всего']);

    var group = null;
    var position = 0;

    for (var i = 0; i < rows.length; i++) {
        var item = rows[i].item;
        if (item.group !== group) {
            group = item.group;
            table += row('GroupRow', ['', '', group, '', '', '', '']);
        }
        position++;
        table += row('Normal', [
            String(position), item.article, item.name, item.unit,
            number(rows[i].quantity), money(rows[i].price) + (rows[i].changed ? ' *' : ''),
            money(rows[i].sum)
        ]);
    }

    table += row('TotalRow', ['', '', 'ИТОГО К ОПЛАТЕ: ' + money(summary.payable) + ' \u20BD', '', '', '', '']);
    table += '</w:tbl>';

    var foot = '';
    if (summary.discount > 0) {
        foot += paragraph('Подытог: ' + money(summary.subtotal), 'FootLine', 'right');
        foot += paragraph('Скидка ' + number(summary.percent) + ' %: \u2212' + money(summary.discount),
                          'FootLine', 'right');
    }
    foot += paragraph('ИТОГО К ОПЛАТЕ: ' + money(summary.payable) + ' \u20BD', 'FootLine', 'right');

    var sign = '<w:tbl><w:tblPr><w:tblW w:w="5000" w:type="pct"/>' +
        '<w:tblBorders><w:top w:val="none"/><w:left w:val="none"/><w:bottom w:val="none"/>' +
        '<w:right w:val="none"/><w:insideH w:val="none"/><w:insideV w:val="none"/></w:tblBorders></w:tblPr>' +
        '<w:tblGrid><w:gridCol w:w="4600"/><w:gridCol w:w="4600"/></w:tblGrid><w:tr>' +
        cell('Исполнитель: ____________________', 'Normal', 'left') +
        cell('Заказчик: ' + (state.document.customer || '____________________'), 'Normal', 'left') +
        '</w:tr></w:tbl>';

    var section = '<w:sectPr><w:pgSz w:w="11906" w:h="16838"/>' +
        '<w:pgMar w:top="1134" w:right="850" w:bottom="1134" w:left="850" ' +
        'w:header="708" w:footer="708" w:gutter="0"/></w:sectPr>';

    var document = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
        '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>' +
        (logo ? logoParagraph(logo) : '') +
        paragraph('СМЕТА', 'Title', 'center') +
        paragraph(documentSubtitle(), 'Subtitle', 'center') +
        table + foot + paragraph('', 'Normal', null) + sign + section +
        '</w:body></w:document>';

    return [
        { name: '[Content_Types].xml', text: contentTypes },
        { name: '_rels/.rels', text: rootRels },
        { name: 'word/document.xml', text: document },
        { name: 'word/styles.xml', text: styles },
        { name: 'word/_rels/document.xml.rels', text: documentRels },
        { name: 'word/media/logo.' + (logo ? logo.extension : 'png'), base64: logo ? logo.base64 : null }
    ];
}

/** Двоичные данные из строки base64. */
function base64Bytes(text) {
    var binary = atob(text);
    var bytes = new Uint8Array(binary.length);
    for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
}

function zipStore(parts) {
    var chunks = [];
    var central = [];
    var offset = 0;

    function number(value, size) {
        var bytes = new Uint8Array(size);
        for (var i = 0; i < size; i++) bytes[i] = (value >>> (8 * i)) & 0xFF;
        return bytes;
    }

    for (var i = 0; i < parts.length; i++) {
        var name = utf8(parts[i].name);
        var data = parts[i].base64 ? base64Bytes(parts[i].base64) : utf8(parts[i].text);
        var crc = crc32(data);

        var local = [number(0x04034b50, 4), number(20, 2), number(0, 2), number(0, 2),
                     number(0, 2), number(0, 2), number(crc, 4), number(data.length, 4),
                     number(data.length, 4), number(name.length, 2), number(0, 2), name];
        chunks.push(local, [data]);

        var head = [number(0x02014b50, 4), number(20, 2), number(20, 2), number(0, 2),
                    number(0, 2), number(0, 2), number(0, 2), number(crc, 4),
                    number(data.length, 4), number(data.length, 4), number(name.length, 2),
                    number(0, 2), number(0, 2), number(0, 2), number(0, 2), number(0, 4),
                    number(offset, 4), name];
        central.push(head);

        offset += 30 + name.length + data.length;
    }

    var centralSize = 0;
    for (var c = 0; c < central.length; c++) {
        for (var s = 0; s < central[c].length; s++) centralSize += central[c][s].length;
    }

    var end = [number(0x06054b50, 4), number(0, 2), number(0, 2), number(parts.length, 2),
               number(parts.length, 2), number(centralSize, 4), number(offset, 4), number(0, 2)];

    var all = [];
    for (var k = 0; k < chunks.length; k++) all.push.apply(all, chunks[k]);
    for (var m = 0; m < central.length; m++) all.push.apply(all, central[m]);
    all.push.apply(all, end);

    return new Blob(all, { type: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' });
}

function exportDocx() {
    if (!chosenRows().length) {
        setStatus('Сначала отметьте нужные услуги.');
        return;
    }

    downloadBlob('Смета.docx', zipStore(buildDocxParts()));
    setStatus('Смета сохранена документом Word: Смета.docx');
}
// ------------------------------------------------------- хранение данных

function save() {
    // в режиме сервиса изменения уходят в общее хранилище
    if (server.online && !server.quiet) {
        serverPush('prices');
        serverPush('templates');
        serverPush('settings');
        serverPush('state');
    }

    try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify({
            items: state.items,
            chosen: state.chosen,
            templates: state.templates,
            document: state.document,
            logo: state.logo,
            theme: state.theme,
            collapsing: state.collapsing
        }));
    } catch (e) {
        setStatus('Не удалось сохранить данные в браузере: ' + e.message);
    }
}

function load() {
    var saved = null;
    try {
        var raw = localStorage.getItem(STORAGE_KEY);
        if (raw) saved = JSON.parse(raw);
    } catch (e) {
        saved = null;
    }

    if (saved && saved.items && saved.items.length) {
        state.items = saved.items;
        state.chosen = normalizeChosen(saved.chosen);
        state.templates = saved.templates || [];
        state.collapsing = saved.collapsing || {};
        state.theme = saved.theme === 'dark' ? 'dark' : 'light';

        state.document = saved.document || { number: '', customer: '', discount: 0 };
        state.logo = saved.logo || null;
        state.document.discount = clampDiscount(state.document.discount);

        setStatus('Загружен прайс-лист из памяти браузера: ' + state.items.length + ' позиций');
        return;
    }

    seedFromFactory();
}

/** Старые сохранения хранили только количество — приводим к новому виду. */
function normalizeChosen(chosen) {
    var result = {};
    if (!chosen) return result;

    for (var rowKey in chosen) {
        if (!chosen.hasOwnProperty(rowKey)) continue;
        var value = chosen[rowKey];

        if (typeof value === 'number') {
            result[rowKey] = { quantity: value };
        } else if (value && typeof value === 'object') {
            result[rowKey] = {
                quantity: Number(value.quantity) || 0,
                price: (value.price === undefined || value.price === null) ? null : Number(value.price)
            };
        }
    }
    return result;
}

function seedFromFactory() {
    state.items = [];
    var seed = window.SEED_PRICES || [];
    for (var i = 0; i < seed.length; i++) {
        state.items.push({
            group: seed[i].group,
            article: seed[i].article || '',
            name: seed[i].name,
            unit: seed[i].unit,
            price: seed[i].price
        });
    }
    setStatus('Загружен заводской прайс-лист: ' + state.items.length + ' позиций');
}

function setStatus(text) {
    var host = document.getElementById('status');
    if (host) host.textContent = text;
}

// ---------------------------------------------------- файл обмена данными

/** Сборка файла данных: прайс, шаблоны, реквизиты и тема. */
function buildExchange() {
    return {
        format: EXCHANGE_FORMAT,
        version: 1,
        saved: new Date().toISOString(),
        logo: state.logo || null,
        settings: {
            theme: state.theme,
            number: state.document.number,
            customer: state.document.customer,
            discount: clampDiscount(state.document.discount)
        },
        prices: state.items,
        templates: state.templates
    };
}

/** Загрузка данных из файла обмена. Возвращает число принятых позиций. */
function applyExchange(data) {
    if (!data || data.format !== EXCHANGE_FORMAT) return -1;

    var applied = { prices: 0, templates: 0 };

    if (data.prices && data.prices.length) {
        state.items = [];
        for (var i = 0; i < data.prices.length; i++) {
            var item = data.prices[i];
            if (!item || !item.name) continue;
            state.items.push({
                group: item.group || 'Прочее',
                article: item.article || '',
                name: item.name,
                unit: item.unit || UNITS[0],
                price: Number(item.price) || 0
            });
        }
        state.chosen = {};
        applied.prices = state.items.length;
    }

    if (data.templates && data.templates.length) {
        state.templates = data.templates;
        applied.templates = state.templates.length;
    }

    if (data.logo && data.logo.data) {
        state.logo = { data: data.logo.data, name: data.logo.name || 'logo' };
    } else if (data.logo === null) {
        state.logo = null;
    }

    if (data.settings) {
        state.theme = data.settings.theme === 'dark' ? 'dark' : 'light';
        state.document.number = data.settings.number || '';
        state.document.customer = data.settings.customer || '';
        state.document.discount = clampDiscount(data.settings.discount);
    }

    return applied;
}

function exportData() {
    download('Данные_сметы.smeta', JSON.stringify(buildExchange(), null, 2), 'application/json;charset=utf-8');
    setStatus('Данные выгружены в файл: прайс, наборы и реквизиты.');
}

function importData(file) {
    var reader = new FileReader();
    reader.onload = function () {
        var data = null;
        try { data = JSON.parse(String(reader.result)); } catch (e) { data = null; }

        if (!data || data.format !== EXCHANGE_FORMAT) {
            setStatus('Файл не похож на данные программы.');
            return;
        }

        if (!confirm('Загрузить данные из файла? Прайс-лист, наборы и реквизиты будут заменены.')) return;

        var applied = applyExchange(data);
        if (applied === -1) { setStatus('Не удалось применить данные.'); return; }

        save();
        applyTheme();
        showDocumentFields();
        renderEditor();
        render();
        setStatus('Загружены данные: позиций — ' + applied.prices + ', наборов — ' + applied.templates);
    };
    reader.readAsText(file, 'utf-8');
}

// ------------------------------------------------ шаблоны наборов услуг

/**
 * Сохранение набора услуг.
 * askName — готовая функция запроса названия (по умолчанию диалог браузера),
 * confirmReplace — подтверждение замены одноимённого набора.
 */
function saveTemplate(askName, confirmReplace) {
    var rows = chosenRows();
    if (!rows.length) {
        setStatus('Сначала отметьте услуги, которые войдут в набор.');
        return false;
    }

    var box = document.getElementById('templateBox');
    var name = typeof askName === 'function'
        ? askName()
        : (typeof askName === 'string' && askName ? askName : (box.value || ''));

    name = (name || '').trim();
    if (!name) {
        name = prompt('Название набора услуг:', 'Набор ' + (state.templates.length + 1));
        if (!name) return false;
        name = name.trim();
        if (!name) return false;
    }

    var existing = findTemplate(name);
    if (existing) {
        var allowed = typeof confirmReplace === 'function'
            ? confirmReplace(name)
            : confirm('Набор «' + name + '» уже есть. Заменить его?');
        if (!allowed) return false;
    }

    var template = { name: name, saved: new Date().toISOString(), items: [] };
    for (var i = 0; i < rows.length; i++) {
        template.items.push({
            group: rows[i].item.group,
            article: rows[i].item.article,
            name: rows[i].item.name,
            quantity: rows[i].quantity
        });
    }

    var kept = [];
    for (var k = 0; k < state.templates.length; k++) {
        if (existing && state.templates[k] === existing) continue;
        kept.push(state.templates[k]);
    }
    kept.push(template);
    state.templates = kept;

    save();
    renderTemplateList();
    box.value = name;
    setStatus('Набор «' + name + '» сохранён: ' + template.items.length + ' позиций');
    return true;
}

function applyTemplate() {
    var box = document.getElementById('templateBox');
    var template = findTemplate(box.value);
    if (!template) {
        setStatus('Выберите набор услуг в списке.');
        return;
    }

    var applied = 0;
    var missing = 0;

    for (var i = 0; i < template.items.length; i++) {
        var wanted = template.items[i];
        var found = null;
        var byName = null;

        for (var k = 0; k < state.items.length; k++) {
            var item = state.items[k];
            if (wanted.article && item.article &&
                item.article.toLowerCase() === wanted.article.toLowerCase()) { found = item; break; }
            if (!byName && item.name.toLowerCase() === wanted.name.toLowerCase()) byName = item;
        }
        if (!found) found = byName;

        if (!found) { missing++; continue; }

        var rowKey = key(found);
        var previous = state.chosen[rowKey];
        state.chosen[rowKey] = {
            quantity: wanted.quantity || 1,
            price: previous && previous.price !== undefined ? previous.price : null
        };
        applied++;
    }

    save();
    render();

    var message = 'Набор «' + template.name + '»: отмечено позиций — ' + applied;
    if (missing > 0) message += ', не найдено в прайсе — ' + missing;
    setStatus(message);
}

function deleteTemplate() {
    var box = document.getElementById('templateBox');
    var template = findTemplate(box.value);
    if (!template) {
        setStatus('Выберите набор услуг в списке.');
        return;
    }

    if (!confirm('Удалить набор «' + template.name + '»?')) return;

    var kept = [];
    for (var i = 0; i < state.templates.length; i++) {
        if (state.templates[i] !== template) kept.push(state.templates[i]);
    }
    state.templates = kept;

    save();
    renderTemplateList();
    setStatus('Набор «' + template.name + '» удалён.');
}

// ----------------------------------------------------- оформление и файлы
// ------------------------------------------------------- логотип компании

var LOGO_LIMIT = 900 * 1024;        // ограничение на размер файла

/** Загрузка логотипа из выбранного файла. */
function setLogoFile(file) {
    if (!file) return false;

    if (file.size > LOGO_LIMIT) {
        setStatus('Файл логотипа больше 900 КБ — выберите изображение поменьше.');
        return false;
    }

    var reader = new FileReader();
    reader.onload = function () {
        state.logo = { data: String(reader.result), name: file.name };
        save();
        refreshLogoControls();
        setStatus('Логотип загружен: ' + file.name);
    };
    reader.onerror = function () {
        setStatus('Не удалось прочитать файл логотипа.');
    };
    reader.readAsDataURL(file);
    return true;
}

/** Удаление логотипа. */
function removeLogo() {
    if (!state.logo) {
        setStatus('Логотип не задан.');
        return false;
    }

    state.logo = null;
    save();
    refreshLogoControls();
    setStatus('Логотип убран.');
    return true;
}

/** Кнопка «Убрать логотип» видна только когда логотип задан. */
function refreshLogoControls() {
    var button = document.getElementById('logoRemove');
    if (button) button.style.display = state.logo ? '' : 'none';
}

/** Размеры картинки по её данным (PNG и JPEG). */
function imageSize(dataUrl) {
    var fallback = { width: 240, height: 80 };
    if (!dataUrl) return fallback;

    var comma = dataUrl.indexOf(',');
    if (comma < 0) return fallback;

    var binary = atob(dataUrl.substring(comma + 1));
    var bytes = new Uint8Array(binary.length);
    for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

    // PNG: размеры лежат в блоке IHDR
    if (bytes.length > 24 && bytes[0] === 0x89 && bytes[1] === 0x50) {
        var w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        if (w > 0 && h > 0) return { width: w, height: h };
    }

    // JPEG: ищем маркер начала кадра с размерами
    if (bytes.length > 4 && bytes[0] === 0xFF && bytes[1] === 0xD8) {
        var at = 2;
        while (at + 9 < bytes.length) {
            if (bytes[at] !== 0xFF) { at++; continue; }
            var marker = bytes[at + 1];
            var size = (bytes[at + 2] << 8) | bytes[at + 3];
            if (marker >= 0xC0 && marker <= 0xCF && marker !== 0xC4 && marker !== 0xC8 && marker !== 0xCC) {
                var jh = (bytes[at + 5] << 8) | bytes[at + 6];
                var jw = (bytes[at + 7] << 8) | bytes[at + 8];
                if (jw > 0 && jh > 0) return { width: jw, height: jh };
            }
            at += 2 + size;
        }
    }

    return fallback;
}

/** Размер логотипа в документе: не шире 180 и не выше 64 точек. */
function logoBox() {
    var size = imageSize(state.logo ? state.logo.data : null);
    var width = size.width;
    var height = size.height;

    if (width > 180) {
        height = Math.round(height * 180 / width);
        width = 180;
    }
    if (height > 64) {
        width = Math.round(width * 64 / height);
        height = 64;
    }

    return { width: Math.max(1, width), height: Math.max(1, height) };
}

/** Данные логотипа для вставки в документ Word. */
function logoForDocx() {
    if (!state.logo || !state.logo.data) return null;

    var comma = state.logo.data.indexOf(',');
    if (comma < 0) return null;

    var header = state.logo.data.substring(0, comma);
    var extension = 'png';
    if (header.indexOf('image/jpeg') >= 0) extension = 'jpeg';
    else if (header.indexOf('image/gif') >= 0) extension = 'gif';
    else if (header.indexOf('image/webp') >= 0) extension = 'webp';

    var box = logoBox();
    return {
        base64: state.logo.data.substring(comma + 1),
        extension: extension,
        width: box.width,
        height: box.height
    };
}

// ------------------------------------------------------- логотип компании
// ------------------------------------------------------- сервис программы

/** Адрес сервиса без завершающей косой черты. */
function serverBase() {
    return server.url.replace(/\/+$/, '');
}

/** Запрос к сервису. Возвращает разобранный ответ или null. */
function serverRequest(path, body, method) {
    return new Promise(function (resolve) {
        var request = new XMLHttpRequest();
        request.open(method || (body ? 'POST' : 'GET'), serverBase() + path, true);
        request.timeout = 8000;

        if (body) request.setRequestHeader('Content-Type', 'application/json; charset=utf-8');

        request.onload = function () {
            var data = null;
            try { data = JSON.parse(request.responseText); } catch (e) { data = null; }
            resolve(data);
        };
        request.onerror = function () { resolve(null); };
        request.ontimeout = function () { resolve(null); };

        request.send(body ? JSON.stringify(body) : null);
    });
}

/** Подключение к сервису: читаем данные, запоминаем адрес. */
function connectServer(address) {
    var value = (address || '').trim();
    if (!value) {
        setStatus('Укажите адрес сервиса, например http://127.0.0.1:8791');
        return Promise.resolve(false);
    }

    if (value.indexOf('://') < 0) value = 'http://' + value;

    var previous = server.url;
    server.url = value;

    return serverRequest('/api/data').then(function (data) {
        if (!data || data.ok !== true) {
            server.url = previous;
            setStatus('Сервис не отвечает по адресу ' + value +
                      '. Проверьте, что программа запущена и сервис включён.');
            return false;
        }

        applyServerData(data);
        server.online = true;
        server.stamp = data.stamp || '';

        try { localStorage.setItem(SERVER_KEY, server.url); } catch (e) { }

        refreshServerControls();
        renderEditor();
        render();

        setStatus('Работа через сервис: ' + (data.store || server.url) +
                  '. Данные сохраняются в общем хранилище.');
        return true;
    });
}

/** Отключение от сервиса: дальше работаем в памяти браузера. */
function disconnectServer() {
    server.url = '';
    server.online = false;
    server.stamp = '';

    try { localStorage.removeItem(SERVER_KEY); } catch (e) { }

    refreshServerControls();
    setStatus('Сервис отключён. Данные снова хранятся в памяти браузера.');
    return true;
}

/** Разбор данных, полученных от сервиса. */
function applyServerData(data) {
    // данные пришли с сервиса: отправлять их обратно не нужно
    server.quiet = true;
    if (data.prices) {
        state.items = [];
        for (var i = 0; i < data.prices.length; i++) {
            var item = data.prices[i];
            if (!item || !item.name) continue;
            state.items.push({
                group: item.group || 'Прочее',
                article: item.article || '',
                name: item.name,
                unit: item.unit || UNITS[0],
                price: Number(item.price) || 0
            });
        }
    }

    if (data.templates) {
        var templates = [];
        for (var k = 0; k < data.templates.length; k++) {
            var template = data.templates[k];
            if (!template || !template.name) continue;

            var items = [];
            var source = template.items || [];
            for (var n = 0; n < source.length; n++) {
                items.push({
                    group: source[n].group || '',
                    article: source[n].article || '',
                    name: source[n].name || '',
                    quantity: Number(source[n].quantity) || 1
                });
            }
            templates.push({ name: template.name, items: items });
        }
        state.templates = templates;
    }

    if (data.document) {
        state.document.number = data.document.number || '';
        state.document.customer = data.document.customer || '';
        state.document.discount = clampDiscount(data.document.discount);
    }

    if (data.theme) state.theme = data.theme === 'dark' ? 'dark' : 'light';
    if (data.logo !== undefined) {
        state.logo = data.logo ? { data: data.logo, name: 'logo' } : null;
    }

    state.chosen = {};
    if (data.state && data.state.chosen) state.chosen = normalizeChosen(data.state.chosen);

    applyTheme();
    showDocumentFields();
    refreshLogoControls();
    renderTemplateList();

    try { save(); } finally { server.quiet = false; }
}

/** Отправка изменений на сервис — с небольшой задержкой, чтобы не частить. */
function serverPush(part) {
    if (!server.online) return;

    server.pending[part] = true;

    if (server.timer) clearTimeout(server.timer);

    server.timer = setTimeout(function () {
        var pending = server.pending;
        server.pending = {};
        server.timer = null;

        if (pending.prices) {
            serverRequest('/api/prices', { prices: state.items }).then(function (answer) {
                reportServerAnswer(answer, 'прайс-лист');
            });
        }

        if (pending.templates) {
            serverRequest('/api/templates', { templates: state.templates }).then(function (answer) {
                reportServerAnswer(answer, 'наборы услуг');
            });
        }

        if (pending.settings) {
            serverRequest('/api/settings', {
                document: state.document,
                theme: state.theme,
                logo: state.logo ? state.logo.data : ''
            }).then(function (answer) {
                reportServerAnswer(answer, 'реквизиты');
            });
        }

        if (pending.state) {
            serverRequest('/api/state', {
                chosen: state.chosen,
                collapsing: state.collapsing
            }).then(function (answer) {
                reportServerAnswer(answer, 'смета');
            });
        }
    }, 400);
}

/** Сообщение о результате отправки. */
function reportServerAnswer(answer, what) {
    if (!answer) {
        server.online = false;
        refreshServerControls();
        setStatus('Сервис перестал отвечать: изменения сохранены только в браузере.');
        return;
    }

    if (answer.ok !== true) {
        setStatus('Сервис не принял данные (' + what + '): ' + (answer.error || 'без пояснения'));
        return;
    }

    if (answer.stamp) server.stamp = answer.stamp;
}

/** Подписи кнопок и строки о режиме работы. */
function refreshServerControls() {
    var connect = document.getElementById('serverConnect');
    var disconnect = document.getElementById('serverDisconnect');
    var address = document.getElementById('serverAddress');
    var hint = document.getElementById('storageHint');

    if (connect) connect.style.display = server.online ? 'none' : '';
    if (disconnect) disconnect.style.display = server.online ? '' : 'none';
    if (address) {
        address.value = server.url || '';
        address.disabled = server.online;
    }
    if (hint) {
        hint.textContent = server.online
            ? 'Веб-версия: данные хранятся в общей базе через сервис программы'
            : 'Веб-версия: данные хранятся в самом браузере';
    }
}
function applyTheme() {
    var dark = state.theme === 'dark';
    document.body.className = dark ? 'dark' : 'light';

    var button = document.getElementById('themeButton');
    if (button) button.textContent = dark ? 'Светлая тема' : 'Тёмная тема';
}

function toggleTheme() {
    state.theme = state.theme === 'dark' ? 'light' : 'dark';
    applyTheme();
    save();
    setStatus(state.theme === 'dark' ? 'Включена тёмная тема.' : 'Включена светлая тема.');
}

function showDocumentFields() {
    document.getElementById('docNumber').value = state.document.number || '';
    document.getElementById('docCustomer').value = state.document.customer || '';
    document.getElementById('docDiscount').value = state.document.discount
        ? number(state.document.discount) : '';
}

function download(name, text, type) {
    downloadBlob(name, new Blob([text], { type: type || 'text/plain;charset=utf-8' }));
}

function downloadBlob(name, blob) {
    var link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = name;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    setTimeout(function () { URL.revokeObjectURL(link.href); }, 2000);
}

/** Очистка всего прайс-листа: нужно ввести слово ПОЛНОСТЬЮ. */
function clearPrices(givenAnswer) {
    if (!state.items.length) {
        setStatus('Прайс-лист и так пуст.');
        return false;
    }

    var total = state.items.length;
    var answer = (typeof givenAnswer === 'string')
        ? givenAnswer
        : window.prompt(
            'Будут удалены все позиции прайс-листа: ' + total + '.\n' +
            'Отметки в смете при этом сбрасываются.\n\n' +
            'Для подтверждения введите слово ПОЛНОСТЬЮ:');

    if (answer === null) { setStatus('Очистка отменена.'); return false; }
    if (answer.trim().toUpperCase() !== CLEAR_WORD) {
        setStatus('Очистка отменена: слово подтверждения введено неверно.');
        return false;
    }

    // резервная копия на случай ошибки
    var backup = JSON.stringify(state.items);

    state.items = [];
    state.chosen = {};
    save();
    renderEditor();
    render();

    setStatus('Прайс-лист очищен: удалено позиций — ' + total +
              '. Копия сохранена, её можно вернуть кнопкой «Отменить очистку».');

    pendingBackup = backup;
    var undo = document.getElementById('undoClear');
    if (undo) undo.style.display = '';
    return true;
}

/** Возврат прайс-листа, снятого кнопкой очистки. */
function undoClear() {
    if (!pendingBackup) {
        setStatus('Возвращать нечего.');
        return false;
    }

    var restored = null;
    try { restored = JSON.parse(pendingBackup); } catch (e) { restored = null; }

    if (!restored || !restored.length) {
        setStatus('Копия прайс-листа повреждена.');
        return false;
    }

    state.items = restored;
    state.chosen = {};
    pendingBackup = null;

    save();
    renderEditor();
    render();

    var undo = document.getElementById('undoClear');
    if (undo) undo.style.display = 'none';

    setStatus('Прайс-лист восстановлен: позиций — ' + state.items.length);
    return true;
}
function exportCsv() {
    var lines = ['# Прайс-лист программы «Расчет сметы»', '# Разделитель колонок — знак табуляции',
                 '# Группа\tАртикул\tНаименование\tЕд. изм.\tЦена'];
    for (var i = 0; i < state.items.length; i++) {
        var item = state.items[i];
        lines.push([item.group, item.article, item.name, item.unit, money(item.price)].join('\t'));
    }
    download('Прайс-лист.tsv', lines.join('\r\n'), 'text/tab-separated-values;charset=utf-8');
    setStatus('Прайс-лист выгружен в файл.');
}

function importCsv(file) {
    var reader = new FileReader();
    reader.onload = function () {
        var text = String(reader.result);
        var lines = text.split(/\r?\n/);
        var items = [];

        for (var i = 0; i < lines.length; i++) {
            var line = lines[i];
            if (!line.trim() || line.trim().charAt(0) === '#') continue;

            var parts = line.indexOf('\t') >= 0 ? line.split('\t') : line.split(';');
            if (parts.length < 2) continue;

            var group = (parts[0] || '').trim() || 'Прочее';
            var article, name, unit, price;

            if (parts.length >= 5) {
                article = (parts[1] || '').trim();
                name = (parts[2] || '').trim();
                unit = (parts[3] || '').trim();
                price = parseNumber(parts[4]);
            } else {
                article = '';
                name = (parts[1] || '').trim();
                unit = (parts[2] || '').trim();
                price = parseNumber(parts[3]);
            }

            if (!name) continue;
            if (!unit) unit = UNITS[0];
            if (isNaN(price) || price < 0) price = 0;

            items.push({ group: group, article: article, name: name, unit: unit, price: price });
        }

        if (!items.length) {
            setStatus('В файле не найдено ни одной позиции.');
            return;
        }

        state.items = items;
        state.chosen = {};
        save();
        renderEditor();
        render();
        setStatus('Загружено из файла: ' + items.length + ' позиций');
    };
    reader.readAsText(file, 'utf-8');
}

function writeToClipboard(text, what) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).then(function () {
            setStatus(what + ' скопирована в буфер обмена');
        }, function () {
            setStatus('Не удалось скопировать в буфер обмена');
        });
        return;
    }

    var area = document.createElement('textarea');
    area.value = text;
    document.body.appendChild(area);
    area.select();
    try { document.execCommand('copy'); setStatus(what + ' скопирована в буфер обмена'); }
    catch (e) { setStatus('Не удалось скопировать в буфер обмена'); }
    document.body.removeChild(area);
}
// ------------------------------------------------------------- интерфейс

function togglePick(rowKey, picked) {
    var item = findByKey(rowKey);
    if (!item) return;

    if (picked) {
        var previous = state.chosen[rowKey];
        state.chosen[rowKey] = {
            quantity: previous && previous.quantity > 0 ? previous.quantity : 1,
            price: previous && previous.price !== undefined ? previous.price : null
        };
    } else {
        delete state.chosen[rowKey];
    }

    save();
    render();
}

function setQuantity(rowKey, value) {
    var quantity = parseNumber(value);
    if (isNaN(quantity) || quantity < 0) return false;
    if (!state.chosen[rowKey]) return false;

    if (quantity === 0) delete state.chosen[rowKey];
    else state.chosen[rowKey].quantity = quantity;

    save();
    render();
    return true;
}

function setPrice(rowKey, value) {
    var item = findByKey(rowKey);
    if (!item || !state.chosen[rowKey]) return false;

    var price = parseNumber(value);
    if (isNaN(price) || price < 0) return false;

    state.chosen[rowKey].price = price === item.price ? null : price;

    save();
    render();
    return true;
}

function markAll(pick) {
    var query = state.query.trim();

    if (!pick) {
        state.chosen = {};
    } else {
        for (var i = 0; i < state.items.length; i++) {
            var item = state.items[i];
            if (query && !matches(item, query)) continue;

            var rowKey = key(item);
            var previous = state.chosen[rowKey];
            state.chosen[rowKey] = {
                quantity: previous && previous.quantity > 0 ? previous.quantity : 1,
                price: previous && previous.price !== undefined ? previous.price : null
            };
        }
    }

    save();
    render();
}

/** Сворачивание и разворачивание всех разделов сразу. */
function toggleAllGroups() {
    var groups = orderedGroups();
    var allCollapsed = groups.length > 0;

    for (var i = 0; i < groups.length; i++) {
        if (!state.collapsing[groups[i]]) { allCollapsed = false; break; }
    }

    if (allCollapsed) {
        state.collapsing = {};
    } else {
        state.collapsing = {};
        for (var k = 0; k < groups.length; k++) state.collapsing[groups[k]] = true;
    }

    save();
    render();

    var button = document.getElementById('toggleAll');
    if (button) button.textContent = allCollapsed ? 'Свернуть всё' : 'Развернуть всё';

    setStatus(allCollapsed
        ? 'Разделы развёрнуты.'
        : 'Все разделы свёрнуты: ' + groups.length + '.');
}

/** Подпись кнопки сворачивания по текущему состоянию. */
function refreshToggleAllLabel() {
    var button = document.getElementById('toggleAll');
    if (!button) return;

    var groups = orderedGroups();
    var allCollapsed = groups.length > 0;
    for (var i = 0; i < groups.length; i++) {
        if (!state.collapsing[groups[i]]) { allCollapsed = false; break; }
    }
    button.textContent = allCollapsed ? 'Свернуть всё' : 'Развернуть всё';
}
function bind() {
    document.getElementById('list').addEventListener('click', function (event) {
        var head = event.target.closest ? event.target.closest('.group-head') : null;
        if (!head) return;

        var group = head.getAttribute('data-group');
        if (state.collapsing[group]) delete state.collapsing[group];
        else state.collapsing[group] = true;

        save();
        render();
    });

    document.getElementById('list').addEventListener('change', function (event) {
        var target = event.target;
        var row = target.closest ? target.closest('tr') : null;
        if (!row) return;

        var rowKey = row.getAttribute('data-key');
        var action = target.getAttribute('data-act');

        if (action === 'pick') togglePick(rowKey, target.checked);
        else if (action === 'qty') {
            if (!setQuantity(rowKey, target.value)) {
                var mark = state.chosen[rowKey];
                target.value = mark ? number(mark.quantity) : '';
                setStatus('Количество должно быть неотрицательным числом, например 2 или 2,5');
            }
        } else if (action === 'price') {
            if (!setPrice(rowKey, target.value)) {
                var item = findByKey(rowKey);
                target.value = item ? money(item.price) : '';
                setStatus('Цена должна быть неотрицательным числом, например 1 500 или 1500,50');
            }
        }
    });

    document.getElementById('search').addEventListener('input', function (event) {
        state.query = event.target.value;
        render();
    });

    document.getElementById('docNumber').addEventListener('input', onDocumentChanged);
    document.getElementById('docCustomer').addEventListener('input', onDocumentChanged);
    document.getElementById('docDiscount').addEventListener('input', onDocumentChanged);

    document.getElementById('toggleAll').addEventListener('click', function () { toggleAllGroups(); });
    document.getElementById('logoFile').addEventListener('change', function (event) {
        if (event.target.files && event.target.files[0]) setLogoFile(event.target.files[0]);
        event.target.value = '';
    });
    document.getElementById('logoRemove').addEventListener('click', function () { removeLogo(); });

    document.getElementById('markAll').addEventListener('click', function () { markAll(true); });
    document.getElementById('clearMarks').addEventListener('click', function () { markAll(false); });
    document.getElementById('clearAll').addEventListener('click', function () { markAll(false); });

    document.getElementById('templateSearch').addEventListener('input', renderTemplateList);
    document.getElementById('templateApply').addEventListener('click', applyTemplate);
    document.getElementById('templateSave').addEventListener('click', saveTemplate);
    document.getElementById('templateDelete').addEventListener('click', deleteTemplate);

    document.getElementById('themeButton').addEventListener('click', toggleTheme);
    document.getElementById('exportData').addEventListener('click', exportData);
    document.getElementById('serverConnect').addEventListener('click', function () {
        connectServer(document.getElementById('serverAddress').value);
    });

    document.getElementById('serverDisconnect').addEventListener('click', function () {
        disconnectServer();
    });
    document.getElementById('importData').addEventListener('change', function (event) {
        if (event.target.files && event.target.files[0]) importData(event.target.files[0]);
        event.target.value = '';
    });

    document.getElementById('calcTab').addEventListener('click', function () { showTab('calc'); });
    document.getElementById('priceTab').addEventListener('click', function () { showTab('price'); });

    document.getElementById('addItem').addEventListener('click', function () {
        var order = orderedGroups();
        var group = order.length ? order[order.length - 1] : 'Прочее';
        state.items.push({ group: group, article: '', name: 'Новая услуга', unit: UNITS[0], price: 0 });
        save();
        renderEditor();
        render();
        setStatus('Добавлена позиция. Заполните наименование и цену.');
    });

    document.getElementById('exportCsv').addEventListener('click', exportCsv);

    document.getElementById('importCsv').addEventListener('change', function (event) {
        if (event.target.files && event.target.files[0]) importCsv(event.target.files[0]);
        event.target.value = '';
    });

    document.getElementById('clearPrices').addEventListener('click', function () { clearPrices(); });
    document.getElementById('undoClear').addEventListener('click', function () { undoClear(); });

    document.getElementById('resetPrices').addEventListener('click', function () {
        if (!confirm('Вернуть заводской прайс-лист? Ваши изменения будут потеряны.')) return;
        seedFromFactory();
        state.chosen = {};
        save();
        renderEditor();
        render();
    });

    document.getElementById('editor').addEventListener('change', function (event) {
        var target = event.target;
        var row = target.closest ? target.closest('tr') : null;
        if (!row) return;

        var index = Number(row.getAttribute('data-index'));
        var field = target.getAttribute('data-field');
        if (!field || !state.items[index]) return;

        if (field === 'price') {
            var price = parseNumber(target.value);
            if (isNaN(price) || price < 0) {
                target.value = money(state.items[index].price);
                setStatus('Цена должна быть неотрицательным числом');
                return;
            }
            state.items[index].price = price;
            target.value = money(price);
        } else {
            state.items[index][field] = target.value.trim();
        }

        save();
        renderEditor();
        render();
    });

    document.getElementById('editor').addEventListener('click', function (event) {
        var target = event.target;
        if (!target.getAttribute || target.getAttribute('data-act') !== 'drop') return;

        var row = target.closest ? target.closest('tr') : null;
        if (!row) return;

        var index = Number(row.getAttribute('data-index'));
        var item = state.items[index];
        if (!item) return;
        if (!confirm('Удалить позицию «' + item.name + '»?')) return;

        delete state.chosen[key(item)];
        state.items.splice(index, 1);
        save();
        renderEditor();
        render();
        setStatus('Позиция удалена.');
    });

    document.getElementById('preview').addEventListener('click', showPreview);
    document.getElementById('estimate').addEventListener('click', showPreview);

    document.getElementById('closeSheet').addEventListener('click', function () {
        document.getElementById('overlay').classList.remove('open');
    });

    document.getElementById('wordSheet').addEventListener('click', exportDocx);
    document.getElementById('printSheet').addEventListener('click', function () { window.print(); });

    document.getElementById('copyText').addEventListener('click', function () {
        writeToClipboard(buildEstimateText(), 'Смета');
    });

    document.getElementById('downloadText').addEventListener('click', function () {
        if (!chosenRows().length) { setStatus('Сначала отметьте нужные услуги.'); return; }
        download('Смета.txt', buildEstimateText(), 'text/plain;charset=utf-8');
        setStatus('Смета сохранена текстовым файлом.');
    });

    document.getElementById('overlay').addEventListener('click', function (event) {
        if (event.target === this) this.classList.remove('open');
    });
}

function onDocumentChanged() {
    state.document.number = document.getElementById('docNumber').value.trim();
    state.document.customer = document.getElementById('docCustomer').value.trim();
    state.document.discount = clampDiscount(document.getElementById('docDiscount').value);

    save();
    renderSummary();
}

function showPreview() {
    if (!chosenRows().length) {
        setStatus('Сначала отметьте нужные услуги.');
        return;
    }

    document.getElementById('sheet').innerHTML = buildEstimateHtml();
    document.getElementById('overlay').classList.add('open');
}

function showTab(name) {
    var calc = document.getElementById('panelCalc');
    var price = document.getElementById('panelPrice');
    var calcTab = document.getElementById('calcTab');
    var priceTab = document.getElementById('priceTab');

    if (name === 'price') {
        calc.style.display = 'none';
        price.style.display = 'block';
        calcTab.className = 'tab';
        priceTab.className = 'tab active';
        renderEditor();
    } else {
        calc.style.display = 'block';
        price.style.display = 'none';
        calcTab.className = 'tab active';
        priceTab.className = 'tab';
    }
}

// -------------------------------------------------- программный доступ

window.WebEstimate = {
    state: state,
    units: UNITS,
    totals: totals,
    groupTotal: groupTotal,
    groups: orderedGroups,
    chosenRows: chosenRows,
    keyOf: key,
    money: money,
    number: number,
    parseNumber: parseNumber,
    matches: matches,
    buildText: buildEstimateText,
    buildHtml: buildEstimateHtml,
    buildDocx: buildDocxParts,
    zipStore: zipStore,
    exportDocx: exportDocx,
    exportData: exportData,
    buildExchange: buildExchange,
    applyExchange: applyExchange,
    findTemplates: findTemplates,
    findTemplate: findTemplate,
    saveTemplate: saveTemplate,
    applyTemplate: applyTemplate,
    deleteTemplate: deleteTemplate,
    markAll: markAll,
    setQuantity: setQuantity,
    setPrice: setPrice,
    togglePick: togglePick,
    findByKey: findByKey,
    save: save,
    load: load,
    render: render,
    renderEditor: renderEditor,
    renderTemplateList: renderTemplateList,
    showTab: showTab,
    showPreview: showPreview,
    toggleTheme: toggleTheme,
    clearPrices: clearPrices,
    toggleAllGroups: toggleAllGroups,
    connectServer: connectServer,
    disconnectServer: disconnectServer,
    serverPush: serverPush,
    applyServerData: applyServerData,
    server: server,
    setLogoFile: setLogoFile,
    removeLogo: removeLogo,
    imageSize: imageSize,
    logoBox: logoBox,
    logoForDocx: logoForDocx,
    refreshLogoControls: refreshLogoControls,
    undoClear: undoClear,
    clearWord: CLEAR_WORD,
    storageKey: STORAGE_KEY,
    exchangeFormat: EXCHANGE_FORMAT
};

// --------------------------------------------------------- запуск страницы

if (typeof document !== 'undefined' && document.addEventListener) {
    document.addEventListener('DOMContentLoaded', function () {
        load();
        bind();
        applyTheme();
        showDocumentFields();
        showTab('calc');
        renderTemplateList();
        refreshLogoControls();
        render();
    });
}
