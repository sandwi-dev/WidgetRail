-- Artwork layout mirrors the pinned PassiveTreeView presentation rules only.
-- Allocation and jewel effects remain entirely owned by the upstream engine.
return function(build)
 local spec,tree=build.spec,build.spec.tree
 local result=setmetatable({},{__jsontype='array'})
 local factor=tree.scaleImage or 1
 local artScale=tree.scaleImage and 1 or 1.33
 local function add(image,x,y,width,height,dimmed,half,rotation)
  if image and x and y then result[#result+1]={image=image,x=x,y=y,width=width,height=height,scale=artScale,dimmed=not not dimmed,half=not not half,rotation=rotation or 0}end
 end
 local class=tree.classes[spec.curClassId]
 if class and class.background then
  local bg=class.background
  local asc=class.classes[spec.curAscendClassId]
  add(asc and asc.background and asc.background.image or bg.image,bg.x*factor,bg.y*factor,bg.width,bg.height)
  local start=spec.nodes[class.startNodeId]
  if bg.active and start then add('BGTreeActive',bg.x*factor,bg.y*factor,bg.active.width,bg.active.height,false,false,math.pi/2+math.atan2(start.y-bg.y*factor,start.x-bg.x*factor))end
  if bg.bg then add('BGTree',bg.x*factor,bg.y*factor,bg.bg.width,bg.bg.height)end
 elseif not tree.scaleImage then
  -- Desktop PoE1 uses these positions: older upstream data has no class anchor.
  local anchors={{'BackgroundStr',-2750,1600},{'BackgroundDex',2550,1600},{'BackgroundInt',-250,-2200},{'BackgroundStrDex',-150,2350},{'BackgroundStrInt',-2100,-1500},{'BackgroundDexInt',2350,-1950}}
  local bg=anchors[spec.curClassId];if bg then add(bg[1],bg[2],bg[3])end
 end
 if tree.scaleImage then
  local names={};for name in pairs(tree.ascendNameMap or {})do names[#names+1]=name end;table.sort(names)
  for _,name in ipairs(names)do
   local asc=tree.ascendNameMap[name].ascendClass
   local bg=asc.background
   if bg and not(asc.replaceBy and asc.replaceBy==spec.curAscendClassBaseName)and not(asc.replace and name~=spec.curAscendClassBaseName)then
    add(bg.image,bg.x*factor,bg.y*factor,bg.width,bg.height,name~=spec.curAscendClassBaseName)
   end
  end
 end
 local function groups(list,expansion)
  local ids={};for id in pairs(list or {})do ids[#ids+1]=id end;table.sort(ids)
  for _,id in ipairs(ids)do local group=list[id]
   if not group.isProxy then
    local bg=group.background
    if tree.scaleImage and bg then
     local x,y=group.x*factor,group.y*factor
     if bg.offsetX and bg.offsetY then x=group.x+bg.offsetX;y=group.y+bg.offsetY end
     add(bg.image,x,y,nil,nil,false,bg.isHalfImage~=nil)
    elseif not tree.scaleImage then
     if group.ascendancyName then
      if group.isAscendancyStart then add('Classes'..group.ascendancyName,group.x,group.y,nil,nil,group.ascendancyName~=spec.curAscendClassBaseName and not(spec.curSecondaryAscendClass and group.ascendancyName==spec.curSecondaryAscendClass.id))end
     else
      local oo=group.oo or {}
      if oo[3]then add(expansion and 'GroupBackgroundLargeHalfAlt'or 'PSGroupBackground3',group.x,group.y,nil,nil,false,true)
      elseif oo[2]then add(expansion and 'GroupBackgroundMediumAlt'or 'PSGroupBackground2',group.x,group.y)
      elseif oo[1]then add(expansion and 'GroupBackgroundSmallAlt'or 'PSGroupBackground1',group.x,group.y)end
     end
    end
   end
  end
 end
 groups(tree.groups)
 for _,graph in pairs(spec.subGraphs or {})do groups(graph.groups,true)end
 return result
end
