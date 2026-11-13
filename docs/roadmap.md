# Roadmap e backlog

[Voltar ao índice](index.md)

A prioridade da entrega é demonstrar que um comerciante consegue registrar créditos e débitos, consultar o movimento do dia e continuar registrando durante a queda do consolidado. O [enunciado foi mapeado aos critérios de aceite](criterios-de-aceite.md#matriz-de-cobertura-do-enunciado). O roadmap organiza a execução e registra evoluções para discussão após a entrega. As etapas abaixo estão planejadas.

## Backlog da entrega do desafio

| Ordem | Entrega | Resultado e evidência esperada | Dependência |
| --- | --- | --- | --- |
| 1 | Fixar o contrato e preparar a massa de teste | Decisão de saldo e retroatividade registrada em requisitos, contrato e aceite. Preparar a massa sintética para a janela de medição prevista em CA-12 | [ADR-11](decisoes.md#adr-11-saldo-diário-como-movimento-líquido) |
| 2 | Preparar a base reproduzível | Criar o repositório da solução, Compose com SQL Server, RabbitMQ Management e Keycloak, migrações, configuração do realm e dados sintéticos. Garantir inicialização a partir de volumes vazios | Contrato do relatório documentado |
| 3 | Entregar o fluxo completo de caixa | Registrar crédito e débito pelo Blazor, propagar o evento pela outbox, RabbitMQ e dois workers .NET e consultar o consolidado. Disponibilizar histórico, autorização por conta e referências Scalar | Compose e contratos. CA-01 a CA-06 e CA-11 |
| 4 | Demonstrar integridade e recuperação | Executar testes de deduplicação, concorrência, queda do consolidado e do RabbitMQ, reentrega, DLQ e reconciliação. Registrar os resultados com probes e rastros | Fluxo completo. CA-07 a CA-10 e CA-13 a CA-14 |
| 5 | Medir capacidade | Executar o aceite k6 com 50 consultas por segundo e até 5% de perda. Guardar os resultados e investigar gargalos observados. Registrar o estresse exploratório separadamente | Ambiente reproduzível e massa de teste. CA-12 |
| 6 | Preparar a entrega pública | Validar README em clone limpo, MkDocs, Mermaid, tema da demonstração e as duas referências Scalar. Publicar no GitHub com testes e evidências indexados | Evidências da implementação. CA-15 e CA-16 |

Adicionar testes unitários e de integração junto com cada incremento. A [estratégia de testes](testes.md) define as suítes, os cenários do k6 e a execução no CI.

## Evoluções candidatas após o desafio

| Evolução | Valor esperado | Quando priorizar |
| --- | --- | --- |
| Saldo de abertura e fechamento | Acompanhar o caixa acumulado e explicar correções retroativas | Priorizar quando o negócio precisar acompanhar caixa acumulado e houver uma fonte definida para o saldo inicial |
| Estorno auditável e conciliação | Corrigir erros de caixa com histórico e identificar divergências com extratos | Após definir regras de estorno, origem dos extratos e responsabilidade pela conciliação |
| Várias contas por comerciante e acesso delegado | Apoiar múltiplas lojas ou caixas com permissões específicas | Quando o cadastro de estabelecimentos e papéis exigir mais de uma conta por usuário |
| Operação da arquitetura-alvo | Comprovar failover, restauração, retenção e tempos de recuperação | Antes de uso em produção. Validar RPO e RTO de desastre, volume, custos cotados e responsabilidades |
| Redis para consultas do consolidado | Reduzir carga de leitura do SQL em consultas repetidas | Se o teste de carga identificar o SQL de leitura como gargalo e houver meta de defasagem de cache definida |
