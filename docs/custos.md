# Estimativa de infraestrutura e licenças

[Voltar ao índice](index.md)

Estimativa preliminar para comparar opções de entrega. A unidade do cenário-alvo é **USD por mês**, com 730 horas mensais para componentes contínuos. As faixas são **hipóteses de planejamento**, calculadas a partir dos valores unitários ilustrativos abaixo. Não são preços coletados nem proposta comercial. O cenário assume uma região brasileira com as zonas e SKUs necessários disponíveis. Confirmar região, capacidade, termos e valores antes de usar a estimativa para contratação. [Calculadora de preços do Azure](https://azure.microsoft.com/pricing/calculator/) · [Guia da Microsoft para estimar custos](https://learn.microsoft.com/pt-br/azure/cost-management-billing/costs/pricing-calculator)

## Ambiente de avaliação

Custo incremental planejado de infraestrutura em nuvem: **US$ 0/mês**, usando máquina já disponível. O dimensionamento inicial é de 8 vCPUs e 16 GB reservados para Docker com o perfil de observabilidade completo, sujeito ao teste. Considerar armazenamento local para imagens, banco e filas. A edição SQL Server Developer se destina a desenvolvimento e teste. O Seq possui licença Individual para uso individual. Um ambiente compartilhado precisa de plano ou avaliação compatível. Conferir também os termos do Docker Desktop se ele for usado na máquina avaliadora. [Edições do SQL Server](https://learn.microsoft.com/en-us/sql/linux/sql-server-linux-editions-and-components-2025) · [Licenças do Seq](https://datalust.co/Pricing) · [Licenciamento do Docker Desktop](https://docs.docker.com/subscription/desktop-license/)

## Arquitetura-alvo com armazenamento de leitura isolado

| Grupo | Premissa de dimensionamento | Hipótese unitária | Faixa mensal |
| --- | --- | --- | --- |
| Web, APIs e workers | 3 nós Linux de aplicação, referência de 4 vCPU e 16 GB por nó, distribuídos entre zonas | US$ 120–250/nó | US$ 360–750 |
| RabbitMQ | 3 nós Linux, referência de 2 vCPU e 8 GB por nó, com volume persistente | US$ 70–150/nó | US$ 210–450 |
| SQL de Lançamentos e de Consolidado | 2 instâncias compatíveis com o mecanismo SQL Server, com HA e licença de produção. Referência inicial de 4 vCores e 128 GB de dados por instância, conforme SKU disponível | US$ 700–2.000/instância, incluindo capacidade de HA e licença assumidas | US$ 1.400–4.000 |
| Observabilidade | Collector e capacidade de armazenamento e consulta para métricas, rastros e logs. Hipótese de retenção quente de 7 dias | US$ 180–550 pelo conjunto, sem licença Seq | US$ 180–550 |
| Identidade, segredos e rede | Entrada balanceada, tráfego de saída baixo, capacidade para Keycloak, seu banco de identidade, Vault e backup adicional | US$ 100–350 pelo conjunto | US$ 100–350 |
| **Subtotal ilustrativo** | Soma das cinco linhas |  | **US$ 2.250–6.100/mês** |

A hipótese de SQL pode ser cotada com duas instâncias do Azure SQL Managed Instance com licença inclusa e redundância por zona, sujeita à edição, SKU, disponibilidade regional e compatibilidade. Se a decisão exigir SQL Server instalado em VMs ou disponibilidade em outra configuração, recotar computação, discos, réplicas, licenciamento e operação. [Preços e opções do Azure SQL Managed Instance](https://azure.microsoft.com/en-us/pricing/details/azure-sql-managed-instance/single/) · [Preços de SQL Server Standard em VMs Azure](https://azure.microsoft.com/en-us/pricing/details/virtual-machines/sql-server-standard/)

**Itens a cotar separadamente:** plano Seq para operação compartilhada, suporte comercial de produtos quando contratado, operação humana, impostos, variação cambial e desastre em outra região. O subtotal também depende da ingestão de telemetria, dos backups, do tráfego entre zonas e do volume de dados retido. O teste de 50 req/s comprova um cenário de avaliação. A capacidade para produção depende de medir escrita, cardinalidade das consultas, crescimento da outbox e concorrência de projeção.

## Taxonomia de custos e tags

O orçamento parte de **produto → ambiente → componente**. Cada linha de custo também tem uma natureza para conciliar a estimativa com o faturamento. Os grupos abaixo mapeiam os recursos do [catálogo tecnológico](stack.md) para o cenário-alvo. No Compose, funcionam como inventário da avaliação local. Na nuvem, servem para orçamento e rateio.

| Grupo da estimativa | `cost-group` | Exemplos de `component` | Direcionadores de custo |
| --- | --- | --- | --- |
| Web, APIs e workers | `aplicacao` | `web`, `lancamentos`, `consolidado`, `publicador`, `projetor`, `nos-aplicacao` | Nó-hora, capacidade de CPU e memória reservada |
| RabbitMQ | `mensageria` | `rabbitmq` | Nó-hora, GB-mês de volume, tráfego entre nós |
| SQL de escrita e leitura | `dados` | `sql-lancamentos`, `sql-consolidado` | Instância-hora, GB-mês de dados e backup, licença incluída ou contratada |
| Observabilidade | `observabilidade` | `collector`, `prometheus`, `grafana`, `jaeger`, `seq` | Ingestão por sinal, retenção em GB-mês e licença Seq conforme o plano |
| Identidade, segredos e rede | `plataforma` | `keycloak`, `sql-identidade`, `vault`, `entrada-https` | Instância-hora, armazenamento de identidade e segredos, tráfego de saída |
| Validação e documentação | `entrega` | `ci`, `k6`, `mkdocs` | Minutos de execução e armazenamento de artefatos, quando cobrados. Fora do subtotal operacional |

As naturezas de custo do relatório são `computacao`, `armazenamento`, `backup`, `rede`, `licenca`, `suporte` e `operacao`. A natureza vem do item de cobrança ou da planilha de estimativa. Um mesmo recurso pode gerar várias linhas de naturezas distintas. Quando o SKU inclui a licença, ela acompanha o preço do SKU. Impostos e câmbio entram na conciliação financeira.

| Tag | Aplicação | Valores e regra |
| --- | --- | --- |
| `product` | Obrigatória | `fluxo-caixa`. Usar `plataforma-compartilhada` quando o recurso atender a vários produtos |
| `environment` | Obrigatória | `local`, `dev`, `hml` ou `prod` |
| `cost-group` | Obrigatória | Uma das categorias do quadro acima |
| `component` | Obrigatória | Identificador estável do componente, em minúsculas e com hífens |
| `owner` | Obrigatória | Identificador da equipe responsável por revisar o consumo |
| `cost-center` | Obrigatória em ambiente faturado | Código fornecido pela organização. Definir antes do provisionamento |
| `allocation` | Obrigatória | `dedicated` para cobrança direta ou `shared` para rateio |
| `expires-on` | Obrigatória para recursos temporários | Data prevista para remoção no formato `AAAA-MM-DD` |

Exemplo para o banco dedicado de leitura em produção, com equipe e centro de custo ilustrativos.

```yaml
product: fluxo-caixa
environment: prod
cost-group: dados
component: sql-consolidado
owner: equipe-caixa
cost-center: cc-exemplo
allocation: dedicated
```

## Alocação e acompanhamento

- **Dedicado.** Atribuir 100% do custo ao produto e ambiente do recurso. Vincular disco e backup ao serviço que os utiliza.
- **Compartilhado.** Ratear nós de aplicação pela capacidade reservada de CPU e memória ponderada por hora, documentando a ponderação. Ratear ingestão e retenção de telemetria por volume medido por serviço e sinal. Usar tráfego medido para rede. Se faltar uma base confiável, manter a parcela visível como compartilhada sem rateio até a medição.
- **Publicação em nuvem.** Definir as tags no provisionamento, validar campos obrigatórios e revisar os registros sem atribuição no fechamento mensal. No Azure, a política de tags atua sobre recursos. A herança no Cost Management complementa os registros de cobrança onde o contrato permitir. Conciliar o total direto, rateado e ainda sem atribuição com a fatura, contando cada despesa uma vez. [Organização e alocação de custos no Azure](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/cost-allocation-introduction)
- **Orçamento.** Depois da cotação, definir teto mensal por ambiente e alertas internos sugeridos em 80% e 100%. A equipe responsável compara consumo e projeção com o orçamento e investiga variações de ingestão, retenção, armazenamento e capacidade.
