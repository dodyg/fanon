using Microsoft.AspNetCore.Html;
using Scriban;
using System;
using System.Collections.Generic;
using System.Globalization;

class Render
{
    static string KebabToNormalCase(string txt) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(txt.Replace('-', ' '));

    static string[] MarkdownEditorHead() => new[]
    {
      @"<link rel=""stylesheet"" href=""https://unpkg.com/easymde/dist/easymde.min.css"">",
      @"<script src=""https://unpkg.com/easymde/dist/easymde.min.js""></script>"
    };

    static string[] MarkdownEditorFoot() => new[]
    {
      @"<script>
        var easyMDE = new EasyMDE({
          insertTexts: {
            link: [""["", ""]()""]
          }
        });

        function copyMarkdownLink(element) {
          element.select();
          document.execCommand(""copy"");
        }
        </script>"
    };

    (Template head, Template body, Template layout) _templates = (
      head: Scriban.Template.Parse(@"
        <meta charset=""utf-8"">
        <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
        <title>{{ title }}</title>
        <link rel=""stylesheet"" href=""https://cdn.jsdelivr.net/npm/uikit@3.5.5/dist/css/uikit.min.css"" />
        {{ header }}
        <style>
          .last-modified { font-size: small; }
          a:visited { color: blue; }
          a:link { color: red; }
        </style>
      "),
      body: Scriban.Template.Parse(@"
      <nav class=""uk-navbar-container"">
        <div class=""uk-container"">
          <div class=""uk-navbar"">
            <div class=""uk-navbar-left"">
              <ul class=""uk-navbar-nav"">
                <li class=""uk-active""><a href=""/""><span uk-icon=""home""></span></a></li>
              </ul>
            </div>
            <div class=""uk-navbar-center"">
              <div class=""uk-navbar-item"">
                <form action=""/new-page"">
                  <input class=""uk-input uk-form-width-large"" type=""text"" name=""pageName"" placeholder=""Type desired page title here""></input>
                  <input type=""submit""  class=""uk-button uk-button-default"" value=""Add New Page"">
                </form>
              </div>
            </div>
          </div>
        </div>
      </nav>
      {{ if at_side_panel != """" }}
        <div class=""uk-container"">
        <div uk-grid>
          <div class=""uk-width-4-5"">
            <h1>{{ page_name }}</h1>
            {{ content }}
          </div>
          <div class=""uk-width-1-5"">
            {{ at_side_panel }}
          </div>
        </div>
        </div>
      {{ else }}
        <div class=""uk-container"">
          <h1>{{ page_name }}</h1>
          {{ content }}
        </div>
      {{ end }}
            
      <script src=""https://cdn.jsdelivr.net/npm/uikit@3.5.5/dist/js/uikit.min.js""></script>
      <script src=""https://cdn.jsdelivr.net/npm/uikit@3.5.5/dist/js/uikit-icons.min.js""></script>    
      {{ at_foot }}
      "),
      layout: Scriban.Template.Parse(@"
      <!DOCTYPE html>
        <head>
          {{ head }}
        </head>
        <body>
          {{ body }}
        </body>
      </html>
    ")
    );

    // Use only when the page requires editor
    public HtmlString BuildEditorPage(string title, Func<IEnumerable<string>> atBody, Func<IEnumerable<string>>? atSidePanel = null) =>
      BuildPage(
        title,
        atHead: () => MarkdownEditorHead(),
        atBody: atBody,
        atSidePanel: atSidePanel,
        atFoot: () => MarkdownEditorFoot()
        );

    // General page layout building function
    public HtmlString BuildPage(string title, Func<IEnumerable<string>>? atHead = null, Func<IEnumerable<string>>? atBody = null, Func<IEnumerable<string>>? atSidePanel = null, Func<IEnumerable<string>>? atFoot = null)
    {
        var head = _templates.head.Render(new
        {
            title,
            header = string.Join("\r", atHead?.Invoke() ?? new[] { "" })
        });

        var body = _templates.body.Render(new
        {
            PageName = KebabToNormalCase(title),
            Content = string.Join("\r", atBody?.Invoke() ?? new[] { "" }),
            AtSidePanel = string.Join("\r", atSidePanel?.Invoke() ?? new[] { "" }),
            AtFoot = string.Join("\r", atFoot?.Invoke() ?? new[] { "" })
        });

        return new HtmlString(_templates.layout.Render(new { head, body }));
    }
}
