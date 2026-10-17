-- Reuse the desktop dialogs' available choices and mutation callbacks.
local M={}
function M.kind(node)
  if node.type=='Mastery'and node.masteryEffects then return'mastery'end
  if node.isAttribute and build.treeTab.ModifyAttributePopup then return'attribute'end
  return''
end
local function nodeFor(req)
  local node=assert(build.spec.nodes[req.id],'This passive no longer exists')
  assert(M.kind(node)~='','This passive has no selectable effect')
  assert(not build.calcsTab.mainEnv.grantedPassives[node.id],'This passive is granted by an item')
  assert(node.alloc or node.path,'There is no allocation path to this passive')
  if not node.alloc and build.spec.GetEffectiveAllocationPath then assert(build.spec:GetEffectiveAllocationPath(node),'This passive cannot be allocated in the selected tree mode')end
  return node
end
local function dialog(node,action)
  local open,close=main.OpenPopup,main.ClosePopup
  main.OpenPopup=function(_,width,height,title,controls)return action(controls)end
  main.ClosePopup=function()end
  local ok,err=pcall(function()
    if M.kind(node)=='mastery'then build.treeTab:OpenMasteryPopup(node)
    else build.treeTab:ModifyAttributePopup(node)end
  end)
  main.OpenPopup,main.ClosePopup=open,close
  assert(ok,err)
end
function M.choices(req,clean)
  local node=nodeFor(req);local choices={}
  dialog(node,function(controls)
    if controls.effect then
      for _,effect in ipairs(controls.effect.list)do choices[#choices+1]={id=effect.id,title=clean(effect.label),description=clean(effect.label),active=build.spec.masterySelections[node.id]==effect.id}end
    else
      for i,title in ipairs(controls.attrSelect.list)do
        local option=(node.options or build.spec.tree.nodes[node.id].options)[i]
        choices[#choices+1]={id=i,title=clean(title),description=clean(table.concat(option.sd or option.stats or{},'\n')),active=node.dn==title}
      end
    end
  end)
  return choices
end
function M.edit(req)
  local node=nodeFor(req);local applied=false
  dialog(node,function(controls)
    if controls.effect then
      local choice;for _,effect in ipairs(controls.effect.list)do if effect.id==req.choice then choice=effect end end
      assert(choice,'This mastery effect is unavailable or already used elsewhere')
      controls.effect.selValue=choice;build.treeTab:SaveMasteryPopup(node,controls.effect)
    else
      assert(type(req.choice)=='number'and req.choice%1==0 and controls.attrSelect.list[req.choice],'Choose an attribute')
      controls.attrSelect:SetSel(req.choice);controls.save.onClick()
    end
    applied=true
  end)
  assert(applied,'There are no available choices for this passive')
  assert(node.alloc,'PoB could not allocate this passive')
end
return M
