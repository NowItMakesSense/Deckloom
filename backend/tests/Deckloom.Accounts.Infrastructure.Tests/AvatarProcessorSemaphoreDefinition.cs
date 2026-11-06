namespace Deckloom.Accounts.Infrastructure.Tests;

// O SkiaAvatarImageProcessor usa um SemaphoreSlim estatico (compartilhado pelo processo); os testes de timeout
// seguram vagas, entao as classes que usam o processador nao podem rodar em paralelo entre si.
[CollectionDefinition("AvatarProcessorSemaphore", DisableParallelization = true)]
public sealed class AvatarProcessorSemaphoreDefinition;
