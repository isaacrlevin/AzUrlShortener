# Azure Container Apps migration and deployment

The `feature/azure-container-apps-migration` implementation uses the .NET Aspire AppHost as the deployment source of truth. It replaces the Static Web Apps/Function App workflow with `aspire publish` and `aspire deploy`. No Azure resources are provisioned until you deliberately deploy.

## Architecture

| Service | Ingress | Scaling and functions |
|---|---|---|
| `shortenertools-functions` | Public HTTPS | Exactly one replica, 0.25 vCPU / 0.5 GiB; only `UrlRedirect` |
| `management-api` | Internal HTTPS | Minimum zero, maximum one; management, statistics, description-generation and manual social-post endpoints |
| `admin` | Public HTTPS | Minimum zero, maximum one; server-interactive Blazor with Microsoft Entra sign-in |
| `shortenertools-scheduled-posts` | Internal only | Exactly one replica; only `SchedulePostTimer` |

The existing Functions assembly is reused in three independently configured containers with explicit function allowlists. This preserves the existing API payloads and storage behavior instead of rewriting every handler into ASP.NET Core. No Azure Functions hosting resource or Static Web App is required. Public redirects have no access to admin secrets, social credentials, or AI services. Management requests originate on the authenticated Blazor server, use Aspire service discovery, and carry a server-only API key. The private API validates that key as an additional boundary; it is never included in browser assets.

The admin accepts only the configured Entra tenant and administrator object ID. Other Microsoft accounts do not receive the `admin` role or a sign-in cookie. The server-hosted UI preserves MudBlazor components, paging, clipboard support, and browser-local statistics dates. Its HTTP calls no longer rely on SWA routing or mock authentication.

The scheduled container retains the original weekday UTC cron expression, `0 0 13,16,19,23 * * 1-5`. It stays running so the Functions timer listener remains available. The timer defaults disabled in Production until explicit cutover, is always disabled locally/in Staging, and is excluded from the public redirect and management containers. Each Functions service has a different host ID to isolate leases/checkpoints. This preserves scheduled posting, but adds another always-running container to the bill. It is deliberately not an HTTP scale-to-zero timer, which would miss executions.

## Storage and configuration

Production deployment attaches your existing storage account with `AsExisting`. The `UrlsDetails` and `ClickStats` tables, keys, links, and analytics records are not copied or replaced. Functions obtain its Table endpoint through Aspire's `tables:tableServiceUri` configuration and use `DefaultAzureCredential` with their managed identity. Aspire assigns Storage Table Data Contributor to each Functions identity. A separate managed storage account holds Functions host state.

The deployment identity needs permission to create resources and role assignments, including assignments on the existing data storage account. An account in another resource group is supported by the `existing-storage-resource-group` parameter.

Local development defaults to Azurite. The existing `Storage:UseProduction` opt-in still permits live-data testing with the `production-data` connection-string secret; host state remains emulated and the timer remains disabled. Live redirects/create/edit/archive requests modify production data.

For Rider, select the AppHost's `https-production-data` Aspire Host launch profile. The local admin and local management API can use the existing production tables without deploying ACA or calling the old SWA API. If Rider opens the dashboard but does not launch the admin child process, start the AppHost with Aspire CLI using that profile and attach Rider to the running admin process. See the [local development guide](../src/ShortenerTools.Admin/README.md#rider) for configuration, diagnosis, and live-data precautions.

## Configure Entra authentication

1. Create a **single-tenant Web** app registration in your Entra tenant. For a personal Microsoft account, invite that account into the tenant and use its object ID in that tenant.
2. Add `https://<admin-host>/signin-oidc` as a Web redirect URI and `https://<admin-host>/signout-callback-oidc` as the front-channel logout URL. Add the equivalent local HTTPS URLs when developing.
3. Create a client secret; store its **value**, not its ID, in the deployment secret store. Plan renewal before its expiration.
4. Set the tenant ID, client/application ID, client secret, and your administrator's object ID using the parameters below. These are distinct from the service principal used for GitHub deployment.

TLS is terminated at ACA ingress; the admin is configured to process its forwarded HTTPS scheme. Aspire's ACA integration enables managed .NET data protection for cookies across restarts. Do not enable anonymous admin access or expose `management-api` externally.

## Deployment inputs

Set parameters on the deployment process through environment variables or a protected CI environment. The [production helper](production-deployment.md) normalizes portable underscore names to exact dashed Aspire configuration keys. With direct Aspire commands, use exact dashed keys (for example `[Environment]::SetEnvironmentVariable('Parameters__existing-storage-account', '<account>')`). Do not put secrets in source files, shell history, or published artifacts.

| Parameter / connection | Environment variable |
|---|---|
| Existing data account | `Parameters__existing_storage_account` |
| Existing account resource group | `Parameters__existing_storage_resource_group` |
| Entra tenant | `Parameters__entra_tenant_id` |
| Entra application | `Parameters__entra_client_id` |
| Entra client secret | `Parameters__entra_client_secret` |
| Administrator object ID | `Parameters__administrator_object_id` |
| Private API key (strong random secret) | `Parameters__admin_api_key` |
| Short-link domain | `Parameters__custom_domain` |
| Unknown/archived-link fallback URL | `Parameters__default_redirect_url` |
| Azure OpenAI connection for description generation | `ConnectionStrings__chat` |
| Azure OpenAI model deployment name | `Parameters__azure_openai_deployment_name` |
| Timer disabled state (default `true`; helper controls through `-EnableScheduler`) | `Parameters__scheduler_disabled` |
| Social/email integration secrets | `Parameters__twitter_consumer_key`, `Parameters__twitter_consumer_secret`, `Parameters__twitter_access_token`, `Parameters__twitter_access_secret`, `Parameters__mastodon_access_token`, `Parameters__linkedin_access_token`, `Parameters__bluesky_username`, `Parameters__bluesky_password`, `Parameters__threads_token`, `Parameters__communication_services_connection_string`, `Parameters__email_from`, `Parameters__email_to`, `Parameters__twitter_via_handle` |

Keep your existing social/email and Azure OpenAI configuration when migrating. Description generation and scheduled posting have their own usage charges. The redirect container does not require those credentials. `custom-domain` supplies the complete short-link base URL, including the scheme and without a trailing slash (for example, `https://isaacl.dev`); `default-redirect-url` must be an absolute URL. DNS/certificate binding is a separate operator step, not performed by setting that parameter.

## Preview and deploy

Install .NET 10, Aspire CLI, Azure CLI, and Docker with Linux containers. Authenticate with `az login` only when actually deploying.

A non-provisioning preview from the repository root:

```powershell
aspire publish --apphost src\ShortenerTools.AppHost\ShortenerTools.AppHost.csproj --environment Production --non-interactive -o .\aspire-output
```

Publish generates Bicep with parameter placeholders; it does not deploy infrastructure or require real production secret values. Review ingress, replica limits, identities, storage references, and secrets in the generated artifacts. Do not check generated deployment state containing resolved credentials into Git.

When ready to deploy, provide the required parameters and Azure target configuration:

```powershell
$env:Azure__SubscriptionId = "<subscription-id>"
$env:Azure__Location = "<region>"
$env:Azure__ResourceGroup = "<new-deployment-resource-group>"
# Import allowlisted Functions settings and AppHost user secrets; fail on missing inputs.
.\src\tools\Invoke-ProductionDeployment.ps1 -Mode Deploy `
    -LocalSettingsPath .\src\ShortenerTools.Functions\local.settings.json -LoadUserSecrets
```

Supply other inputs securely before running deploy. This command builds/pushes the containers and provisions/updates the AppHost resources, including the Consumption environment, registry, identities, host storage, and logging. For later application or infrastructure changes, run the same `aspire deploy` command. This checkout does not use the upstream `azd up` path because it already has a current Aspire-native deployment model.

### GitHub Actions

`.github\workflows\azure-container-apps.yml` builds/tests pull requests and pushes to `main`. Production deployment is **manual only**, from `main`, using the `production` environment choice. There is no production deployment on push, and no required reviewer approval is requested. Configure the GitHub Environment named `production` and its secrets/variables; restrict its deployment branches to `main`. See [production setup and dispatch inputs](production-deployment.md).

| Kind | Names |
|---|---|
| Azure OIDC deployment secrets | `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` |
| Admin/integration secrets | `ENTRA_CLIENT_SECRET`, `ADMIN_API_KEY`, `AZURE_OPENAI_CONNECTION_STRING`, `TWITTER_CONSUMER_KEY`, `TWITTER_CONSUMER_SECRET`, `TWITTER_ACCESS_TOKEN`, `TWITTER_ACCESS_SECRET`, `MASTODON_ACCESS_TOKEN`, `LINKEDIN_ACCESS_TOKEN`, `BLUESKY_USERNAME`, `BLUESKY_PASSWORD`, `THREADS_TOKEN`, `COMMUNICATION_SERVICES_CONNECTION_STRING`, `EMAIL_FROM`, `EMAIL_TO` |
| Variables | `AZURE_LOCATION`, `AZURE_RESOURCE_GROUP`, `EXISTING_STORAGE_ACCOUNT`, `EXISTING_STORAGE_RESOURCE_GROUP`, `ENTRA_CLIENT_ID`, `ADMINISTRATOR_OBJECT_ID`, `SHORT_LINK_DOMAIN`, `DEFAULT_REDIRECT_URL`, `TWITTER_VIA_HANDLE`, `AZURE_OPENAI_DEPLOYMENT_NAME` |

The workflow uses `AZURE_TENANT_ID` for both the deployment and administrator tenant. Configure Azure federation for `repo:isaacrlevin/AzUrlShortener:environment:production` (adjust for forks). It no longer uses a SWA deployment token or Functions publish profile. Pull-request validation also publishes and compiles the infrastructure without provisioning, then asserts public/private ingress, replica limits, function allowlists, storage endpoints, and secret references using `src\tools\Test-AcaArtifacts.ps1`. Legacy `src\deployment\azureDeploy*.json` and SWA CLI assets are historical artifacts, not the active deployment path.

## Cutover and rollback

### Isolated staging deployment

Use `--environment Staging` with a dedicated Azure resource group. In this environment the AppHost provisions a new data storage account instead of attaching the production account, ignores local production-data opt-ins, and keeps all four application containers. `UrlsDetails` and `ClickStats` are created on first access; no live records are copied.

Staging disables the scheduled timer and blocks social/email sends at the application boundary, even if a caller attempts to enable posting. No social/email credentials or AI connection are required or passed to the containers. Description generation returns HTTP 503 in this environment. Production receives configured integrations but defaults its timer disabled until explicit cutover. Manual posting remains available to the authenticated administrator; avoid using it against live accounts during migration checks.

Supply staging-specific Entra tenant/client/secret, administrator object ID, and API key along with `Azure__SubscriptionId`, `Azure__Location`, and `Azure__ResourceGroup`. Do not use the production GitHub workflow: it is explicitly bound to Production. Publish and deploy with the staging environment:

```powershell
aspire publish --apphost src\ShortenerTools.AppHost\ShortenerTools.AppHost.csproj --environment Staging -o .\aspire-staging-output
aspire deploy --apphost src\ShortenerTools.AppHost\ShortenerTools.AppHost.csproj --environment Staging
```

The initial short-link and fallback defaults use reserved `.invalid` hosts. After deployment, configure the short-link base to the new redirect app's HTTPS hostname, configure the fallback to a safe test URL, and register the admin hostname's `/signin-oidc` and `/signout-callback-oidc` in the staging Entra registration. The configuration-backed defaults use exact keys `Parameters:custom-domain` and `Parameters:default-redirect-url`; environment-variable equivalents are `Parameters__custom-domain` and `Parameters__default-redirect-url` (PowerShell can set dashed names with `[Environment]::SetEnvironmentVariable`). Keep deployment inputs available when redeploying. If cached parameter values override updated inputs, use `aspire deploy --environment Staging --clear-cache` with the complete staging configuration supplied.

Do not bind production custom domains or change existing DNS. Creating/editing links and recording clicks then affect only staging tables. Full social/AI integration testing is intentionally excluded from this safe staging mode.

The test deployment created on October 8, 2026 uses resource group `rg-shortener-staging` in West US 2:

| Purpose | Address |
|---|---|
| Admin | `https://admin.whitesand-77c2bfb0.westus2.azurecontainerapps.io` |
| Redirect | `https://shortenertools-functions.whitesand-77c2bfb0.westus2.azurecontainerapps.io` |
| Entra application/client ID | `86a7cac8-8d20-4c5a-9da1-a85f0efb440a` |

Staging credentials are stored only in local AppHost user secrets under `StagingDeployment:*`, not in this document. The staging Entra client secret expires April 8, 2027. Staging remains billable until deliberately removed. To remove it, confirm the subscription/resource group target and use `aspire destroy --apphost src\ShortenerTools.AppHost\ShortenerTools.AppHost.csproj --environment Staging`; remove the staging-only Entra registration separately after confirming it is no longer needed. Never target the old production resource group for staging cleanup.

### Production cutover

1. Keep the old resources and data account until the new deployment is proven; never delete the existing storage account.
2. Register the new admin URL in Entra. Confirm only your account can access URL Manager/Statistics; confirm sign-out and rejected-account behavior.
3. Confirm the public app enables only `UrlRedirect`, stays at one replica, and has no management routes. Confirm API ingress is internal and unauthenticated/keyless requests are rejected.
4. Verify known, unknown, archived, and bot links and click records against the existing tables. Verify create/edit/archive, pagination, description generation, and browser-time-zone statistics.
5. Confirm the scheduler has only `SchedulePostTimer`, its distinct host ID and working provider credentials. Disable the old timer before redeploying with `enable_scheduled_posting=true` (workflow) or `-EnableScheduler` (helper). Without the opt-in, every helper/workflow redeploy disables the new timer; include it deliberately on subsequent deployments after cutover.
6. Bind the existing short-link custom domain to ACA ingress with domain-validation DNS records and a managed certificate. Lower DNS TTL ahead of time and switch the CNAME/A record only after checking redirects.
7. For rollback, restore the old domain target and disable the new scheduler before re-enabling the old one. Both deployments use the same table schema; no reverse data migration is needed.

## Cost impact

Each deployment now includes Aspire-managed workspace-based Application Insights,
connected to admin, management API, redirect and scheduler with distinct roles.
Local runs still use the Aspire dashboard without provisioning Azure telemetry.
See [Application Insights setup and queries](production-deployment.md#application-insights-telemetry)
for host/worker correlation, privacy, deployment wiring and verification.

The redirect is intentionally kept warm at 0.25 vCPU / 0.5 GiB, one replica. At illustrative US idle rates of $0.000003/vCPU-second and $0.000003/GiB-second, 730 hours is about **$5.91/month before any applicable grants**. At 1,000 redirects/day, external requests are well below the shared 2-million/month ACA allowance; active compute depends on actual duration and the runtime's eligibility for idle rates. A process that remains active will cost more than the idle estimate.

The scheduler adds another warm container, so the $6 redirect-only estimate is **not** the total project bill. Admin and API scale to zero when unused; an open Blazor server connection keeps the admin active. Registry, storage, Application Insights/Log Analytics ingestion and retention, outbound transfer, AI, and social/email services are additional. Review monitoring volume, sampling, retention and caps after deployment. One replica removes scale-to-zero cold starts but is not an uptime guarantee or zero-downtime promise during upgrades.

ACA Consumption grants are shared per subscription: 180,000 vCPU-seconds, 360,000 GiB-seconds, and 2 million external HTTP requests/month. The old ARM template specified SWA Standard (roughly $9/month at illustrative US pricing) and classic Functions Consumption. Compare against the actual existing SKUs/bill, not assumed template defaults.

Sources: [ACA billing](https://learn.microsoft.com/azure/container-apps/billing), [ACA pricing](https://azure.microsoft.com/pricing/details/container-apps/), [Functions costs](https://learn.microsoft.com/azure/azure-functions/functions-consumption-costs), [SWA pricing](https://azure.microsoft.com/pricing/details/app-service/static/), [upstream architecture](https://github.com/fboucher/AzUrlShortener/blob/main/src/AppHost/Program.cs). Prices are illustrative, checked October 2026; region and subscription usage affect the bill.
