# Estratégia de testes

[Voltar ao índice](index.md)

## Reproduzir a deduplicação com dois consumidores

Na raiz do projeto, gere as credenciais com `powershell.exe -NoProfile -File scripts/setup.ps1` no Windows ou `bash scripts/setup.sh` no macOS ou Linux. O ensaio usa um projeto Compose separado, RabbitMQ Management na porta local 25672 e Python 3 com a biblioteca padrão.

```bash
docker compose -p caixa-concorrencia -f compose.yaml -f testes/compose.concorrencia.yaml up -d --build --scale projetor=2 sql sql-init rabbitmq projetor
python testes/concorrencia.py
docker compose -p caixa-concorrencia -f compose.yaml -f testes/compose.concorrencia.yaml down
```

O script confirma dois consumidores no broker, observa duas requisições de aplicação simultaneamente bloqueadas no SQL e exige um ACK em cada réplica. O teste termina com sucesso quando o evento aparece uma única vez na inbox e o resumo contém exatamente um movimento de R$ 42,50. [Evidência do CA-07](evidencias.md#dois-consumidores-concorrentes-ca-07)

O plano cobre os [critérios de aceite](criterios-de-aceite.md). A implementação inclui xUnit, verificações HTTP contra SQL Server e RabbitMQ no Compose, Playwright e k6. A tabela registra também a cobertura planejada. Os limites exigidos pelo enunciado estão identificados em [requisitos](requisitos.md#requisitos-não-funcionais).

## Suítes .NET

| Suíte | Ferramentas | Cobertura prioritária |
| --- | --- | --- |
| Unitários | xUnit, sem infraestrutura externa | Validação monetária, tipos de lançamento e datas no fuso do caixa. Cálculo de crédito, débito e movimento líquido, dias vazios, retroatividade e normalização para idempotência |
| Integração | xUnit, `WebApplicationFactory` para as Minimal APIs e Testcontainers com SQL Server e RabbitMQ nas versões do Compose | Atomicidade do lançamento e da outbox com rollback, idempotência sob concorrência, incremento simultâneo do mesmo dia, confirms, publicação sem rota, redelivery, retry e DLQ |
| Contrato e autorização | Na suíte de integração, exercitando HTTP e usando amostras versionadas dos eventos | Status e Problem Details, JSON e campos exigidos pelo OpenAPI, token inválido e isolamento por conta. Compatibilidade com campos opcionais adicionais no evento e rejeição de versão incompatível |
| Ponta a ponta | Playwright em Chromium, com a aplicação no Compose e identidades sintéticas | Login, registro, consulta e paginação, projeção, página 404 e retorno ao login ao usar Voltar após sair. Atualizar uma consulta refaz uma leitura sem duplicar o lançamento. Relatório indisponível com cadastro ainda funcional |
| Resiliência | Compose, controles de falha do ambiente de teste e asserções automatizadas | Parar broker e consolidado separadamente. Reproduzir interrupções entre confirm e atualização da outbox e entre commit e ack. Verificar probes, retomada, duplicatas e reconciliação com o histórico |

Para fluxos assíncronos, aguardar a condição observável com prazo explícito e guardar o estado das dependências em caso de timeout. Testes de concorrência devem verificar os efeitos gravados e os códigos HTTP. A suíte de navegador exercita a autenticação real. WebApplicationFactory e Testcontainers seguem como evolução da suíte. [Testes de Minimal APIs](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis/test-min-api) · [Testcontainers para SQL Server](https://dotnet.testcontainers.org/modules/mssql/) · [Testcontainers para RabbitMQ](https://dotnet.testcontainers.org/modules/rabbitmq/) · [Playwright](https://playwright.dev/python/docs/intro)

## Carga e estresse com k6

O gerador usa a imagem `grafana/k6` com versão fixada, como execução sob demanda no Compose. Os cenários chamam a API de Consolidado diretamente, com tokens válidos do Keycloak para contas sintéticas preparados antes da janela medida. Cada iteração de leitura oferece **uma consulta HTTP**. A execução de aceite deixa o gerador com recursos suficientes e registra se ele compartilhou o host com a aplicação. [k6 no Docker](https://grafana.com/docs/k6/latest/set-up/install-k6/) · [Taxa de chegada constante](https://grafana.com/docs/k6/latest/using-k6/scenarios/executors/constant-arrival-rate/)

| Cenário | Configuração inicial | Evidência |
| --- | --- | --- |
| Teste rápido | Poucas consultas com massa conhecida | Credenciais válidas, resposta coerente e coleta de métricas |
| Carga de aceite | `constant-arrival-rate`, 50 consultas/s por 5 min e cenário simultâneo de 5 lançamentos/s, conforme [CA-12](criterios-de-aceite.md#cenário-de-carga) | Até 5% de perda sobre 15.000 consultas ofertadas e p95 de até 500 ms entre as respostas válidas, conforme meta proposta |
| Estresse exploratório | `ramping-arrival-rate`, com patamares de 50, 75, 100 e 150 consultas/s, por 2 min cada como ponto de partida | Maior patamar observado dentro das metas, componente saturado e comportamento ao retornar a 50/s. Interromper se houver saturação sustentada, perda de integridade ou risco de esgotar recursos do ambiente |
| Recuperação sob carga | Opcional após os testes controlados de falha. Manter uma taxa moderada de escrita durante a interrupção do broker ou consumidor | Confirmar a disponibilidade de escrita, medir o tempo de drenagem e reconciliar os lançamentos confirmados após a retomada |

A leitura de aceite registra um contador de **consultas bem-sucedidas**. Só incrementa para HTTP 200 no prazo, com corpo válido segundo as asserções do cenário. O limite é **pelo menos 14.250 sucessos das 15.000 oportunidades previstas**, equivalente a no máximo 750 perdas. Iterações descartadas pelo gerador (`dropped_iterations`), requisições com erro e respostas inválidas entram na perda pela diferença entre oferta e sucessos. Reportar cada motivo separadamente, sem somá-lo de novo à perda. Uma métrica própria de latência registra apenas sucessos válidos, com limiar p95 de 500 ms. No cenário de aceite, os limiares do k6 devem sinalizar falha da execução sem encerrar antecipadamente a janela medida. Se o gerador saturar, guardar a execução e repetir com recursos suficientes antes de concluir sobre a API. [Iterações descartadas](https://grafana.com/docs/k6/latest/using-k6/scenarios/concepts/dropped-iterations/) · [Limiares do k6](https://grafana.com/docs/k6/latest/using-k6/thresholds/) · [Taxa de chegada variável](https://grafana.com/docs/k6/latest/using-k6/scenarios/executors/ramping-arrival-rate/)

## Execução e evidências

- **Pull requests:** rodar unitários e integrados, incluindo contratos e autorização. A suíte de navegador cobre os fluxos essenciais quando o ambiente Compose estiver disponível no CI.
- **Antes da entrega:** rodar o cenário de carga, os testes de falha e o clone limpo em ambiente identificado. Executar estresse em ambiente isolado como diagnóstico de capacidade. A meta de 50 req/s continua sendo o critério do enunciado.
- **Resultados:** guardar a revisão testada, versões das imagens, perfil do Compose, CPU e memória, massa sintética, duração, comando executado e resultado por critério. Para carga, registrar consultas oferecidas, iniciadas, descartadas e bem-sucedidas, p95, distribuição de erros e taxa de sucesso da escrita de fundo. Para resiliência, guardar a linha do tempo, os totais reconciliados e os sinais operacionais relevantes. O README final referencia as instruções e evidências.
