# Identidade visual

[Voltar ao índice](index.md)

A demonstração de Fluxo de Caixa usa o **Carrefour Banco** como referência visual. O nome da aplicação será **Fluxo de Caixa Diário**. Nas interfaces destinadas à avaliação, o cabeçalho ou rodapé identifica o ambiente como **Demonstração técnica - dados fictícios**. [Página institucional do Carrefour Banco](https://grupocarrefourbrasil.com.br/marcas/carrefour-banco) · [Carrefour Soluções Financeiras](https://www.carrefoursolucoes.com.br/sobre-nos/)

## Direção visual

- Usar o azul principal da logomarca para links e ações, e o azul-claro para bordas e detalhes. Reservar o vermelho para mensagens de erro e estados que exigem atenção.
- A [logomarca](assets/carrefour-banco.png) e o [favicon](assets/carrefour-banco-icon.png) são recursos locais compartilhados. A imagem da marca corresponde à referência institucional, com arte digital obtida na matéria abaixo. O arquivo foi ajustado nas margens e dimensões para cabeçalhos e abas. [Referência institucional](https://grupocarrefourbrasil.com.br/marcas/carrefour-banco) · [Fonte do arquivo digital](https://www.bloomberglinea.com.br/2025/07/24/banco-carrefour-aposta-em-digitalizacao-e-dados-para-seguir-crescendo-alem-das-lojas/)
- Adotar **Inter** como tipografia da aplicação, com alternativas de sistema e números tabulares nas colunas monetárias. Exibir créditos, débitos e saldo com rótulos textuais e usar pt-BR nas datas e nos valores.
- Compartilhar uma base de cores, fonte e espaçamentos. Cada ferramenta aplica essa base pela configuração ou pelos estilos que suporta. Carregar os recursos visuais localmente no Compose para que a demonstração funcione sem depender dos sites de referência.

| Uso | Cor proposta |
| --- | --- |
| Navegação, links e ações primárias | `#00529C` |
| Bordas e destaques decorativos | `#61B2E5` |
| Texto, cabeçalhos escuros | `#0B2440` |
| Erros e alertas críticos | `#BB1731` |
| Fundo secundário | `#F3F6FA` |
| Superfície principal e texto sobre azul | `#FFFFFF` |

## Aplicação por interface

| Superfície | Personalização |
| --- | --- |
| **Blazor** | Logomarca no cabeçalho quando disponível, favicon, navegação azul e formulários com foco visível. Relatório com totais legíveis, data da última atualização e mensagens de validação e indisponibilidade. |
| **MkDocs** | Tema Material com logomarca no cabeçalho, favicon, paleta e tipografia compartilhadas. Estilos leves para tabelas e navegação. Governar a cor dos diagramas Mermaid pelo tema onde houver suporte e manter legibilidade na leitura direta do Markdown. |
| **Scalar** | Títulos distintos para **API de Lançamentos** e **API de Consolidado**, favicon e cores via configuração e CSS. Usar cabeçalho com logomarca quando houver suporte no pacote ASP.NET Core adotado. |
| **Keycloak** | Tema de login pt-BR com o título **Fluxo de Caixa Diário**, a mesma paleta e aviso de demonstração junto ao formulário. Logomarca institucional onde houver arquivo disponível para uso, em ambiente local de avaliação com credenciais sintéticas. |
| **Grafana** | Nome, organização dos painéis e cores do projeto nos dashboards próprios, preservando as convenções do Grafana para estados e alertas. |

[Logo e favicon no Material for MkDocs](https://squidfunk.github.io/mkdocs-material/setup/changing-the-logo-and-icons/) · [Scalar no ASP.NET Core](https://scalar.com/products/api-references/integrations/aspnetcore/integration) · [Temas do Scalar](https://scalar.com/products/api-references/themes) · [Temas do Keycloak](https://www.keycloak.org/ui-customization/themes)

## Verificação visual

- Abrir o Blazor, o MkDocs, as duas referências Scalar e o login do Keycloak pelo roteiro de avaliação. Confirmar título, identificação de demonstração, cores e ausência de imagens quebradas.
- Conferir proporção e legibilidade da logomarca quando aplicada, contraste de texto e botões, foco de teclado, layout em celular e desktop e legibilidade dos diagramas.
- Conferir que status, erros e sinais dos valores são compreensíveis com os rótulos mesmo sem distinguir as cores. Em superfícies com modo escuro, verificar também essa variação.
