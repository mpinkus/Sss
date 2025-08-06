
using FluentValidation;
using Shamir.Ceremony.Common.Configuration;
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

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();
            builder.Services.AddSignalR();

            builder.Services.Configure<MongoDbSettings>(
                builder.Configuration.GetSection("MongoDb"));

            builder.Services.AddSingleton<IValidator<MongoDbSettings>, MongoDbSettingsValidator>();
            builder.Services.AddSingleton<IKeyValueStore, MongoDbKeyValueStore>();
            builder.Services.AddScoped<CeremonyService>();

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
