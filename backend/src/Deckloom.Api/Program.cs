using Deckloom.Api.Composition;

var builder = WebApplication.CreateBuilder(args);
ApiHost.ConfigureServices(builder);

var app = builder.Build();
ApiHost.ConfigurePipeline(app);

app.Run();

// Torna o ponto de entrada acessivel ao WebApplicationFactory dos testes de integracao.
public partial class Program;
