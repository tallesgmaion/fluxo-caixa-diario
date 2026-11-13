#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
$utf8 = [Text.UTF8Encoding]::new($false)
$keys = @('SA_PASSWORD','WRITE_PASSWORD','PUBLISH_PASSWORD','READ_PASSWORD','PROJECT_PASSWORD',
    'IDENTITY_PASSWORD','RABBIT_ADMIN_PASSWORD','RABBIT_PUBLISH_PASSWORD','RABBIT_PROJECT_PASSWORD',
    'KEYCLOAK_ADMIN_PASSWORD','WEB_CLIENT_SECRET','DEMO_PASSWORD','LOAD_CLIENT_SECRET','SEQ_ADMIN_PASSWORD','GRAFANA_ADMIN_PASSWORD')
function New-Secret { 'Fx9!' + [Guid]::NewGuid().ToString('N') }
$values = @{}
if (Test-Path .env) {
    Get-Content .env | ForEach-Object {
        if ($_ -match '^([A-Z_]+)=(.*)$') { $values[$Matches[1]] = $Matches[2] }
    }
}
foreach ($key in $keys) {
    if (!$values.ContainsKey($key)) { $values[$key] = New-Secret }
    if ($values[$key] -notmatch '^[a-zA-Z0-9!_-]{16,128}$') { throw "Valor inválido para $key." }
}
if (!$values.ContainsKey('OTEL_ENDPOINT')) { $values.OTEL_ENDPOINT = '' }
$envLines = @($keys | ForEach-Object { "$_=$($values[$_])" }) + "OTEL_ENDPOINT=$($values.OTEL_ENDPOINT)"
[IO.File]::WriteAllText((Join-Path (Get-Location) '.env'), (($envLines -join "`n") + "`n"), $utf8)

function RabbitUser($name, $password, $tag) {
    $salt = [byte[]]::new(4)
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($salt) }
    finally { $random.Dispose() }
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { $digest = $sha256.ComputeHash([byte[]]($salt + $utf8.GetBytes($password))) }
    finally { $sha256.Dispose() }
    @{ name=$name; password_hash=[Convert]::ToBase64String([byte[]]($salt + $digest)); hashing_algorithm='rabbit_password_hashing_sha256'; tags=@($tag | Where-Object { $_ }) }
}
$vhost = 'fluxo-caixa'
$exchange = 'caixa.lancamentos'
$queue = 'caixa.consolidado.v1'
$dlx = 'caixa.falhas'
$route = 'lancamento.registrado.v1'
$deadRoute = 'consolidado.v1.falha'
$defs = @{
    users=@(
        (RabbitUser 'operacao' $values.RABBIT_ADMIN_PASSWORD 'administrator')
        (RabbitUser 'publicador' $values.RABBIT_PUBLISH_PASSWORD '')
        (RabbitUser 'projetor' $values.RABBIT_PROJECT_PASSWORD '')
    )
    vhosts=@(@{name=$vhost})
    permissions=@(
        @{user='operacao';vhost=$vhost;configure='.*';write='.*';read='.*'}
        @{user='publicador';vhost=$vhost;configure='^$';write='^caixa\.lancamentos$';read='^$'}
        @{user='projetor';vhost=$vhost;configure='^$';write='^$';read='^caixa\.consolidado\.v1$'}
    )
    exchanges=@(@{name=$exchange;type='topic';vhost=$vhost;durable=$true;auto_delete=$false;internal=$false;arguments=@{}},
        @{name=$dlx;type='direct';vhost=$vhost;durable=$true;auto_delete=$false;internal=$false;arguments=@{}})
    queues=@(@{name=$queue;vhost=$vhost;durable=$true;auto_delete=$false;arguments=@{'x-queue-type'='quorum';'x-quorum-initial-group-size'=1}},
        @{name="$queue.dlq";vhost=$vhost;durable=$true;auto_delete=$false;arguments=@{'x-queue-type'='quorum';'x-quorum-initial-group-size'=1}})
    bindings=@(@{source=$exchange;destination=$queue;destination_type='queue';vhost=$vhost;routing_key=$route;arguments=@{}},
        @{source=$dlx;destination="$queue.dlq";destination_type='queue';vhost=$vhost;routing_key=$deadRoute;arguments=@{}})
    policies=@(@{vhost=$vhost;name='consolidacao';pattern='^caixa\.consolidado\.v1$';priority=10;'apply-to'='quorum_queues';definition=@{
        'dead-letter-exchange'=$dlx;'dead-letter-routing-key'=$deadRoute;'dead-letter-strategy'='at-least-once'
        overflow='reject-publish';'max-length'=100000;'delivery-limit'=5
        'delayed-retry-type'='failed';'delayed-retry-min'=1000;'delayed-retry-max'=5000
    }})
}
function DemoUser($username, $account, $roles) {
    @{username=$username;enabled=$true;emailVerified=$true;firstName='Conta';lastName='Sintética'
      email="$username@example.invalid"; attributes=@{conta_id=@($account)}; clientRoles=$roles
      credentials=@(@{type='password';value=$values.DEMO_PASSWORD;temporary=$false})}
}
$both = @{'lancamentos-api'=@('lancamentos.registrar','lancamentos.consultar');'consolidado-api'=@('consolidado.consultar')}
$consult = @{'consolidado-api'=@('consolidado.consultar')}
$write = @{'lancamentos-api'=@('lancamentos.registrar','lancamentos.consultar')}
$account = '00000000-0000-4000-8000-000000000001'
$users = @((DemoUser 'demo' $account $both), (DemoUser 'consulta' $account $consult), (DemoUser 'operador' $account $write))
foreach ($i in 2..100) {
    $users += DemoUser ('carga-{0:000}' -f $i) ('00000000-0000-4000-8000-{0:000000000000}' -f $i) $both
}
$accountMapper = @{name='conta-id'; protocol='openid-connect';protocolMapper='oidc-usermodel-attribute-mapper';config=@{
    'user.attribute'='conta_id';'claim.name'='conta_id';'jsonType.label'='String';'access.token.claim'='true'
}}
$realm = @{
    realm='fluxo-caixa';displayName='Fluxo de Caixa Diário';enabled=$true;loginTheme='caixa'
    internationalizationEnabled=$true;supportedLocales=@('pt-BR');defaultLocale='pt-BR'
    accessTokenLifespan=1800;sslRequired='none'; registrationAllowed=$false
    roles=@{client=@{'lancamentos-api'=@(@{name='lancamentos.registrar'},@{name='lancamentos.consultar'})
        'consolidado-api'=@(@{name='consolidado.consultar'})}}
    clients=@(
        @{clientId='caixa-web';enabled=$true;protocol='openid-connect';secret=$values.WEB_CLIENT_SECRET
          standardFlowEnabled=$true;directAccessGrantsEnabled=$false;publicClient=$false;fullScopeAllowed=$true
          redirectUris=@('http://localhost:18083/signin-oidc');attributes=@{
              'pkce.code.challenge.method'='S256'
              'post.logout.redirect.uris'='http://localhost:18083/signout-callback-oidc'
          }
          defaultClientScopes=@('profile','email','roles');protocolMappers=@($accountMapper)}
        @{clientId='caixa-carga';enabled=$true;protocol='openid-connect';secret=$values.LOAD_CLIENT_SECRET
          standardFlowEnabled=$false;directAccessGrantsEnabled=$true;publicClient=$false;fullScopeAllowed=$true
          defaultClientScopes=@('profile','roles');protocolMappers=@($accountMapper)}
        @{clientId='lancamentos-api';enabled=$true;bearerOnly=$true;protocol='openid-connect';standardFlowEnabled=$false}
        @{clientId='consolidado-api';enabled=$true;bearerOnly=$true;protocol='openid-connect';standardFlowEnabled=$false}
    )
    components=@{'org.keycloak.userprofile.UserProfileProvider'=@(@{name='declarative-user-profile'; providerId='declarative-user-profile'; config=@{'kc.user.profile.config'=@('{"attributes":[{"name":"username","permissions":{"view":["admin","user"],"edit":["admin"]}},{"name":"email","permissions":{"view":["admin","user"],"edit":["admin"]}},{"name":"firstName","permissions":{"view":["admin","user"],"edit":["admin"]}},{"name":"lastName","permissions":{"view":["admin","user"],"edit":["admin"]}},{"name":"conta_id","permissions":{"view":["admin","user"],"edit":["admin"]}}]}')}})}
    users=$users
}
foreach ($item in @(@{path='.local/rabbit/definitions.json';data=$defs},@{path='.local/keycloak/fluxo-caixa-realm.json';data=$realm})) {
    $full = Join-Path (Get-Location) $item.path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full)) | Out-Null
    [IO.File]::WriteAllText($full, ($item.data | ConvertTo-Json -Depth 20), $utf8)
}
Write-Host 'Arquivos locais preparados. As credenciais estão no .env.'
