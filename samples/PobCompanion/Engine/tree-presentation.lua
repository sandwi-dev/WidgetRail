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
  local jewel
  if node.type=='Socket'or node.containJewelSocket then
    local _,item=build.itemsTab:GetSocketAndJewelForNodeID(node.id)
    if (node.alloc or granted)and item then jewel=item.title and item.title~=''and item.title or item.baseName end
  end
  local secondary=build.spec.curSecondaryAscendClass
  return {visible=not not visible,interactive=visible and not decoration and not start,
    decoration=decoration,landmark=start,granted=granted,icon=icon,
    radius=decoration and(node.targetSize and node.targetSize.width and node.targetSize.width/2 or 190*(build.spec.tree.scaleImage or 1))or(node.size or 50),
    socketedItem=jewel or '',
    dimmed=node.ascendancyName~=nil and node.ascendancyName~=build.spec.curAscendClassBaseName and
      not(secondary and node.ascendancyName==secondary.id)}
end
