# Packages

One file per database package (stored-procedure package, schema module, etc.): `<name>.md`.

```
# <name>

Purpose: <why this package exists>
Contains: <functions/procedures it groups>
Introduced by feature: <feature-slug>
Side effects: <writes, external calls, none>
```

Static design description only - describe the package's contract, do not dump its real source if that would expose production logic better kept elsewhere. See `../README.md`.
