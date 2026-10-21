# NoStrategia - C# Edge Telemetry Ingestion Agent & Autonomous Mock Engine

> **Курсова робота:** «Хмарна мікросервісна платформа для розподіленої обробки та візуалізації телеметричних даних у реальному часі» (Тема №9)  
> **Компонент:** Граничний C# агент збору телеметрії з пам'яті (Edge Ingestion Agent), перехоплення JSON-налаштувань, адаптивний троттлінг та Faceit-подібний клієнт з підтримкою системного трею.

---

## 1. Архітектурний огляд та можливості

Агент спроектований за патерном граничних обчислень (**Edge Computing**) та оптимізований як для нативного виробничого середовища **Windows 10/11 x64**, так і для роботи в середині пляшок емуляції **Wine / CrossOver на macOS (Apple Silicon)** через шари **GPTK (Apple Game Porting Toolkit) та DXMT (DirectX-to-Metal)**.

### Ключові модулі:

1. **Unmanaged Shared Memory Reader (Win32 MMF & Wine IPC):**
   * Зчитує відображені у пам'ять файли Assetto Corsa Competizione:
     * `Local\acpmf_physics` (60–100 Гц): прискорення, G-сили, тиск і температура шин, кути уводу, хід підвіски.
     * `Local\acpmf_graphics` (15–30 Гц): стан гри (`AC_LIVE`, `AC_PAUSE`, `AC_REPLAY`), час кола, дельти секторів, прапорці піт-лейну (`IsInPitLane`), координати суперників.
     * `Local\acpmf_static`: константи автомобіля, довжина сплайну траси, ліміти оборотів.
   * Забезпечує неблокуюче читання без накладних витрат GC та безпечний fallback за відсутності запущеного процесу гри.

2. **Перехоплення налаштувань автодрому та боліда (Setup Interceptor):**
   * `FileSystemWatcher` асинхронно відстежує директорію `Documents/Assetto Corsa Competizione/Setups/<carModel>/<track>/<setup>.json`.
   * **SHA-256 Deduplication:** усуває подвійні/потрійні виклики Windows API при записі файлу грою, гарантуючи семантику **Exactly-Once**.
   * Автоматичне виділення моделі авто, треку та структури JSON сетапу (тиски, аеродинаміка, баланс стабілізаторів ARB, електроніка) та синхронізація на `/api/v1/setups/sync`.

3. **Скінченний автомат адаптивного троттлінгу (State-Driven Throttling):**
   * `AC_LIVE` на бойовому колі: **60 Гц / 100 Гц** (повночастотна передача).
   * `AC_LIVE` на піт-лейні (`IsInPitLane == 1`): динамічний даунсемплінг до **10 Гц** (скорочення надлишкових пакетів на 83.3%).
   * `AC_PAUSE` / `AC_REPLAY` / Меню: троттлінг до **0.5 Гц** (Heartbeat 1 раз на 2 секунди).
   * Сумарне скорочення надлишкового мережевого та процесорного навантаження сягає **до 68%**.

4. **Автономний генератор телеметрії та відтворювач дампів (Mock Replay Engine):**
   * Повноцінна імітація Mercedes-AMG GT3 Evo на трасі Circuit de Spa-Francorchamps (7004 м) без необхідності встановлення гри.
   * **Генератор синтетичних аномалій (Synthetic Anomaly Injector):**
     * `Wheel Lockup`: блокування переднього лівого колеса ($\omega_{FL} \to 0$), стрибок гальмівного зусилля до 1.0 та від'ємний сплеск slip ratio (перевірка Layer 1 Lockup Alert).
     * `Oversteer Spin`: аномальне відхилення швидкості рискання (+0.45 рад/с) від кінематичної моделі велосипеда (перевірка детектора зриву заносу).
     * `Fuel Emergency`: штучне падіння палива до критичних 1.15 л (перевірка розрахунку паливної стратегії).
     * `Pit Lane Entry`: тригер входу на піт-лейн для перевірки перемикання троттлінгу.
   * **NDJSON Recorder & Replayer:** збереження реальних та тестових заїздів у форматі Newline-Delimited JSON та їх повторне відтворення з точним темпом.

5. **Faceit Anti-Cheat Style Desktop UI (Avalonia UI):**
   * Темна естетика кіберспортивного клієнта (#0D0E12, акценти Faceit Orange #FF5500 та Neon Green #00E676).
   * **3 головні інформаційні колони (Pillars):**
     1. **АГЕНТ (Agent Engine):** поточна частота (Hz), режим троттлінгу, відсоток збереження трафіку.
     2. **ГРА (Game Link):** статус виявлення ACC в пам'яті (Connected / Searching / Mock Mode), модель боліда та траса.
     3. **СЕРВЕР (Ingestion Gateway):** статус зв'язку з бекендом (127.0.0.1:4000), затримка (Ping ms), лічильники відправлених та відкинутих пакетів.
     4. **СЕТАПИ (Setup Interceptor):** моніторинг папки налаштувань, хеш SHA-256 останнього сетапу.
   * **Live Mini-HUD:** спідометр, поточна передача, динамічна шкала оборотів (RPM), рівні педалей газу/гальма, час кола, сектор, тиск шин PSI.
   * **Панель автономних тестів:** ін'єкція аномалій в один клік, генерація тестових сетапів, перемикач джерела даних (Live / Mock).
   * **Згортання в трей (System Tray):**
     * Програма вміє згортатися в системний трей (macOS menu bar / Windows notification area) при кліку на кнопку "Tray" або закритті вікна.
     * Контекстне меню трею ("Open Dashboard", "Exit Agent").

---

## 2. Структура проекту

```
TelemetryAgent/
├── TelemetryAgent.slnx
├── README.md
├── sample_data/
│   ├── spa_gt3_benchmark.ndjson       <- Записаний еталонний заїзд Spa GT3 (1200 кадрів, NDJSON)
│   └── amg_gt3_evo_spa_baseline.json  <- Еталонний JSON сетап Mercedes-AMG GT3 Evo
├── src/
│   ├── TelemetryAgent.Core/           <- Базова бібліотека логіки агента (net8.0 / net9.0)
│   │   ├── Models/
│   │   │   ├── Memory/                <- Некеровані структури пам'яті ACC (Physics, Graphics, Static)
│   │   │   ├── TelemetryPayload.cs    <- Серіалізований пакет для бекенду
│   │   │   └── SetupPayload.cs        <- Пакет сетапу з SHA-256 хешем
│   │   ├── SharedMemory/              <- Win32/Wine MMF Reader з SafeHandle
│   │   ├── Throttling/                <- Скінченний автомат адаптивного регулювання потоку
│   │   ├── Setups/                    <- FileSystemWatcher + SHA-256 дедуплікація
│   │   ├── Mock/                      <- Фізичний емулятор GT3 Spa, Injector, NDJSON Replay
│   │   ├── Buffer/                    <- Lock-Free Ring Buffer (переповнення витісняє старі кадри)
│   │   ├── Network/                   <- Ingestion HTTP/2 клієнт з Ping та Circuit Breaker
│   │   └── Services/                  <- Головний координатор TelemetryAgentCoordinator
│   │
│   └── TelemetryAgent.App/            <- Faceit-style Desktop UI на Avalonia UI
│       ├── MainWindow.axaml           <- Темний UI з 4 статусами, Mini-HUD та панеллю тестів
│       ├── ViewModels/                <- MainWindowViewModel з реактивними оновленнями UI
│       └── App.axaml                  <- Інтеграція System TrayIcon та життєвого циклу
│
└── tests/
    └── TelemetryAgent.Tests/          <- Автономний xUnit тестовий набір (17 тестів)
        ├── MockTelemetryTests.cs
        ├── AnomalyInjectionTests.cs
        ├── AdaptiveThrottlingTests.cs
        ├── SetupWatcherTests.cs
        ├── TraceRecorderAndReplayTests.cs
        └── RingBufferTests.cs
```

---

## 3. Інструкція із запуску та тестування

### 3.1. Запуск автономних модульних та інтеграційних тестів:

```bash
cd NoStrategia/TelemetryAgent
dotnet test
```
*Усі 17 тестів виконуються повністю автономно без зовнішніх залежностей та запущеної гри.*

### 3.2. Запуск Faceit Anti-Cheat клієнта з графічним інтерфейсом:

```bash
cd NoStrategia/TelemetryAgent
dotnet run --project src/TelemetryAgent.App
```
*За замовчуванням запускається у режимі автономного GT3 емулятора зі швидкістю 60 FPS, живим Mini-HUD, лічильниками трафіку та можливістю ін'єкції аномалій.*

### 3.3. Запуск у консольному (Headless / CLI) режимі (для Docker / CI / сервера):

```bash
# Режим автономного мок-генератора в терміналі
dotnet run --project src/TelemetryAgent.App -- --mock

# Режим зчитування пам'яті живої гри в терміналі
dotnet run --project src/TelemetryAgent.App -- --headless
```

---

## 4. Перевірка функціоналу Faceit-стилю

1. **Відображення трьох основних статусів:**
   * **Agent Status:** горить зеленим ("ACTIVE (FULL RATE)") або помаранчевим ("THROTTLED (PIT LANE)"), показуючи реальні герци (наприклад, `60.0 Hz`) та відсоток збереження смуги пропускання.
   * **Game Status:** виявляє запущену гру ACC через Shared Memory або показує "MOCK SIMULATOR" з активним болідом та трасою.
   * **Server Status:** перевіряє доступність бекенду (127.0.0.1:4000), вимірює Ping та рахує кількість успішно відправлених пакетів.
   * **Setup Interceptor:** повідомляє про готовність відстежувати зміни сетапів та відображає SHA-256 хеш останнього збереженого файлу.
2. **Згортання в трей:**
   * Натисніть кнопку **"🗕 Tray"** або закрийте вікно хрестиком `[X]`: вікно сховається в системний трей (menu bar на Mac / трей на Windows).
   * Клік по іконці в треї або пункт **"Open Dashboard"** повертає вікно в активний стан.
3. **Автономне тестування без гри:**
   * Натисніть **"⚠ Wheel Lockup"** — на Mini-HUD шкала гальма підскочить до 100%, швидкість переднього лівого колеса впаде до нуля, а аналітичний воркер отримає кадр із заблокованим колесом.
   * Натисніть **"⟳ Oversteer Spin"** — спровокує занос із кутом уводу понад 14°.
   * Натисніть **"⚙ Test Setup Sync"** — згенерує тестовий JSON сетап боліда та перевірить роботу SHA-256 дедуплікатора.
   * Натисніть **"⏺ Record NDJSON"** — запише живий телеметричний трейс у файл на диску для повторного тестування.
