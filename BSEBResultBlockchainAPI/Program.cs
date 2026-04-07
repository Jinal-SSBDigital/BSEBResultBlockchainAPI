
using BSEBResultBlockchainAPI.Helpers;
using BSEBResultBlockchainAPI.Services;
using BSEBResultBlockchainAPI.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ── Full Serilog setup from appsettings.json
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        //.Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithThreadId()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: "Logs/bseb-result-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            fileSizeLimitBytes: 10 * 1024 * 1024, // 10 MB per file
            outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{SourceContext}] [{ThreadId}] {Message:lj}{NewLine}{Exception}");
});

// EF Core
builder.Services.AddDbContext<AppDBContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("dbcs")));

// Helpers
builder.Services.AddScoped<DbHelper>();

// Fluree HTTP Client
builder.Services.AddHttpClient("FlureeClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

// Services
builder.Services.AddScoped<IFlureeService, FlureeService>();
builder.Services.AddScoped<IResultPublishService, ResultPublishService>();

// Background Job (runs once on startup — swap for Hangfire/Quartz for scheduled)
//builder.Services.AddHostedService<ResultPublishJob>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();