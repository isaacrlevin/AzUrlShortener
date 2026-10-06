# TinyBlazorAdmin
<!-- ALL-CONTRIBUTORS-BADGE:START - Do not remove or modify this section -->
[![All Contributors](https://img.shields.io/badge/all_contributors-6-orange.svg?style=flat-square)](#contributors-)
<!-- ALL-CONTRIBUTORS-BADGE:END -->

 Admin tools for [Azure Url Shortener](https://github.com/microsoft/AzUrlShortener) using [Blazor Single Page Application (webassembly)](https://azure.microsoft.com/services/app-service/static/?WT.mc_id=dotnet-0000-frbouche).

The project is now at version 3 and ready to be used! It is using Azure Static Web App native security and the API is an Azure Function.

![Tiny Blazor Admin home page][tinyBA_home]

Once authenticated you can manage your URLs and see statistics using [MudBlazor](https://mudblazor.com/), an open-source component library with no license key required. This project targets .NET 10.

The URL manager initially retrieves only the newest 100 active links (by storage timestamp). Sorting, per-column filtering, and paging request the matching page from the API rather than downloading the complete list. Page sizes of 15, 30, 50, and 100 are available. Azure Table Storage cannot order by arbitrary columns, so the API scans active links server-side to calculate the matching count and sorted page; only that page is sent to the browser. Create, edit, archive, clipboard copy, social posting, and message generation remain available.

Statistics initially show all clicks. The date-range picker filters by inclusive local calendar dates and updates the daily chart, category charts, and 50-row click-data grid together; clearing the range restores all clicks. URL-specific statistics use the same behavior.

To build locally, run `dotnet build src\Cloud5mins.ShortenerTools.TinyBlazorAdmin\Cloud5mins.ShortenerTools.TinyBlazorAdmin.csproj` with the .NET 10 SDK. API requests use the browser's origin so they pass through Azure Static Web Apps (or its local emulator), including its authentication and route rules. The old `API_Prefix` development setting is no longer used.

## Local development and debugging with Aspire

Install the .NET 10 SDK, Aspire CLI, Node.js 22 or newer, Azure Functions Core Tools v4, and Docker Desktop with Linux containers running. From the repository root, install the pinned SWA CLI:

```powershell
Set-Location src
npm ci
Set-Location ..
```

Open `src\shortenerTools.sln` in an Aspire-compatible Visual Studio version, set `shortenerTools.AppHost` as the startup project, select its `http` launch profile, and press F5. Set breakpoints in Functions or the Blazor client; use the IDE's supported Blazor WebAssembly debugging and Hot Reload features. Do not start a second Functions or frontend instance alongside the AppHost.

For terminal-based development:

```powershell
aspire start --apphost src\shortenerTools.AppHost\shortenerTools.AppHost.csproj --launch-profile http
aspire wait swa --apphost src\shortenerTools.AppHost\shortenerTools.AppHost.csproj
```

The CLI starts the processes but does not attach an IDE debugger. Use the IDE-managed AppHost launch for debugging.

Open the **swa** endpoint in the dashboard, not the direct **admin** endpoint. Aspire allocates the emulator port automatically to avoid occupied or Windows-reserved ports. Its endpoint proxy exposes the dashboard's `localhost` address while SWA listens on IPv4 loopback, avoiding connection refusals when a browser resolves `localhost` to IPv6. Aspire starts the Blazor development server and Functions; the SWA CLI only proxies them and emulates `/.auth`, `/api`, and `staticwebapp.config.json` routing. Backend ports are resolved by Aspire rather than hard-coded. The frontend can start independently of the API; the SWA emulator waits for both.

Wait until **swa** is healthy before opening its endpoint. The first Ollama model download can delay Functions and SWA startup. Reopen the endpoint from the current dashboard after restarting: automatically allocated URLs from previous runs are no longer valid. If the endpoint refuses connections, check that the AppHost is still running and inspect **swa** console logs in the dashboard.

Visit `/.auth/login/aad` on the **swa** endpoint, enter a mock identity, and add `admin` to its roles before opening URL Manager or Statistics. Authentication is simulated locally, not a real Entra login. Use `/.auth/logout` to test signing out.

By default, Azurite is used for both Functions host storage and the application's `DataStorage`, so local URL changes do not use a production storage connection from `local.settings.json`. The scheduled social-posting timer is disabled by the AppHost; production configuration is unchanged. SWA CLI may print a non-HTTP-trigger warning because the separately hosted Functions app also contains this timer. That warning is non-fatal: SWA proxies the HTTP API rather than hosting managed functions. Other external integrations still require their own credentials when explicitly exercised.

### Using production storage data

To test statistics against real links and clicks, store the production storage connection string in the AppHost's user secrets (never in source control):

```powershell
dotnet user-secrets set "ConnectionStrings:production-data" "<production storage connection string>" --project src\shortenerTools.AppHost
```

Then launch the AppHost with the `http-production-data` profile (select it in the IDE, or pass `--launch-profile http-production-data` to `aspire start`). Only the application's `DataStorage` switches to production; Functions host storage stays on Azurite and the scheduled social-posting timer stays disabled.

> **Warning:** this is live data. Viewing statistics is read-only, but creating, editing, or archiving URLs, and following short links through the local `UrlRedirect` function (which records clicks), change production data. If the secret is missing, Aspire marks the `production-data` resource as missing and Functions does not start.

Ollama runs locally and persists its model data in a Docker volume. The first start downloads `llama3` and may take several minutes; Functions waits for the model. CPU mode is the default. To opt into NVIDIA GPU acceleration when your Docker runtime supports it:

```powershell
dotnet user-secrets set "Ollama:UseGpu" "true" --project src\shortenerTools.AppHost
```

If you prefer a stable emulator URL, set `Swa:Port` in the AppHost's user secrets to a free, unreserved port. Otherwise use the allocated dashboard endpoint. Use the dashboard for resource logs and lifecycle controls. Stop the terminal-started app with:

```powershell
aspire stop --apphost src\shortenerTools.AppHost\shortenerTools.AppHost.csproj
```

The optional `src\swa-cli.config.json` is for a separate, manually started frontend on port 5000 and API on port 7071 (`npx swa start AzUrlShortener` from `src`). Aspire deliberately does not use that configuration or let SWA launch duplicate application processes.

![Tiny Blazor Admin URLs manager page][tinyBA_urls]

![Tiny Blazor Admin Statistics page][tinyBA_stats]


# Deployment

Until an automatic deployment is created, you will need to deploy some part manually. [All the steps to deploy the TinyBlazorAdmin app into Azure are listed here](https://github.com/microsoft/AzUrlShortener/wiki/How-to-deploy-TinyBlazorAdmin). You can also run it somewhere else if you prefer, even locally.

# Contributing

If you find a bug or would like to add a feature, check out those resources:

[tinyBA_home]: /Media/tinyBA_home.png
[tinyBA_stats]: /Media/tinyBA_stats.png
[tinyBA_urls]: /Media/tinyBA_urls.png

## Contributors ✨

Thanks goes to these wonderful people ([emoji key](https://allcontributors.org/docs/en/emoji-key)):

<!-- ALL-CONTRIBUTORS-LIST:START - Do not remove or modify this section -->
<!-- prettier-ignore-start -->
<!-- markdownlint-disable -->
<table>
  <tbody>
    <tr>
      <td align="center"><a href="http://cloud5mins.com"><img src="https://avatars3.githubusercontent.com/u/2404846?v=4?s=100" width="100px;" alt="Frank Boucher"/><br /><sub><b>Frank Boucher</b></sub></a><br /><a href="https://github.com/FBoucher/TinyBlazorAdmin/commits?author=FBoucher" title="Documentation">📖</a> <a href="https://github.com/FBoucher/TinyBlazorAdmin/commits?author=FBoucher" title="Code">💻</a> <a href="#ideas-FBoucher" title="Ideas, Planning, & Feedback">🤔</a></td>
      <td align="center"><a href="http://www.mayoclinic.org"><img src="https://avatars3.githubusercontent.com/u/765798?v=4?s=100" width="100px;" alt="jbrule"/><br /><sub><b>jbrule</b></sub></a><br /><a href="https://github.com/FBoucher/TinyBlazorAdmin/commits?author=jbrule" title="Documentation">📖</a></td>
      <td align="center"><a href="https://cmatskas.com"><img src="https://avatars3.githubusercontent.com/u/4126750?v=4?s=100" width="100px;" alt="Christos Matskas"/><br /><sub><b>Christos Matskas</b></sub></a><br /><a href="#security-cmatskas" title="Security">🛡️</a> <a href="https://github.com/FBoucher/TinyBlazorAdmin/issues?q=author%3Acmatskas" title="Bug reports">🐛</a></td>
      <td align="center"><a href="https://github.com/ronhowe"><img src="https://avatars1.githubusercontent.com/u/5210043?v=4?s=100" width="100px;" alt="Ron Howe"/><br /><sub><b>Ron Howe</b></sub></a><br /><a href="https://github.com/FBoucher/TinyBlazorAdmin/commits?author=ronhowe" title="Documentation">📖</a></td>
      <td align="center"><a href="https://github.com/Mark-Phillipson"><img src="https://avatars0.githubusercontent.com/u/16239024?v=4?s=100" width="100px;" alt="Mark Phillipson"/><br /><sub><b>Mark Phillipson</b></sub></a><br /><a href="https://github.com/FBoucher/TinyBlazorAdmin/commits?author=Mark-Phillipson" title="Documentation">📖</a> <a href="https://github.com/FBoucher/TinyBlazorAdmin/commits?author=Mark-Phillipson" title="Code">💻</a> <a href="https://github.com/FBoucher/TinyBlazorAdmin/pulls?q=is%3Apr+reviewed-by%3AMark-Phillipson" title="Reviewed Pull Requests">👀</a></td>
      <td align="center"><a href="https://github.com/fatpacket"><img src="https://avatars.githubusercontent.com/u/5621063?v=4?s=100" width="100px;" alt="fatpacket"/><br /><sub><b>fatpacket</b></sub></a><br /><a href="https://github.com/FBoucher/TinyBlazorAdmin/commits?author=fatpacket" title="Documentation">📖</a></td>
    </tr>
  </tbody>
</table>

<!-- markdownlint-restore -->
<!-- prettier-ignore-end -->

<!-- ALL-CONTRIBUTORS-LIST:END -->

This project follows the [all-contributors](https://github.com/all-contributors/all-contributors) specification. Contributions of any kind welcome!
