---
description: Format check, build and test the solution
---

Run, in order, and stop at the first failure:

1. `dotnet format --verify-no-changes` (if it reports changes, run `dotnet format` and review the diff)
2. `dotnet build -p:CI=true` (warnings are errors, as in CI)
3. `dotnet test --no-build`

Report the test totals, or the first failure with its output. Do not change tests to make them pass without saying why.
