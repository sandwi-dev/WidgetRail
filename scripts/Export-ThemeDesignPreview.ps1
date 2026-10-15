param(
    [Parameter(Mandatory = $true)][string]$AuditJson,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$BaselineAuditJson
)
$ErrorActionPreference = 'Stop'
# The executable test harness emits resolved production WRSS styles. This page
# is an illustrative browser study, not the WinUI renderer or an acceptance gate.
$audit = Get-Content -LiteralPath $AuditJson -Raw | ConvertFrom-Json
$json = ConvertTo-Json -InputObject @($audit) -Depth 20 -Compress
$json = $json.Replace('<', '\u003c').Replace('>', '\u003e').Replace('&', '\u0026')
$baselineJson = '[]'
if ($BaselineAuditJson) {
    $baseline = Get-Content -LiteralPath $BaselineAuditJson -Raw | ConvertFrom-Json
    $baselineJson = (ConvertTo-Json -InputObject @($baseline) -Depth 20 -Compress).Replace('<', '\u003c').Replace('>', '\u003e').Replace('&', '\u0026')
}
$html = @'
<!doctype html>
<html lang="en">
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>WidgetRail theme studies</title>
<style>
* { box-sizing: border-box; }
body { margin:0; background:#e9e9e6; color:#252828; font:16px/1.5 'Segoe UI',sans-serif; }
header { padding:32px 4vw 24px; background:#fafaf8; border-bottom:1px solid #ccc; }
h1 { margin:0 0 8px; font-size:30px; font-weight:600; }
header p { margin:0 0 18px; max-width:850px; color:#545959; }
.toolbar { display:flex; gap:24px; flex-wrap:wrap; align-items:center; }
label { display:flex; align-items:center; gap:10px; }
select { font:inherit; padding:6px 10px; }
main { padding:28px 4vw 48px; display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:28px; }
article { min-width:0; }
.caption { display:flex; justify-content:space-between; gap:12px; margin:0 0 12px; }
.caption h2 { margin:0; font-size:21px; font-weight:600; }
.kind { color:#545959; font-size:13px; align-self:center; }
.description { min-height:48px; margin:0 0 14px; font-size:14px; color:#545959; }
.stage { padding:22px; background:linear-gradient(130deg,#142738,#4b3542 60%,#20372f); border-radius:14px; }
body.bright .stage { background:linear-gradient(130deg,#eef7ff,#ffe8d4 60%,#efffe0); }
.panel { padding:24px; border:1px solid; min-width:0; }
.heading { margin:0 0 4px; }
.hint { margin:0; }
.status { display:flex; gap:8px; align-items:center; margin:20px 0 14px; }
.dot { width:8px; height:8px; border-radius:50%; background:currentColor; }
.tiles { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:12px; }
.tile { min-width:0; border:1px solid; padding:12px; }
.art { height:86px; border-radius:4px; overflow:hidden; margin-bottom:10px; }
.art.one { background:linear-gradient(150deg,#34434b 35%,#c8a26c 36%,#6d645a 52%,#273b43 53%); }
.art.two { background:radial-gradient(circle at 65% 40%,#b7c6b5 0 10%,transparent 11%),linear-gradient(140deg,#242b32,#5d7270); }
.game-name { font-size:15px; font-weight:600; }
.game-meta { font-size:13px; margin-top:2px; }
.actions { display:flex; flex-wrap:wrap; gap:10px; margin-top:18px; }
.demo-button { display:inline-flex; align-items:center; justify-content:center; border:1px solid; }
.demo-button.focused { outline-style:solid; }
.focus-note { margin-top:9px; font-size:12px; }
.divider { margin:20px 0 14px; border-top:1px solid; }
.footer { display:flex; justify-content:space-between; align-items:center; gap:10px; }
.tray { margin-top:12px; padding:10px 14px; border:1px solid; display:flex; gap:18px; align-items:center; font-size:13px; }
.glyph { font-size:20px; font-weight:600; }
.swatches { display:flex; gap:7px; margin-top:12px; }
.swatches span { width:32px; height:12px; border:1px solid #8887; border-radius:2px; }
.metrics { font-size:12px; color:#545959; margin-top:9px; }
@media(max-width:900px) { main { grid-template-columns:1fr; } }
@media(max-width:480px) { .stage { padding:12px; } .panel { padding:16px; } }
</style>
<header>
  <h1>WidgetRail theme studies</h1>
  <p>Five new themes alongside the existing collection. Colors, type and corners come from computed WRSS. This browser study illustrates design choices; it is not a native WidgetRail capture.</p>
  <div class="toolbar">
    <label>Collection <select id="collection"><option value="new">Five new themes</option><option value="existing">Existing themes — improved</option><option value="before">Existing themes — before</option><option value="all">All ten themes</option></select></label>
    <label>Backdrop <select id="backdrop"><option value="dark">Dark scene</option><option value="bright">Bright scene</option></select></label>
  </div>
</header>
<main id="gallery"></main>
<script id="theme-data" type="application/json">__THEME_DATA__</script>
<script id="baseline-data" type="application/json">__BASELINE_DATA__</script>
<script>
const themes = JSON.parse(document.getElementById('theme-data').textContent);
const baseline = JSON.parse(document.getElementById('baseline-data').textContent);
if(!baseline.length) document.querySelector('option[value="before"]').remove();
const descriptions = {
 'Mineral Light':'Chalk surfaces and petrol ink. A daylight option with opaque panels and a light artwork scrim.',
 'Canopy':'Evergreen surfaces, sage accents and soft rectangles. Low visual noise for long sessions.',
 'Amber Signal':'Amber equipment readouts, squared controls and Bahnschrift headings. Warm, orderly and direct.',
 'Velvet Dusk':'Muted violet, lavender accents and Georgia headings. A quiet library feel with familiar controls.',
 'Mono Ink':'Graphite and white with firm edges. Color is reserved for status; shading is removed.',
 'Default':'Warm graphite and parchment accents, with brighter supporting text and clearer focus.',
 'Warm Graphite':'Warm graphite and parchment accents, with brighter supporting text and clearer focus.',
 'Cool Slate':'Cool blue-grey surfaces, lighter steel accents and more readable status colors.',
 'Neon Circuit':'Indigo and electric cyan, with quiet sentence-case labels and angular controls.',
 'Arcade Rush':'Plum surfaces and readable pink accents. Rounded rectangles balance circular icon buttons.',
 'Redline':'Oxblood and coral interaction, with a single scarlet tray edge and quieter headings.'
};
function paint(el, props) {
 const mapping={'corner-radius':'border-radius','font-family':'font-family','font-size':'font-size','font-weight':'font-weight','line-height':'line-height','letter-spacing':'letter-spacing','background':'background','color':'color','border-color':'border-color','border-width':'border-width','border-bottom-color':'border-bottom-color','border-bottom-width':'border-bottom-width','outline-color':'outline-color','outline-width':'outline-width','outline-offset':'outline-offset','min-height':'min-height','padding':'padding'};
 for (const [key,val] of Object.entries(props)) if (mapping[key]) el.style.setProperty(mapping[key],val);
 // WRSS family values are native family names; CSS is only a study approximation.
 return el;
}
function node(parent,tag,cls,text,style) {
 const el=document.createElement(tag);el.className=cls;if(text)el.textContent=text;if(style)paint(el,style);parent.append(el);return el;
}
function render() {
 const gallery=document.getElementById('gallery');gallery.replaceChildren();
 const filter=document.getElementById('collection').value;
 for(const theme of (filter==='before'?baseline:themes).filter(t=>filter==='all'||(filter==='new' ? t.IsNew : !t.IsNew))) {
  const s=theme.Styles, a=node(gallery,'article','');
  const caption=node(a,'div','caption');node(caption,'h2','',theme.Name);node(caption,'span','kind',theme.IsNew?'New theme':filter==='before'?'Before refinement':'Improved theme');
  node(a,'p','description',filter==='before'?'Original palette and geometry from the main-branch baseline.':descriptions[theme.Name]);
  const stage=node(a,'div','stage'), panel=node(stage,'div','panel',null,s.panel);
  node(panel,'div','heading','Your library',s.title);node(panel,'p','hint','A little time for a good game.',s.hint);
  const status=node(panel,'div','status',null,s.success);node(status,'span','dot');node(status,'span','','Ready to play');
  const tiles=node(panel,'div','tiles');
  for(const [name,meta,art] of [['Mountain trail','Last played yesterday','one'],['Quiet orbit','Continue your journey','two']]) {
   const tile=node(tiles,'div','tile',null,s.subtle);node(tile,'div','art '+art);node(tile,'div','game-name',name,{color:s.body.color});node(tile,'div','game-meta',meta,{color:s.hint.color});
  }
  const actions=node(panel,'div','actions');
  node(actions,'span','demo-button focused','Resume',s.primary);
  node(actions,'span','demo-button','Selected',{...s.selected,'outline-width':'0px'});
  node(actions,'span','demo-button','Unavailable',s.disabled);
  node(panel,'div','focus-note','Focus stays separate from the selected fill.',{color:s.hint.color});
  const divider=node(panel,'div','divider');divider.style.borderColor=s.panel['border-color'];
  const footer=node(panel,'div','footer');node(footer,'span','hint','3 games installed',s.hint);node(footer,'span','glyph','A  Open',{color:s.glyph.color});
  const tray=node(stage,'div','tray',null,s.tray||s.panel);node(tray,'span','','Library');node(tray,'span','','Audio');node(tray,'span','','Settings');
  const swatches=node(a,'div','swatches');
  for(const color of [s.panel.background,s.button.background,s.selected.background,s.accent.color,s.body.color,s.hint.color]) {const sw=node(swatches,'span','');sw.style.background=color;sw.title=color;}
  const m=theme.Metrics;node(a,'div','metrics',`Measured secondary text ${m.find(x=>x.Name==='Secondary text').Ratio.toFixed(2)}:1 · Primary focus ${m.find(x=>x.Name==='Primary button focus').Ratio.toFixed(2)}:1`);
 }
}
document.getElementById('collection').addEventListener('change',render);
document.getElementById('backdrop').addEventListener('change',event=>document.body.classList.toggle('bright',event.target.value==='bright'));
render();
</script>
</html>
'@
$html = $html.Replace('__THEME_DATA__', $json)
$html = $html.Replace('__BASELINE_DATA__', $baselineJson)
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $html, [Text.UTF8Encoding]::new($false))
Write-Output ([IO.Path]::GetFullPath($OutputPath))
