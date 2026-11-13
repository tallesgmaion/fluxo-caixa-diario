import http from 'k6/http';
import exec from 'k6/execution';
import { Counter, Trend } from 'k6/metrics';

const mode = __ENV.MODO || 'aceite';
const success = new Counter('consultas_validas');
const latency = new Trend('latencia_consultas_validas', true);
const errors = new Counter('consultas_invalidas');
const writes = new Counter('lancamentos_confirmados');
const acceptance = mode === 'aceite';
const scenarios = {
  leitura: acceptance
    ? { executor: 'constant-arrival-rate', rate: 50, timeUnit: '1s', duration: '5m', preAllocatedVUs: 120 }
    : mode === 'estresse'
      ? { executor: 'ramping-arrival-rate', startRate: 50, timeUnit: '1s', preAllocatedVUs: 350,
          stages: [50, 75, 100, 150, 50].flatMap((target) => [{ target, duration: '10s' }, { target, duration: '2m' }]) }
      : { executor: 'constant-arrival-rate', rate: 2, timeUnit: '1s', duration: '10s', preAllocatedVUs: 4 },
};
if (acceptance) {
  scenarios.escrita = { executor: 'constant-arrival-rate', exec: 'write', rate: 5, timeUnit: '1s',
    duration: '5m', preAllocatedVUs: 12 };
}

export const options = {
  scenarios,
  thresholds: acceptance
    ? { consultas_validas: ['count>=14250'], latencia_consultas_validas: ['p(95)<=500'] }
    : mode === 'rapido' ? { consultas_validas: ['count==20'] } : {},
};

const accounts = JSON.parse(open('/config/carga.json'));

function headers(account) {
  return { Authorization: 'Bearer ' + account.token, 'Content-Type': 'application/json' };
}

export default function () {
  const index = exec.scenario.iterationInTest;
  const account = accounts.users[index % accounts.users.length];
  const days = [1, 7, 31][index % 3];
  const r = http.get('http://consolidado:8080/v1/contas/' + account.id +
    '/consolidado-diario?inicio=' + accounts.starts[days] + '&fim=' + accounts.today,
    { headers: headers(account), timeout: '2s', redirects: 0 });
  let valid = false;
  if (r.status === 200) {
    try {
      const payload = r.json();
      valid = payload.contaId === account.id && payload.moeda === 'BRL' &&
        payload.dias.length === days && payload.dias[0].dataCaixa === accounts.starts[days] &&
        payload.dias[days - 1].dataCaixa === accounts.today &&
        payload.dias.every((d) => /^-?\d+\.\d{2}$/.test(d.movimentoLiquido) &&
          /^\d+\.\d{2}$/.test(d.creditos) && /^\d+\.\d{2}$/.test(d.debitos) &&
          (d.atualizadoEmUtc === null || typeof d.atualizadoEmUtc === 'string')) &&
        (days === 1 || payload.dias[days - 2].movimentoLiquido === '1.00');
    } catch (_) { valid = false; }
  }
  if (valid) {
    success.add(1);
    latency.add(r.timings.duration);
  } else {
    errors.add(1, { reason: r.status === 200 ? 'contrato' : String(r.status) });
    success.add(0);
  }
}

export function write() {
  const index = exec.scenario.iterationInTest;
  const account = accounts.users[index % accounts.users.length];
  const key = 'ffffffff-0000-4000-8000-' + String(accounts.seed + index).padStart(12, '0');
  const r = http.post('http://lancamentos:8080/v1/contas/' + account.id + '/lancamentos',
    JSON.stringify({ dataCaixa: accounts.today, tipo: 'credito', moeda: 'BRL', valor: '1.00' }),
    { headers: { ...headers(account), 'Idempotency-Key': key }, timeout: '2s', redirects: 0 });
  writes.add(r.status === 201 ? 1 : 0);
}
