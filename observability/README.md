# Observability

Локальный observability stack состоит из Prometheus, Grafana, Loki и Grafana Alloy.

API можно запускать внутри Docker Compose вместе с observability services.

## Запуск

Из корня репозитория:

```powershell
docker compose up -d prometheus loki alloy grafana
```

Запусти API в Docker Compose:

```powershell
docker compose up -d api
```

Prometheus собирает метрики API внутри compose network с:

```text
http://api:8080/metrics
```

## Адреса

| Service | URL |
|---|---|
| API metrics | `http://localhost:5285/metrics` |
| Prometheus | `http://localhost:9091` |
| Grafana | `http://localhost:3000` |
| Loki | `http://localhost:3100` |
| Alloy | `http://localhost:12345` |

Grafana credentials для локальной разработки:

```text
login: admin
password: admin
```

## Grafana

Grafana provisioning создаёт:

- datasource `Prometheus`;
- datasource `Loki`;
- dashboard `PolymarketLab Collector`.

Dashboard находится в folder `PolymarketLab`.

## Метрики

API публикует Prometheus endpoint `/metrics` через OpenTelemetry.

Текущий meter:

```text
PolymarketLab.DataCollection.RawMessages
```

Session id не должен добавляться в metric labels. Используй session id только в structured logs.

## Логи

API пишет JSON logs в stdout. Alloy читает Docker logs через Docker socket и отправляет их в Loki. Для попадания API logs в Loki запускай API через `docker compose up -d api`.

## Быстрая проверка

Проверь Prometheus targets:

```text
http://localhost:9091/targets
```

Проверь Grafana datasource:

```text
http://localhost:3000/connections/datasources
```

Проверь Loki readiness:

```text
http://localhost:3100/ready
```

Проверь Alloy UI:

```text
http://localhost:12345
```
