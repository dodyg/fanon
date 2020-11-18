using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lunr;
using System.Globalization;

class Search
{
    Lunr.Index? _index;
    Wiki _wiki;

    public Search(Wiki wiki)
    {
        _wiki = wiki;
    }

    static string KebabToNormalCase(string txt) => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(txt.Replace('-', ' '));

    public async IAsyncEnumerable<Result> SearchTerm(string term)
    {
        if (_index is not object)
            throw new ArgumentNullException("Must call BuildIndex() at least once");

        await foreach (var result in _index.Search(term))
            yield return result;
    }

    public async Task BuildIndex()
    {
        _index = await Lunr.Index.Build(async builder =>
        {
            var all = _wiki.ListAllPages();
            Console.WriteLine("Pages Count " + all.Count);
            if (all.Count > 0)
            {
                builder
                    .AddField("id")
                    .AddField("pageName")
                    .AddField("content");

                foreach (var p in all)
                {
                    await builder.Add(new Document 
                    {
                    { "id", p.Id.ToString() },
                    { "pageName", p.NsName },
                    { "content", string.Join(' ', p.Contents) }
                    });
                }
            }
        });
    }
}