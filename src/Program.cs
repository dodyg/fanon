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
using System.Text;

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
          UI.RenderPageContent(page, (contentId) =>  A.Href($"/edit?pageName={HomePageName}&contentId={contentId}").Append("Edit")),
          UI.RenderPageAttachments(page),
          UI.RenderPageNamespace(page),
          A.Href($"/edit?pageName={HomePageName}").Class("uk-button uk-button-default uk-button-small").Append("Add Segment").ToHtmlString()
        },
        atSidePanel: () => UI.AllPages(wiki)
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
    var contentId = context.Request.Query["contentId"];

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
          UI.BuildForm(PageInput.From(page!, contentId), path: $"{pageName}", antiForgery: antiForgery.GetAndStoreTokens(context)),
          UI.RenderPageAttachmentsForEdit(page!, antiForgery.GetAndStoreTokens(context))
        },
      atSidePanel: () =>
      {
          var list = new List<string>();
          // Do not show delete button on home page
          if (!pageName!.ToString().Equals(HomePageName, StringComparison.Ordinal))
              list.Add(UI.RenderDeletePageButton(page!, antiForgery: antiForgery.GetAndStoreTokens(context)));

          list.Add(Br.ToHtmlString());
          list.AddRange(UI.AllPagesForEditing(wiki));
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
            UI.RenderPageContent(page, (contentId) =>  A.Href($"/edit?pageName={pageName}&contentId={contentId}").Append("Edit")),
            UI.RenderPageAttachments(page),
            UI.RenderLastModified(page),
            UI.RenderPageNamespace(page),
            A.Href($"/edit?pageName={pageName}").Append("Edit").ToHtmlString()
          },
          atSidePanel: () => UI.AllPages(wiki)
        ).ToString());
    }
    else
    {
        await context.Response.WriteAsync(render.BuildEditorPage(pageName,
        atBody: () =>
          new[]
          {
            UI.BuildForm(new PageInput
            (
              Id: null, 
              Name: pageName, 
              ContentId: string.Empty, 
              Content: string.Empty, 
              Attachment: null
            ), 
            path: pageName, antiForgery: antiForgery.GetAndStoreTokens(context))
          },
        atSidePanel: () => UI.AllPagesForEditing(wiki)).ToString());
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
              UI.BuildForm(input, path: $"{pageName}", antiForgery: antiForgery.GetAndStoreTokens(context), modelState)
            },
          atSidePanel: () => UI.AllPages(wiki)).ToString());
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
