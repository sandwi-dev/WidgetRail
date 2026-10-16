-- Run with LuaJIT from the repository root; no PoB process or build is needed.
colorCodes={NORMAL='^7',UNSUPPORTED='^1',COLD='^xABCDEF'}
data={skills={a={name='Ice Fragments'},b={name='Greater Ice Fragments'},c={name='Frenzy'}}}
local format=dofile('samples/PobCompanion/Engine/text-runs.lua')
local function flatten(runs)local out={}for _,run in ipairs(runs)do out[#out+1]=run.text end return table.concat(out)end
local function has(runs,text,tone)for _,run in ipairs(runs)do if run.text==text and run.tone==tone then return true end end return false end
local text='Trigger Greater Ice Fragments or Ice Fragments with a Frenzy Charge.'
local runs=format(text,true)
assert(flatten(runs)==text)
assert(has(runs,'Greater Ice Fragments','violet') and has(runs,'Ice Fragments','violet'))
assert(not has(runs,'Frenzy','violet'))
assert(#format('Ice FragmentsExtra and SuperIce Fragments',true)==1)
assert(#format(text)==1) -- Ordinary upstream formatting remains opt-in.
assert(has(format('^1Ice Fragments',true),'Ice Fragments','warning'))
assert(has(format('^xABCDEFIce Fragments',true),'Ice Fragments','blue'))
assert(flatten(format('^1Warning^7: Trigger Ice Fragments.',true))=='Warning: Trigger Ice Fragments.')
print('PASS upstream skill references: longest name, boundaries, wording, warning precedence and opt-in scope')
