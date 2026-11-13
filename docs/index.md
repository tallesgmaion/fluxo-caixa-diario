# Fluxo de Caixa Diário

**Demonstração técnica - dados fictícios**

O projeto registra créditos e débitos e projeta o movimento líquido diário. O [desenho da solução](system-design.md) explica o fluxo e os [critérios de aceite](criterios-de-aceite.md) definem as verificações.

## Execução local

Na raiz do projeto, execute `powershell.exe -NoProfile -File scripts/subir.ps1` no Windows ou `bash scripts/subir.sh` no macOS ou Linux. O comando gera as credenciais no arquivo `.env` e inicia a aplicação, a observabilidade e o MkDocs. A aplicação abre em `http://localhost:18083`. Entre com o usuário `demo` e o valor de `DEMO_PASSWORD` no `.env`. O README da raiz também contém os comandos para subir cada parte separadamente.

## Índice da documentação

### Negócio e entrega

- [Requisitos e domínio](requisitos.md) - capacidades de negócio, escopo e regras de caixa.
- [Critérios de aceite](criterios-de-aceite.md) - cenários verificáveis e matriz do desafio.
- [Roadmap](roadmap.md) - etapas da entrega e evolução do produto.

### Arquitetura e integração

- [System Design](system-design.md) - arquitetura-alvo, diagramas, fluxos e tratamento de falhas.
- [Catálogo tecnológico](stack.md) - stack em uso, dependências e ativação por ambiente.
- [Contratos](contratos.md) - APIs, paginação, idempotência e eventos.
- [Decisões arquiteturais](decisoes.md) - alternativas avaliadas e consequências das escolhas.
- [Identidade e acesso](identidade-e-acesso.md) - autenticação, autorização e sessão.
- [Identidade visual](identidade-visual.md) - aplicação da marca nas interfaces.

### Operação e verificação

- [Operação](operacao.md) - verificações de saúde, monitoramento e recuperação.
- [Estratégia de testes](testes.md) - suítes e cenários de verificação.
- [Evidências locais](evidencias.md) - execuções registradas e cobertura pendente.
- [Custos](custos.md) - premissas e estimativas de infraestrutura.
