"""Verifica a deduplicação com duas réplicas e o SQL real em ambiente isolado."""
import base64
import json
import subprocess
import time
import urllib.request
from datetime import date, datetime, timezone
from decimal import Decimal
from pathlib import Path
from uuid import uuid4


COMPOSE = ["docker", "compose", "-p", "caixa-concorrencia",
           "-f", "compose.yaml", "-f", "testes/compose.concorrencia.yaml"]
SQL = COMPOSE + ["exec", "-T", "sql", "bash", "-ec",
                 'export SQLCMDPASSWORD="$MSSQL_SA_PASSWORD"; exec /opt/mssql-tools18/bin/sqlcmd '
                 '-S localhost -U sa -C -b -d CaixaLeitura -h -1 -W -s "|" "$@"', "sqlcmd"]
QUEUE = "/queues/fluxo-caixa/caixa.consolidado.v1"


def execute(command):
    return subprocess.check_output(command, text=True, encoding="utf-8", timeout=20).strip()


def sql(query):
    return execute(SQL + ["-Q", "SET NOCOUNT ON; " + query])


def wait_for(description, condition, timeout=25):
    until = time.monotonic() + timeout
    while time.monotonic() < until:
        if value := condition():
            return value
        time.sleep(0.4)
    raise AssertionError(f"Tempo esgotado: {description}")


def main():
    config = dict(line.split("=", 1) for line in Path(".env").read_text().splitlines() if "=" in line)
    auth = base64.b64encode(f'operacao:{config["RABBIT_ADMIN_PASSWORD"]}'.encode()).decode()

    def rabbit(path, body=None):
        request = urllib.request.Request(
            "http://127.0.0.1:25672/api" + path,
            data=json.dumps(body).encode() if body is not None else None,
            headers={"Authorization": f"Basic {auth}", "Content-Type": "application/json"})
        with urllib.request.urlopen(request, timeout=4) as response:
            return json.load(response)

    replicas = execute(COMPOSE + ["ps", "-q", "projetor"]).splitlines()
    assert len(replicas) == 2, "O ensaio exige duas réplicas do projetor."
    wait_for("dois consumidores registrados", lambda: rabbit(QUEUE)["consumers"] == 2)
    event_id, account = str(uuid4()), str(uuid4())
    day = date.today().isoformat()
    event = {
        "eventId": event_id, "eventType": "lancamento.registrado", "schemaVersion": 1,
        "occurredAtUtc": datetime.now(timezone.utc).isoformat(),
        "data": {"lancamentoId": str(uuid4()), "contaId": account, "dataCaixa": day,
                 "tipo": "credito", "valor": "42.50", "moeda": "BRL"},
    }
    summary = f"FROM dbo.ResumoDiario WHERE ContaId='{account}' AND DataCaixa='{day}'"
    inbox = f"FROM dbo.EventoAplicado WHERE EventoId='{event_id}'"
    assert sql(f"SELECT COUNT(*) {summary}; SELECT COUNT(*) {inbox};").splitlines() == ["0", "0"]

    # Mantém as duas requisições reais de Apply bloqueadas para comprovar a sobreposição.
    gate = subprocess.Popen(SQL + ["-Q",
        "SET NOCOUNT ON; BEGIN TRANSACTION; "
        "SELECT COUNT_BIG(*) FROM dbo.EventoAplicado WITH (TABLOCKX, HOLDLOCK); "
        "WAITFOR DELAY '00:00:40'; ROLLBACK;"], stdout=subprocess.DEVNULL)
    try:
        blocker = wait_for("bloqueio de teste", lambda: sql(
            "SELECT session_id FROM sys.dm_exec_requests WHERE command='WAITFOR' AND database_id=DB_ID();"))
        assert blocker.isdigit()
        message = {"properties": {"message_id": event_id, "delivery_mode": 2},
                   "routing_key": "lancamento.registrado.v1", "payload": json.dumps(event),
                   "payload_encoding": "string"}
        for _ in range(2):
            assert rabbit("/exchanges/fluxo-caixa/caixa.lancamentos/publish", message)["routed"]
        blocked = f"""SELECT COUNT(*) FROM sys.dm_exec_requests AS r
            JOIN sys.dm_exec_sessions AS s ON s.session_id=r.session_id
            CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) AS q
            WHERE s.login_name='caixa_project' AND r.wait_type LIKE 'LCK_M_%'
                AND r.blocking_session_id>0 AND q.text LIKE '%INSERT dbo.EventoAplicado%';"""
        wait_for("duas transações Apply aguardando simultaneamente", lambda: sql(blocked) == "2")
        assert gate.poll() is None
        print("Antes: saldo R$ 0,00, zero movimentos e zero registros na inbox.")
        print(f"Evento: {event_id}")
        print("RabbitMQ: duas publicações roteadas e dois consumidores registrados.")
        print("SQL Server: duas transações Apply aguardando bloqueio ao mesmo tempo.")
    finally:
        if gate.poll() is None:
            gate.wait(timeout=45)

    for replica in replicas:
        expected = f"Entrega confirmada {event_id}"
        wait_for("ack em cada réplica", lambda: expected in execute(
            ["docker", "logs", replica]))
        log = execute(["docker", "logs", replica])
        assert log.count(expected) == 1
        print(f"ACK confirmado na réplica {replica[:12]}.")

    result = sql(f"SELECT Quantidade, CAST(Creditos AS decimal(19,2)), "
                 f"CAST(Debitos AS decimal(19,2)), CAST(Creditos-Debitos AS decimal(19,2)) {summary}; "
                 f"SELECT COUNT(*) {inbox};").splitlines()
    assert len(result) == 2 and result[1] == "1", result
    quantity, credits, debits, balance = map(Decimal, result[0].split("|"))
    assert (quantity, credits, debits, balance) == (1, Decimal("42.50"), 0, Decimal("42.50")), result
    print("Depois: saldo R$ 42,50, um movimento e um registro na inbox.")
    print("Aprovado: duas entregas confirmadas, um único efeito no saldo.")


if __name__ == "__main__":
    main()
