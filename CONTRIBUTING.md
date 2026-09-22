# Contributing

Build before opening a pull request:

```bash
dotnet build MacAC.slnx -c Release
```

Then run the client against your DATs (`docs/building-and-running.md`) and,
if you touched networking or gameplay, log into a server and play for a bit.

Behavior follows the original client. If you change something a player would
notice in the world, say what the original did and how you checked it.

Don't commit `macac.pak`, DAT files, or anything from `dist/`. Package
versions live in `Directory.Packages.props`; after changing one, run
`dotnet restore MacAC.slnx --force-evaluate` and commit the lock files.

Keep PRs focused: one fix or one feature each.
