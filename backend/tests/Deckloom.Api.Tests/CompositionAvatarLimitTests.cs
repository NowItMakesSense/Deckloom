using Deckloom.Accounts.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Deckloom.Api.Tests;

// T-094 (pendencia do T-061): Avatar:MaxBytes chega ao limite do PostgresAvatarStorage de producao.
// O limite e verificado antes de qualquer acesso ao banco, entao nenhuma conexao e aberta (valores 100% ficticios).
public sealed class CompositionAvatarLimitTests
{
    [Fact]
    public async Task Avatar_max_bytes_setting_is_the_size_limit_of_the_production_avatar_storage()
    {
        using var factory = new LimitFactory(maxBytes: 100);
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAvatarStorage>();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync("avatar-key", new byte[101]));
    }

    private sealed class LimitFactory(int maxBytes) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSyntheticModuleSettings();
            builder.UseSetting("Avatar:MaxBytes", maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.ConfigureServices(services => services.RemoveDatabaseRoleCheck());
        }
    }
}
