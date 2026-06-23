# IoT Scenarios for Hercules Micro-Agents

Hercules is built as a tiny, self-improving micro-agent. One of its most compelling deployment targets is **Raspberry Pi at the edge**: a low-cost device that runs a single-purpose agent, connects to sensors and actuators, and participates in a larger agent mesh when needed. The LLM itself runs externally (YandexGPT, Ollama Cloud, or any OpenAI-compatible provider) — the Pi is the agent runtime, not the model host.

This document collects realistic **micro-scenarios** for B2C and B2B use. Each scenario is deliberately small, vertically focused, and deployable as one Hercules agent on one Pi.

---

## 1. Reference architecture: one agent on one Pi

```text
┌───────────────────────────────────────────────┐
│              Raspberry Pi (edge)              │
│  ┌────────────┐  ┌──────────┐  ┌───────────┐ │
│  │  Sensors   │  │ Actuators│  │  Hercules │ │
│  │  GPIO/I2C  │  │  GPIO    │  │  micro-   │ │
│  │  MQTT      │  │  relay   │  │  agent    │ │
│  └─────┬──────┘  └────┬─────┘  └─────┬─────┘ │
│        │              │               │       │
│        └──────────────┴───────────────┘       │
│                       │                       │
│              ┌────────▼────────┐              │
│              │  skills/ memory │              │
│              │  data/ (local)  │              │
│              └─────────────────┘              │
│                       │                       │
│              ┌────────▼────────┐              │
│              │   LLM provider  │              │
│              │  (cloud/fallback)│             │
│              └─────────────────┘              │
└───────────────────────────────────────────────┘
```

Key assumptions for this phase:

- The Pi hosts the **Hercules runtime**, skills, memory, and device interfaces.
- The LLM runs **outside** the Pi (cloud provider with API key).
- The agent has one clear responsibility per device.
- Multiple Pi agents can discover each other and form a mesh.

---

## 2. B2C scenarios

### 2.1 Smart greenhouse for home gardeners

**Goal:** keep one balcony greenhouse or small grow tent in ideal condition.

**Agent name:** `hercules-greenhouse-{id}`

**Devices on Pi:**
- DHT22/BME280 — temperature and humidity
- Soil moisture sensor
- Relay module — water pump and grow light
- Optional: light sensor

**Skills:**
- `water-plant` — trigger when soil moisture is below threshold and schedule allows.
- `control-climate` — turn on fan/heater/humidifier based on temp/humidity.
- `light-schedule` — switch grow light based on time of day and season.
- `alert-owner` — send Telegram message when anomaly persists.

**Memory:**
- Plant profile (species, preferred temp/humidity, watering schedule).
- Historical sensor min/max/averages.
- Owner preferences (quiet hours, vacation mode).

**Why micro:** one Pi, one greenhouse, one agent. The user asks via Telegram: *"How is my basil?"* — agent reads sensors, consults memory, and answers with context.

---

### 2.2 Home energy monitor

**Goal:** track electricity usage of one appliance or one room and give advice.

**Agent name:** `hercules-energy-{id}`

**Devices on Pi:**
- PZEM-004T or Shelly EM — power meter via TTL/Wi-Fi
- Optional: relay to cut standby power

**Skills:**
- `log-consumption` — read wattage every minute, store in SQLite.
- `detect-anomaly` — flag unusual spikes vs. historical baseline.
- `suggest-savings` — once a day summarize usage and suggest actions.
- `appliance-identify` — ask "what is my fridge costing me?" and compute from signatures.

**Memory:**
- Appliance signatures (on/off patterns, typical wattage).
- Daily/weekly/monthly totals.
- Tariff settings.

**Why micro:** cheap to deploy per room. Over time skills improve to distinguish appliances by power signature.

---

### 2.3 Baby room / elderly care monitor

**Goal:** watch comfort and safety of one room, not a whole house.

**Agent name:** `hercules-room-{id}`

**Devices on Pi:**
- DHT22 — temperature and humidity
- PIR — motion
- Microphone module (anomaly only, no cloud audio) — cry detection via local trigger
- Optional: air quality sensor

**Skills:**
- `comfort-check` — report if room is too hot/cold/humid.
- `presence-summary` — at end of day note unusual absence/presence patterns.
- `cry-alert` — local trigger sends alert to parent.
- `sleep-insight` — correlate temp/humidity with motion/cry logs over time.

**Memory:**
- Normal daily pattern.
- Alert history and false-positive feedback.

**Why micro:** privacy-first, one room, local audio triggers only. LLM is used for natural-language summaries, not for streaming audio.

---

### 2.4 Pet feeder and tracker

**Goal:** feed a pet on schedule and answer owner questions about feeding history.

**Agent name:** `hercules-pet-{id}`

**Devices on Pi:**
- Servo or relay-controlled feeder
- Load cell — measure remaining food
- Motion sensor near bowl

**Skills:**
- `feed-now` — dispense portion.
- `schedule-feed` — daily feeding plan.
- `low-food-alert` — notify when food is running low.
- `diet-qna` — answer "did the cat eat today?" from logs.

**Memory:**
- Pet profile (type, weight, dietary notes).
- Feeding log and owner feedback.

**Why micro:** one pet, one device, one agent. Skills evolve around this single pet's routine.

---

### 2.5 Workshop air quality guard

**Goal:** protect a single room (garage, workshop, 3D-print room) from harmful air.

**Agent name:** `hercules-air-{id}`

**Devices on Pi:**
- MH-Z19B / SCD40 — CO2
- PMS5003 — particulates
- SGP30 — VOCs
- Relay — exhaust fan

**Skills:**
- `ventilate-on-threshold` — turn fan on when any metric exceeds safe level.
- `air-report` — natural-language summary of current air quality.
- `job-safety-check` — before sanding/printing, check if ventilation is adequate.
- `maintenance-reminder` — suggest sensor calibration or filter change.

**Memory:**
- Baseline levels per season/time of day.
- Event log (fan cycles, spikes).

**Why micro:** targeted at one hazardous room, not whole-building HVAC.

---

## 3. B2B scenarios

### 3.1 Cold-chain spot checker (small shops, pharmacies, food trucks)

**Goal:** ensure one fridge or freezer stays in temperature range and logs compliance.

**Agent name:** `hercules-coldchain-{id}`

**Devices on Pi:**
- DS18B20 waterproof temperature probe
- Door sensor
- Optional: 4G USB modem for remote deployment

**Skills:**
- `temperature-log` — log every minute.
- `out-of-range-alert` — alert immediately if temp leaves safe band.
- `compliance-report` — generate daily PDF/CSV for inspector.
- `door-left-open` — alert if door is open too long.

**Memory:**
- Product profile (vaccines, dairy, frozen meat) with required temperature band.
- Incident history.

**Why micro:** one fridge = one agent = one invoice. Skills improve per product type across deployments.

---

### 3.2 Office desk / meeting room comfort agent

**Goal:** optimize one meeting room or open-space zone.

**Agent name:** `hercules-room-comfort-{id}`

**Devices on Pi:**
- BME680 — temperature, humidity, CO2, VOC
- PIR — occupancy
- Relay — control local fan/AC plug or blinds motor

**Skills:**
- `occupancy-boost` — pre-cool/ventilate when room is booked.
- `air-quality-alert` — warn when CO2 is high.
- `end-of-day-report` — summarize comfort and energy use.
- `book-room-qna` — answer "is Meeting Room 3 comfortable right now?"

**Memory:**
- Room schedule integration (calendar link).
- Historical comfort scores.

**Why micro:** one room, one agent, easy to scale floor-by-floor.

---

### 3.3 Small-server / network closet watchdog

**Goal:** monitor one rack, closet, or edge cabinet and react to environmental issues.

**Agent name:** `hercules-closet-{id}`

**Devices on Pi:**
- DS18B20 — intake and exhaust temperature
- DHT22 — humidity
- Relay — smart PDU outlet
- Network interface — ping local devices

**Skills:**
- `thermal-alert` — alert when exhaust is too hot.
- `device-ping-check` — verify router/switch/NAS are reachable.
- `graceful-shutdown` — turn off non-critical gear if temperature is critical.
- `incident-summary` — generate event timeline.

**Memory:**
- Device inventory and criticality.
- Thermal baseline and seasonal adjustments.

**Why micro:** one cabinet, one agent. IT can deploy dozens with identical base config and per-site overrides.

---

### 3.4 Vending machine / locker assistant

**Goal:** make one unattended machine conversational and proactive.

**Agent name:** `hercules-vending-{id}`

**Devices on Pi:**
- Touchscreen or connected tablet
- GPIO to machine controller / payment module
- Temperature sensor (for food machines)
- Camera (optional, for inventory visual check)

**Skills:**
- `answer-product-question` — "Is this snack vegan?" from product DB.
- `report-stock` — alert operator when inventory is low.
- `temperature-alert` — for refrigerated machines.
- `handle-complaint` — log issue and offer refund code.

**Memory:**
- Product catalog.
- Transaction and complaint history.
- Restock schedule.

**Why micro:** one machine, one agent, independent of central platform.

---

### 3.5 Construction / rental equipment telemetry

**Goal:** track usage and health of one generator, compressor, or scaffold set.

**Agent name:** `hercules-equipment-{id}`

**Devices on Pi:**
- Vibration sensor (SW-420 / ADXL345)
- Current clamp — runtime hours
- GPS module — location
- Relay — engine kill switch (authorized only)

**Skills:**
- `usage-log` — count engine hours.
- `geofence-alert` — alert if equipment leaves site.
- `maintenance-due` — predict next service from hours/vibration.
- `rental-summary` — daily usage report for rental office.

**Memory:**
- Equipment model and service intervals.
- Site boundaries.
- Rental contract dates.

**Why micro:** one asset, one agent, survives intermittent connectivity by buffering data locally.

---

## 4. Common design patterns across scenarios

| Pattern | How Hercules helps |
| ------- | ------------------ |
| **One agent = one physical thing** | Simple deployment, simple ownership, simple billing. |
| **Local skills, cloud LLM** | Pi handles logic; LLM gives natural language and reasoning. |
| **Skill improves per deployment** | Each device learns its own thresholds and owner preferences. |
| **Mesh when needed** | A "home hub" or "fleet manager" agent can ask a room agent for status. |
| **Offline resilience** | Sensors keep logging; alerts queue; skills do not depend on constant cloud. |

---

## 5. Hardware baseline

A minimal reference Pi setup for any scenario:

- Raspberry Pi 4 (2 GB) or Raspberry Pi Zero 2 W for lighter loads.
- SD card 32 GB+ (or SSD for heavy logging).
- Ethernet or Wi-Fi.
- Docker or self-contained `dotnet` executable.
- External LLM API key stored in `appsettings.json` or env var.

Hercules itself remains small: one process, one `data/` folder, one config file.

---

## 6. From scenario to product

For each scenario above, the path to a sellable product looks the same:

1. **Agent template** — pre-built skills, memory schema, and device wiring for the scenario.
2. **SD card image** — ready-to-flash Pi image with Hercules + template.
3. **Activation flow** — user sets Wi-Fi and API key, agent registers in mesh/cloud.
4. **Managed mesh** — optional central dashboard lists all deployed agents.

The same runtime serves B2C (one device at home) and B2B (hundreds of devices in field). The only difference is scale and how agents are provisioned.

---

## 7. Next steps

- Pick one scenario as the reference implementation.
- Define the GPIO/MQTT interface between Hercules and hardware.
- Build the first agent template (greenhouse is recommended as the friendliest starter).
- Validate memory and skill schema against real sensor data.

For the mesh vision, see [docs/AGENT-MESH-EN.md](AGENT-MESH-EN.md).  
For the phased roadmap, see [docs/ROADMAP-EN.md](ROADMAP-EN.md).
