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
for kind,data in pairs(sprites or {})do
  if not kind:lower():match('inactive') and not kind:lower():match('disabled') then
    local sheet=data.filename and data or data[0.3835] or data[#data] or data[1]
    if sheet and sheet.filename and sheet.coords then
      for name,rect in pairs(sheet.coords)do
        local function last(v)return type(v)=='table' and v[#v] or v end
        local item={file=base(sheet.filename),x=last(rect.x),y=last(rect.y),w=last(rect.w),h=last(rect.h)}
        if not out[name] or item.w>(out[name].w or 0)then out[name]=item end
      end
    end
  end
end
for file,entries in pairs(tree.ddsCoords or {})do
  if file:match('^skills_')then
    for name,layer in pairs(entries)do out[name]={file=file,layer=layer}end
  end
end
local used={}
for _,node in pairs(tree.nodes or {})do
  if type(node)=='table' and node.icon and out[node.icon]then used[node.icon]=out[node.icon]end
end
io.write(json.encode(used))
