# This folder is superseded

The PowerShell/Pester harness that used to live here has been **replaced by C# test
projects** under [`../tests-dotnet/`](../tests-dotnet/README.md), runnable from
Visual Studio:

- **LogicAppUnit.Tests** — real engine, mocked edges (fast)
- **RealHost.IntegrationTests** — true integration via a real `func host` (Corvus)

The `*.ps1` / `*.psm1` files here are now empty stubs and can be deleted.

`sample-requests.http` is kept for manual testing with the VS Code REST Client.
