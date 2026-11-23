using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using System.Runtime.InteropServices.WindowsRuntime;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class FileBrowserDialog
{
    internal async Task ChooseFolderForValidationAsync(string target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!IsLoaded || page is null || reading is not null) await Task.Delay(20,timeout.Token);
        await NavigateAsync(target,"",null,null,true,true);
        if (page?.Directory != target) throw new InvalidOperationException("Test folder was not selected.");
        await CommitAsync();
    }
    internal static async Task DesktopCheckAsync(FrameworkElement origin,string output)
    {
        Directory.CreateDirectory(output);
        var root = Directory.CreateTempSubdirectory("wr-file-interaction-");
        try
        {
            var nested=root.CreateSubdirectory("Folder with spaces");
            await File.WriteAllTextAsync(Path.Combine(root.FullName,"First.txt"),"1");
            await File.WriteAllTextAsync(Path.Combine(nested.FullName,"Second.txt"),"2");
            await File.WriteAllTextAsync(Path.Combine(output,"source.txt"),nested.FullName);
            var dialog = new FileBrowserDialog(new(FileBrowserMode.Multiple,"Attach files","Attach",root.FullName,Limit:16)){XamlRoot=origin.XamlRoot};
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var theme=NativePopupTheme.Dialog(dialog,origin);
            await dialog.PresentAsync(origin,timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(output,"result.json"),System.Text.Json.JsonSerializer.Serialize(dialog.Result.Select(Path.GetFileName).ToArray()));
        }
        finally { root.Delete(true); }
    }
    internal static async Task ValidateAsync(FrameworkElement origin,string output)
    {
        var checks=new List<string>();
        var data=Directory.CreateTempSubdirectory("wr-picker-ui-");
        var folder=data.CreateSubdirectory("Screenshots");
        var picture=Path.Combine(folder.FullName,"Capture 001.png");
        var clip=Path.Combine(data.FullName,"Replay 001.mp4");
        var package=Path.Combine(data.FullName,"Widget update.wrwidget");
        await File.WriteAllTextAsync(picture,"fixture"); await File.WriteAllTextAsync(clip,"fixture"); await File.WriteAllTextAsync(package,"fixture");
        Directory.CreateDirectory(output);
        string? failure=null;
        try
        {
            if(Environment.GetCommandLineArgs().Contains("--file-picker-compact"))
            {
                Microsoft.UI.Windowing.AppWindow.GetFromWindowId(origin.XamlRoot.ContentIslandEnvironment.AppWindowId)
                    .Resize(new Windows.Graphics.SizeInt32(410,360));
                await Until(()=>origin.XamlRoot.Size.Width<440);
            }
            await Run(new(FileBrowserMode.Multiple,"Attach files to Discord","Attach",data.FullName,Limit:2),async d=>
            {
                Check(d.FocusWithinRows(),"Directory contents receive initial focus");
                if(origin.XamlRoot.Size.Width<440) Check(d.PositionsOwnPopup,"Small requesting window retains a work-area file browser");
                d.FocusRow(d.rows.Single(r=>r.Entry.Path==clip)); Press(d,0x1000);
                Check(d.picked.ContainsKey(clip),"Controller A checks the file");
                d.typeFilter.SelectedIndex=(int)FileBrowserFilter.Documents;
                await Until(()=>d.reading is null && d.rows.Length==1);
                Check(d.picked.Count==1 && d.rows[0].Entry.Folder,"File-type filtering preserves selection and folder navigation");
                d.typeFilter.SelectedIndex=0; await Until(()=>d.reading is null && d.rows.Length==3);
                d.Activate(d.rows.Single(r=>r.Entry.Folder)); await Until(()=>d.page?.Directory==folder.FullName && d.reading is null);
                var f=ControllerFrame.Create(); f.Connected=1; f.State.LeftTrigger=255; d.Receive(f,true);
                Check(d.picked.ContainsKey(picture),"L2 adds a file across folder navigation");
                f.State.LeftTrigger=0;d.Receive(f,true);
                d.search.Text="absent";await Until(()=>d.rows.Length==0 && d.reading is null);
                Check(d.picked.Count==2,"Search never drops hidden selections");
                d.ToggleReview(); Check(d.rows.Length==2 && d.reviewing,"Review includes selections from both folders");
                d.ToggleReview(); Press(d,0x2000); await Until(()=>d.page?.Directory==data.FullName && d.reading is null);
                await Task.Delay(100);
                Check(d.FocusedRow()?.Entry.Path==folder.FullName,$"Back restores the folder row (focus={d.FocusedRow()?.Name}, selected={Path.GetFileName(d.selectedPath)}, items={d.rows.Length}, query={d.appliedQuery}, filter={d.filter}, native={FocusManager.GetFocusedElement(d.XamlRoot)?.GetType().Name})");
                d.Go(@"Z:invalid");await Until(()=>d.reading is null);
                Check(d.page!.Directory==data.FullName && d.picked.Count==2,"Invalid location preserves the current folder and selection");
                d.Go(clip); await Until(()=>d.FocusedRow()?.Entry.Path==clip && d.reading is null);
                await Shot(d,"attach");
                Press(d,0x4000); await Until(()=>d.closed);
                Check(d.Result.ToHashSet().SetEquals(new[]{clip,picture}),"Explicit commit returns exactly the selected files");
            });
            await Run(new(FileBrowserMode.Open,"Choose widget package","Review package",data.FullName,".wrwidget"),async d=>
            {
                Check(d.rows.All(r=>r.Entry.Folder || r.Entry.Path==package),"Settings retains the widget-package filter");
                d.Go(package);await Until(()=>d.selectedPath==package && d.reading is null);
                await d.CommitAsync(); Check(d.Result.SequenceEqual([package]),"Pasted package path is admitted after explicit confirmation");
            });
            await Run(new(FileBrowserMode.Folder,"Copy captures","Copy here",data.FullName),async d=>
            {
                Check(d.rows.Length==1 && d.rows[0].Entry.Folder,"Copy destination shows folders");
                d.Activate(d.rows[0]); await Until(()=>d.page?.Directory==folder.FullName && d.reading is null);
                await Shot(d,"copy");await d.CommitAsync(); Check(d.Result.SequenceEqual([folder.FullName]),"Copy here returns the browsed folder");
            });
            await Run(new(FileBrowserMode.Save,"Save download","Save",data.FullName,".mp4","Replay 001.mp4"),async d=>
            {
                await d.CommitAsync(); Check(d.confirming && d.Result.Length==0,"Existing file requires separate replace confirmation");
                d.Back(); d.filename.Text="New replay"; await d.CommitAsync();
                Check(d.Result.SequenceEqual([Path.Combine(data.FullName,"New replay.mp4")]) && !File.Exists(d.Result[0]),"Save returns a destination without writing it");
            });
            await Run(new(FileBrowserMode.Open,"Choose file","Open",data.FullName),async d=>
            {
                d.Edit(d.search); await Until(()=>d.Keyboard is { IsLoaded:true });
                Check(true,"Controller search uses the existing on-screen keyboard");
                d.Keyboard!.Hide(); await Until(()=>d.Keyboard is null && d.body.IsLoaded);
                d.Cancel(); Check(d.Result.Length==0,"Keyboard dismissal and picker cancellation produce no selection");
            });
            if (!Environment.GetCommandLineArgs().Contains("--file-picker-compact"))
            {
                var many=data.CreateSubdirectory("Large directory");
                await Task.Run(() =>
                {
                    for(var i=0;i<4096;i++)using(File.Create(Path.Combine(many.FullName,$"Capture {i:0000}.png"))){}
                });
                await Run(new(FileBrowserMode.Multiple,"Choose files","Choose",data.FullName,Limit:16),async d=>
                {
                    var begin=System.Diagnostics.Stopwatch.GetTimestamp();
                    await d.NavigateAsync(many.FullName,"",null,null,false,true);
                    Check(d.rows.Length==FileBrowserDirectory.PageSize && d.page!.HasMore,
                        $"4096 files produce one {d.rows.Length}-row native page in {System.Diagnostics.Stopwatch.GetElapsedTime(begin).TotalMilliseconds:F0} ms");
                    d.Page(1);await Until(()=>d.reading is null);
                    Check(d.rows[0].Name=="Capture 0128.png","Next page preserves file order");
                    var unavailable = many.FullName + "-unavailable";
                    Directory.Move(many.FullName, unavailable);
                    try
                    {
                        d.Page(-1);
                        await Until(() => d.reading is null);
                        Check(d.pages.Count == 1 && d.rows[0].Name == "Capture 0128.png" && d.notice.Visibility == Visibility.Visible,
                            "A failed page read keeps the current entries and previous-page cursor");
                    }
                    finally { Directory.Move(unavailable, many.FullName); }
                    d.Page(-1);await Until(()=>d.reading is null);
                    Check(d.rows[0].Name=="Capture 0000.png","Previous page restores its first entry");
                    d.Go(many.FullName); d.Go(folder.FullName); await Until(()=>d.reading is null);
                    Check(d.page?.Directory==folder.FullName && d.rows.Length==1,"A superseded large-directory read cannot replace the current folder");
                    Press(d, 0x100);
                    await Until(() => d.page?.Directory == data.FullName && d.reading is null);
                    Check(d.FocusedRow()?.Entry.Path == folder.FullName, "LB opens the parent and reveals the folder just left");
                    d.Go(many.FullName);
                    await Until(() => d.page?.Directory == many.FullName && d.reading is null);
                    d.FocusRow(d.rows[80]);
                    Press(d, 0x1000);
                    Check(d.picked.Count == 1, "Controller can select a file deep in a long list");
                    Press(d, 0x10);
                    await Until(() => d.closed);
                    Check(d.Result.Length == 0, "Menu cancels from the list without committing the checked file");
                });
            }
        }
        catch(Exception error){failure=error.ToString();}
        finally {data.Delete(true);await File.WriteAllTextAsync(Path.Combine(output,"result.json"),System.Text.Json.JsonSerializer.Serialize(new{passed=failure is null,checks,failure}));}

        async Task Run(FileBrowserRequest request,Func<FileBrowserDialog,Task> test)
        {
            var dialog=new FileBrowserDialog(request){XamlRoot=origin.XamlRoot};
            using var theme=NativePopupTheme.Dialog(dialog,origin);
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var showing=dialog.PresentAsync(origin,deadline.Token);
            try {await Until(()=>dialog.page is not null && dialog.reading is null && dialog.IsLoaded);await test(dialog);}
            finally {if(!dialog.closed)dialog.Cancel();await showing;}
        }
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);checks.Add(label);}
        static async Task Until(Func<bool> predicate)
        {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!predicate())await Task.Delay(20,timeout.Token);}
        static void Press(FileBrowserDialog d,ushort mask)
        {
            var frame=ControllerFrame.Create();frame.Connected=1;d.Receive(frame,true);
            frame.PressedButtons=frame.State.Buttons=mask;d.Receive(frame,true);
            frame.PressedButtons=frame.State.Buttons=0;frame.ReleasedButtons=mask;d.Receive(frame,true);
        }
        async Task Shot(FileBrowserDialog dialog,string name)
        {
            await Task.Delay(220);
            var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(dialog.DialogTemplatePart("BackgroundElement")??dialog);
            using var stream=new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,stream);
            encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,(await bitmap.GetPixelsAsync()).ToArray());
            await encoder.FlushAsync();stream.Seek(0);using var dest=File.Create(Path.Combine(output,name+".png"));await stream.AsStreamForRead().CopyToAsync(dest);
        }
    }
    private bool FocusWithinRows()=>FocusedRow() is not null;
}
