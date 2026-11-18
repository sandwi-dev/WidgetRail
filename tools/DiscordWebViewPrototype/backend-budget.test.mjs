import assert from 'node:assert/strict';
import test from 'node:test';
import {maximumBackendBytes,minifySource,validateBackendSource} from './Build-Backend.mjs';

test('backend resource budget measures UTF-8 bytes and rejects overflow before writing',()=>{
    assert.equal(maximumBackendBytes,128*1024);
    assert.equal(validateBackendSource('x'.repeat(maximumBackendBytes)),maximumBackendBytes);
    assert.equal(validateBackendSource('é'.repeat(maximumBackendBytes/2)),maximumBackendBytes);
    assert.throws(()=>validateBackendSource('é'.repeat(maximumBackendBytes/2)+'x'),/byte bound/);
    assert.throws(()=>validateBackendSource(''),/byte bound/);
});
test('minification retains executable semantics without property mangling',async()=>{
    const output=await minifySource('globalThis.fixture={publicMethod:function retainedName(value){return value+1}};');
    assert.match(output,/publicMethod/);assert.match(output,/retainedName/);validateBackendSource(output);
    const target={};new Function('globalThis',output)(target);assert.equal(target.fixture.publicMethod(3),4);
});
