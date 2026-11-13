param([ValidateSet('Integracao','Resiliencia','Carga')][string]$Modo = 'Integracao',
    [switch]$ReutilizarMassa)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot)
$config = Get-Content .env | ConvertFrom-StringData
$identity = 'http://127.0.0.1:18080/realms/fluxo-caixa/protocol/openid-connect/token'
$write = 'http://127.0.0.1:18081/v1/contas'
$read = 'http://127.0.0.1:18082/v1/contas'
$account = '00000000-0000-4000-8000-000000000001'
$zone = [TimeZoneInfo]::FindSystemTimeZoneById('America/Sao_Paulo')
$time = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $zone)
$today = $time.ToString('yyyy-MM-dd')
function Token($user) {
    $result = Invoke-RestMethod -Uri $identity -Method Post -TimeoutSec 15 -Body @{
        grant_type='password'; client_id='caixa-carga'; client_secret=$config.LOAD_CLIENT_SECRET
        username=$user; password=$config.DEMO_PASSWORD; scope='openid'
    }
    $result.access_token
}
function Request($url, $token, $method='GET', $key=$null, $body=$null) {
    $params = @{ Uri=$url; Method=$method; SkipHttpErrorCheck=$true; TimeoutSec=12; Headers=@{} }
    if ($token) { $params.Headers.Authorization = "Bearer $token" }
    if ($key) { $params.Headers['Idempotency-Key'] = $key }
    if ($body) { $params.Body = ($body | ConvertTo-Json -Compress); $params.ContentType='application/json' }
    Invoke-WebRequest @params
}
function WaitFor($name, [scriptblock]$test, $seconds=120) {
    $until = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        try { if (& $test) { return } } catch {}
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $until)
    throw "Tempo esgotado: $name"
}
function Require($predicate, $label) { if (!$predicate) { throw "Falhou: $label" } }
function Compose([string[]]$arguments) {
    docker compose @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Falha ao executar docker compose." }
}
function Query($id, $token, $day) {
    $response = Request "$read/$id/consolidado-diario?inicio=$day&fim=$day" $token
    Require ($response.StatusCode -eq 200) "consulta consolidada (HTTP $($response.StatusCode))"
    ($response.Content | ConvertFrom-Json).dias[0]
}
function Bala($day) {
    return [decimal](Query $account $token $day).movimentoLiquido
}
WaitFor 'Keycloak e APIs' {
    (Invoke-WebRequest http://127.0.0.1:18081/health/ready -TimeoutSec 3).StatusCode -eq 200 -and
    (Invoke-WebRequest http://127.0.0.1:18082/health/ready -TimeoutSec 3).StatusCode -eq 200 -and
    (Invoke-WebRequest http://127.0.0.1:18080/realms/fluxo-caixa/.well-known/openid-configuration -TimeoutSec 3).StatusCode -eq 200
}
$token = Token demo
$register = "$write/$account/lancamentos"
if ($Modo -eq 'Carga') {
    $starts = @{ '1'=$today; '7'=$time.AddDays(-6).ToString('yyyy-MM-dd'); '31'=$time.AddDays(-30).ToString('yyyy-MM-dd') }
    $accounts = [Collections.Generic.List[object]]::new()
    $previous = $time.AddDays(-1).ToString('yyyy-MM-dd')
    foreach ($i in 1..100) {
        $id = '00000000-0000-4000-8000-{0:000000000000}' -f $i
        $username = if ($i -eq 1) { 'demo' } else { 'carga-{0:000}' -f $i }
        $access = if ($i -eq 1) { $token } else { Token $username }
        $accounts.Add(@{id=$id;token=$access})
        $date = [DateOnly]::Parse($previous)
        for ($d=0; !$ReutilizarMassa -and $d -lt 90; $d++) {
            $cashDay = $date.AddDays(-$d)
            $key = 'eeeeeeee-0000-4000-8000-{0:000000000000}' -f ($cashDay.DayNumber * 100L + $i)
            $body = @{dataCaixa=$cashDay.ToString('yyyy-MM-dd');tipo='credito';valor='1.00';moeda='BRL'}
            $r = Request "$write/$id/lancamentos" $access POST $key $body
            Require ($r.StatusCode -eq 201) "preparação de carga, conta $i, dia $d"
        }
        if (!$ReutilizarMassa -and $i % 10 -eq 0) { Write-Host "Massa registrada para $i de 100 contas." }
    }
    foreach ($entry in $accounts) {
        WaitFor 'projeção da massa sintética' { (Query $entry.id $entry.token $previous).movimentoLiquido -eq '1.00' }
    }
    # Refresh all tokens after seeding so they cover the measured window.
    for ($i=1; $i -le 100; $i++) {
        $username = if ($i -eq 1) { 'demo' } else { 'carga-{0:000}' -f $i }
        $accounts[$i-1].token = Token $username
    }
    $path = Join-Path (Get-Location) '.local/carga'
    [IO.Directory]::CreateDirectory($path) | Out-Null
    $seed = ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() % 100000000) * 10000L
    $payload = @{users=$accounts;today=$today;starts=$starts;seed=$seed} | ConvertTo-Json -Depth 7
    [IO.File]::WriteAllText((Join-Path $path 'carga.json'), $payload)
    Write-Host 'Massa e credenciais temporárias preparadas para k6.'
    exit
}
$d = if ($Modo -eq 'Resiliencia') { $time.AddDays(-180).ToString('yyyy-MM-dd') } else { $time.AddDays(-190).ToString('yyyy-MM-dd') }
$body = @{dataCaixa=$d;tipo='credito';valor='50.00';moeda='BRL';descricao='Teste sintético'}
$before = Bala $d
if ($Modo -eq 'Integracao') {
    Require ((Request $register $null POST ([Guid]::NewGuid()) $body).StatusCode -eq 401) 'autenticação obrigatória'
    Require ((Request $register (Token consulta) POST ([Guid]::NewGuid()) $body).StatusCode -eq 401) 'audiência incompatível'
    Require ((Request $register (Token 'carga-002') POST ([Guid]::NewGuid()) $body).StatusCode -eq 403) 'isolamento por conta'
    $key = [Guid]::NewGuid()
    $a = Request $register $token POST $key $body
    $b = Request $register $token POST $key $body
    Require ($a.StatusCode -eq 201 -and $b.StatusCode -eq 201 -and
        ($a.Content | ConvertFrom-Json).id -eq ($b.Content | ConvertFrom-Json).id) 'idempotência HTTP'
    $changed = $body.Clone()
    $changed.valor = '51.00'
    Require ((Request $register $token POST $key $changed).StatusCode -eq 409) 'conflito da chave'
    $invalid = $body.Clone()
    $invalid.valor = '0.00'
    Require ((Request $register $token POST ([Guid]::NewGuid()) $invalid).StatusCode -eq 400) 'validação monetária'
    $id = ($a.Content | ConvertFrom-Json).id
    Require ((Request "$register/$id" $token).StatusCode -eq 200) 'consulta individual'
    $history = Request ("${register}?inicio=$d&fim=$d&limite=1") $token
    Require ($history.StatusCode -eq 200 -and ($history.Content | ConvertFrom-Json).itens.Count -eq 1) 'histórico filtrado'
    WaitFor 'projeção única' { (Bala $d) -eq ($before + 50) }
    Write-Host 'Integração aprovada: autenticação, isolamento, escrita idempotente e resumo.'
    exit
}
foreach ($scenario in @(@{services=@('consolidado','projetor');label='Consolidado'},@{services=@('rabbitmq');label='RabbitMQ'})) {
    Compose (@('stop') + $scenario.services)
    try {
        $total = 0
        foreach ($i in 1..10) {
            $r = Request $register $token POST ([Guid]::NewGuid()) $body
            Require ($r.StatusCode -eq 201) "registro durante a queda de $($scenario.label)"
            $total += 50
        }
        Require ((Invoke-WebRequest http://127.0.0.1:18081/health/ready).StatusCode -eq 200) 'readiness da escrita'
        Start-Sleep -Seconds 60
    } finally {
        Compose (@('start') + $scenario.services)
    }
    WaitFor "recuperação de $($scenario.label)" { (Bala $d) -eq ($before + $total) }
    $before += $total
    Write-Host "Recuperação de $($scenario.label) aprovada. Dez lançamentos projetados uma vez."
}
