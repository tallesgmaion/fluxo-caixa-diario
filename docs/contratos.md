# Contratos

[Voltar ao índice](index.md)

## HTTP

Identificadores são UUIDs. Datas de caixa usam `YYYY-MM-DD` e instantes usam ISO 8601 em UTC. Valores monetários usam strings decimais com duas casas, ponto como separador e moeda explícita. A interface Blazor apresenta os valores em pt-BR. A conta na URL é validada contra a identidade autorizada. O contrato OpenAPI será publicado por API e explorável via Scalar no ambiente local. [OpenAPI e Scalar no ASP.NET Core](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/using-openapi-documents)

| API | Método e rota | Sucesso | Falhas previstas |
| --- | --- | --- | --- |
| Lançamentos | `POST /v1/contas/{contaId}/lancamentos` | `201 Created`, corpo com o lançamento e `Location` para o recurso | `400` dados inválidos ou chave ausente, `409` chave reutilizada com dados distintos, `503` escrita indisponível |
| Lançamentos | `GET /v1/contas/{contaId}/lancamentos/{id}` | `200 OK`, lançamento | `404` recurso ausente naquela conta, `503` indisponibilidade |
| Lançamentos | `GET /v1/contas/{contaId}/lancamentos?inicio=...&fim=...&limite=...&cursor=...` | `200 OK`, itens e cursores opacos `cursorAnterior` e `proximoCursor` quando houver página nessa direção | `400` filtro ou cursor inválido, `503` indisponibilidade |
| Consolidado | `GET /v1/contas/{contaId}/consolidado-diario?inicio=...&fim=...` | `200 OK`, totais diários no intervalo inclusivo | `400` intervalo inválido ou maior que 31 dias, `503` leitura indisponível |

Em todas as rotas, token ausente ou inválido retorna `401`. Identidade válida sem permissão para a operação ou conta retorna `403`. Erros HTTP seguem Problem Details com código estável e identificador de correlação, com detalhes técnicos no log. O histórico ordena por data de caixa e ID em ordem decrescente, com limite padrão de 20 e máximo de 100. O cursor pertence ao conjunto de filtros e deve ser validado.

O Blazor recebe filtros e paginação em formulários POST com proteção antifalsificação. Datas, tamanho da página, cursores e confirmação de lançamento ficam fora da URL do navegador. O servidor web obtém a conta da sessão autenticada e usa o contrato GET das APIs. A URL da tela abre com o período padrão quando acessada diretamente.

### Exemplo de cadastro

```http
POST /v1/contas/00000000-0000-4000-8000-000000000001/lancamentos
Idempotency-Key: 00000000-0000-4000-8000-000000000002
Content-Type: application/json
```

```json
{
  "dataCaixa": "2026-11-13",
  "tipo": "credito",
  "valor": "50.00",
  "moeda": "BRL",
  "descricao": "Venda no balcão"
}
```

Campos de resposta de lançamento: `id`, `contaId`, `dataCaixa`, `tipo`, `valor`, `moeda`, `descricao` e `criadoEmUtc`. A descrição, quando informada, tem até 200 caracteres. Falha de transporte depois do envio pode ser resolvida repetindo o pedido com a mesma chave.

### Idempotência HTTP

A chave é obrigatória no cadastro e tem formato UUID. O Blazor gera uma chave por registro intencional e a mantém em reenvios do mesmo pedido. O servidor compara um hash dos dados normalizados da operação dentro do escopo da conta. Uma repetição equivalente devolve o mesmo `201`, representação e `Location`. Uma repetição divergente devolve `409`. Em corrida, a restrição única no SQL define o vencedor. O segundo pedido resolve o resultado relendo a chave confirmada. A retenção das chaves acompanha o histórico durante o exercício.

### Exemplo de consolidado

```json
{
  "contaId": "00000000-0000-4000-8000-000000000001",
  "moeda": "BRL",
  "dias": [
    {
      "dataCaixa": "2026-11-13",
      "creditos": "50.00",
      "debitos": "20.00",
      "movimentoLiquido": "30.00",
      "atualizadoEmUtc": "2026-11-13T13:00:01Z"
    }
  ]
}
```

O campo `movimentoLiquido` equivale a `creditos - debitos` para a conta e a data da linha. A tela usa o rótulo **Saldo do dia (movimento líquido)**. Um movimento retroativo altera os totais da própria data de caixa. Em dias sem linha projetada, retorna totais zero e `atualizadoEmUtc` nulo. A data de atualização descreve a última aplicação registrada na linha. A interface explica que novos eventos podem estar pendentes, inclusive quando a linha já foi atualizada. Se a consulta falhar, a interface mostra indisponibilidade e oferece nova tentativa.

## Evento RabbitMQ

Exchange `caixa.lancamentos`. Chave `lancamento.registrado.v1`. Mensagem persistente com `content_type=application/json` e `message_id=eventId`. O contexto W3C de rastreamento, se disponível, viaja nos cabeçalhos `traceparent` e `tracestate`. O evento leva apenas os dados necessários à projeção.

```json
{
  "eventId": "00000000-0000-4000-8000-000000000003",
  "eventType": "lancamento.registrado",
  "schemaVersion": 1,
  "occurredAtUtc": "2026-11-13T13:00:00Z",
  "data": {
    "lancamentoId": "00000000-0000-4000-8000-000000000004",
    "contaId": "00000000-0000-4000-8000-000000000001",
    "dataCaixa": "2026-11-13",
    "tipo": "credito",
    "valor": "50.00",
    "moeda": "BRL"
  }
}
```

A versão 1 aceita campos adicionais opcionais e exige os campos exibidos. Formato inválido ou versão incompatível segue para a DLQ sem alterar o resumo. O mesmo evento preserva o identificador em retomadas e reprocessamentos. A [seção de operação](operacao.md#falhas-e-recuperação) descreve a recuperação.
