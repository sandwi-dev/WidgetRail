using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class TextEntryValidationPage
{
    private TextEntryInputKind? purpose;
    private async Task VerifyKeyboardEnhancementsAsync()
    {
        sensitive=false;purpose=null;maximumLength=2048;value="one two abcdefghijk";Apply();await Open();
        var dialog=(WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        await presenter.HandleControllerButtonAsync(ControllerButton.X);
        dialog.SampleHeldButtons(0x4000,0x4000,0);
        foreach(var time in new[]{400,490,580,670})dialog.SampleHeldButtons(0x4000,0,time);
        Check(dialog.TakeValue()=="one two abcdef","Initial hold still deletes individual text elements");
        dialog.SampleHeldButtons(0x4000,0,1000);
        Check(dialog.TakeValue()=="one two ","Sustained backspace deletes the remaining word using Windows boundaries");
        dialog.SampleHeldButtons(0,0,1010);
        dialog.SampleHeldButtons(0x10,0x10,1100);dialog.SampleHeldButtons(0,0,1110);
        Check(dialog.TakeValue()=="one two  ","Menu release inserts one space");
        dialog.SampleHeldButtons(0x20,0x20,1200);dialog.SampleHeldButtons(0x30,0x10,1210);dialog.SampleHeldButtons(0,0,1220);
        Check(dialog.TakeValue()=="one two  ","View plus Menu inserts neither space nor clipboard text");
        await presenter.HandleControllerButtonAsync(ControllerButton.B);await Closed();

        value="one two three four";Apply();await Open();dialog=(WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
        var editor=(TextBox)Editor();editor.Select(0,0);
        await presenter.HandleControllerButtonAsync(ControllerButton.RightBumper);
        dialog.SampleHeldButtons(0x200,0x200,0);
        foreach(var time in new[]{400,490,580,670})dialog.SampleHeldButtons(0x200,0,time);
        Check(editor.SelectionStart==5,"Initial caret hold moves by characters");
        dialog.SampleHeldButtons(0x200,0,1000);
        Check(editor.SelectionStart==8,"Sustained caret hold moves to the next word");
        await presenter.HandleControllerButtonAsync(ControllerButton.B);await Closed();

        foreach(var kind in new[]{TextEntryInputKind.Integer,TextEntryInputKind.Decimal,TextEntryInputKind.Url,TextEntryInputKind.Email})
        {
            purpose=kind;value="";Apply();await Open();dialog=(WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;
            if(kind is TextEntryInputKind.Integer or TextEntryInputKind.Decimal)
            {
                Check(FocusedId=="Widget.TextEntry.Key.4","Numeric layout starts on its visible central key");
                Check(FindDialog("Widget.TextEntry.Key.20")!.Visibility==Visibility.Collapsed,"Numeric layout hides alphabet keys");
                Check(!WidgetViewPresenter.CaptureKeyboardGuide(dialog).Any(hint => hint.Prompt == ControllerPrompt.LeftTrigger), "Numeric guide omits the unavailable Shift action");
                var utilities = new[] { "CaretLeft", "CaretRight", "Space" }.Select(id => FindDialog("Widget.TextEntry." + id)!).ToArray();
                Check(utilities.Sum(Grid.GetColumnSpan) == 20, "Numeric utility row fills its available columns");
                await presenter.HandleControllerButtonAsync(ControllerButton.A);
                Check(dialog.TakeValue()=="5","Numeric key inserts its displayed digit");
            }
            else Check(((Button)FindDialog("Widget.TextEntry.Key.37")!).Content?.ToString()==(kind==TextEntryInputKind.Url?":":"@"),"Address layout exposes its common punctuation directly");
            await presenter.HandleControllerButtonAsync(ControllerButton.B);await Closed();
        }

        var culture=System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("en-US");
            purpose=TextEntryInputKind.Prose;value=new string('a',520)+" helter later";Apply();await Open();
            dialog=(WidgetTextEntryDialog)FindDialog("Widget.TextEntry.Dialog")!;editor=(TextBox)Editor();editor.Select(524,0);
            var deadline=Environment.TickCount64+6000;
            while(!dialog.CanAcceptFirstSuggestion){if(Environment.TickCount64>deadline)throw new TimeoutException("Local word completion did not appear");await Task.Delay(20);}
            var suggestion=(Button)FindDialog("Widget.TextEntry.Suggestion.0")!;
            var word=((Grid)suggestion.Content).Children.OfType<TextBlock>().Single().Text;
            var count=actions.Count;
            await presenter.HandleControllerButtonAsync(ControllerButton.LeftStick);
            Check(editor.Text==new string('a',520)+" "+word+" later"&&editor.SelectionStart==521+word.Length,"Word completion preserves surrounding long text and the insertion point");
            Check(actions.Count==count,"Accepting a word never submits the field");
            await presenter.HandleControllerButtonAsync(ControllerButton.B);await Closed();
        }
        finally{purpose=null;System.Globalization.CultureInfo.CurrentUICulture=culture;}
    }
}
