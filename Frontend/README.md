# NoStrategia Frontend & 60 FPS WebSocket Stream Gateway

> **Курсова робота (Тема №9):** Високопродуктивна платформа аналітики та телеметрії для симулятора *Assetto Corsa Competizione* (ACC).  
> **Компонент:** Клієнтська візуалізація, Uncontrolled Canvas 2D рушій (60 FPS), WebSocket-шлюз та інтегрований голосовий інженер (Web Speech API).

---

## 1. Архітектура та подолання React Reconciliation Loop

При передачі високочастотної телеметрії з симулятора (60–100 Гц) класичний підхід React через хук `useState` призводить до Garbage Collection Thrashing (GC зупинки 20–60 мс) та падіння частоти кадрів нижче 25 FPS.

```
Класичний підхід React (Втрата кадрів та GC Thrashing):
WebSocket (60-100 Hz) ──► setState() ──► VDOM Diffing ──► GC Freeze (40ms) ──► Втрата кадрів (25 FPS)

Архітектура Uncontrolled Canvas NoStrategia (Zero-GC 60 FPS):
WebSocket (60-100 Hz) ──► Float32Array (useRef) ──► requestAnimationFrame() ──► Canvas 2D (Стабільні 60 FPS)
```

### Ключові інженерні рішення:
1. **Мутабельні циклічні буфери (Zero-Allocation Ring Buffers):**  
   Сирі високочастотні дані (газ, гальма, швидкість, кут керма, оберти) записуються безпосередньо у фіксовані типізовані масиви `Float32Array` всередині `useRef` без виділення динамічної пам'яті в купі (Heap).
2. **Незалежний Render Loop (60 FPS Canvas):**  
   Окремий цикл `requestAnimationFrame()` вичитує останні точки з кільцевого буфера та малює графіки у бітовий растр HTML5 Canvas без участі React Reconciliation.
3. **Двосторонній WebSocket шлюз (`/ws/stream`):**  
   Шлюз на базі `ws` підтримує мультиплексування каналів (`telemetry:fast`, `strategy:tactics`), автоматичне підключення до Redis Streams (`stream:telemetry:raw`) та вбудований симулятор телеметрії траси Spa-Francorchamps для тестування та академічного захисту.
4. **Аудіо-інженер на базі Web Speech API:**  
   Голосовий супровід гоночного інженера з 4-рівневою пріоритетною чергою (P1 Critical, P2 Strategy, P3 Telemetry, P4 Status), дедуплікацією фраз та імітацією радіо-переговорів.

---

## 2. Структура каталогу `Frontend`

```
Frontend/
├── server/
│   ├── gateway.ts             # 60 FPS WebSocket шлюз та HTTP /health ендпоінт
│   ├── redis-consumer.ts      # Споживач Redis Streams (cg:gateway)
│   └── simulator.ts           # Фізичний симулятор ACC (Spa-Francorchamps GT3)
├── src/
│   ├── app/
│   │   ├── globals.css        # Стилі Tailwind CSS та темна тема
│   │   ├── layout.tsx         # Кореневий макет Next.js
│   │   └── page.tsx           # Головний екран телеметричного дашборду
│   ├── components/
│   │   ├── audio/
│   │   │   └── AudioEngineerWidget.tsx # Панель голосового інженера та тестування
│   │   └── telemetry/
│   │       ├── TelemetryCanvas.tsx     # Uncontrolled Canvas 60 FPS графіки
│   │       └── TelemetryGauges.tsx     # Цифрові показники приладів (HUD)
│   ├── hooks/
│   │   └── useTelemetryWebSocket.ts    # React хук підключення та кільцевих буферів
│   ├── services/
│   │   └── AudioRaceEngineer.ts        # Підсистема синтезу мовлення (TTS Queue)
│   └── types/
│       └── telemetry.ts                # TypeScript інтерфейси та протокол
├── test/
│   ├── audio-and-buffer.test.mjs       # Тести кільцевих буферів та пріоритетної черги
│   └── gateway.test.mjs               # E2E тести WebSocket шлюзу та /health API
├── package.json
└── tsconfig.json
```

---

## 3. Швидкий запуск

### 3.1. Встановлення залежностей
```bash
cd Frontend
npm install
```

### 3.2. Запуск у режимі розробки
Для одночасного запуску WebSocket-шлюзу (порт 4001) та Next.js додатку (порт 3000):
```bash
npm run dev:all
```

Або окремо у двох терміналах:
```bash
# Термінал 1: Запуск WebSocket Gateway
npm run gateway

# Термінал 2: Запуск Next.js Dashboard
npm run dev
```

Після запуску відкрийте браузер за адресою:  
**http://localhost:3000**

WebSocket-шлюз слухає:  
**ws://localhost:4001/ws/stream**  
Health Check: **http://localhost:4001/health**

---

## 4. Пріоритетна черга аудіоподій (Audio Race Engineer)

| Рівень | Тип події | Поведінка черги | Приклад фрази |
|---|---|---|---|
| **P1** | **CRITICAL** | **Негайно перериває** поточну фразу (`cancel()`), очищує другорядні повідомлення | *"Lockup front right into turn one! Modulate braking."* |
| **P2** | **STRATEGY** | Вставляється на **початок** черги, перед телеметрією | *"Box window opens in two laps. Target fuel 2.8 liters."* |
| **P3** | **TELEMETRY**| Стандартна черга повідомлень з дедуплікацією 40 сек | *"Front tyre scrub detected in Pouhon. Ease steering."* |
| **P4** | **STATUS**   | Скидається при наявності черги або затримці | *"Sector one personal best. Delta minus 0.18s."* |

---

## 5. Автоматизоване тестування

Запуск всього пакету модульних та інтеграційних тестів:
```bash
npm test
```

Перевіряється:
* Робота кільцевих типізованих буферів `Float32Array` без переповнення та релокацій пам'яті.
* Пріоритезація та витіснення P1/P2/P3/P4 у черзі голосового інженера.
* Підключення клієнтів до WebSocket-шлюзу, рукостискання підписки (`SUBSCRIPTION_ACK`).
* Передача телеметричних пакетів на частоті 60 FPS та валідація їх структури.
* Трансляція та обробка тактичних подій (`TACTICAL_EVENT`).
