# Requisitos e domínio

[Voltar ao índice](index.md)

## Contexto e resultado esperado

Um comerciante precisa registrar entradas e saídas de caixa e saber quanto movimentou em cada dia. O Carrefour Banco serve como referência de contexto para este estudo hipotético de um produto voltado ao comerciante. O usuário principal é o operador de um estabelecimento. O responsável pelo estabelecimento consulta o relatório. Uma pessoa de operação técnica monitora a integração e recupera mensagens com falha.

A cadeia de valor compreende **registrar o movimento → consolidar suas consequências → consultar os totais → identificar divergências e recuperar o processamento**.

## Domínios e capacidades

| Domínio ou capacidade de apoio | Responsabilidade | Componente responsável | Dado sob sua responsabilidade |
| --- | --- | --- | --- |
| Controle de Caixa | Validar e registrar créditos e débitos. Consultar o histórico | API de Lançamentos | Lançamento, chave de idempotência, evento na outbox |
| Consolidação | Projetar totais e disponibilizar o relatório diário | Worker e API de Consolidado | Resumo por conta e dia, eventos aplicados |
| Identidade e acesso | Identificar o usuário, autorizar operações e delimitar a conta de caixa | Keycloak, Blazor e APIs | Identidade e vínculo autorizado com a conta |
| Integração e operação | Encaminhar eventos, observar atraso, tratar falhas e reconciliar dados | Publicador, RabbitMQ e equipe de operação | Estado de publicação, filas, diagnósticos e registros operacionais |

## Escopo funcional

| ID | Requisito | Origem | Aceite |
| --- | --- | --- | --- |
| RF-01 | Registrar créditos e débitos com data de caixa, valor e descrição opcional | Enunciado, detalhado | CA-01 e CA-02 |
| RF-02 | Consultar histórico de lançamentos por período com paginação | Refinamento do controle de lançamentos | CA-03 |
| RF-03 | Consultar totais diários de créditos, débitos e movimento líquido de um período | Enunciado, com interpretação de saldo definida em P-01 | CA-04 |
| RF-04 | Exibir quando o resumo do dia foi atualizado e informar a atualização assíncrona | Decisão de experiência | CA-04 e CA-05 |
| RF-05 | Reenviar um cadastro sem duplicar o movimento, inclusive sob concorrência | Refinamento de confiabilidade | CA-06 |
| RF-06 | Aplicar eventos com deduplicação e recuperar a consolidação após interrupções | Refinamento de confiabilidade | CA-07 a CA-10 |
| RF-07 | Autorizar registro e consulta por conta de caixa | Refinamento de segurança | CA-11 |

## Requisitos não funcionais

| ID | Requisito ou meta | Origem | Aceite |
| --- | --- | --- | --- |
| RNF-01 | Lançamentos continuam disponíveis quando o Consolidado cai | Enunciado | CA-08 |
| RNF-02 | Consolidado suporta 50 requisições por segundo em pico, com até 5% de perda | Enunciado | CA-12 |
| RNF-03 | Lançamentos continuam disponíveis durante indisponibilidade do RabbitMQ, enquanto houver capacidade no SQL de escrita | Decisão de arquitetura | CA-09 |
| RNF-04 | Em operação normal, p95 de até 5 s entre confirmação SQL e aplicação no resumo, no cenário de teste | Meta proposta | CA-05 |
| RNF-05 | p95 de até 500 ms nas respostas bem-sucedidas ao consultar o Consolidado sob a carga de RNF-02 | Meta proposta | CA-12 |
| RNF-06 | Proteção de credenciais e tráfego, autorização por conta e menor privilégio | Enunciado e refinamento | CA-11 |
| RNF-07 | Disponibilizar métricas, rastros, logs correlacionáveis, probes e alertas operacionais | Diferencial e decisão de projeto | CA-13 |
| RNF-08 | Para o lote de falha do teste, convergir em até 120 s depois que as dependências estiverem prontas, preservando os movimentos confirmados | Meta proposta para o cenário de teste | CA-08 e CA-09 |

## Regras de caixa e premissas

| ID | Regra ou premissa adotada no desenho |
| --- | --- |
| P-01 | O saldo diário do MVP é o **movimento líquido do dia**, calculado por conta como créditos menos débitos na data de caixa. O relatório exibe créditos, débitos e **Saldo do dia (movimento líquido)**. R$ 50 em créditos e R$ 20 em débitos resultam em R$ 30. [ADR-11](decisoes.md#adr-11-saldo-diário-como-movimento-líquido) |
| P-02 | Uma conta de caixa por comerciante na demonstração. O identificador de conta existe no modelo e delimita dados e operações. Serão usadas contas e identidades sintéticas. |
| P-03 | Moeda BRL. O valor é informado como positivo e o tipo determina o sinal no cálculo. Débitos podem superar créditos no dia. Valores são armazenados em decimal de precisão fixa. |
| P-04 | A data de caixa é um dia civil em `America/Sao_Paulo`. O sistema registra o instante de criação em UTC separadamente. O lançamento pode ter data anterior ou igual ao dia corrente nesse fuso. O evento preserva a data informada. |
| P-05 | Lançamentos são imutáveis no MVP. Um movimento retroativo atualiza somente o dia de caixa correspondente. Edição, exclusão, estorno formal, importação, conciliação com extrato, saldo de abertura e integração bancária ficam fora do escopo inicial. |
| P-06 | O relatório aceita um intervalo de até 31 dias e retorna uma linha por dia, inclusive com totais zero quando ainda não existe projeção. Zero descreve o que já foi consolidado. Durante interrupções, a tela comunica que o relatório pode estar defasado. |
| P-07 | A retenção do histórico e dos eventos do exercício permite conciliação e recomposição durante a avaliação. Prazo de guarda de produção, volume real de escrita e metas de desastre ficam sujeitos à validação de negócio e operação. |

As regras e premissas acima são a base da implementação e dos testes da demonstração.
