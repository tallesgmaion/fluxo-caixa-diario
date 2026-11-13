# Evidências de execução local

[Voltar ao índice](index.md)

## Ambiente

Execução local em Docker Desktop com contêineres Linux x86-64. O Docker disponibilizou 22 CPUs lógicas e cerca de 15,4 GiB de memória. A aplicação foi compilada com o SDK .NET 11. O mesmo host executou o gerador e o perfil de observabilidade.

## Verificações concluídas

| Verificação | Resultado observado |
| --- | --- |
| Testes unitários | 10 aprovados em .NET 11 |
| Integração HTTP | Login para obtenção de token, autenticação obrigatória, audiência inválida, isolamento por conta, repetição idempotente, conflito de chave e aplicação única no resumo |
| Navegador | Login no Keycloak, lançamento, histórico, crédito em verde, consolidado e paginação. A atualização da consulta usou GET e manteve uma única linha para o lançamento. Após sair do histórico, o botão Voltar exibiu o login, sem pedir reenvio do formulário. O cookie antigo foi rejeitado. Uma rota inválida exibiu a página HTML com status 404 e link para o início |
| Queda do consolidado | Dez lançamentos confirmados enquanto a API e o worker de consolidação estavam parados. Após a retomada, o saldo refletiu os dez lançamentos uma vez |
| Queda do RabbitMQ | Dez lançamentos confirmados durante a parada do broker e refletidos uma vez no saldo após a retomada |
| Infraestrutura e documentação | Compose base e configuração de observabilidade iniciados. Scalar e interfaces locais responderam. Jaeger registrou os cinco serviços e Prometheus coletou métricas HTTP, de caixa e de fila. MkDocs compilou em modo estrito |
| CA-05 - atraso da projeção | 100 de 100 movimentos aplicados, oferecidos a 5/s. Diferença entre o instante registrado na outbox e a aplicação no SQL Server com mínimo de 6 ms, média de 190 ms, p95 de 350 ms e máximo de 385 ms |
| CA-06 - idempotência concorrente | 12 pedidos simultâneos com a mesma chave devolveram o mesmo ID e geraram uma única linha na outbox |
| CA-07 - falhas entre confirmação e persistência | Na cópia isolada, o publicador foi interrompido após confirmação do broker e antes da atualização da outbox. O projetor foi interrompido após o commit e antes do ack. Após cada reinício, o lançamento contribuiu uma única vez para o saldo |
| CA-10 - roteamento, retentativa e DLQ | A mensagem sem vínculo permaneceu pendente na outbox. Após restaurar o vínculo, a publicação e a aplicação foram retomadas. Também foram verificados a retentativa de falha SQL, o envio à DLQ após o limite, a rejeição de versão inválida e o reprocessamento com o mesmo ID após a recuperação, com um único efeito no saldo |
| CA-14 - bancos indisponíveis | Em ambiente isolado, o banco de leitura foi colocado offline e a escrita continuou. Com o banco de escrita offline, registrar retornou 503 e o consolidado permaneceu disponível. Os probes de processo e prontidão refletiram o estado das dependências |
| CA-15 - ambiente limpo antes da publicação | A partir de uma cópia só com arquivos elegíveis ao Git, foram criados volumes e credenciais novos. Passaram o teste de navegador e o acesso aos dois Scalar, ao RabbitMQ Management e ao MkDocs. O ensaio usou portas alternativas para preservar o ambiente local |

## Aceite de carga

Em 13/11/2026, com k6 1.5.0, 100 contas e 90 dias de dados sintéticos por conta, o cenário de 5 minutos atingiu **50 consultas/s, 0% de perda observada e p95 de 2,46 ms** nas consultas válidas. O gerador registrou 15.001 consultas válidas e 1.501 lançamentos confirmados em paralelo, sem falhas HTTP, descartes ou interrupções. A diferença de uma iteração em cada cenário decorre da contagem do gerador na fronteira da janela. O teste rápido anterior validou 20 de 20 consultas.

```powershell
pwsh -NoProfile -File scripts/caixa-testes.ps1 -Modo Carga
docker compose -f compose.yaml -f compose.testes.yaml run --rm -e MODO=aceite k6
```

## Estresse exploratório

O k6 alternou patamares de 50, 75, 100 e 150 consultas/s e voltou a 50 consultas/s. No total de 10 min 50 s, houve 55.249 consultas válidas, 0 falhas HTTP, 0 descartes observados e p95 agregado de 3,25 ms. O limite de capacidade ficou acima do maior patamar deste ensaio. O cenário de estresse usou somente leituras.

## Verificações adicionais

A verificação de acesso anônimo ao repositório e de execução a partir da URL pública fica para a publicação. Os ensaios locais cobrem reentrega após interrupção e republicação. A variante com dois consumidores simultâneos, o timeout HTTP deliberado após commit e a verificação completa da telemetria durante uma queda permanecem como cobertura adicional. A [estratégia de testes](testes.md) reúne os cenários reproduzíveis.
