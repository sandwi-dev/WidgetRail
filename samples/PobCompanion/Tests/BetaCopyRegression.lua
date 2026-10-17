-- Run from the pinned engine src directory with the adapter's shim path.
local output=io.stdout
local shim=arg[1]
print=function()end
dofile('HeadlessWrapper.lua')
dofile(shim)
local source=new('ModList'):ModList()
local parent=rawget(source.ModStore,'_parent')
for _=1,100 do
  local copy=copyTableSafe(source,false,true)
  assert(rawget(source,'_object')==nil,'Copy mutated the source via __newindex')
  assert(copy.Object==copy and copy~=source,'Clone lost its self reference')
  assert(rawget(copy.ModStore,'_object')==copy,'Cloned parent proxy points to source')
  assert(rawget(copy.ModStore,'_parent')==parent,'Class identity must remain shared')
  assert(getmetatable(copy.ModStore)==copy.ModStore,'Proxy metatable must follow its clone')
  copy.ModStore.fixture=7
  assert(copy.fixture==7 and source.fixture==nil,'Parent writes must target only the cloned object')
end
output:write('PASS 100 deep copies preserve source, class identity and proxy ownership\n')
