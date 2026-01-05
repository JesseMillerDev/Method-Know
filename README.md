# Method-Know

Method-Know is a shared knowledge app built for the Framework Bake-Off. Teams can create, browse, and search learning resources (articles, code snippets, and learning links), with AI-assisted tagging, summaries, and semantic search.

## Features
- Authenticated user accounts (signup/login)
- CRUD for resources with categories and tags
- Semantic search with vector embeddings
- Markdown editor and syntax highlighting
- Tagging + summarization background pipeline
- Cloud deployment via Fly.io (API) and Vercel (Web)

## Tech Stack
- API: .NET 9, Minimal APIs, EF Core, SQLite + sqlite-vec, JWT auth
- AI: Gemini for embeddings, tags, and summaries
- Web: Blazor WebAssembly, Tailwind CSS, EasyMDE, Marked, Prism

## Local Development
Prereqs:
- .NET 9 SDK
- Node.js (optional, only if you change Tailwind styles)

Run the API:
```bash
cd Know.ApiService
dotnet run
```

Run the Web app:
```bash
cd Know.Web
dotnet run
```

Optional Tailwind build:
```bash
cd Know.Web
npm install
npm run watch:css
```

If the API port changes, update `BackendUrl` in `Know.Web/wwwroot/appsettings.json`.

## Configuration
Recommended environment variables for the API:
- `Gemini__ApiKey` (required for embeddings, tags, summaries)
- `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`
- `Cors__AllowedOrigins__0` (and additional indexes as needed)
- `VectorDb__LibraryPath` (optional override for sqlite-vec library)

Note: `Know.ApiService/appsettings.json` currently includes a Gemini key for local testing; replace with your own and avoid committing secrets.

## Deployment
The repo includes a GitHub Actions workflow at `.github/workflows/deploy.yml` that:
- Deploys the API to Fly.io using `Know.ApiService/fly.toml`
- Publishes Blazor WASM and deploys the static output to Vercel

Required GitHub secrets:
- `FLY_API_TOKEN`
- `VERCEL_TOKEN`
- `VERCEL_ORG_ID`
- `VERCEL_PROJECT_ID`

Update `Know.Web/wwwroot/appsettings.Production.json` with your Fly.io API URL.

## Showcase (Bake-Off)
Pick 2-3 items and fill in details.

Recommended highlights:
- Developer experience: Minimal APIs + Blazor WASM keep the stack cohesive.
- Type safety and error prevention: Shared C# models across API and Web.
- Code maintainability: Shared models, isolated services, and clear endpoints.

Metrics:
- Total development time: TBD
- Estimated AI contribution: TBD
- AI usage: Gemini for tags, summaries, and semantic embeddings
- Estimated monthly hosting cost: TBD (Fly.io + Vercel)
- Initial page load time: TBD (measure with Lighthouse or WebPageTest)

## Testing Approach
Automated tests live in `Know.ApiService.Tests` and cover basic auth and article flows:
```bash
dotnet test
```

Use this manual checklist as a smoke test:
1. Signup and login successfully.
2. Create each resource type (Article, Code Snippet, Learning Resources).
3. Verify tags and summaries populate after background processing.
4. Search by keyword and confirm semantic matches are relevant.
5. Filter by category and tags.
6. Edit and delete a resource you own.
7. Verify code blocks render with syntax highlighting.
