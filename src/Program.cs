using FluentValidation.AspNetCore;
using Ganss.XSS;
using HtmlBuilders;
using Markdig;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static HtmlBuilders.HtmlTags;
using Lunr;
using System.Globalization;

const string DisplayDateFormat = "MMMM dd, yyyy";
const string HomePageName = "home-page";

var builder = WebApplication.CreateBuilder(args);
builder.Services
  .AddSingleton<Wiki>()
  .AddSingleton<Render>()
  .AddSingleton<Search>()
  .AddAntiforgery()
  .AddMemoryCache();

builder.Logging.AddConsole().SetMinimumLevel(LogLevel.Warning);

var app = builder.Build();

var search = app.Services.GetService<Search>()!;
await search.BuildIndex();

// Load home page
app.MapGet("/", async context =>
{
    var wiki = context.RequestServices.GetService<Wiki>()!;
    var render = context.RequestServices.GetService<Render>()!;
    Page? page = wiki.GetPage(HomePageName);

    if (page is not object)
    {
        context.Response.Redirect($"/{HomePageName}");
        return;
    }

    await context.Response.WriteAsync(render.BuildPage(HomePageName, atBody: () =>
        new[]
        {
          RenderPageContent(page),
          RenderPageAttachments(page),
          RenderPageNamespace(page),
          A.Href($"/edit?pageName={HomePageName}").Class("uk-button uk-button-default uk-button-small").Append("Edit").ToHtmlString()
        },
        atSidePanel: () => AllPages(wiki)
      ).ToString());
});

static string KebabToNormalCase(string txt) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(txt.Replace('-', ' '));
app.MapGet("/search", async context =>
{
    var term = context.Request.Query["term"];
    var search = context.RequestServices.GetService<Search>()!;
    var render = context.RequestServices.GetService<Render>()!;
    var wiki = app.Services.GetService<Wiki>()!;
    var all = wiki.ListAllPages();

    var list = Ul;

    await foreach (Result result in search.SearchTerm(term))
    {
        var id = Convert.ToInt32(result.DocumentReference);
        var page = all.FirstOrDefault(x => x.Id == id);
        if (page is object)
        {
            list = list.Append(Li.Append(A.Href($"/{page.NsName}").Append(KebabToNormalCase(page.NsName))));
        }
    }

    await context.Response.WriteAsync(render.BuildPage($"Search '{term}'",
      atBody: () =>
      new[]
      {
       list.ToHtmlString()
      }
    ).ToString());
});

app.MapGet("/new-page", context =>
{
    var pageName = context.Request.Query["pageName"];
    if (StringValues.IsNullOrEmpty(pageName))
    {
        context.Response.Redirect("/");
        return Task.CompletedTask;
    }

    // Copied from https://www.30secondsofcode.org/c-sharp/s/to-kebab-case
    string ToKebabCase(string str)
    {
        Regex pattern = new Regex(@"[A-Z]{2,}(?=[A-Z][a-z]+[0-9]*|\b)|[A-Z]?[a-z]+[0-9]*|[A-Z]|[0-9]+");
        return string.Join("-", pattern.Matches(str)).ToLower();
    }

    var page = ToKebabCase(pageName);
    context.Response.Redirect($"/{page}");
    return Task.CompletedTask;
});

// Edit a wiki page
app.MapGet("/edit", async context =>
{
    var wiki = context.RequestServices.GetService<Wiki>()!;
    var render = context.RequestServices.GetService<Render>()!;

    var antiForgery = context.RequestServices.GetService<IAntiforgery>()!;
    var pageName = context.Request.Query["pageName"];

    Page? page = wiki.GetPage(pageName);
    if (page is not object)
    {
        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        return;
    }

    await context.Response.WriteAsync(render.BuildEditorPage(pageName,
      atBody: () =>
        new[]
        {
          BuildForm(PageInput.From(page!), path: $"{pageName}", antiForgery: antiForgery.GetAndStoreTokens(context)),
          RenderPageAttachmentsForEdit(page!, antiForgery.GetAndStoreTokens(context))
        },
      atSidePanel: () =>
      {
          var list = new List<string>();
          // Do not show delete button on home page
          if (!pageName!.ToString().Equals(HomePageName, StringComparison.Ordinal))
              list.Add(RenderDeletePageButton(page!, antiForgery: antiForgery.GetAndStoreTokens(context)));

          list.Add(Br.ToHtmlString());
          list.AddRange(AllPagesForEditing(wiki));
          return list;
      }).ToString());
});

// Deal with attachment download
app.MapGet("/attachment", async context =>
{
    var fileId = context.Request.Query["fileId"];
    var wiki = context.RequestServices.GetService<Wiki>()!;

    var file = wiki.GetFile(fileId);
    if (file is not object)
    {
        context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        return;
    }

    app!.Logger.LogInformation("Attachment " + file.Value.meta.Id + " - " + file.Value.meta.Filename);
    context.Response.Headers.Append(HeaderNames.ContentType, file.Value.meta.MimeType);
    await context.Response.Body.WriteAsync(file.Value.file);
});

// Load a wiki page
app.MapGet("/{**pageName}", async context =>
{
    var wiki = context.RequestServices.GetService<Wiki>()!;
    var render = context.RequestServices.GetService<Render>()!;
    var antiForgery = context.RequestServices.GetService<IAntiforgery>()!;

    var pageName = context.Request.RouteValues["pageName"] as string ?? "";

    Page? page = wiki.GetPage(pageName);

    if (page is object)
    {
        await context.Response.WriteAsync(render.BuildPage(pageName, atBody: () =>
          new[]
          {
            RenderPageContent(page),
            RenderPageAttachments(page),
            RenderLastModified(page),
            RenderPageNamespace(page),
            A.Href($"/edit?pageName={pageName}").Append("Edit").ToHtmlString()
          },
          atSidePanel: () => AllPages(wiki)
        ).ToString());
    }
    else
    {
        await context.Response.WriteAsync(render.BuildEditorPage(pageName,
        atBody: () =>
          new[]
          {
            BuildForm(new PageInput
            (
              Id: null, 
              Name: pageName, 
              ContentId: string.Empty, 
              Content: string.Empty, 
              Attachment: null
            ), 
            path: pageName, antiForgery: antiForgery.GetAndStoreTokens(context))
          },
        atSidePanel: () => AllPagesForEditing(wiki)).ToString());
    }
});

// Delete a page
app.MapPost("/delete-page", async context =>
{
    var antiForgery = context.RequestServices.GetService<IAntiforgery>()!;
    await antiForgery.ValidateRequestAsync(context);
    var wiki = context.RequestServices.GetService<Wiki>()!;
    var id = context.Request.Form["Id"];

    if (StringValues.IsNullOrEmpty(id))
    {
        app.Logger.LogWarning($"Unable to delete page because form Id is missing");
        context.Response.Redirect("/");
        return;
    }

    var (isOk, exception) = wiki.DeletePage(Convert.ToInt32(id), HomePageName);

    if (!isOk && exception is object)
        app.Logger.LogError(exception, $"Error in deleting page id {id}");
    else if (!isOk)
        app.Logger.LogError($"Unable to delete page id {id}");

    if (isOk)
      await context.RequestServices.GetService<Search>()!.BuildIndex();
   
    context.Response.Redirect("/");
});

app.MapPost("/delete-attachment", async context =>
{
    var antiForgery = context.RequestServices.GetService<IAntiforgery>()!;
    await antiForgery.ValidateRequestAsync(context);
    var wiki = context.RequestServices.GetService<Wiki>()!;
    var id = context.Request.Form["Id"];

    if (StringValues.IsNullOrEmpty(id))
    {
        app.Logger.LogWarning($"Unable to delete attachment because form Id is missing");
        context.Response.Redirect("/");
        return;
    }

    var pageId = context.Request.Form["PageId"];
    if (StringValues.IsNullOrEmpty(pageId))
    {
        app.Logger.LogWarning($"Unable to delete attachment because form PageId is missing");
        context.Response.Redirect("/");
        return;
    }

    var (isOk, page, exception) = wiki.DeleteAttachment(Convert.ToInt32(pageId), id.ToString());

    if (!isOk)
    {
        if (exception is object)
            app.Logger.LogError(exception, $"Error in deleting page attachment id {id}");
        else
            app.Logger.LogError($"Unable to delete page attachment id {id}");

        if (page is object)
            context.Response.Redirect($"/{page.Name}");
        else
            context.Response.Redirect("/");

        return;
    }

    context.Response.Redirect($"/{page!.NsName}");
});

// Add or update a wiki page
app.MapPost("/{**pageName}", async context =>
{
    var pageName = context.Request.RouteValues["pageName"] as string ?? "";
    var wiki = context.RequestServices.GetService<Wiki>()!;
    var render = context.RequestServices.GetService<Render>()!;
    var antiForgery = context.RequestServices.GetService<IAntiforgery>()!;
    await antiForgery.ValidateRequestAsync(context);

    PageInput input = PageInput.From(context.Request.Form);

    var modelState = new ModelStateDictionary();
    var validator = new PageInputValidator(pageName, HomePageName);
    validator.Validate(input).AddToModelState(modelState, null);

    if (!modelState.IsValid)
    {
        await context.Response.WriteAsync(render.BuildEditorPage(pageName,
          atBody: () =>
            new[]
            {
              BuildForm(input, path: $"{pageName}", antiForgery: antiForgery.GetAndStoreTokens(context), modelState)
            },
          atSidePanel: () => AllPages(wiki)).ToString());
        return;
    }

    var (isOk, p, ex) = wiki.SavePage(input);
    if (!isOk)
    {
        app.Logger.LogError(ex, "Problem in saving page");
        return;
    }

    await context.RequestServices.GetService<Search>()!.BuildIndex();

    context.Response.Redirect($"/{p!.NsName}");
});

await app.RunAsync();

// End of the web part

static string[] AllPages(Wiki wiki) => new[]
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

static string[] AllPagesForEditing(Wiki wiki)
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

static string RenderMarkdown(string[] str)
{
    Console.WriteLine($"length {str.Length} " + str[0]);
    var sanitizer = new HtmlSanitizer();
    return sanitizer.Sanitize(Markdown.ToHtml(string.Join("\n", str), new MarkdownPipelineBuilder().UseSoftlineBreakAsHardlineBreak().UseAdvancedExtensions().Build()));
}

static string RenderPageContent(Page page) => RenderMarkdown(page.GetContents());

static string RenderLastModified(Page page) => Div.Class("last-modified").Append("Last modified: " + page.LastModifiedUtc.ToString(DisplayDateFormat)).ToHtmlString();

static string RenderPageNamespace(Page page)
{
    if (page.Ns is not object)
        return string.Empty;

    var div = Div.Class("namespace").Append($"Namespace: {page.Ns.Name}");
    return div.ToHtmlString();
}

static string RenderDeletePageButton(Page page, AntiforgeryTokenSet antiForgery)
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

static string RenderPageAttachmentsForEdit(Page page, AntiforgeryTokenSet antiForgery)
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

static string RenderPageAttachments(Page page)
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
static string BuildForm(PageInput input, string path, AntiforgeryTokenSet antiForgery, ModelStateDictionary? modelState = null)
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