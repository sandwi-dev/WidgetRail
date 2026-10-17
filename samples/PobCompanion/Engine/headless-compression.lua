-- The upstream headless wrapper leaves Inflate empty. Use its pinned x64 zlib.
-- The library is bundled with the engine; never resolve an arbitrary PATH DLL.
local ffi=require('ffi')
ffi.cdef[[int uncompress(unsigned char *dest, unsigned long *destLen, const unsigned char *source, unsigned long sourceLen);]]
local zlib
function Inflate(input)
  assert(type(input)=='string'and #input<=192*1024*1024,'Compressed jewel data exceeds its size limit')
  zlib=zlib or ffi.load('../runtime/zlib1.dll')
  local capacity=4*1024*1024
  while capacity<=512*1024*1024 do
    local buffer=ffi.new('unsigned char[?]',capacity)
    local length=ffi.new('unsigned long[1]',capacity)
    local status=zlib.uncompress(buffer,length,input,#input)
    if status==0 then return ffi.string(buffer,tonumber(length[0]))end
    assert(status==-5,'Could not decompress upstream jewel data')
    buffer=nil;collectgarbage('collect');capacity=capacity*2
  end
  error('Uncompressed jewel data exceeds its size limit')
end
