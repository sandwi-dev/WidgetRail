-- Reuse upstream's current trade converter locally. Never process its network queue.
local json=require('dkjson')
local M={}
local function assert(value,message)if not value then error(message,0)end;return value end
function M.convert(entry)
 assert(type(entry)=='table' and type(entry.item)=='table' and type(entry.listing)=='table','Invalid trade item')
 assert(not entry.item.veiledMods or #entry.item.veiledMods==0,'This item has unrevealed modifiers. Compare it after they are revealed.')
 local requests=new('TradeQueryRequests')
 if requests.TradeQueryRequests then requests:TradeQueryRequests()end
 local raw
 requests:FetchResultBlock('https://example.invalid/not-requested',function(items,err)
  assert(not err,err);assert(items and #items==1,'Trade conversion did not return one item');raw=items[1].item_string
 end)
 assert(requests.requestQueue.fetch[1],'Upstream trade converter changed')
 -- The converter expects price/account metadata even though this operation only returns an item.
 local copy=copyTable(entry,true)
 copy.listing.price=copy.listing.price or {amount=0,currency='unknown'}
 copy.listing.account=copy.listing.account or {name='unknown'}
 requests.requestQueue.fetch[1].callback(json.encode({result={copy}}))
 assert(type(raw)=='string' and #raw<=32768,'Trade item is too large')
 for group,mods in pairs(entry.item)do
  if type(group)=='string' and group:match('Mods$') and group~='pseudoMods' and type(mods)=='table'then
   for _,mod in ipairs(mods)do
    local line=type(mod)=='table' and mod.description or mod
    assert(type(line)=='string','Unsupported trade modifier format')
    line=escapeGGGString and escapeGGGString(line)or line
    assert(raw:find(line,1,true),'Upstream conversion omitted a modifier: '..line)
   end
  end
 end
 local item=new('Item');if item.Item then item:Item(raw)else item=new('Item',raw)end
 assert(item.base,'PoB does not recognize this item base')
 item:BuildModList()
 for _,group in ipairs({'implicitModLines','explicitModLines','enchantModLines','runeModLines'})do
  for _,line in ipairs(item[group]or{})do
   assert(not line.extra and line.modList and #line.modList>0,'PoB cannot calculate modifier: '..line.line)
  end
 end
 return raw
end
return M
