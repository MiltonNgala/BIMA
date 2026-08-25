# BIMA Workspace Guidance

- Frontend code lives in `frontend` and uses React, TypeScript, and Vite.
- Backend code lives in `backend` and uses ASP.NET Core with C#.
- Keep insurance capabilities modular around policies, claims, billing, customers, products, users, audit, and tenant context.
- Prefer explicit application-service boundaries before adding persistence or external integrations.
- Keep sample data and local configuration clearly marked; never commit secrets.
- Use `npm.cmd` on Windows when PowerShell execution policy prevents `npm.ps1` from running.
- Validate frontend changes with `Set-Location frontend; npm.cmd run build` and backend changes with `dotnet build backend\backend.csproj`.
