-- Build-time projection of upstream artwork metadata. No game code is changed.
local json=require('dkjson')
local tree=dofile(arg[1]..'/tree.lua')
local out,assets={},{}
local function artName(name)
 return name:match("[Ff]rame")or name:match("[Bb]ackground")or name:match("^BG")or name:match("^Classes")or name:match("[Ss]ocket")or name:match("[Cc]enter")or name:match("AscendancyMiddle")or name:match("Jewel")or name:match("Charm")
end
local function base(path)return path:gsub('%?.*$',''):gsub('^.*[/\\]','')end
local sprites=tree.skillSprites or tree.sprites
if not sprites then
  local file=io.open(arg[1]..'/sprites.lua','r')
  if file then file:close();local data=dofile(arg[1]..'/sprites.lua');sprites=data.skillSprites or data.sprites end
end
local ascendancyNames={}
for _,node in pairs(tree.nodes or {})do if type(node)=='table'and node.ascendancyName then ascendancyNames[node.ascendancyName:lower()]=node.ascendancyName end end
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
        if artName(name)then
          local prefix=ascendancyNames[kind:gsub('Bloodline$',''):lower()]or(kind:sub(1,1):upper()..kind:sub(2):gsub('Bloodline$',''))
          if kind:match('Bloodline$')and prefix and name:match('^Ascendancy')then assets[prefix..name]=item
          elseif not kind:match('Bloodline$')or name:match('^Classes')then assets[name]=item end
        end
      end
    end
end
for file,entries in pairs(tree.ddsCoords or {})do
  if not file:match('^skills%-disabled')then
    for name,coords in pairs(entries)do
      out[name]=type(coords)=='table' and {file=file,x=coords[1],y=coords[2],w=coords[3],h=coords[4],layer=coords[5]}
        or {file=file,layer=coords}
      if file:match('^jewel%-sockets')then assets[name]=out[name]end
    end
  end
end
-- Standalone upstream assets supplement modern sprite sheets. PoE1 keeps these
-- in shared TreeData; PoE2 uses version-local assets and DDS/sprite coordinates.
local standalone=tree.assets
if not standalone then
  local file=io.open(arg[1]..'/../3_19/Assets.lua','r')
  if file then file:close();standalone=dofile(arg[1]..'/../3_19/Assets.lua').assets end
end
for name,entry in pairs(standalone or {})do
 if artName(name)then
  local shared=true
  local filename=name..'.png'
  local file=io.open(arg[1]..'/../'..filename,'rb')
  if not file then
   shared=false;filename=base(entry[1]or entry[0.3835])
   file=io.open(arg[1]..'/'..filename,'rb')
  end
  if file then file:close();assets[name]={file=filename,shared=shared,whole=true}end
 end
end
for name,entry in pairs(out)do if artName(name)and not assets[name]then assets[name]=entry end end
for filename,entries in pairs(tree.spriteCoords or {})do
 for name,rect in pairs(entries)do
  if artName(name)then assets[name]={file=filename,x=rect.x,y=rect.y,w=rect.w,h=rect.h}end
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
    if kind=='treeAssets'then
      for _,sheet in ipairs(data)do for name,rect in pairs(sheet.coords or {})do
        if artName(name)then assets[name]={file='legion/'..base(sheet.filename),shared=true,x=rect.x,y=rect.y,w=rect.w,h=rect.h}end
      end end
    elseif not kind:lower():match('inactive')then
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
for name in pairs(assets)do if used[name]or decorations[name]then assets[name]=nil end end
assets.ShadedOuterRing={file='ShadedOuterRing.png',source='assets',whole=true}
assets.ShadedInnerRing={file='ShadedInnerRing.png',source='assets',whole=true}
io.write(json.encode({nodes=used,decorations=decorations,assets=assets}))
