namespace WidgetRail.WidgetProtocol;

/// <summary>One independently bounded preview, separate from the collection row batch.</summary>
public static class FocusPreviewContentContract
{
    public const int MaximumNodes = 256;
    public static void Validate(ViewNode content,bool interactive)
    {
        ArgumentNullException.ThrowIfNull(content);
        var snapshot=new ViewSnapshot
        {
            ProtocolVersion=ProtocolConstants.CurrentVersion,Sequence=1,WidgetInstanceId="preview-validation",ActiveInputScopeId="preview-validation-root",
            Root=new(){Id="preview-validation-root",Kind=ViewNodeKind.FocusPresentationSurface,
                FocusPreview=new(){EnterButton=interactive?ControllerButton.X:null},
                DefaultFocusPresentation=new(){Id="preview-validation-empty",Kind=ViewNodeKind.Text,Text=""},
                Children=[new(){Id="preview-validation-source",Kind=ViewNodeKind.Button,Text="Source",ActionId="source",FocusPresentation=content}]},
        };
        var errors=ViewSnapshotValidator.Validate(snapshot,ProtocolVersionRequirements.Calculate(snapshot),materializedIndexedCollections:false,deferredFocusContent:true);
        if(errors.Count!=0)throw new ProtocolValidationException(errors);
    }
}
