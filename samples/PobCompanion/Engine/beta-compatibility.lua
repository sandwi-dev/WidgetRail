-- PoE 1 beta 274d129 and PoE 2 beta 84a11f1 use explicit constructors and parent proxies with
-- _object/_parent fields. Upstream copyTableSafe installs __newindex before
-- copying, which writes into the source object and can grow attribute-node
-- graphs on subsequent loads. Keep class metadata shared and attach metatables
-- only after copying raw fields. No calculation rules are changed.
local compatibility=dofile((debug.getinfo(1,'S').source:sub(2):match('^(.*[/\\])')or '')..'engine-compatibility.lua')
if not compatibility.hasExplicitConstructors() then return end
local upstreamCopy=copyTableSafe
copyTableSafe=function(value,noRecurse,preserveMeta)
  if noRecurse or not preserveMeta then return upstreamCopy(value,noRecurse,preserveMeta) end
  local seen={}
  local function clone(source)
    if seen[source] then return seen[source] end
    local result={};seen[source]=result
    local meta=getmetatable(source)
    local proxy=meta==source and rawget(source,'_object') and rawget(source,'_parent')
    for key,item in pairs(source)do
      result[key]=type(item)=='table' and not(proxy and key=='_parent') and clone(item) or item
    end
    return setmetatable(result,meta==source and result or meta)
  end
  return clone(value)
end
