-- Use upstream's game-file serializer; WidgetRail owns destination and atomic writes.
local M={}
function M.export(req)
  assert(build.treeTab,'Open a build first')
  local exporter=require('Modules.BuildExportPoE2')
  assert(type(req.name)=='string'and #req.name<=400,'Invalid build name')
  assert(type(req.author)=='string'and #req.author<=400,'Invalid author')
  assert(type(req.description)=='string'and #req.description<=4096,'Description is too long')
  local selections=req.all and exporter.GetLoadouts(build)or{{name=req.name,specIndex=build.treeTab.activeSpec,
    skillSetId=build.skillsTab.activeSkillSetId,itemSetId=build.itemsTab.activeItemSetId}}
  assert(#selections>0,'There are no linked loadouts to export. Export the selected sets instead.')
  assert(#selections<=128,'Export up to 128 loadouts at a time')
  local files={}
  for _,selection in ipairs(selections)do
    local spec,skills,items=exporter.ResolveSelection(build,selection)
    assert(spec and skills and items,'A loadout is missing a tree, skill set or item set. Select complete sets before exporting.')
    local name=req.all and(req.name..' - '..selection.name)or req.name
    local text,err=exporter.Export(build,{name=name,author=req.author,description=req.description,useGeneratedItemText=true},selection)
    assert(text,err);files[#files+1]={name=name,text=text}
  end
  return files
end
return M
