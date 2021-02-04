using System;
using System.Linq;
using System.Text;
using Ganss.XSS;
using HtmlBuilders;
using Markdig;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using static HtmlBuilders.HtmlTags;

public static class UI
{
    const string DisplayDateFormat = "MMMM dd, yyyy";

    public static string[] AllPages(Wiki wiki) => new[]
    {
    @"<span class=""uk-label"">Pages</span>",
    @"<ul class=""uk-list"">",
    string.Join("",
        wiki.ListAllPages().OrderBy(x => x.Name)
        .Select(x => Li.Append(A.Href("/" + x.NsName).Append(x.NsName)).ToHtmlString()
        )
    ),
    "</ul>"
    };

    public static string[] AllPagesForEditing(Wiki wiki)
    {
        return new[]
        {
        @"<span class=""uk-label"">Pages</span>",
        @"<ul class=""uk-list"">",
        string.Join("",
            wiki.ListAllPages().OrderBy(x => x.Name)
            .Select(x => Li.Append(Div.Class("uk-inline")
                .Append(Span.Class("uk-form-icon").Attribute("uk-icon", "icon: copy"))
                .Append(Input.Text.Value($"[{x.NsName}](/{x.NsName})").Class("uk-input uk-form-small").Style("cursor", "pointer").Attribute("onclick", "copyMarkdownLink(this);"))
            ).ToHtmlString()
            )
        ),
        "</ul>"
        };
    }

    public static string RenderMarkdown(string str)
    {
        var sanitizer = new HtmlSanitizer();
        return sanitizer.Sanitize(Markdown.ToHtml(str, new MarkdownPipelineBuilder().UseSoftlineBreakAsHardlineBreak().UseAdvancedExtensions().Build()));
    }

    public static string RenderPageContent(Page page, Func<string, HtmlTag>? proc)
    {
        var str = new StringBuilder();
        foreach (var c in page.Contents)
        {
            str.AppendLine(RenderMarkdown(c.Body));
            if (proc != null)
            {
                var tag = proc(c.Id?.ToString() ?? string.Empty)!;
                str.AppendLine(Div.Class("edit-segment").Append(tag).ToHtmlString());
            }
        }

        var rtr = str.ToString();
        Console.WriteLine(rtr);
        return rtr;
    }

    public static string RenderLastModified(Page page) => Div.Class("last-modified").Append("Last modified: " + page.LastModifiedUtc.ToString(DisplayDateFormat)).ToHtmlString();

    public static string RenderPageNamespace(Page page)
    {
        if (page.Ns is not object)
            return string.Empty;

        var div = Div.Class("namespace").Append($"Namespace: {page.Ns.Name}");
        return div.ToHtmlString();
    }

    public static string RenderDeletePageButton(Page page, AntiforgeryTokenSet antiForgery)
    {
        var antiForgeryField = Input.Hidden.Name(antiForgery.FormFieldName).Value(antiForgery.RequestToken);
        HtmlTag id = Input.Hidden.Name("Id").Value(page.Id.ToString());
        var submit = Div.Style("margin-top", "20px").Append(Button.Class("uk-button uk-button-danger").Append("Delete Page"));

        var form = Form
                .Attribute("method", "post")
                .Attribute("action", $"/delete-page")
                .Attribute("onsubmit", $"return confirm('Please confirm to delete this page');")
                    .Append(antiForgeryField)
                    .Append(id)
                    .Append(submit);

        return form.ToHtmlString();
    }

    public static string RenderPageAttachmentsForEdit(Page page, AntiforgeryTokenSet antiForgery)
    {
        if (page.Attachments.Count == 0)
            return string.Empty;

        var label = Span.Class("uk-label").Append("Attachments");
        var list = Ul.Class("uk-list");

        HtmlTag CreateEditorHelper(Attachment attachment) =>
        Span.Class("uk-inline")
            .Append(Span.Class("uk-form-icon").Attribute("uk-icon", "icon: copy"))
            .Append(Input.Text.Value($"[{attachment.FileName}](/attachment?fileId={attachment.FileId})")
                .Class("uk-input uk-form-small uk-form-width-large")
                .Style("cursor", "pointer")
                .Attribute("onclick", "copyMarkdownLink(this);")
            );

        static HtmlTag CreateDelete(int pageId, string attachmentId, AntiforgeryTokenSet antiForgery)
        {
            var antiForgeryField = Input.Hidden.Name(antiForgery.FormFieldName).Value(antiForgery.RequestToken);
            var id = Input.Hidden.Name("Id").Value(attachmentId.ToString());
            var name = Input.Hidden.Name("PageId").Value(pageId.ToString());

            var submit = Button.Class("uk-button uk-button-danger uk-button-small").Append(Span.Attribute("uk-icon", "icon: close; ratio: .75;"));
            var form = Form
                .Style("display", "inline")
                .Attribute("method", "post")
                .Attribute("action", $"/delete-attachment")
                .Attribute("onsubmit", $"return confirm('Please confirm to delete this attachment');")
                    .Append(antiForgeryField)
                    .Append(id)
                    .Append(name)
                    .Append(submit);

            return form;
        }

        foreach (var attachment in page.Attachments)
        {
            list = list.Append(Li
            .Append(CreateEditorHelper(attachment))
            .Append(CreateDelete(page.Id, attachment.FileId, antiForgery))
            );
        }
        return label.ToHtmlString() + list.ToHtmlString();
    }

    public static string RenderPageAttachments(Page page)
    {
        if (page.Attachments.Count == 0)
            return string.Empty;

        var label = Span.Class("uk-label").Append("Attachments");
        var list = Ul.Class("uk-list uk-list-disc");
        foreach (var attachment in page.Attachments)
        {
            list = list.Append(Li.Append(A.Href($"/attachment?fileId={attachment.FileId}").Append(attachment.FileName)));
        }
        return label.ToHtmlString() + list.ToHtmlString();
    }

    // Build the wiki input form 
    public static string BuildForm(PageInput input, string path, AntiforgeryTokenSet antiForgery, ModelStateDictionary? modelState = null)
    {
        bool IsFieldOK(string key) => modelState!.ContainsKey(key) && modelState[key].ValidationState == ModelValidationState.Invalid;

        var antiForgeryField = Input.Hidden.Name(antiForgery.FormFieldName).Value(antiForgery.RequestToken);

        var nameField = Div
        .Append(Label.Class("uk-form-label").Append(nameof(input.Name)))
        .Append(Div.Class("uk-form-controls")
            .Append(Input.Text.Class("uk-input").Name("Name").Value(input.Name))
        );

        var contentIdField = Input.Hidden.Name("ContentId").Value(input.ContentId ?? "");

        var contentField = Div
        .Append(Label.Class("uk-form-label").Append(nameof(input.Content)))
        .Append(Div.Class("uk-form-controls")
            .Append(Textarea.Name("Content").Class("uk-textarea").Append(input.Content))
        );

        var attachmentField = Div
        .Append(Label.Class("uk-form-label").Append(nameof(input.Attachment)))
        .Append(Div.Attribute("uk-form-custom", "target: true")
            .Append(Input.File.Name("Attachment"))
            .Append(Input.Text.Class("uk-input uk-form-width-large").Attribute("placeholder", "Click to select file").ToggleAttribute("disabled", true))
        );

        if (modelState is object && !modelState.IsValid)
        {
            if (IsFieldOK("Name"))
            {
                foreach (var er in modelState["Name"].Errors)
                {
                    nameField = nameField.Append(Div.Class("uk-form-danger uk-text-small").Append(er.ErrorMessage));
                }
            }

            if (IsFieldOK("Content"))
            {
                foreach (var er in modelState["Content"].Errors)
                {
                    contentField = contentField.Append(Div.Class("uk-form-danger uk-text-small").Append(er.ErrorMessage));
                }
            }
        }

        var submit = Div.Style("margin-top", "20px").Append(Button.Class("uk-button uk-button-primary").Append("Submit"));

        var form = Form
                .Class("uk-form-stacked")
                .Attribute("method", "post")
                .Attribute("enctype", "multipart/form-data")
                .Attribute("action", $"/{path}")
                    .Append(antiForgeryField)
                    .Append(nameField)
                    .Append(contentIdField)
                    .Append(contentField)
                    .Append(attachmentField);

        if (input.Id.HasValue)
        {
            HtmlTag id = Input.Hidden.Name("Id").Value(input.Id.ToString());
            form = form.Append(id);
        }

        form = form.Append(submit);

        return form.ToHtmlString();
    }
}