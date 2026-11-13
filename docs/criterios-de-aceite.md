# Critérios de aceite e evidências

[Voltar ao índice](index.md)

Os critérios abaixo especificam a entrega. Os resultados das verificações executadas estão em [Evidências de execução local](evidencias.md). As metas propostas estão identificadas nos [requisitos](requisitos.md).

## Funcionalidade e segurança

| ID | Dado e ação | Resultado verificável |
| --- | --- | --- |
| CA-01 | Usuário autorizado registra R$ 50 de crédito e R$ 20 de débito pelo Blazor | A tela confirma cada cadastro. A API responde `201` com ID e `Location`. Cada ID tem um lançamento e um evento na outbox |
| CA-02 | Enviar valor zero, negativo ou com precisão inválida, tipo ou moeda desconhecidos e data futura no fuso de caixa | `400` com Problem Details. As tabelas de lançamento e outbox permanecem sem registros para os pedidos inválidos |
| CA-03 | Semear mais itens do que o limite da página, inclusive na mesma data, e navegar pelo cursor | Filtro, ordenação e paginação são reproduzíveis. No conjunto estável de teste, nenhum lançamento se repete nem fica de fora |
| CA-04 | Consultar três dias consecutivos com R$ 50 em créditos e R$ 20 em débitos no primeiro dia, R$ 10 em créditos no segundo e nenhum movimento no terceiro. Registrar depois um débito retroativo de R$ 5 no primeiro e consultar após a projeção | Saldos iniciais de R$ 30, R$ 10 e R$ 0. Após o retroativo, o primeiro dia retorna créditos de R$ 50, débitos de R$ 25 e saldo de R$ 25. Os outros dias continuam em R$ 10 e R$ 0. A tela mostra **Saldo do dia (movimento líquido)**, a última atualização disponível por dia e a indicação de atualização assíncrona |
| CA-05 | Em operação normal, registrar 100 movimentos sintéticos a uma taxa oferecida de 5/s e medir do commit do lançamento ao commit da projeção | Todos os 100 eventos entram na amostra e no relatório. O p95 do atraso é ≤ 5 s. Guardar a distribuição e a quantidade de eventos aplicados, usando a mesma referência de tempo ou relógios sincronizados |
| CA-06 | Repetir o cadastro com a mesma chave em sequência e em paralelo, reaproveitá-la com conteúdo diferente e simular timeout após commit | Mesma chave e dados retornam mesmo ID e `Location`. Conteúdo divergente retorna `409`. Há um movimento e um evento. Após timeout o reenvio recupera o ID já confirmado |
| CA-11 | Autenticar pelo Keycloak no fluxo web. Chamar as APIs com token ausente, expirado, de audiência inválida, sem permissão e de outra conta. Usar credenciais de integração fora do papel | Aplica `401` ou `403` conforme o [contrato](contratos.md). Nenhum dado de outra conta é exposto. Credenciais de leitura, publicação e consumo só acessam os recursos concedidos. A escrita no Blazor rejeita formulário sem proteção antifalsificação |

## Falhas, recuperação e capacidade

| ID | Falha ou condição | Resultado verificável |
| --- | --- | --- |
| CA-07 | Derrubar o publicador após confirmação do broker e antes de marcar a outbox. Derrubar o consumidor após commit e antes do ack. Reentregar o mesmo evento, inclusive com dois consumidores | O mesmo `eventId` pode reaparecer e produz um único efeito. O total coincide com o histórico. O evento tem estado final publicado e aplicado |
| CA-08 | Parar API e worker de Consolidado por 60 s. Registrar dez movimentos pelo fluxo normal durante a interrupção | Todos os dez cadastros retornam sucesso e persistem. `ready` de Lançamentos permanece saudável. Após retomada, os dez efeitos aparecem uma única vez e o resumo converge em até 120 s |
| CA-09 | Parar RabbitMQ por 60 s. Registrar dez movimentos e consultar o resumo já projetado durante a interrupção | Os cadastros persistem e os eventos ficam na outbox. Após retorno do broker, todos são publicados e aplicados uma vez em até 120 s. A API de Lançamentos permanece pronta durante o teste |
| CA-10 | Remover o vínculo de roteamento. Após restaurá-lo, provocar falha transitória, falha até o limite e evento com esquema inválido | Evento sem rota continua na outbox. Falha transitória recuperável converge. Falhas terminais ficam na DLQ e geram diagnóstico, sem gravar saldo inválido. Após correção da causa de uma falha recuperável, reprocessar com o mesmo ID e verificar um único efeito |
| CA-12 | Executar o [cenário de carga](#cenário-de-carga) na API de Consolidado | Oferta de 50 req/s com perda ≤ 5% e p95 bem-sucedido ≤ 500 ms. Relatório inclui recursos, massa, janela, contadores, latência e resultado |
| CA-13 | Ativar o perfil de observabilidade, registrar um movimento e repetir CA-08 ou CA-09 | A operação acompanha outbox, fila, atraso e recuperação. Um trace correlaciona HTTP, publicação e consumo. Logs e métricas chegam aos destinos previstos. Probes reagem conforme as dependências definidas |
| CA-14 | Interromper primeiro o banco de leitura e depois o de escrita, isoladamente no cenário alvo ou com instâncias separadas para o teste | Com leitura fora, Lançamentos continua apto a receber. Com escrita fora, a API de Lançamentos sinaliza indisponibilidade e não confirma gravações que não persistiram. Liveness continua saudável enquanto o processo estiver íntegro |

### Cenário de carga

1. Usar **k6** com `constant-arrival-rate` e aquecimento separado. Cada iteração de leitura oferece uma consulta HTTP. Preencher dados sintéticos de 90 dias por conta para 100 contas e registrar a versão das imagens, a configuração do Compose, a CPU e a memória do host e do gerador.
2. Durante **cinco minutos**, oferecer 50 requisições autenticadas por segundo, totalizando 15.000 consultas. Alternar contas autorizadas e janelas válidas de 1, 7 e 31 dias com distribuição uniforme. Durante a janela, oferecer 5 registros/s em paralelo e manter o consolidador ativo. A carga paralela de escrita e a duração são premissas de teste.
3. Usar timeout de 2 s por consulta. Contar como perda toda iteração não iniciada, falha de transporte, timeout, resposta diferente de `200` ou resposta fora do contrato. Validar totais conhecidos em datas estáveis e validar formato nas demais respostas. Cada oportunidade ofertada é contada uma só vez.
4. **Taxa de perda = (15.000 − consultas bem-sucedidas) ÷ 15.000 × 100.** O limite é de 750 perdas. Medir o p95 das consultas bem-sucedidas separadamente. Registrar se o gerador saturou e repetir com capacidade suficiente antes de atribuir perda à aplicação.

## Entrega e reprodutibilidade

- **CA-15 - Clone limpo.** Em ambiente com os pré-requisitos descritos, seguir o README para subir Compose, abrir a web, os dois portais Scalar e RabbitMQ Management, executar o fluxo de caixa e rodar os testes. Anotar endereços, credenciais de demonstração ou sua preparação, comandos e como limpar volumes. Validar a navegação MkDocs, a renderização dos diagramas e a [verificação visual](identidade-visual.md#verificação-visual).
- **CA-16 - Entrega pública.** Acesso ao repositório GitHub em sessão sem autenticação, com código, configuração, documentação, testes e evidências referenciados a partir do README. Revisar segredos, dados sintéticos e histórico do Git antes da publicação.

## Estratégia de testes

A [estratégia de testes](testes.md) detalha as suítes .NET, os testes de navegador e os cenários de falha, carga e estresse. Os critérios desta página definem o que deve ser demonstrado.

## Matriz de cobertura do enunciado

| Item do enunciado | Cobertura documental | Evidência ou situação |
| --- | --- | --- |
| Serviço de controle de lançamentos | RF-01, RF-02 e contrato HTTP | API, formulário e histórico implementados. Fluxo principal verificado. Cobertura completa de validação e paginação pendente |
| Serviço de consolidado diário | RF-03, RF-04, projeção e contrato HTTP | API e tela implementadas. Projeção e retroatividade verificadas no navegador. CA-05 medido em [execução local](evidencias.md) |
| Mapeamento de domínios e capacidades | [Requisitos e domínio](requisitos.md#domínios-e-capacidades) | Documentado |
| Refinamento de requisitos funcionais e não funcionais | [Requisitos e regras](requisitos.md) e critérios desta página | Documentado, com metas adicionais identificadas |
| Arquitetura-alvo completa | [Desenho da solução](system-design.md) e [operação](operacao.md) | Contexto, contêineres, dados, fluxos, implantação e falhas documentados |
| Justificativa de arquitetura, linguagem e tecnologias | [Decisões](decisoes.md) | Documentado |
| Lançamentos disponíveis na queda do consolidado | [Falhas no desenho](system-design.md#consulta-diária-e-degradação) | CA-08 verificado em Compose |
| 50 req/s e até 5% de perda | [Cenário de carga](#cenário-de-carga) | CA-12 verificado com [k6 no Compose](evidencias.md#aceite-de-carga) |
| Testes | Critérios nesta página e [estratégia de testes](testes.md) | Suítes executáveis e [evidências locais](evidencias.md). Cobertura ampliada pendente |
| README com funcionamento e execução local | [Execução local](index.md#execução-local) | Comando do README e fluxo principal validados a partir do [clone público](evidencias.md#repositório-público-e-clone-limpo) |
| Repositório público GitHub e toda a documentação no repositório | [Índice de documentação](index.md#índice-da-documentação) | Clone HTTPS e documentação pública verificados sem autenticação |
| Arquitetura de transição, quando necessária | [Implantação e transição](system-design.md#implantação-local-e-arquitetura-alvo) | Greenfield. Plano de entrada em operação documentado |
| Estimativa de infraestrutura e licenças | [Custos](custos.md) | Cenários e faixas ilustrativas documentados |
| Monitoramento e observabilidade | [Operação](operacao.md#observabilidade) | Perfil Compose disponível, com coleta verificada no Prometheus e no Jaeger. Evidência completa de CA-13 pendente |
| Segurança para consumo e integração | [Operação e segurança](operacao.md#segurança-das-integrações) | Login, ausência de token, audiência e conta verificados. Casos adicionais de CA-11 pendentes |
| Evoluções futuras, sugeridas nas observações | [Roadmap e backlog](roadmap.md) | Ordem de entrega e condições para priorizar evoluções documentadas |
