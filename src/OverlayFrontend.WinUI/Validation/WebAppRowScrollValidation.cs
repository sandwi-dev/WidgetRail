using Microsoft.UI.Xaml.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WebAppSpatialValidationPage
{
    private async Task RunRowScrollChecksAsync(Func<string, Task> body, Func<string, Task> pointAt, Action<bool, string> check)
    {
        const string open = "<div id=rows style='position:absolute;left:40px;top:40px;width:300px;height:180px;overflow:auto;overflow-anchor:none'>";
        static string Row(string id, int height, string tag = "article") => $"<{tag} id='{id}' style='height:{height}px;margin:0'>Text-only row</{tag}>";
        async Task Jump(FocusNavigationDirection direction)
        {
            browser.MoveFocus(direction);
            await Until(() => !browser.SpatialBusyForValidation);
            check(browser.SpatialErrorForValidation is null, "Row scrolling finishes without a script or frame error");
        }
        async Task<double> Top() => double.Parse(await browser.EvaluateFixtureAsync("rows.scrollTop"), System.Globalization.CultureInfo.InvariantCulture);
        async Task<bool> RestInList()
        {
            var p = browser.SpatialPointerForValidation;
            return await browser.EvaluateFixtureAsync(FormattableString.Invariant($"(()=>{{const x=visualViewport.offsetLeft+{p.X}*visualViewport.width,y=visualViewport.offsetTop+{p.Y}*visualViewport.height;return rows.contains(document.elementFromPoint(x,y))&&y>200&&y<220;}})()")) == "true";
        }

        await body(open + string.Concat(Enumerable.Range(0, 10).Select(i => Row("row" + i, 60))) + "</div>");
        await pointAt("row0"); await Jump(FocusNavigationDirection.Down);
        check(Math.Abs(await Top() - 72) < 2, "Text-only article list reveals the next row with a 12px margin instead of a fixed viewport step");
        check(await RestInList(), "Text-only row scrolling places the circle near the advancing edge inside the list");
        check(await browser.EvaluateFixtureAsync("clicks.length===0&&window.scrollY===0") == "true", "Reading rows are not clicked and nested scrolling does not move the outer document");
        await Jump(FocusNavigationDirection.Down);
        check(Math.Abs(await Top() - 132) < 2, "Successive row scrolling advances by the next row height");

        await body(open + string.Concat(Enumerable.Range(0, 1000).Select(i => Row("row" + i, 60))) + "</div>");
        await browser.EvaluateFixtureAsync("rows.scrollTop=30000");
        await pointAt("row500"); await Jump(FocusNavigationDirection.Down);
        check(Math.Abs(await Top() - 30072) < 2, "Long retained lists measure rows near the visible edge instead of scanning from their first row");

        await body(open + Row("row0", 50) + Row("row1", 150) + Row("row2", 35) + Row("row3", 300) + "</div>");
        await pointAt("row0"); await Jump(FocusNavigationDirection.Down);
        check(Math.Abs(await Top() - 32) < 2, "Variable-height rows reveal only the clipped row and its margin");

        await body(open + Row("row0", 500) + Row("row1", 200) + "</div>");
        // The tall row center is outside the viewport; point inside its visible part.
        await browser.EvaluateFixtureAsync("rows.insertAdjacentHTML('afterbegin','<span id=point style=position:absolute;top:20px;left:20px>Text</span>')");
        await pointAt("point"); await Jump(FocusNavigationDirection.Down);
        check(Math.Abs(await Top() - 117) < 2, "Oversized rows advance by at most 65 percent of the visible region");

        await body(open + "<span id=point>Text</span><div style='height:1000px'>Unstructured text</div></div>");
        await pointAt("point"); await Jump(FocusNavigationDirection.Down);
        check(Math.Abs(await Top() - 63) < 2 && await RestInList(), "Unstructured lists use a bounded 35 percent fallback and retain a safe edge resting point");

        await body(open + string.Concat(Enumerable.Range(0, 8).Select(i => $"<div role=listitem id=row{i} style='height:60px'><article style='height:60px'>Nested article</article></div>")) + "</div>");
        await pointAt("row0"); await Jump(FocusNavigationDirection.Down);
        check(Math.Abs(await Top() - 72) < 2, "ARIA list rows participate while nested articles do not count as extra scroll units");

        await body(open + string.Concat(Enumerable.Range(0, 8).Select(i => Row("row" + i, 60))) + "</div>");
        await browser.EvaluateFixtureAsync("rows.addEventListener('scroll',()=>{rows.innerHTML=Array.from({length:8},(_,i)=>'<article style=height:60px id=recycled'+i+'>Recycled row</article>').join('')},{once:true})");
        await pointAt("row0"); await Jump(FocusNavigationDirection.Down);
        check(await browser.EvaluateFixtureAsync("!!document.getElementById('recycled0')") == "true" && await RestInList(), "Scroll settles through layout frames and retains the list after virtualized row replacement");

        await body("<div id=rows style='position:absolute;left:40px;top:40px;width:180px;height:120px;overflow:auto;white-space:nowrap;direction:rtl'>" +
            string.Concat(Enumerable.Range(0, 10).Select(i => $"<article id=row{i} style='display:inline-block;width:60px;height:100px'>Row</article>")) + "</div>");
        await pointAt("row0"); await Jump(FocusNavigationDirection.Left);
        check(await browser.EvaluateFixtureAsync("Math.abs(rows.scrollLeft+72)<2") == "true", "Horizontal RTL rows use physical direction and row-sized scrolling");

        await body(open + string.Concat(Enumerable.Range(0, 8).Select(i => Row("row" + i, 60))) + "</div><button id=exit class=fixed style='left:40px;top:260px'>Outside</button>");
        await browser.EvaluateFixtureAsync("rows.scrollTop=rows.scrollHeight");
        await pointAt("row7"); await Jump(FocusNavigationDirection.Down);
        var at = browser.SpatialPointerForValidation;
        check(await browser.EvaluateFixtureAsync(FormattableString.Invariant($"document.elementFromPoint(visualViewport.offsetLeft+{at.X}*visualViewport.width,visualViewport.offsetTop+{at.Y}*visualViewport.height)?.id==='exit'")) == "true", "At the real list boundary D-pad can leave for an external control in the requested direction");
    }
}
