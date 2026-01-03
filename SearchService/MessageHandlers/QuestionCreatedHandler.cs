using System.Text.RegularExpressions;
using Contracts;
using SearchService.Models;
using Typesense;

namespace SearchService.MessageHandlers;

public class QuestionCreatedHandler(ITypesenseClient client)
{
    public async Task HandleAsync(QuestionCreated message)
    {
        var created = new DateTimeOffset(message.Created).ToUnixTimeSeconds();

        var doc = new SearchQuestion
        {
            Id = message.QuestionId,
            CreatedAt = created,
            Title = message.Title,
            Content = StripHtml(message.Content),
            Tags = message.Tags.ToArray()
        };

        await client.CreateDocument("questions", doc);
    }

    private static string StripHtml(string messageContent)
    {
       return Regex.Replace(messageContent, @"<.*?>", string.Empty);
    }
}