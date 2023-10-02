// See https://aka.ms/new-console-template for more information

using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// builder.Logging
//     .AddJsonConsole();
//
// builder.Configuration
//     .AddJsonFile("appsettings.json")
//     .AddEnvironmentVariables();

builder.Services
    .AddSingleton<HttpServer>();

using var host = builder.Build();
await host.StartAsync();

using var scope = host.Services.CreateScope();
var httpServer = scope.ServiceProvider.GetRequiredService<HttpServer>();
await httpServer.Start().ConfigureAwait(true);

Console.WriteLine("Done.");

public class HttpServer
{
    private ILogger<HttpServer> Logger { get; }
    private IConfiguration Config { get; }

    public HttpServer(ILogger<HttpServer> logger, IConfiguration config)
    {
        Logger = logger;
        Config = config;

        Logger.LogTrace("HOST: {Host}", Host);
    }

    private IConfiguration InstanceSettings => Config.GetSection("InstanceSettings");
    private string? Host => InstanceSettings.GetValue<string>("Host");
    private string? Instance => InstanceSettings.GetValue<string>("Instance");
    private string? ConsumerProd => InstanceSettings.GetValue<string>("ConsumerProd");
    private string? AuditUserId => InstanceSettings.GetValue<string>("AuditUserId");
    private string? InstRtId => InstanceSettings.GetValue<string>("InstRtId");

    private async void LaunchCallCenter(string ani)
    {
        var launchData = new StringBuilder();
        try
        {
            launchData
                .Append("jhaxp:")
                .Append($"Instance={Instance}&")
                .Append(
                    "Msg=<StartCallLink xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"http://jackhenry.com/jxchange/JES/2008\">")
                .Append("<XPMsgRqHdr><XPHdr>")
                .Append($"<ConsumerProd>{ConsumerProd}</ConsumerProd>")
                .Append($"<AuditUsrId>{AuditUserId}</AuditUsrId>")
                .Append($"<InstRtId>{InstRtId}</InstRtId>")
                .Append($"</XPHdr></XPMsgRqHdr>")
                .Append($"<PhoneNum>{ani}</PhoneNum>")
                .Append($"<Identifier>{InstRtId}</Identifier>")
                .Append($"</StartCallLink>");

            Logger.LogDebug("Launch Data: {LaunchData}", launchData);

            Process.Start(new ProcessStartInfo()
            {
                FileName = launchData.ToString(),
                UseShellExecute = true
            });
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error Launching Target URL: {TargetUrl}", launchData);
        }
    }

    public async ValueTask Start()
    {
        var listener = new HttpListener();

        try
        {
            if (string.IsNullOrWhiteSpace(Host))
            {
                Logger.LogCritical("No HOST Configured");
                return;
            }

            listener.Prefixes.Add(new Uri(Host).ToString());
            listener.Start();

            Logger.LogInformation("Listening at {Host}", listener.Prefixes.FirstOrDefault());

            while (true)
            {
                try
                {
                    var context = await listener.GetContextAsync().ConfigureAwait(true);

                    var request = context.Request;
                    var response = context.Response;
                    response.ContentType = "text/json";

                    var ani = request.QueryString.Get("ANI");
                    if (!request.QueryString.HasKeys() || string.IsNullOrWhiteSpace(ani))
                    {
                        await context.SendResponse(new { success = false, message = "Missing ANI" });
                        continue;
                    }

                    LaunchCallCenter(ani);

                    await context.SendResponse(new { success = true });
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Error processing request");
                }
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error processing request");
        }
        finally
        {
            listener.Stop();
        }
    }
}


internal static class Helpers
{
    public static async ValueTask SendResponse<T>(this HttpListenerContext self, T objData)
    {
        var responseData = JsonSerializer.Serialize(objData);
        var responseBytes = Encoding.ASCII.GetBytes(responseData);
        self.Response.ContentLength64 = responseBytes.Length;
        await self.Response.OutputStream.WriteAsync(responseBytes).ConfigureAwait(true);
        self.Response.OutputStream.Close();
    }
}