param(
    [Parameter(Mandatory)]
    [string] $ArtifactPath,
    [ValidateSet('Production', 'Staging')]
    [string] $DeploymentEnvironment = 'Production'
)

$ErrorActionPreference = 'Stop'

function Read-Template([string] $Name) {
    $source = Join-Path $ArtifactPath "$Name\$Name.bicep"
    $output = Join-Path $ArtifactPath "$Name\$Name.json"
    if ($Name -eq 'main') {
        $source = Join-Path $ArtifactPath 'main.bicep'
        $output = Join-Path $ArtifactPath 'main.json'
    }
    az bicep build --file $source --outfile $output
    if ($LASTEXITCODE -ne 0) { throw "Bicep compilation failed for $Name." }
    return Get-Content $output -Raw | ConvertFrom-Json
}

function Read-ContainerApp([string] $Name) {
    $template = Read-Template $Name
    $app = @($template.resources | Where-Object type -EQ 'Microsoft.App/containerApps')
    if ($app.Count -ne 1) { throw "Expected one Container App in $Name." }
    return $app[0]
}

function Assert-Condition([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$redirect = Read-ContainerApp 'shortenertools-functions'
$api = Read-ContainerApp 'management-api'
$admin = Read-ContainerApp 'admin'
$scheduler = Read-ContainerApp 'shortenertools-scheduled-posts'

$insightsTemplate = Read-Template 'app-insights'
$insights = @($insightsTemplate.resources | Where-Object type -EQ 'Microsoft.Insights/components')
$workspaces = @($insightsTemplate.resources | Where-Object type -EQ 'Microsoft.OperationalInsights/workspaces')
Assert-Condition ($insights.Count -eq 1 -and $workspaces.Count -eq 1) 'Each deployment must provision workspace-based Application Insights.'
Assert-Condition ($workspaces[0].properties.sku.name -eq 'PerGB2018') 'Insights workspace must use the supported pay-as-you-go SKU.'
# Verify the component points to the workspace created in this module, not an
# unrelated resource or a manually supplied connection string.
$workspaceId = $insights[0].properties.WorkspaceResourceId
Assert-Condition ($workspaceId -like "[[]resourceId('Microsoft.OperationalInsights/workspaces',*") 'Insights must link to its managed workspace.'
$workspaceName = $workspaces[0].name.Trim('[', ']')
Assert-Condition ($workspaceId.Contains($workspaceName)) 'Insights must reference the provisioned workspace name.'
Assert-Condition ($insightsTemplate.outputs.appInsightsConnectionString.value -match "reference\(.+ConnectionString") 'Insights must output its generated connection string.'
$mainSource = Get-Content (Join-Path $ArtifactPath 'main.bicep') -Raw
Assert-Condition ($mainSource.Contains("module app_insights 'app-insights/app-insights.bicep'")) 'Main deployment must include the Insights module.'
Assert-Condition ($mainSource.Contains('app_insights.outputs.appInsightsConnectionString')) 'Main deployment must expose the generated connection for application deployment.'
$telemetryApps = @{
    'admin' = $admin
    'shortenertools-functions' = $redirect
    'management-api' = $api
    'shortenertools-scheduled-posts' = $scheduler
}
foreach ($name in $telemetryApps.Keys) {
    $app = $telemetryApps[$name]
    $envVars = $app.properties.template.containers[0].env
    $connection = @($envVars | Where-Object name -EQ 'APPLICATIONINSIGHTS_CONNECTION_STRING')
    Assert-Condition ($connection.Count -eq 1 -and
        $connection[0].value -eq "[parameters('app_insights_outputs_appinsightsconnectionstring')]") "Insights connection must be resource-reference-backed for $name."
    $template = Get-Content (Join-Path $ArtifactPath "$name/$name.json") -Raw | ConvertFrom-Json
    Assert-Condition ($template.parameters.app_insights_outputs_appinsightsconnectionstring.type -eq 'string') "Insights connection parameter must be declared for $name."
    Assert-Condition (@($envVars | Where-Object name -EQ 'OTEL_SERVICE_NAME')[0].value -eq $name) "Telemetry role must distinguish $name."
    if ($name -ne 'admin') {
        Assert-Condition (@($envVars | Where-Object name -EQ 'AzureFunctionsJobHost__telemetryMode')[0].value -eq 'OpenTelemetry') "Functions host must export OpenTelemetry for $name."
        Assert-Condition (@($envVars | Where-Object name -EQ 'IN_ASPIRE')[0].value -eq 'true') "Functions worker must use only Aspire telemetry for $name."
    }
}

Assert-Condition ($redirect.properties.configuration.ingress.external -eq $true) 'Redirect must be public.'
Assert-Condition ($api.properties.configuration.ingress.external -eq $false) 'Management API must be internal.'
Assert-Condition ($scheduler.properties.configuration.ingress.external -eq $false) 'Scheduler must not be public.'
foreach ($app in @($redirect, $scheduler)) {
    Assert-Condition ($app.properties.template.scale.minReplicas -eq 1 -and
        $app.properties.template.scale.maxReplicas -eq 1) 'Redirect and scheduler must each have exactly one replica.'
}
foreach ($app in @($api, $admin)) {
    Assert-Condition ($app.properties.template.scale.minReplicas -eq 0) 'Admin and API must support scale-to-zero.'
}

$redirectEnv = $redirect.properties.template.containers[0].env
$redirectResources = $redirect.properties.template.containers[0].resources
Assert-Condition ($redirectResources.memory -eq '0.5Gi' -and
    $redirectResources.cpu -in @("0.25", "[json('0.25')]")) 'Redirect must use the documented 0.25 vCPU / 0.5 GiB allocation.'
$redirectFunctions = @($redirectEnv | Where-Object name -Like 'AzureFunctionsJobHost__functions__*')
Assert-Condition ($redirectFunctions.Count -eq 1 -and
    $redirectFunctions[0].value -eq 'UrlRedirect') 'Public container must only enable redirects.'
Assert-Condition (@($redirectEnv | Where-Object name -EQ 'AdminApiKey').Count -eq 0) 'Redirect must not receive the admin key.'
Assert-Condition (@($redirectEnv | Where-Object {
    $_.name -eq 'ConnectionStrings__chat' -or
    $_.name -match '^(Twitter|Mastodon|LinkedIn|Bluesky|Threads|Email|COMMUNICATION_SERVICES)'
}).Count -eq 0) 'Redirect must not receive AI/social/email credentials.'
$schedulerFunctions = @($scheduler.properties.template.containers[0].env |
    Where-Object name -Like 'AzureFunctionsJobHost__functions__*')
$coffeeTimers = @('PostTeaserTimer', 'PostAnnouncementTimer', 'PostArchiveTimer')
$expectedTimers = @('SchedulePostTimer') + $coffeeTimers
Assert-Condition ($schedulerFunctions.Count -eq $expectedTimers.Count -and
    @(Compare-Object $expectedTimers @($schedulerFunctions.value)).Count -eq 0) 'Scheduler must enable exactly the shortener and Coffee timers.'

$apiEnv = $api.properties.template.containers[0].env
$schedulerEnv = $scheduler.properties.template.containers[0].env
foreach ($app in @($redirect, $api)) {
    Assert-Condition (@($app.properties.template.containers[0].env |
        Where-Object name -EQ 'AzureWebJobs.SchedulePostTimer.Disabled')[0].value -eq 'true') 'Redirect/API timers must always be disabled.'
    foreach ($timer in $coffeeTimers) {
        Assert-Condition (@($app.properties.template.containers[0].env |
            Where-Object name -EQ "AzureWebJobs.$timer.Disabled")[0].value -eq 'true') "Redirect/API must disable $timer."
    }
}
# Compile the subscription template and its infrastructure modules too.
$null = Read-Template 'main'
$coffeeDisabledValues = @($coffeeTimers | ForEach-Object {
    @($schedulerEnv | Where-Object name -EQ "AzureWebJobs.$_.Disabled")[0].value
})
$timerDisabled = @($schedulerEnv | Where-Object name -EQ 'AzureWebJobs.SchedulePostTimer.Disabled')[0].value
if ($DeploymentEnvironment -eq 'Production') {
    # The runtime input controls cutover, not a baked-in enabled timer.
    Assert-Condition ($timerDisabled -match "^\[parameters\('([^']+)'\)\]$") 'Production scheduler disabled state must be a parameter.'
    $parameterName = $Matches[1]
    $schedulerTemplate = Get-Content (Join-Path $ArtifactPath 'shortenertools-scheduled-posts/shortenertools-scheduled-posts.json') -Raw | ConvertFrom-Json
    Assert-Condition ($null -ne $schedulerTemplate.parameters.$parameterName) 'Scheduler disabled parameter must be declared.'
    Assert-Condition ($schedulerTemplate.parameters.$parameterName.defaultValue -eq 'true') 'Production scheduler must default disabled in its deployment module.'
    foreach ($value in $coffeeDisabledValues) {
        Assert-Condition ($value -eq "[parameters('coffee_scheduler_disabled_value')]" -and
            $value -ne $timerDisabled) 'Coffee timers must share an independent disabled parameter, not the shortener gate.'
    }
    Assert-Condition ($schedulerTemplate.parameters.coffee_scheduler_disabled_value.defaultValue -eq 'true') 'Coffee must default disabled in its standalone module.'
    foreach ($envVars in @($apiEnv, $schedulerEnv)) {
        Assert-Condition (@($envVars | Where-Object name -EQ 'PostSocials')[0].value -eq 'True') 'Production Coffee must allow opted-in timers and administrator manual posting.'
    }
    Assert-Condition (@($apiEnv | Where-Object name -EQ 'DeploymentName')[0].value -match '^\[parameters\(') 'AI deployment name must be parameter-backed.'
    Assert-Condition (-not [string]::IsNullOrWhiteSpace(@($apiEnv | Where-Object name -EQ 'ConnectionStrings__chat')[0].secretRef)) 'AI connection must be a secret reference.'
}
else {
    Assert-Condition ($timerDisabled -eq 'true') 'Staging scheduler must remain disabled, regardless of cutover inputs.'
    Assert-Condition (@($coffeeDisabledValues | Where-Object { $_ -ne 'true' }).Count -eq 0) 'Staging Coffee timers must ignore cutover opt-ins.'
    foreach ($envVars in @($apiEnv, $schedulerEnv)) {
        Assert-Condition (@($envVars | Where-Object name -EQ 'PostSocials')[0].value -eq 'False') 'Staging Coffee must not post socials.'
    }
    foreach ($app in @($redirect, $api, $scheduler)) {
        Assert-Condition (@($app.properties.template.containers[0].env |
            Where-Object name -EQ 'DisableExternalPosting')[0].value -eq 'True') 'Staging must block external posts.'
        Assert-Condition (@($app.properties.template.containers[0].env | Where-Object {
            $_.name -match '^(Twitter|Mastodon|LinkedIn|Bluesky|Threads|Email|COMMUNICATION_SERVICES)'
        }).Count -eq 0) 'Staging must not receive social/email credentials.'
    }
    Assert-Condition (@($apiEnv | Where-Object name -EQ 'EnableDescriptionGeneration')[0].value -eq 'False') 'Staging must disable AI.'
    Assert-Condition (@($apiEnv | Where-Object name -EQ 'ConnectionStrings__chat').Count -eq 0) 'Staging must not receive AI credentials.'
}
Assert-Condition (@($apiEnv | Where-Object {
    $_.name -like 'AzureFunctionsJobHost__functions__*' -and $_.value -in (@('UrlRedirect') + $expectedTimers)
}).Count -eq 0) 'API must not enable redirects or timer.'
$expectedApiFunctions = @('UrlList', 'UrlCreate', 'UrlUpdate', 'UrlArchive', 'UrlStats',
    'UrlClickStatsByDay', 'CreateDescription', 'SchedulePostHttp', 'TestShortUrl',
    'PostPublishHttp', 'PostTeaserHttp', 'PostAnnouncementHttp', 'PostArchiveHttp')
$apiFunctions = @($apiEnv | Where-Object name -Like 'AzureFunctionsJobHost__functions__*')
Assert-Condition ($apiFunctions.Count -eq $expectedApiFunctions.Count -and
    @(Compare-Object $expectedApiFunctions @($apiFunctions.value)).Count -eq 0) 'Management must allow exactly its HTTP functions, including Coffee.'
Assert-Condition (@($apiEnv | Where-Object name -EQ 'AdminApiKey')[0].secretRef -eq 'adminapikey') 'API key must be a secret reference.'
foreach ($app in @($redirect, $api, $scheduler)) {
    Assert-Condition (@($app.properties.template.containers[0].env |
        Where-Object name -EQ 'tables__tableServiceUri').Count -eq 1) 'Each Functions app must receive the existing Table endpoint.'
}
$adminEnv = $admin.properties.template.containers[0].env
Assert-Condition (@($adminEnv | Where-Object name -EQ 'Entra__ClientSecret')[0].secretRef -eq 'entra--clientsecret') 'Entra client secret must be a secret reference.'
Assert-Condition (@($adminEnv | Where-Object name -EQ 'Entra__AdministratorObjectId').Count -eq 1) 'Admin must receive its allowed object ID.'
Write-Output 'ACA ingress, scaling, function isolation, storage, secrets, AI, independent shortener/Coffee scheduler cutover and workspace-based Insights/4-app telemetry assertions passed.'
