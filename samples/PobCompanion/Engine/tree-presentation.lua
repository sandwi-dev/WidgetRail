-- Presentation facts from the pinned engine. Calculation/allocation state stays
-- separate: decorative art, proxy anchors and item grants are not spent passives.
return function(build,node)
  local viewer=build.treeTab.viewer
  local visible=node.group~=nil and not node.isProxy and not node.group.isProxy
  if visible and node.unlockConstraint then
    visible=viewer.checkUnlockConstraints~=nil and viewer:checkUnlockConstraints(build,node)or false
  end
  local decoration=node.type=='OnlyImage'or(node.type=='Mastery'and not node.masteryEffects)
  local start=node.type=='ClassStart'or node.type=='AscendClassStart'
  local granted=not not build.calcsTab.mainEnv.grantedPassives[node.id]
  local icon=node.icon or ''
  if decoration then icon=node.activeEffectImage or icon
  elseif node.type=='Mastery'then
    local override=build.spec.hashOverrides and build.spec.hashOverrides[node.id]
    icon=override and override.icon or((node.alloc or granted)and node.activeIcon or node.inactiveIcon)or icon
  end
  local jewel,jewelItem
  if node.type=='Socket'or node.containJewelSocket then
    local _,item=build.itemsTab:GetSocketAndJewelForNodeID(node.id)
    if (node.alloc or granted)and item then jewelItem=item;jewel=item.title and item.title~=''and item.title or item.baseName end
  end
  local tree=build.spec.tree
  local active=node.alloc or granted
  local state=active and 'alloc'or 'unalloc'
  local frame,socketArt
  if node.type=='ClassStart'then
    frame=not tree.scaleImage and(active and node.startArt or 'PSStartNodeBackgroundInactive')or nil
  elseif node.type=='AscendClassStart'then
    frame='AscendancyMiddle'
    local prefix=tree.bloodlineSpritePrefixes and tree.bloodlineSpritePrefixes[node.ascendancyName]
    if prefix then frame=prefix..frame
    elseif tree.secondaryAscendNameMap and tree.secondaryAscendNameMap[node.ascendancyName]then frame='Azmeri'..frame end
  elseif node.overlay and not decoration and node.type~='Mastery'then
    local suffix=not tree.scaleImage and(node.type=='Socket'and node.expansionJewel and 'Alt'or node.ascendancyName and 'Ascend'or node.isBlighted and 'Blighted'or '')or ''
    frame=node.overlay[state..suffix]or node.overlay[state]
    local conqueror=node.conqueredBy and node.conqueredBy.conqueror
    if not tree.scaleImage and conqueror and conqueror.type and conqueror.type:match('^abyss_')and(node.type=='Notable'or node.type=='Keystone')then
      local alternate='Abyss'..(node.ascendancyName and 'Ascendancy'or node.type)..'Frame'..(active and 'Allocated'or 'Unallocated')
      if tree.assets[alternate]then frame=alternate end
    end
    if frame and not tree.scaleImage then
      local prefix=node.bloodlineOverlayPrefix or(tree.bloodlineSpritePrefixes and tree.bloodlineSpritePrefixes[node.ascendancyName])
      if prefix then frame=prefix..frame end
      if node.name=='Charm Socket'then frame='Azmeri'..frame end
    end
  end
  if jewelItem then
    if tree.scaleImage then
      local title=jewelItem.title
      local unique=jewelItem.rarity=='UNIQUE'and title and((tree.ddsMap or {})[title]or(tree.assets or {})[title]or(tree.spriteMap or {})[title])
      socketArt=unique and title or jewelItem.baseName
    elseif viewer.GetJewelSocketOverlay then socketArt=viewer:GetJewelSocketOverlay(jewelItem,node.expansionJewel)end
  end
  local radii=setmetatable({},{__jsontype='array'})
  local rad=jewelItem and build.data.jewelRadius[jewelItem.jewelRadiusIndex]
  if rad and node.name~='Charm Socket'and(not node.expansionJewel or node.expansionJewel.size==2)then
    local factor=tree.scaleImage and(build.data.gameConstants.PassiveTreeJewelDistanceMultiplier or 1)or 1
    local jewelData=jewelItem.jewelData or {}
    local centers=jewelData.fromNothingKeystones or jewelData.impossibleEscapeKeystones
    local function add(x,y,inner)radii[#radii+1]={x=x,y=y,outer=rad.outer*factor,inner=inner or rad.inner*factor}end
    if centers then
      local names={};for name in pairs(centers)do names[#names+1]=name end;table.sort(names)
      for _,name in ipairs(names)do local key=tree.keystoneMap[name];if key and key.x and key.y then add(key.x,key.y,150)end end
    elseif not(jewelData.conqueredBy and jewelData.conqueredBy.conqueror and jewelData.conqueredBy.conqueror.type and jewelData.conqueredBy.conqueror.type:match('^abyss_')and not tree.scaleImage)then add(node.x,node.y)end
  end
  local secondary=build.spec.curSecondaryAscendClass
  return {visible=not not visible,interactive=visible and not decoration and not start,
    decoration=decoration,landmark=start,granted=granted,icon=icon,
    radius=decoration and(node.targetSize and node.targetSize.width and node.targetSize.width/2 or 190*(build.spec.tree.scaleImage or 1))or(node.size or 50),
    art={frame=frame or '',socket=socketArt or '',radii=radii,scale=tree.scaleImage and 1 or 1.33},
    socketedItem=jewel or '',
    dimmed=node.ascendancyName~=nil and node.ascendancyName~=build.spec.curAscendClassBaseName and
      not(secondary and node.ascendancyName==secondary.id)}
end
