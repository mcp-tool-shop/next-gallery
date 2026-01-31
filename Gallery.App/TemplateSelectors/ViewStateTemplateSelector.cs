using Gallery.App.ViewModels;

namespace Gallery.App.TemplateSelectors;

/// <summary>
/// Selects DataTemplate based on CodeComfyViewState.
/// Pure mapping - no logic.
/// </summary>
public class CodeComfyViewStateTemplateSelector : DataTemplateSelector
{
    public DataTemplate? LoadingTemplate { get; set; }
    public DataTemplate? EmptyTemplate { get; set; }
    public DataTemplate? ListTemplate { get; set; }
    public DataTemplate? FatalTemplate { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not CodeComfyViewModel vm)
            return LoadingTemplate;

        return vm.CurrentCodeComfyViewState switch
        {
            CodeComfyViewState.Loading => LoadingTemplate,
            CodeComfyViewState.Empty => EmptyTemplate,
            CodeComfyViewState.List => ListTemplate,
            CodeComfyViewState.Fatal => FatalTemplate,
            _ => LoadingTemplate
        };
    }
}
