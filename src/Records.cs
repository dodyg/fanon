
using FluentValidation;
using LiteDB;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;

static class Collections 
{
    public const string Pages = "Pages";

    public const string Namespaces = "Namespaces";    
}

public record Namespace 
(
    [BsonId]int Id, string Name, string? Description
);

public record Page
{
    public int Id { get; set; }

    [BsonRef(Collections.Namespaces)]
    public Namespace? Ns { get; set; }

    public string Name { get; set; } = string.Empty;

    [BsonIgnore]
    public string NsName 
    { 
        get 
        {
            if (Ns is object)
                return Ns.Name + "/" + Name;
            else
                return Name;
        }
    }

    public List<Content> Contents { get; set; } = new List<Content>();

    public void UpdateOrInsertContent(Content content)
    {
        var idx = Contents.FindIndex(x => x.Id == content.Id);
        if (idx > -1)
        {
            var existing = Contents[idx];
            Contents[idx] = existing with 
            {
                Meta = content.Meta,
                Body = content.Body
            };
        }
        else 
            Contents.Add(content);
    }

    public string[] GetContents () 
    {
        return Contents.Select(x => x.Body).ToArray();
    }

    public DateTime LastModifiedUtc { get; set; }

    public List<Attachment> Attachments { get; set; } = new();
}

public record Content
{
    public ObjectId Id { get; set;} = ObjectId.NewObjectId();

    public Dictionary<string, string> Meta { get; set; } = new Dictionary<string, string>();

    public string Body { get; set; } = string.Empty;

    public Content()
    {
        
    }

    public Content(string? id, string body)
    {
        if (!string.IsNullOrWhiteSpace(id))
            Id = new ObjectId(id);
        
        Body = body;
    }
    public Content(string body)
    {
        Body = body;
    }

}

public record Attachment
(
    string FileId,

    string FileName,

    string MimeType,

    DateTime LastModifiedUtc
);

public record PageInput(int? Id, string Name, string? ContentId, string Content, IFormFile? Attachment)
{
    public static PageInput From(IFormCollection form)
    {
        var (id, name, contentId, content) = (form["Id"], form["Name"], form["ContentId"], form["Content"]);

        int? pageId = null;

        if (!StringValues.IsNullOrEmpty(id))
            pageId = Convert.ToInt32(id);

        IFormFile? file = form.Files["Attachment"];

        return new PageInput(pageId, name, contentId, content, file);
    }

    public static PageInput From(Page input, string? contentId) 
    {
        Content? cnt = null;

        if (!string.IsNullOrWhiteSpace(contentId))
        {
            cnt = input.Contents.Find(x => x.Id.ToString() == contentId);
        }

        return new PageInput(
            Id: input.Id, 
            Name: input.NsName, 
            ContentId: contentId, 
            Content: cnt?.Body ?? string.Empty, 
            Attachment: null
        );
    } 
}

class PageInputValidator : AbstractValidator<PageInput>
{
    public PageInputValidator(string pageName, string homePageName)
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
        if (pageName.Equals(homePageName, StringComparison.OrdinalIgnoreCase))
            RuleFor(x => x.Name).Must(name => name.Equals(homePageName)).WithMessage($"You cannot modify home page name. Please keep it {homePageName}");

        RuleFor(x => x.Content).NotEmpty().WithMessage("Content is required");
    }
}