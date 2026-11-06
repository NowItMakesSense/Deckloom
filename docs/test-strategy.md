# Test strategy guide

Guides how the AISEF `backend`, `frontend` and `qa` agents set up and grow the test suite for this project.

## Test pyramid
- **Unit (many)**: one behavior at a time, no IO, fast. Written first, in the TDD red phase, by the implementing agent.
- **Integration (some)**: real collaborators within a bounded context, persistence via fakes/in-memory/disposable infra (ADR-0012) - never the system database. Owned by `qa` after implementation.
- **End-to-end (few)**: full flow through the real interface (API/UI/CLI), still never against the system database or production services. Owned by `qa`.

## Fixtures and test doubles
- Prefer fakes/in-memory implementations of ports over mocks where behavior matters; use mocks/stubs only to verify interaction, not state.
- Name test doubles by role (`InMemoryUserRepository`, not `FakeThing1`); keep them next to the port they implement so they evolve together.
- Persistence-dependent tests use in-memory fakes or EF Core on SQLite in-memory (ADR-0012) - never the live system DB.
- **Every fake of a persistence port has a contract suite** (retro conta-jogador, 2026-11-01): one abstract test class per port, run once against the in-memory fake and once against the real adapter (EF Core on SQLite in-memory). The suite exercises the port exactly as the use cases call it - e.g. `IUnitOfWork.ExecuteAsync` with work that only queues changes and never calls `CommitAsync`. A behavior the fake has and the adapter lacks is a bug, even with every unit test green. Write the contract test before adding a fake or an adapter method.

## Real composition (for `qa`)
Hosts that swap adapters for fakes or rewrite the request (e.g. forcing `https`) hide defects of the real composition. Keep at least one path that runs the production composition (real EF adapters on SQLite, plain `http` like the local compose) through the main flows, and an API test asserting that every `Set-Cookie` with the `__Host-` prefix carries `Secure`, `Path=/` and no `Domain` (retro conta-jogador: an unsecured `__Host-dl_csrf` broke every mutation while the suite stayed green).

## Where tests live
Follow the stack's own convention (mirror source layout, e.g. `tests/` or co-located `*_test`/`*.spec` files) - the chosen layout is recorded in the feature's Engineering Blueprint the first time it matters, so later features stay consistent.

## Coverage breadth (for `qa`)
Beyond the happy path each unit test covers, `qa` is responsible for: edge cases and boundary values, invalid/malicious input, concurrency/race conditions where relevant, error-handling and failure-mode paths, and regression scenarios tied to past bugs. Record new scenarios in the feature's Feature Contract if they reveal a gap in the original spec (then get it approved) rather than only adding ad-hoc test code.

## Running suites in parallel (QA-11)
Whoever runs several vitest suites at once (e.g. a load check, or alongside `dotnet test`) sets `VITEST_MAX_WORKERS=2` for each of them. The default is CPUs-1 workers per suite, so N suites oversubscribe the CPU and tests time out at 5 s even though they are not slow (measured: 4 suites x 7 workers on 8 logical CPUs; with 2 workers per suite there was no timeout). Do not raise `testTimeout`/`asyncUtilTimeout` to compensate.
