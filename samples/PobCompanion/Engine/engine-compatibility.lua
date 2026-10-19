-- Engine API shape belongs here; eligibility, calculations and enabled controls
-- remain upstream. Inspect the current build, not the game number or version string.
local M={}

function M.hasExplicitConstructors()
  return common.classes.Tooltip.Tooltip~=nil
end

function M.create(class,...)
  if M.hasExplicitConstructors() then
    local object=new(class)
    object[class](object,...)
    return object
  end
  return new(class,...)
end

function M.timelessFinder(build)
  -- Some engines retain the method and jewel data but omit the desktop control.
  return build.treeTab.FindTimelessJewel~=nil and build.treeTab.controls.findTimelessJewel~=nil
end

function M.capabilities(build)
  return {
    canCreateLoadout=type(build.CustomLoadout)=='function',
    skillWeaponSets=build.skillsTab.controls.set1Enabled~=nil,
    canEditNodeNotes=build.spec.nodeNotes~=nil,
    weaponTreeModes=build.spec.GetEffectiveAllocationPath~=nil,
    timelessFinder=M.timelessFinder(build),
  }
end

-- Library previews use upstream variant eligibility and formatting, without
-- constructing a display item or evaluating a replacement on focus.
function M.itemPreviewLines(item,clean,textRuns)
  local lines={}
  if type(item.GetModLineVariantCount)~='function'or not itemLib or type(itemLib.formatModLine)~='function'then return lines end
  for _,key in ipairs({'implicitModLines','explicitModLines'})do
    for _,mod in ipairs(item[key]or{})do
      if item:GetModLineVariantCount(mod)>0 then
        local formatted=itemLib.formatModLine(mod,true)
        if type(formatted)=='string'and #formatted<=384 then
          lines[#lines+1]={text=clean(formatted),runs=textRuns(formatted)}
          if #lines==3 then return lines end
        end
      end
    end
  end
  return lines
end

return M
