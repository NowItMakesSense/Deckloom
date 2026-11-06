using Deckloom.SharedKernel;

namespace Deckloom.Accounts.Domain;

public sealed record PlayerRegistered(PlayerId PlayerId, RegistrationMethod Method, string TermsVersion) : DomainEvent;
