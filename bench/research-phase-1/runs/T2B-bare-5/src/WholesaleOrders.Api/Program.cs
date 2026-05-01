using WholesaleOrders.Api;
using WholesaleOrders.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddWholesaleOrders();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

app.MapControllers();

app.Run();

public partial class Program { }
