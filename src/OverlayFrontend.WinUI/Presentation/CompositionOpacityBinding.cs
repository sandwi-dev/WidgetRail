using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Compositor-owned opacity for paint relocated outside its logical ancestry.</summary>
internal sealed class CompositionOpacityBinding : IDisposable
{
    private const int AncestorsPerExpression = 16;
    private readonly Visual target;
    private readonly List<CompositionPropertySet> products = [];
    private bool disposed;
#if ENABLE_WIDGET_VALIDATION
    internal static int ActiveProductsForValidation { get; private set; }
#endif

    internal CompositionOpacityBinding(Visual target, UIElement anchor, DependencyObject? exclusiveRoot)
    {
        this.target = target;
        try
        {
            DependencyObject? item = anchor;
            while (item is UIElement && !ReferenceEquals(item, exclusiveRoot))
            {
                using var expression = target.Compositor.CreateExpressionAnimation();
                var terms = new List<string>();
                if (products.Count > 0)
                {
                    expression.SetReferenceParameter("previous", products[^1]);
                    terms.Add("previous.Value");
                }
                for (var i = 0; i < AncestorsPerExpression && item is UIElement element &&
                    !ReferenceEquals(item, exclusiveRoot); i++, item = VisualTreeHelper.GetParent(item))
                {
                    var name = "a" + i;
                    SetReference(expression, name, element);
                    terms.Add(name + ".Opacity");
                }
                // WinUI rejects long expression strings. Keep every ancestor,
                // but reduce their product in bounded compositor expressions.
                var product = target.Compositor.CreatePropertySet();
                products.Add(product);
#if ENABLE_WIDGET_VALIDATION
                ActiveProductsForValidation++;
#endif
                product.InsertScalar("Value", 1);
                expression.Expression = string.Join(" * ", terms);
                product.StartAnimation("Value", expression);
            }
            using var result = target.Compositor.CreateExpressionAnimation();
            if (products.Count == 0) result.Expression = "1.0";
            else
            {
                result.SetReferenceParameter("product", products[^1]);
                result.Expression = "product.Value";
            }
            target.StartAnimation("Opacity", result);
        }
        catch { Dispose(); throw; }
    }

    private static void SetReference(ExpressionAnimation expression, string name, UIElement element)
    {
        Visual visual;
        try { visual = ElementCompositionPreview.GetElementVisual(element); }
        catch (UnauthorizedAccessException)
        {
            // XAML Scale/Translation facade ownership forbids a handout Visual.
            expression.SetExpressionReferenceParameter(name, element);
            return;
        }
        // Raw Visual opacity is independent of the XAML facade. Preserve both
        // ownership models, rather than silently losing compositor-driven fades.
        expression.SetReferenceParameter(name, visual);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        target.StopAnimation("Opacity");
        for (var i = products.Count - 1; i >= 0; i--)
        {
            products[i].StopAnimation("Value");
            products[i].Dispose();
#if ENABLE_WIDGET_VALIDATION
            ActiveProductsForValidation--;
#endif
        }
        products.Clear();
    }
}
