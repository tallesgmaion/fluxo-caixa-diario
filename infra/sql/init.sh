#!/bin/bash
set -euo pipefail
export SQLCMDPASSWORD="$SA_PASSWORD"
sqlcmd=/opt/mssql-tools18/bin/sqlcmd
conexao=(-S sql -U sa -C -l 2 -b)

# O login pode funcionar enquanto os bancos de um volume existente ainda estão em recuperação.
prontidao="IF EXISTS (
    SELECT 1 FROM sys.databases
    WHERE name IN (N'model', N'tempdb', N'CaixaEscrita', N'CaixaLeitura', N'CaixaIdentidade')
      AND (state <> 0 OR ISNULL(HAS_DBACCESS(name), 0) <> 1)
) THROW 50001, 'Aguardando a recuperacao dos bancos de caixa.', 1;"

for ((tentativa = 1; tentativa <= 60; tentativa++)); do
    if "$sqlcmd" "${conexao[@]}" -d master -t 3 -Q "$prontidao" >/dev/null 2>&1; then
        if saida=$("$sqlcmd" "${conexao[@]}" -d master -i /infra/schema.sql \
            -v WritePassword="$WRITE_PASSWORD" PublishPassword="$PUBLISH_PASSWORD" ReadPassword="$READ_PASSWORD" \
               ProjectPassword="$PROJECT_PASSWORD" IdentityPassword="$IDENTITY_PASSWORD" 2>&1); then
            printf '%s\n' "$saida"
            exit 0
        fi
        if [[ ! "$saida" =~ (^|[[:space:]])Msg[[:space:]]904, ]]; then
            printf '%s\n' "$saida" >&2
            exit 1
        fi
    fi
    if ((tentativa < 60)); then sleep 2; fi
done

printf '%s\n' 'Tempo esgotado aguardando a recuperação do SQL Server. Consulte os logs de sql e sql-init.' >&2
exit 1
