
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

record Namespace 
(
    [BsonId]int Id, string Name, string? Description
);

record Page
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

    public string[] GetContents () 
    {
        return Contents.Select(x => x.Body).ToArray();
    }

    public DateTime LastModifiedUtc { get; set; }

    public List<Attachment> Attachments { get; set; } = new();
}

record Content
{
    public ObjectId Id { get; set;} = ObjectId.NewObjectId();

    public Dictionary<string, string> Meta { get; set; } = new Dictionary<string, string>();

    public string Body { get; set; } = string.Empty;

    public Content()
    {
        
    }

    public Content(string body)
    {
        Body = body;
    }

}

record Attachment
(
    string FileId,

    string FileName,

    string MimeType,

    DateTime LastModifiedUtc
);

record PageInput(int? Id, string Name, string Content, IFormFile? Attachment)
{
    public static PageInput From(IFormCollection form)
    {
        var (id, name, content) = (form["Id"], form["Name"], form["Content"]);

        int? pageId = null;

        if (!StringValues.IsNullOrEmpty(id))
            pageId = Convert.ToInt32(id);

        IFormFile? file = form.Files["Attachment"];

        return new PageInput(pageId, name, content, file);
    }

    public static PageInput From(Page input) => new PageInput(input.Id, input.NsName, input.Contents[0].Body, null);
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