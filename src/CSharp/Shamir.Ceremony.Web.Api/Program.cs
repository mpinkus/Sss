
using FluentValidation;
using Microsoft.Extensions.Options;
using Prometheus;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;
using Shamir.Ceremony.Common.Configuration;
using Shamir.Ceremony.Common.Configuration.Validators;
using Shamir.Ceremony.Common.Services;
using Shamir.Ceremony.Common.Storage;
using Shamir.Ceremony.Web.Api.Hubs;
using Shamir.Ceremony.Web.Api.Services;

namespace Shamir.Ceremony.Web.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.File("logs/webapi-.log", 
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.GrafanaLoki(Environment.GetEnvironmentVariable("LOKI_URL") ?? "http://localhost:3100")
    .CreateLogger();

builder.Host.UseSerilog();

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddSignalR();

            builder.Services.Configure<MongoDbSettings>(
                builder.Configuration.GetSection("MongoDb"));
            builder.Services.Configure<SecuritySettings>(
                builder.Configuration.GetSection("Security"));
            builder.Services.Configure<FileSystemSettings>(
                builder.Configuration.GetSection("FileSystem"));
            builder.Services.Configure<OrganizationSettings>(
                builder.Configuration.GetSection("Organization"));

            builder.Services.AddSingleton<IValidator<MongoDbSettings>, MongoDbSettingsValidator>();
            builder.Services.AddSingleton<IValidator<SecuritySettings>, SecuritySettingsValidator>();
            builder.Services.AddSingleton<IValidator<FileSystemSettings>, FileSystemSettingsValidator>();
            builder.Services.AddSingleton<IValidator<OrganizationSettings>, OrganizationSettingsValidator>();
            builder.Services.AddSingleton<IValidator<CeremonyConfiguration>, CeremonyConfigurationValidator>();
            
            builder.Services.AddSingleton<IKeyValueStore, MongoDbKeyValueStore>();
            builder.Services.AddScoped<CeremonyService>();
            builder.Services.AddScoped<IStructuredLogger, StructuredLogger>();

            builder.Services.AddHealthChecks()
                .AddCheck<FileSystemHealthCheck>("filesystem");

            builder.Services.AddSingleton<FileSystemHealthCheck>();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowBlazorClient", policy =>
                {
                    policy.WithOrigins("https://localhost:7001", "http://localhost:5001")
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });

            var app = builder.Build();

app.UseRouting();
app.UseHttpMetrics();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready");
app.MapMetrics();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseCors("AllowBlazorClient");
            app.UseAuthorization();

            app.MapControllers();
            app.MapHub<CeremonyHub>("/ceremonyhub");

            app.Run();
        }
    }
}
