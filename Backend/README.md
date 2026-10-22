# NoStrategia Backend: Cloud Backbone & Storage Pipeline

Цей каталог містить хмарний мікросервісний контур для збору, буферизації та збереження високочастотної телеметрії з симулятора *Assetto Corsa Competizione* (ACC) відповідно до архітектури курсової роботи (Тема №9).

---

## 1. Архітектура та компоненти

```
[ TelemetryAgent (.NET 8 Edge) ]
               │
               │ HTTP/2 POST (/api/v1/telemetry)
               ▼
┌────────────────────────────────────────────────────────────────────────┐
│ Ingestion API Gateway (Go Fiber) - Port 4000                           │
│ • Обробка одиночних та пакетних телеметричних кадрів                  │
│ • Валідація та нормалізація просторових/кінематичних полів            │
│ • Захист зворотного тиску (Circuit Breaker)                            │
└────────────────┬───────────────────────────────────────┬───────────────┘
                 │ XADD                                  │ XADD
                 ▼                                       ▼
┌─────────────────────────────────┐   ┌──────────────────────────────────┐
│ Redis Stream:                   │   │ Redis Stream:                    │
│ `stream:telemetry:raw`          │   │ `stream:setups`                  │
│ (MAXLEN ~ 10000 ring buffer)    │   │ (Retained configuration events)  │
└────────────────┬────────────────┘   └──────────────────┬───────────────┘
                 │                                       │
      ┌──────────┴──────────┐                            │
      ▼                     ▼                            ▼
 Consumer Group        Consumer Group              Consumer Group
 `cg:analytics`        `cg:gateway`                `cg:setup_worker`
 (Spatial Worker)      (60 FPS WebSocket)          (Git Diff Engine)
                 │
                 ▼
┌────────────────────────────────────────────────────────────────────────┐
│ TimescaleDB (PostgreSQL 16 + Hypertables) - Port 5433 / 5432           │
│ • `telemetry_samples` (Hypertable, 1-hour chunks, Gorilla compression) │
│ • `sessions`, `setup_revisions`, `laps`, `race_strategy_events`        │
│ • `micro_sectors` (30-50 зон Spa-Francorchamps)                        │
│ • `lap_telemetry_summary` (Continuous Aggregate View)                  │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Швидкий запуск через Docker Compose

### 2.1. Передумови
* Встановлений Docker Engine 24+ або OrbStack
* Встановлений Docker Compose v2+

### 2.2. Запуск сервісів
Перейдіть до каталогу бекенду:
```bash
cd Backend
cp .env.example .env
docker compose up -d --build
```

### 2.3. Перевірка статусу контейнерів
```bash
docker compose ps
```
Очікуваний стан:
* `nostrategia-timescaledb` — `healthy` (Port 5433 -> 5432)
* `nostrategia-redis` — `healthy` (Port 6379 -> 6379)
* `nostrategia-ingestion-api` — `running / healthy` (Port 4000 -> 4000)

---

## 3. Специфікація API Ingestion Gateway

Сервіс слухає порт `4000` (налаштовується через `PORT` у `.env`).

| Метод | Ендпоінт | Призначення | Тіло запиту / Відповідь |
|---|---|---|---|
| `GET` | `/health` | Комплексний чек готовності Redis та DB | `200 OK` `{"status":"healthy","redis":true,"timescaledb":true}` |
| `GET` | `/ready` | Легковаговий Liveness probe для Kubernetes/Compose | `200 OK` `"READY"` |
| `GET` | `/metrics` | Оперативні метрики Ingestion Gateway | `200 OK` (Кількість кадрів, затримка, стан Circuit Breaker) |
| `POST` | `/api/v1/telemetry` | Прийом одиночного телеметричного кадру | `202 Accepted` `{"status":"accepted","streamId":"..."}` |
| `POST` | `/api/v1/telemetry/stream` | Аліас для потокової телеметрії з агента | `202 Accepted` |
| `POST` | `/api/v1/telemetry/batch` | Пакетна вставка кадрів (до 1024 семплів) | `202 Accepted` `{"count":N,"firstId":"...","lastId":"..."}` |
| `POST` | `/api/v1/setups/sync` | Фіксація зміни файлу конфігурації авто | `200 OK` `{"status":"synced","hash":"..."}` |
| `POST` | `/api/v1/setups` | Аліас для синхронізації сетапів | `200 OK` |

### Приклад відправки кадру телеметрії (cURL):
```bash
curl -X POST http://127.0.0.1:4000/api/v1/telemetry \
  -H "Content-Type: application/json" \
  -H "X-Session-ID: 4a73b2c1-840e-436f-bca8-e9f3b259d640" \
  -d '{
    "packetId": 1050,
    "carModel": "mercedes_amg_gt3_evo",
    "track": "spa",
    "speedKmh": 248.5,
    "gear": 5,
    "rpm": 7300,
    "gas": 1.0,
    "brake": 0.0,
    "steerAngle": -0.015,
    "completedLaps": 4,
    "normalizedCarPosition": 0.354,
    "wheelsPressure": [27.6, 27.5, 27.8, 27.8]
  }'
```

---

## 4. Буферизація у Redis Streams

* **`stream:telemetry:raw`**:
  * Обмежується довжиною `MAXLEN ~ 10000` (кільцевий буфер ~100 секунд при 100 Гц).
  * Передстворені групи споживачів:
    * `cg:analytics`: для аналітичного сервісу просторової декомпозиції та Rule Engine.
    * `cg:gateway`: для шлюзу трансляції живого потоку клієнтам по WebSocket (60 FPS).
* **`stream:setups`**:
  * Події збереження налаштувань у гаражі гри (Exactly-Once семантика за SHA-256 хешем).
  * Група споживачів: `cg:setup_worker`.

---

## 5. Проектування TimescaleDB (DDL та оптимізації)

Схеми автоматично ініціалізуються при першому старті контейнера з каталогу `timescaledb/init/`:

1. **`01_schema.sql`**:
   * `sessions`: сесії заїздів (трек, авто, пілот, погода).
   * `setup_revisions`: Git-подібні ревізії налаштувань боліда з JSONB та diff-структурою.
   * `laps`: кола сесії, час секторів, валідність (cut-track).
   * `telemetry_samples`: часорядні зрізи фізики (тиск шин, температури гальм, G-force, кут керма).
   * `micro_sectors`: довідник 30-50 мікро-секторів траси (апекси, зони важкого гальмування).
   * `lap_micro_sector_times`: часи проходження та детекція аномалій пілотування.
   * `race_strategy_events`: тактичні директиви (Undercut, піт-вікно, розрахунок палива).
   * `driver_dna_profiles`: довгостроковий індекс стабільності та деградації шин пілота.

2. **`02_hypertables_and_compression.sql`**:
   * Перетворення `telemetry_samples` у гіпертаблицю з 1-годинними чанками.
   * Увімкнення компресії TimescaleDB (алгоритм Gorilla для чисел із плаваючою комою, Delta-of-delta для міток часу).
   * Політика автоматичного стиснення чанків старших за 2 години (економія диску **90–94%**).

3. **`03_continuous_aggregates.sql`**:
   * Матеріалізоване представлення `lap_telemetry_summary` з автоматичним оновленням кожні 10 секунд для миттєвого рендерингу графіків темпу в UI.
   * Матеріалізоване представлення `session_pace_summary` для моніторингу витрати палива.

4. **`04_seed.sql`**:
   * Еталонна сесія GT3 на трасі Спа-Франкоршам.
   * Розбиття легендарної траси Спа (7.004 км) на 25 ключових мікро-секторів (La Source, Eau Rouge, Raidillon, Kemmel, Les Combes, Bruxelles, Pouhon, Blanchimont, Bus Stop).

---

## 6. Автоматичне тестування та валідація

Для перевірки повного конвеєра запустіть скрипт верифікації:
```bash
./scripts/verify_pipeline.sh
```

Скрипт перевіряє:
1. Доступність ендпоінту `/health`.
2. Доставку одиночного кадру телеметрії.
3. Пакетну передачу телеметрії.
4. Синхронізацію сетапу боліда.
5. Наявність записів у черзі Redis Streams.
6. Ініціалізацію гіпертаблиць і мікро-секторів у TimescaleDB.
