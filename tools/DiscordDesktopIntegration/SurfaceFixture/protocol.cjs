'use strict';
const maxMessageBytes = 4096;

function validateRequest(value, token) {
    if (!value || typeof value !== 'object' || Array.isArray(value) ||
        value.token !== token || !Number.isSafeInteger(value.id) || value.id < 1 ||
        !['status', 'borrow', 'prepareReturn', 'restore', 'close'].includes(value.command))
        throw new Error('invalid_request');
    const keys = value.command === 'borrow' ? ['id', 'token', 'command', 'bounds'] :
        value.command === 'prepareReturn' ? ['id', 'token', 'command', 'processId'] : ['id', 'token', 'command'];
    if (Object.keys(value).length !== keys.length || keys.some(k => !Object.hasOwn(value, k)))
        throw new Error('invalid_request');
    if (value.command === 'prepareReturn' && (!Number.isInteger(value.processId) || value.processId <= 0 || value.processId > 0x7fffffff))
        throw new Error('invalid_process');
    if (value.command === 'borrow') {
        const b = value.bounds;
        if (!b || typeof b !== 'object' || Object.keys(b).length !== 4 ||
            !['x', 'y', 'width', 'height'].every(k => Number.isInteger(b[k])) ||
            Math.abs(b.x) > 32768 || Math.abs(b.y) > 32768 ||
            b.width < 80 || b.height < 80 || b.width > 8192 || b.height > 8192)
            throw new Error('invalid_bounds');
    }
    return value;
}

// Fragmented/coalesced pipe messages are bounded before JSON parsing. The fixture
// never accepts code, URLs, native handles, media sources or arbitrary file paths.
function createReader(onMessage) {
    let buffer = Buffer.alloc(0);
    return chunk => {
        buffer = Buffer.concat([buffer, chunk]);
        while (true) {
            const end = buffer.indexOf(10);
            if (end < 0) break;
            if (end > maxMessageBytes) throw new Error('oversized_request');
            const line = buffer.subarray(0, end);
            buffer = buffer.subarray(end + 1);
            onMessage(JSON.parse(line.toString('utf8')));
        }
        if (buffer.length > maxMessageBytes) throw new Error('oversized_request');
    };
}
module.exports = { maxMessageBytes, validateRequest, createReader };
