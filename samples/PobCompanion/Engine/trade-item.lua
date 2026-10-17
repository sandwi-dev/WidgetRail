-- Reuse upstream's richer item importer in a detached destination. Its trade-text
-- converter omits socket groups, influence and some new defence/skill metadata.
local M={}
local function assert(value,message)if not value then error(message,0)end;return value end
function M.convert(entry)
 assert(type(entry)=='table' and type(entry.item)=='table' and type(entry.listing)=='table','Invalid trade item')
 assert(not entry.item.veiledMods or #entry.item.veiledMods==0,'This item has unrevealed modifiers. Compare it after they are revealed.')
 assert(entry.item.identified~=false,'Identify this item before comparing its modifiers.')
 local input=copyTable(entry.item,true)
 input.frameType=input.frameType or ({Normal=0,Magic=1,Rare=2,Unique=3})[input.rarity]
 input.ilvl=input.ilvl or 0;input.id=input.id or entry.id
 -- Trade JSON now wraps descriptions and flags; these importer groups still
 -- accept the older string representation. Explicit/implicit flags stay intact.
 for _,group in ipairs({'enchantMods','runeMods','scourgeMods','crucibleMods','craftedMods','fracturedMods','mutatedMods'})do
  for i,mod in ipairs(input[group]or{})do input[group][i]=escapeGGGString(type(mod)=='table' and mod.description or mod)end
 end
 for _,group in ipairs({'implicitMods','explicitMods'})do
  for i,mod in ipairs(input[group]or{})do
   if type(mod)=='table'then mod.description=escapeGGGString(mod.description)else input[group][i]=escapeGGGString(mod)end
  end
 end
 for _,group in ipairs({'properties','requirements','grantedSkills'})do
  for _,property in ipairs(input[group]or{})do property.name=escapeGGGString(property.name)end
 end
 local item
 local destination={items={},slots={Trade={SetSelItemId=function()end}},AddItem=function(_,value)item=value end}
 local context={build={data=build.data,itemsTab=destination}}
 local poe2=new('Item').Item~=nil
 context.ImportItem=function()error('This listing contains socketed jewels. Compare the equipment and jewels separately.',0)end
 context.ImportSocketedItems=function(self,value,contents,slot,sockets)
  if poe2 then return build.importTab.ImportSocketedItems(self,value,contents,slot,sockets)end
  for _,socketed in ipairs(contents)do assert(not socketed.abyssJewel,'This listing contains socketed jewels. Compare the equipment and jewels separately.')end
  -- Equipment comparison keeps the build's existing skill groups. Seller gems
  -- must not replace them; socket colours and links are imported by upstream.
 end
 build.importTab.ImportItem(context,input,'Trade')
 assert(item and item.base,'PoB does not recognize this item base')
 local raw=item.raw
 assert(type(raw)=='string' and #raw<=32768,'Trade item is too large')
 local available={}
 for line in raw:gmatch('[^\r\n]+')do
  while line:match('^{[^}]+}')do line=line:gsub('^{[^}]+}','',1)end
  available[line]=(available[line]or 0)+1
 end
 for group,mods in pairs(entry.item)do
  if type(group)=='string' and group:match('Mods$') and group~='pseudoMods' and type(mods)=='table'then
   for _,mod in ipairs(mods)do
    local line=type(mod)=='table' and mod.description or mod
    assert(type(line)=='string','Unsupported trade modifier format')
    line=escapeGGGString and escapeGGGString(line)or line
    for part in line:gmatch('[^\r\n]+')do
     assert((available[part]or 0)>0,'Upstream conversion omitted a modifier: '..part)
     available[part]=available[part]-1
    end
   end
  end
 end
 item:BuildModList()
 for _,group in ipairs({'implicitModLines','explicitModLines','enchantModLines','runeModLines','scourgeModLines','crucibleModLines'})do
  for _,line in ipairs(item[group]or{})do
   assert(not line.extra and line.modList and #line.modList>0,'PoB cannot calculate modifier: '..line.line)
  end
 end
 return raw
end
return M
