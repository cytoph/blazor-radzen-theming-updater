using Blazor.Radzen.Theming.Updater.Commands;
using Blazor.Radzen.Theming.Updater.Models;
using Blazor.Radzen.Theming.Updater.Services;
using ConsoleAppFramework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Globalization;
using System.Reflection;

string applicationName = Assembly.GetExecutingAssembly().GetName().Name!;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("ApplicationName", applicationName)
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder();

#if DEBUG
    if (!builder.Environment.IsDevelopment())
    {
        builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true, reloadOnChange: true);
    }
#endif

    builder.Services.AddSerilog((services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();
    });

    builder.Services.AddHttpClient();

    // Configuration
    builder.Services.Configure<GeneralOptions>(builder.Configuration.GetSection(GeneralOptions.SectionName));
    builder.Services.Configure<PackageManifest>(builder.Configuration.GetSection(PackageManifest.SectionName));
    builder.Services.Configure<BasePackageManifest>(builder.Configuration.GetSection(BasePackageManifest.SectionName));
    builder.Services.Configure<StagingOptions>(builder.Configuration.GetSection(StagingOptions.SectionName));
    builder.Services.Configure<ArtifactOptions>(builder.Configuration.GetSection(ArtifactOptions.SectionName));
    builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection(GitHubOptions.SectionName));
    builder.Services.Configure<NuGetOptions>(builder.Configuration.GetSection(NuGetOptions.SectionName));

    // Singleton services
    builder.Services.AddSingleton<GitHubApiService>();
    builder.Services.AddSingleton<NuGetApiService>();
    builder.Services.AddSingleton<NuGetCliService>();

    // Scoped services
    builder.Services.AddScoped<FileService>();

    // Cocona Commands
    builder.Services.AddSingleton<CreateReleaseCommand>();

    var app = builder.ToConsoleAppBuilder();

    app.Add<CreateReleaseCommand>();

    await app.RunAsync(args);
}
catch (Exception ex)
{
    Log.Fatal(ex, "An unhandled exception occurred during execution");
}
finally
{
    Log.CloseAndFlush();
}
