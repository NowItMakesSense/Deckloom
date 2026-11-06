var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/health");
app.Run();

// Torna o ponto de entrada acessivel ao WebApplicationFactory dos testes.
public partial class Program;
