local stdout=io.stdout
print=function(...)io.stderr:write(tostring((...))..'\n')end
local json=require('dkjson')
local presentation=dofile(arg[1]..'/tree-presentation.lua')
local backgrounds=dofile(arg[1]..'/tree-artwork.lua')
local geometry=dofile(arg[1]..'/tree-geometry.lua')
dofile('HeadlessWrapper.lua')
local function read(file)local f=assert(io.open(file,'rb'));local s=f:read('*a');f:close();return s end
if arg[4]then loadBuildFromXML(read(arg[4]),'Artwork audit')else newBuild()end
runCallback('OnFrame')
local atlas=json.decode(read(arg[2]..'/'..build.spec.tree.treeVersion..'/index.json'))
local missing,count={},0
for _,node in pairs(build.spec.nodes)do
 if node.x and node.y then
  local p=presentation(build,node)
  if p.visible then
   for _,key in ipairs({'frame','socket'})do local image=p.art[key];if image~=''then count=count+1;if not atlas.layers.sprites[image]then missing[image]=true end end end
  end
 end
end
local bg=backgrounds(build)
for _,entry in ipairs(bg)do if not atlas.layers.sprites[entry.image]then missing[entry.image]=true end end
local missingNames={};for name in pairs(missing)do missingNames[#missingNames+1]=name end;table.sort(missingNames)
if arg[3]then
 local nodes={}
 for _,n in pairs(build.spec.nodes)do if n.x and n.y then local p=presentation(build,n);if p.visible then nodes[#nodes+1]={id=n.id,x=n.x,y=n.y,name=n.dn,kind=n.type,icon=p.icon,radius=p.radius,interactive=p.interactive,decoration=p.decoration,landmark=p.landmark,dimmed=p.dimmed,allocated=not not n.alloc,ascendancy=not not n.ascendancyName,ascendancyName=n.ascendancyName or '',art=p.art}end end end
 table.sort(nodes,function(a,b)return a.id<b.id end)
 local f=assert(io.open(arg[3],'wb'));f:write(json.encode({nodes=nodes,links=geometry(build.spec),backgrounds=bg,treeVersion=build.spec.tree.treeVersion}));f:close()
end
stdout:write(json.encode({version=build.spec.tree.treeVersion,frames=count,backgrounds=#bg,missing=missingNames})..'\n')
assert(#missingNames==0,'Missing upstream artwork')
