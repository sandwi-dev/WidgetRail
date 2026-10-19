-- Present PoB's own modifier-template matching without interpreting game effects.
-- CompareTab/TradeHelpers own formatting, variant selection and template rules.
-- Keep all occurrences: buildModMap alone would overwrite duplicate templates.
local compatibility=dofile((debug.getinfo(1,'S').source:sub(2):match('^(.*[/\\])')or '')..'engine-compatibility.lua')
local M={}
function M.capture()
  local result={}
  for _,slot in ipairs(build.itemsTab.orderedSlots)do
    if slot.slotName and slot:IsShown() and not slot.inactive then
      local item=build.itemsTab.items[slot.selItemId]
      result[slot.slotName]=item and item.raw or ''
    end
  end
  return result
end

function M.compare(before,after,requested,clean,textRuns)
  local helpers=LoadModule('Classes/TradeHelpers')
  assert(type(helpers.modLineTemplate)=='function','This PoB engine has no modifier matching helper')
  local slots,seen={},{}
  for slot in pairs(before)do slots[#slots+1]=slot;seen[slot]=true end
  for slot in pairs(after)do if not seen[slot]then slots[#slots+1]=slot end end
  table.sort(slots)
  local result={}
  for _,slot in ipairs(slots)do
    local old,new=before[slot]or '',after[slot]or ''
    if old~=new or slot==requested then
      local rows,groups={},{}
      local function read(raw,side)
        if raw==''then return end
        local item=compatibility.create('Item',raw)
        assert(item.base,'Item comparison could not read the item')
        -- The same mod sections rendered by upstream CompareTab. Buff lines are
        -- retained too, using its template helper rather than a separate matcher.
        for _,section in ipairs{{'enchantModLines','Enchant'},{'scourgeModLines','Scourge'},
          {'implicitModLines','Implicit'},{'explicitModLines','Explicit'},
          {'crucibleModLines','Crucible'},{'buffModLines','Buff'}}do
          for _,line in ipairs(item[section[1]]or {})do
            if item:CheckModLineVariant(line)then
              local formatted=itemLib.formatModLine(line)
              if formatted then
                local key=helpers.modLineTemplate(line.line)
                local group=groups[key]
                if not group then group={before={},after={}};groups[key]=group;rows[#rows+1]=group end
                group[side][#group[side]+1]={text=clean(formatted),runs=textRuns(formatted),kind=section[2]}
              end
            end
          end
        end
      end
      read(old,'before');read(new,'after')
      local function signature(lines)
        local values={};for _,line in ipairs(lines)do values[#values+1]=line.kind..'\31'..line.text end
        table.sort(values);return table.concat(values,'\30')
      end
      for index,row in ipairs(rows)do
        row.id=tostring(index)
        row.status=#row.before==0 and 'added' or #row.after==0 and 'removed' or
          signature(row.before)==signature(row.after) and 'same' or 'changed'
      end
      result[#result+1]={slot=slot,rows=rows}
    end
  end
  return result
end
return M
