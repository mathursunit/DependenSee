using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ServiceMap.Collector;
using ServiceMap.Core;
using ServiceMap.Core.Storage;
using ServiceMap.Engine;

var builder = Host.CreateApplicationBuilder(args);

// Run as a Windows Service when launched by the SCM; runs as a console app otherwise.
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Carrier DependenSee Collector";
});

builder.Services.Configure<CollectorOptions>(
    builder.Configuration.GetSection(CollectorOptions.SectionName));

// Single shared repository (writer). SQLite + WAL handles concurrent GUI reads.
builder.Services.AddSingleton(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CollectorOptions>>().Value;
    return new SampleRepository(opts.DatabasePath, readOnly: false);
});

builder.Services.AddSingleton(sp =>
{
    var repo = sp.GetRequiredService<SampleRepository>();
    return new CollectionEngine(PlatformFactory.Create(), repo);
});

builder.Services.AddHostedService<CollectorWorker>();

var host = builder.Build();
host.Run();
