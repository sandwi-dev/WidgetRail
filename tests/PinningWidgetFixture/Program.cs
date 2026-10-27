using WidgetRail.Samples.SdkGalleryWidget;
using WidgetRail.WidgetRuntime;

// Real SDK Gallery authoring, wrapped only to run in an isolated native fixture.
return await WidgetApplicationBootstrap.RunAsync(args,()=>new SdkGalleryWidget());
