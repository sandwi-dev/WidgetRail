// Development-only renderer. The app ships the generated assets, not Node or resvg.
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const { Resvg } = require('@resvg/resvg-js');

const root = path.resolve(__dirname, '../..');
const branding = path.join(root, 'assets/branding');
const assets = path.join(root, 'src/OverlayFrontend.WinUI/Assets');
const source = fs.readFileSync(path.join(branding, 'widgetrail.svg'), 'utf8');
const svg = Buffer.from(source).toString('base64');

function render(width, height, logoSize) {
    // Render each size directly from the vector, with centered transparent padding.
    const canvas = `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}"><image x="${(width - logoSize) / 2}" y="${(height - logoSize) / 2}" width="${logoSize}" height="${logoSize}" href="data:image/svg+xml;base64,${svg}"/></svg>`;
    return new Resvg(canvas, { font: { loadSystemFonts: false } }).render().asPng();
}

const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
const images = sizes.map(size => render(size, size, size));
const directory = Buffer.alloc(6 + sizes.length * 16);
directory.writeUInt16LE(1, 2); // Windows icon, not cursor.
directory.writeUInt16LE(sizes.length, 4);
let offset = directory.length;
sizes.forEach((size, index) => {
    const entry = 6 + index * 16;
    directory[entry] = directory[entry + 1] = size === 256 ? 0 : size;
    directory.writeUInt16LE(1, entry + 4);
    directory.writeUInt16LE(32, entry + 6);
    directory.writeUInt32LE(images[index].length, entry + 8);
    directory.writeUInt32LE(offset, entry + 12);
    offset += images[index].length;
});

// Prepare every image before replacing any checked-in output.
const packages = [
    ['StoreLogo.png', 50, 50, 50],
    ['LockScreenLogo.scale-200.png', 48, 48, 48],
    ['Square44x44Logo.scale-200.png', 88, 88, 88],
    ['Square44x44Logo.targetsize-24_altform-unplated.png', 24, 24, 24],
    ['Square44x44Logo.targetsize-48_altform-lightunplated.png', 48, 48, 48],
    ['Square150x150Logo.scale-200.png', 300, 300, 240],
    ['Wide310x150Logo.scale-200.png', 620, 300, 240],
    ['SplashScreen.scale-200.png', 1240, 600, 256]
].map(([name, width, height, size]) => [path.join(assets, name), render(width, height, size)]);

const outputs = [
    [path.join(branding, 'widgetrail.png'), images.at(-1)],
    [path.join(branding, 'widgetrail.ico'), Buffer.concat([directory, ...images])],
    ...packages
];
for (const [file, bytes] of outputs) fs.writeFileSync(file, bytes);
console.log(`Generated PNG, ICO (${sizes.join(', ')} px) and ${packages.length} package logos from widgetrail.svg.`);
