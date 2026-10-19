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

return M
