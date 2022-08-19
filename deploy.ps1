[CmdletBinding()]
Param(
    [Parameter(
        Mandatory,
        HelpMessage = "Unique name for the deployed Azure Container Apps")]
    [Alias("n","Name")]
    [string] $unique_app_name,

    [Parameter(
        Mandatory,
        HelpMessage = "Azure Resource Group name")]
    [Alias("rg","ResourceGroup")]
    [string] $azure_resource_group_name,

    [Parameter(
        Mandatory,
        HelpMessage = "Azure Resource Group location")]
    [Alias("l","Location")]
    [string] $azure_resource_group_location
)

# Login to Azure
">>> Logging in to Azure"
az login

# Flex ACR bicep
">>> Running flex/acr.bicep"
$acr = az deployment group create `
  --resource-group "$($azure_resource_group_name)" `
  --template-file 'flex/acr.bicep' `
  --parameters location="$($azure_resource_group_location)" | ConvertFrom-Json

$acr_name = $acr.properties.outputs.acrName.value
$acr_login_server = $acr.properties.outputs.acrLoginServer.value
">>> ACR resource created. ACR Name: $($acr_name), ACR Login Server: $($acr_login_server)"

# Login to ACR
">>> Fetching access token"
$access_token = az account get-access-token --query accessToken -o tsv
$refresh_token_param = @{
  Uri    = "https://$($acr_login_server)/oauth2/exchange"
  Method = "POST"
  Body   = @{
    grant_type   = "access_token"
    service      = "$($acr_login_server)"
    access_token = "$($access_token)"
  }
}
$refresh_token_result = Invoke-WebRequest @refresh_token_param
$refresh_token_json = $refresh_token_result.Content | ConvertFrom-Json

">>> Logging in to ACR"
# The null GUID 0000... tells the container registry that this is an ACR refresh token during the login flow
$refresh_token_json.refresh_token | docker login -u 00000000-0000-0000-0000-000000000000 --password-stdin "$($acr_login_server)"

# Build and push docker image to registry
">>> Building Docker image"
$docker_image_name = "akka.shoppingcart"
docker build -t "$($acr_login_server)/$($docker_image_name):latest" .
">>> Pushing Docker image to $($acr_login_server)"
docker image push "$($acr_login_server)/$($docker_image_name):latest"

# Flex ACA bicep
">>> Running flex/acr.bicep"
$deploy_result = az deployment group create `
  --resource-group "$($azure_resource_group_name)" `
  --template-file 'flex/main.bicep' `
  --parameters location="$($azure_resource_group_location)" `
    appName="$($unique_app_name)" `
    acrName="$($acr_name)" `
    repositoryImage="$($acr_login_server)/$($docker_image_name):latest" | ConvertFrom-Json

">>> Deploy completed. Website url: https://$($deploy_result.properties.outputs.acaUrl.value)"