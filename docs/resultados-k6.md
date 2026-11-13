# Saídas dos testes de carga e estresse

[Voltar às evidências](evidencias.md) · [Voltar ao índice](index.md)

Trechos do resumo final do k6 de 13/11/2026. O resultado de cada cenário inclui a conclusão da execução e as contagens observadas.

## Aceite de carga

```text
  █ THRESHOLDS

    consultas_validas
    ✓ 'count>=14250' count=15001

    latencia_consultas_validas
    ✓ 'p(95)<=500' p(95)=2.46ms


  █ TOTAL RESULTS

    CUSTOM
    consultas_validas..............: 15001 50.005298/s
    lancamentos_confirmados........: 1501  5.00353/s
    latencia_consultas_validas.....: avg=1.84ms min=182.23µs med=1.76ms max=9.99ms  p(90)=2.25ms p(95)=2.46ms

    HTTP
    http_req_duration..............: avg=2.15ms min=182.23µs med=1.8ms  max=19.2ms  p(90)=3.03ms p(95)=4.96ms
      { expected_response:true }...: avg=2.15ms min=182.23µs med=1.8ms  max=19.2ms  p(90)=3.03ms p(95)=4.96ms
    http_req_failed................: 0.00% 0 out of 16502
    http_reqs......................: 16502 55.008827/s

    EXECUTION
    iteration_duration.............: avg=2.5ms  min=1.32ms   med=2.15ms max=25.87ms p(90)=3.57ms p(95)=5.18ms
    iterations.....................: 16502 55.008827/s
    vus............................: 0     min=0          max=0
    vus_max........................: 132   min=132        max=132

    NETWORK
    data_received..................: 32 MB 107 kB/s
    data_sent......................: 24 MB 81 kB/s




running (5m00.0s), 000/132 VUs, 16502 complete and 0 interrupted iterations
escrita ✓ [ 100% ] 00/12 VUs    5m0s  5.00 iters/s
leitura ✓ [ 100% ] 000/120 VUs  5m0s  50.00 iters/s
```

## Estresse exploratório

```text
  █ TOTAL RESULTS

    CUSTOM
    consultas_validas..............: 55249  85.002319/s
    latencia_consultas_validas.....: avg=2.16ms min=476.03µs med=2.01ms max=225.55ms p(90)=2.89ms p(95)=3.25ms

    HTTP
    http_req_duration..............: avg=2.16ms min=476.03µs med=2.01ms max=225.55ms p(90)=2.89ms p(95)=3.25ms
      { expected_response:true }...: avg=2.16ms min=476.03µs med=2.01ms max=225.55ms p(90)=2.89ms p(95)=3.25ms
    http_req_failed................: 0.00%  0 out of 55249
    http_reqs......................: 55249  85.002319/s

    EXECUTION
    iteration_duration.............: avg=2.58ms min=1.01ms   med=2.42ms max=245.84ms p(90)=3.48ms p(95)=3.92ms
    iterations.....................: 55249  85.002319/s
    vus............................: 0      min=0          max=2
    vus_max........................: 350    min=350        max=350

    NETWORK
    data_received..................: 115 MB 177 kB/s
    data_sent......................: 81 MB  124 kB/s




running (10m50.0s), 000/350 VUs, 55249 complete and 0 interrupted iterations
leitura ✓ [ 100% ] 000/350 VUs  10m50s  050.00 iters/s
```
