-- Build-time projection of upstream artwork metadata. No game code is changed.
local json=require('dkjson')
local tree=dofile(arg[1]..'/tree.lua')
local out={}
local function base(path)return path:gsub('%?.*$',''):gsub('^.*[/\\]','')end
local sprites=tree.skillSprites or tree.sprites
if not sprites then
  local file=io.open(arg[1]..'/sprites.lua','r')
  if file then file:close();local data=dofile(arg[1]..'/sprites.lua');sprites=data.skillSprites or data.sprites end
end
local kinds={};for kind in pairs(sprites or {})do kinds[#kinds+1]=kind end
table.sort(kinds,function(a,b)local ai=a:lower():match('inactive')or a:lower():match('disabled');local bi=b:lower():match('inactive')or b:lower():match('disabled');if not not ai~=not not bi then return not not ai end;return a<b end)
for _,kind in ipairs(kinds)do
    local data=sprites[kind]
    local sheet=data.filename and data or data[0.3835] or data[#data] or data[1]
    if sheet and sheet.filename and sheet.coords then
      for name,rect in pairs(sheet.coords)do
        local function last(v)return type(v)=='table' and v[#v] or v end
        local item={file=base(sheet.filename),x=last(rect.x),y=last(rect.y),w=last(rect.w),h=last(rect.h)}
        if not out[name] or item.w>=(out[name].w or 0)then out[name]=item end
      end
    end
end
for file,entries in pairs(tree.ddsCoords or {})do
  if not file:match('^skills%-disabled')then
    for name,layer in pairs(entries)do out[name]={file=file,layer=layer}end
  end
end
local used,decorations={},{}
-- Cluster jewels and tattoos can supply icons absent from the base tree nodes.
local function extra(file)
  local handle=io.open(file,'r');if not handle then return end;handle:close()
  local function visit(value)
    if type(value)~='table'then return end
    for key,item in pairs(value)do
      if type(item)=='table'then visit(item)
      elseif(key=='icon'or key=='activeIcon'or key=='inactiveIcon')and out[item]then used[item]=out[item]
      elseif key=='masteryIcon'and out[item]then decorations[item]=out[item]end
    end
  end
  visit(dofile(file))
end
extra(arg[1]..'/../../Data/ClusterJewels.lua')
extra(arg[1]..'/../../Data/TattooPassives.lua')
-- Timeless jewels use the separate upstream legion sprite sheets.
local legionFile=arg[1]..'/../legion/tree-legion.lua'
local legion=io.open(legionFile,'r')
if legion then
  legion:close()
  for kind,data in pairs(dofile(legionFile))do
    if kind~='treeAssets'and not kind:lower():match('inactive')then
      local sheet=data[#data]
      for name,rect in pairs(sheet.coords or {})do
        used[name]={file='legion/'..base(sheet.filename),shared=true,x=rect.x,y=rect.y,w=rect.w,h=rect.h}
      end
    end
  end
end
for _,node in pairs(tree.nodes or {})do
  if type(node)=='table'then
    local decoration=node.isOnlyImage or((node.isMastery or node.m)and not node.masteryEffects)
    if decoration then
      local icon=node.activeEffectImage or node.icon
      if icon and out[icon]then decorations[icon]=out[icon]end
    else
      for _,key in ipairs({'icon','activeIcon','inactiveIcon'})do local icon=node[key];if icon and out[icon]then used[icon]=out[icon]end end
    end
  end
end
io.write(json.encode({nodes=used,decorations=decorations}))
