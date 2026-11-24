using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Localization;

namespace LummaSimulator.Web.Localization;

[HtmlTargetElement("local-text")]
public sealed class LocalTextTagHelper(IStringLocalizer<UiText> text) : TagHelper
{
    public string Key { get; set; } = "";
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        output.TagName = "span";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.RemoveAll("key");
        output.Attributes.SetAttribute("data-i18n", Key);
        output.Content.SetContent(text[Key].Value);
    }
}
