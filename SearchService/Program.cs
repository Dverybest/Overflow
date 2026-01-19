using System.Text.RegularExpressions;
using Common;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SearchService.Data;
using SearchService.Models;
using Typesense;
using Typesense.Setup;
using Wolverine;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.AddServiceDefaults();

var typesense = builder.Configuration["services:typesense:typesense:0"];

if (string.IsNullOrWhiteSpace(typesense))
{
    throw new InvalidOperationException("Typesense URI not found in config");
}

var typesenseApiKey = builder.Configuration["typesense-api-key"];

if (string.IsNullOrWhiteSpace(typesenseApiKey))
{
    throw new InvalidOperationException("Typesense ApiKey not found in config");
}

var uri = new Uri(typesense);

builder.Services.AddTypesenseClient(config =>
{
    config.ApiKey = typesenseApiKey;
    config.Nodes = new List<Node>
    {
        new (uri.Host,uri.Port.ToString(), uri.Scheme)
    };
});

await builder.UseWolverineWithRabbitMqAsync(opt =>
{
    opt.ListenToRabbitQueue("questions.search", cfg =>
    {
        cfg.BindExchange("questions");
    });
    opt.ApplicationAssembly = typeof(Program).Assembly;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

app.MapGet("/search", async (string query, ITypesenseClient client) =>
{
    string? tag = null;
    var tagMatch = Regex.Match(query, @"\[(.*?)\]");

    if (tagMatch.Success)
    {
        tag = tagMatch.Groups[1].Value;
        query = query.Replace(tagMatch.Value, string.Empty).Trim();
    }
    
    var searchParameter = new SearchParameters(query,"title,content");

    if (!string.IsNullOrWhiteSpace(tag))
    {
        searchParameter.FilterBy = $"tags:=[{tag}]";
    }

    try
    {
        var result = await client.Search<SearchQuestion>("questions", searchParameter);
        return Results.Ok(result.Hits.Select(hit => hit.Document));
    }
    catch (Exception e)
    {
       return Results.Problem("Typesense search failed",e.Message);
    }
});

app.MapGet("/search/similar-titles", async (string query, ITypesenseClient client) =>
{
    var searchParameter = new SearchParameters(query, "title");
    try
    {
        var result = await client.Search<SearchQuestion>("questions", searchParameter);
        return Results.Ok(result.Hits.Select(hit => hit.Document));
    }
    catch (Exception e)
    {
        return Results.Problem("Typesense search failed",e.Message);
    }
});

using var scope = app.Services.CreateScope();
var client = scope.ServiceProvider.GetRequiredService<ITypesenseClient>();

await SearchInitializier.EnsureIndexExistsAsync(client);

app.Run();

