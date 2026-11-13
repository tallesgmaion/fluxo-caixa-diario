# Fluxo de Caixa Diário

Demonstração técnica de fluxo de caixa com dados fictícios. Registre créditos e débitos pelo Blazor e consulte o movimento líquido diário. A API confirma a gravação no SQL Server, e a outbox, o RabbitMQ e dois workers .NET atualizam o consolidado em segundo plano.

## Execução local

### Subir tudo com um comando

Inicie o Docker Desktop ou Docker Engine com Compose v2 e contêineres Linux x86-64. Reserve 8 GB de memória para a base e 16 GB para executar também a observabilidade e a carga no mesmo host.

**Todas as senhas e segredos necessários para a demonstração são gerados automaticamente no arquivo `.env`, na raiz do projeto.** O setup também prepara os arquivos locais do Keycloak e do RabbitMQ. Consulte a tabela abaixo para localizar a variável de cada login. Guarde o mesmo `.env` enquanto reutilizar os volumes, pois eles armazenam serviços inicializados com essas credenciais. O setup preserva os valores existentes.

**Windows**, na raiz do projeto. Use o Windows PowerShell 5.1 que acompanha o sistema.

```powershell
powershell.exe -NoProfile -File scripts/subir.ps1
```

Se já tiver PowerShell 7 instalado, `pwsh -NoProfile -File scripts/subir.ps1` também funciona.

**macOS ou Linux**, na raiz do projeto.

```bash
bash scripts/subir.sh
```

O script em Bash executa a geração de credenciais dentro de um contêiner com PowerShell. Basta ter Bash e Docker instalados no host. Em Macs com Apple Silicon, use um ambiente Docker Linux x86-64 para a avaliação completa. O SQL Server em contêiner exige x86-64, e a Microsoft não oferece suporte à sua execução em emuladores como Rosetta 2 ou QEMU. [Requisitos dos contêineres SQL Server](https://learn.microsoft.com/en-us/sql/linux/containers/deploy)

Os dois comandos sobem a aplicação, SQL Server, RabbitMQ, Keycloak, observabilidade e MkDocs. A aplicação abre em [localhost:18083](http://localhost:18083) e a documentação em [localhost:8000](http://localhost:8000/). O comando exibe o estado dos contêineres ao terminar. Na primeira execução, aguarde o Keycloak concluir a inicialização e o MkDocs instalar suas dependências.

### Subir por etapas

Para subir apenas a base, gere as credenciais e execute o Compose principal. No macOS ou Linux, use `bash scripts/setup.sh` no passo de geração.

```powershell
powershell.exe -NoProfile -File scripts/setup.ps1
docker compose up -d --build
docker compose ps -a
```

O serviço `sql-init` aguarda a recuperação dos bancos existentes, aplica o esquema e termina com código 0. Se a inicialização falhar, consulte `docker compose logs sql sql-init`. As seções **Observabilidade** e **MkDocs** trazem os comandos para ativá-los separadamente.

Se estiver atualizando um volume de identidade criado antes da opção **Sair**, abra o cliente `caixa-web` do realm `fluxo-caixa` no painel do Keycloak. Adicione `http://localhost:18083/signout-callback-oidc` em **Valid post logout redirect URIs**. O setup já inclui esse endereço para instalações novas.

| Interface | Endereço | Login e variável da senha no `.env` |
| --- | --- | --- |
| Blazor | [localhost:18083](http://localhost:18083) | `demo` com a variável `DEMO_PASSWORD` no `.env` |
| Scalar Lançamentos | [localhost:18081/scalar/v1](http://localhost:18081/scalar/v1) | Operações usam token da conta sintética |
| Scalar Consolidado | [localhost:18082/scalar/v1](http://localhost:18082/scalar/v1) | Operações usam token da conta sintética |
| RabbitMQ Management | [localhost:15672](http://localhost:15672) | `operacao` com `RABBIT_ADMIN_PASSWORD` |
| Keycloak | [localhost:18080](http://localhost:18080) | `operador-identidade` com `KEYCLOAK_ADMIN_PASSWORD` para administração |
| Grafana | [localhost:13000](http://localhost:13000) | `admin` com `GRAFANA_ADMIN_PASSWORD` |
| Seq | [localhost:15341](http://localhost:15341) | `admin` com `SEQ_ADMIN_PASSWORD` como senha inicial |

No Blazor, abra **Lançamentos**, autentique-se como `demo` e registre R$ 50,00 de crédito e R$ 20,00 de débito para o mesmo dia. Em **Consolidado**, consulte a data após o processamento. O movimento líquido esperado para um dia vazio antes do teste é R$ 30,00.

Use `/health/live` e `/health/ready` nas duas APIs e nos workers, cujas portas locais são 18084 e 18085. Veja os logs com `docker compose logs -f web lancamentos consolidado publicador projetor`. Os endereços locais usam HTTP e podem ser acessados apenas pelo próprio host. A configuração registrada de OAuth para os testes automatizados pertence ao ambiente sintético.

## Observabilidade

Use este comando para acrescentar a observabilidade à base, caso tenha escolhido subir por etapas.

```powershell
docker compose -f compose.yaml -f compose.observabilidade.yaml up -d
```

- [Grafana](http://localhost:13000) com usuário `admin` e `GRAFANA_ADMIN_PASSWORD`. Abra o painel **Fluxo de Caixa Diário**.
- [Jaeger](http://localhost:16686) para rastros distribuídos.
- [Seq](http://localhost:15341) com usuário `admin` e `SEQ_ADMIN_PASSWORD`. No primeiro login, o Seq solicita a troca da senha inicial.

O Collector encaminha rastros ao Jaeger, logs ao Seq e métricas ao Prometheus. O Vault está no desenho do ambiente-alvo. A demonstração usa credenciais locais geradas pelo setup.

## MkDocs

Para acrescentar apenas o MkDocs pelo Docker, após preparar o ambiente com o script de setup.

```powershell
docker compose --profile docs up -d mkdocs
```

Para executar diretamente no Windows com Python 3:

```powershell
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements-docs.txt
.venv/Scripts/python -m mkdocs build --strict
.venv/Scripts/python -m mkdocs serve
```

Abra [localhost:8000](http://localhost:8000). Em Linux ou macOS use `.venv/bin/python` nos três comandos após a criação do ambiente. O site estático é gerado em `site/`.

## Testes

Com SDK .NET 11 instalado, execute `dotnet test testes/Caixa.Tests/Caixa.Tests.csproj`. Os cenários abaixo usam o Compose ativo. O script de testes `caixa-testes.ps1` usa PowerShell 7 (`pwsh`).

```powershell
pwsh -NoProfile -File scripts/caixa-testes.ps1 -Modo Integracao
pwsh -NoProfile -File scripts/caixa-testes.ps1 -Modo Resiliencia
pwsh -NoProfile -File scripts/caixa-testes.ps1 -Modo Carga
docker compose -f compose.yaml -f compose.testes.yaml run --rm -e MODO=rapido k6
docker compose -f compose.yaml -f compose.testes.yaml run --rm -e MODO=aceite k6
```

A preparação de carga cria dados sintéticos para 100 contas e grava tokens temporários em `.local/carga`. Preferir um ambiente dedicado de avaliação. Execute `MODO=estresse` no último comando para o cenário exploratório.

Para testar o fluxo pelo navegador no mesmo ambiente virtual do MkDocs:

```powershell
.venv/Scripts/python -m pip install -r requirements-testes.txt
.venv/Scripts/python -m playwright install chromium
.venv/Scripts/python testes/navegador.py
```

A [estratégia de testes](docs/testes.md) detalha os parâmetros e a [matriz de aceite](docs/criterios-de-aceite.md) descreve a cobertura.

Para parar preservando dados, use `docker compose -f compose.yaml -f compose.observabilidade.yaml --profile docs down`. Para recriar um ambiente descartável, use o mesmo comando com `--volumes`, que exclui os bancos e filas locais. Para gerar novas credenciais após a limpeza, remova o `.env` e repita o setup.

## Integração contínua

O [workflow do GitHub Actions](.github/workflows/validar.yml) roda em pushes e pull requests. Ele executa testes unitários, compila o MkDocs em modo estrito, sobe o Compose base com credenciais sintéticas geradas na própria execução e roda os testes de integração HTTP e de navegador. Em falha, exibe os logs dos serviços e, ao final, limpa o ambiente temporário. Carga e resiliência são executadas sob demanda conforme os comandos acima.

## Documentação

[Abrir o índice da documentação](docs/index.md)

| Artefato | Conteúdo |
| --- | --- |
| [Requisitos e domínio](docs/requisitos.md) | Capacidades, escopo e regras |
| [Desenho da solução](docs/system-design.md) | Contexto, contêineres, dados, falhas e arquitetura-alvo |
| [Catálogo tecnológico](docs/stack.md) | Stack, relações entre tecnologias e ativação por ambiente |
| [Contratos](docs/contratos.md) | HTTP, idempotência e evento |
| [Decisões](docs/decisoes.md) | Alternativas, justificativas e consequências |
| [Critérios de aceite](docs/criterios-de-aceite.md) | Evidências e matriz do enunciado |
| [Estratégia de testes](docs/testes.md) | Suítes e cenários |
| [Evidências locais](docs/evidencias.md) | Verificações executadas e cobertura pendente |
| [Identidade e acesso](docs/identidade-e-acesso.md) | Keycloak, papéis e isolamento |
| [Identidade visual](docs/identidade-visual.md) | Aplicação por interface |
| [Operação](docs/operacao.md) | Probes, segurança, diagnóstico e recuperação |
| [Custos](docs/custos.md) | Faixas ilustrativas |
| [Roadmap](docs/roadmap.md) | Sequência da entrega e evoluções |
