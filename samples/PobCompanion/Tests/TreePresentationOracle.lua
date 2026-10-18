-- Compare the adapter's presentation facts with pinned desktop tree metadata.
-- Run separately from bridge.lua so its snapshot cannot define the expectation.
local stdout=io.stdout
local inputPath,importedPath=arg[2],arg[3]
print=function(...)io.stderr:write(tostring((...))..'\n')end
local json=require('dkjson')
local presentation=dofile(arg[1])
dofile('HeadlessWrapper.lua')
local file=assert(io.open(inputPath,'rb'));local xml=file:read('*a');file:close()
loadBuildFromXML(xml,'Tree audit');runCallback('OnFrame')
local counts={nodes=0,decorations=0,hidden=0,masteries=0,sockets=0,granted=0,proxies=0}
local function check(node)
  local p=presentation(build,node)
  local visible=node.group~=nil and not node.isProxy and not node.group.isProxy
  if visible and node.unlockConstraint then
    visible=build.treeTab.viewer:checkUnlockConstraints(build,node)
  end
  assert(p.visible==not not visible,'Visibility '..node.id)
  local decorative=node.type=='OnlyImage'or(node.type=='Mastery'and not node.masteryEffects)
  assert(p.decoration==decorative,'Decoration '..node.id)
  assert(p.interactive==not not(visible and not decorative and node.type~='ClassStart'and node.type~='AscendClassStart'),'Hit target '..node.id)
  if decorative then assert(p.icon==(node.activeEffectImage or node.icon or ''),'Decoration artwork '..node.id)end
  if node.type=='Mastery'and node.masteryEffects and not build.spec.hashOverrides[node.id]then
    assert(p.icon==((node.alloc and node.activeIcon or node.inactiveIcon)or node.icon or ''),'Mastery state '..node.id)
  end
  assert(type(p.art)=='table'and type(p.art.radii)=='table','Artwork state '..node.id)
  if node.overlay and node.type~='Mastery'and not decorative and node.type~='ClassStart'and node.type~='AscendClassStart'then assert(p.art.frame~='','Frame '..node.id)end
  return p
end
for _,node in pairs(build.spec.nodes)do
  if node.x and node.y then
    local p=check(node);counts.nodes=counts.nodes+1
    if p.decoration then counts.decorations=counts.decorations+1 end
    if not p.visible then counts.hidden=counts.hidden+1 end
    if node.type=='Mastery'and node.masteryEffects then counts.masteries=counts.masteries+1 end
    if p.socketedItem~=''then counts.sockets=counts.sockets+1 end
    if p.granted then counts.granted=counts.granted+1 end
    if node.isProxy or(node.group and node.group.isProxy)then counts.proxies=counts.proxies+1 end
  end
end
-- Conditional PoE2 cluster: visibility follows upstream in both directions.
local hidden=build.spec.nodes[29663]
if hidden and hidden.type=='OnlyImage'and hidden.unlockConstraint then
  local unlock=build.spec.nodes[5571];local old=unlock.alloc
  unlock.alloc=false;assert(not check(hidden).visible,'Unseen Path cluster hidden before unlock')
  unlock.alloc=true;assert(check(hidden).visible and not check(hidden).interactive,'Unlocked artwork stays decorative')
  unlock.alloc=old;counts.unlockTransition=true
end
-- Exercise grant and occupied/empty socket presentation without changing a saved build.
local normal,socket,mastery
for _,node in pairs(build.spec.nodes)do
  if node.type=='Normal'and node.group then normal=node end
  if node.type=='Socket'and node.group then socket=node end
  if node.type=='Mastery'and node.masteryEffects then mastery=node end
end
assert(normal and socket)
local grants=build.calcsTab.mainEnv.grantedPassives
local old=grants[normal.id];grants[normal.id]=true;assert(check(normal).granted);grants[normal.id]=old
local getter=build.itemsTab.GetSocketAndJewelForNodeID
local allocated=socket.alloc;socket.alloc=true
build.itemsTab.GetSocketAndJewelForNodeID=function()return {},{title='Audit jewel',baseName='Sapphire'}end
assert(check(socket).socketedItem=='Audit jewel')
build.itemsTab.GetSocketAndJewelForNodeID=function()return {},{title='',baseName='Sapphire'}end
assert(check(socket).socketedItem=='Sapphire')
build.itemsTab.GetSocketAndJewelForNodeID=function()return {},nil end
assert(check(socket).socketedItem=='')
build.itemsTab.GetSocketAndJewelForNodeID=getter;socket.alloc=allocated
if mastery then
  local old=mastery.alloc;mastery.alloc=false;check(mastery);mastery.alloc=true;check(mastery);mastery.alloc=old
end
counts.stateTransitions=true
for _,node in pairs(build.spec.tree.nodes)do
  if node.isProxy or(node.group and node.group.isProxy)then
    local p=presentation(build,node);assert(not p.visible and not p.interactive,'Proxy must not render')
    counts.proxies=counts.proxies+1
  end
end
if importedPath then
  local file=assert(io.open(importedPath,'rb'));local oldXml=file:read('*a');file:close()
  loadBuildFromXML(oldXml,'Imported tree audit');runCallback('OnFrame')
  local count=0
  for _,node in pairs(build.spec.nodes)do
    if node.x and node.y then check(node);count=count+1 end
  end
  counts.importedNodes=count
end
stdout:write(json.encode(counts)..'\n')
