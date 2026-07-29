using PolymarketLab.DataCollection.Infrastructure.DependencyInjection;
using PolymarketLab.DataCollection.Core.Application.DependencyInjection;
using PolymarketLab.Markets.Core.Application.DependencyInjection;
using PolymarketLab.Markets.Infrastructure.DependencyInjection;
using PolymarketLab.Markets.Presentation.Controllers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddControllers()
    .AddApplicationPart(typeof(MarketController).Assembly);
builder.Services.AddMarketsApplication();
builder.Services.AddMarketsInfrastructure(builder.Configuration);
builder.Services.AddDataCollectionApplication();
builder.Services.AddDataCollectionInfrastructure(builder.Configuration);
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "PolymarketLab API v1");
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
