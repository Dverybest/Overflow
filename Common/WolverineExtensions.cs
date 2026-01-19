using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Wolverine;
using Wolverine.RabbitMQ;

namespace Common;

public static class WolverineExtensions
{
    public static async Task UseWolverineWithRabbitMqAsync(
        this IHostApplicationBuilder builder, Action<WolverineOptions> configureMessaging)
    {
        var retryPolicy = Policy
            .Handle<BrokerUnreachableException>()
            .Or<SocketException>()
            .WaitAndRetryAsync(
                retryCount:5,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (exception, time, retryCount) =>
                {
                    Console.WriteLine($"Retry attempt {retryCount} failed. " +
                                      $"Retrying in {time.Seconds} seconds...");
                });
        
        await retryPolicy.ExecuteAsync(async () =>
        {
            var endpoint = builder.Configuration.GetConnectionString("messaging")
                           ?? throw new InvalidOperationException("messaging connection string not found");

            var factory = new ConnectionFactory
            {
                Uri = new Uri(endpoint)
            };
            await using var connection = await factory.CreateConnectionAsync();
        });
    
        builder.Services.AddOpenTelemetry().WithTracing(traceProviderBuilder =>
        {
            traceProviderBuilder.SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService(builder.Environment.ApplicationName))
                .AddSource("wolverine");
        });
        
        builder.UseWolverine(options =>
        {
            options.UseRabbitMqUsingNamedConnection("messaging")
                .AutoProvision()
                .DeclareExchange("questions");
            
            configureMessaging(options);
        });
    }
}