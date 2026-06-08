# Steamoff

![Platform](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?style=for-the-badge&logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)
![Firewall](https://img.shields.io/badge/Firewall-Microsoft%20Defender-33D17A?style=for-the-badge)
![Privacy](https://img.shields.io/badge/Privacy-no%20telemetry-FF9F1A?style=for-the-badge)

Steamoff is a small Windows utility for quickly cutting Steam off from the
internet through Microsoft Defender Firewall rules.

It is built for people who like the useful parts of Steam Offline Mode, but want
a more explicit network switch: one click to block Steam, one click to let it
back online. No router changes, no unplugging Ethernet, no killing Steam, no
editing game files.

Steamoff can also block any extra apps or folders you add, so the same switch can
be used for games, launchers, tools, test builds, or anything else that should
temporarily stay offline.

> Русская версия ниже.

---

## Why This Exists

Common searches around this problem look like this:

> "Steam offline mode not working"
>
> "force Steam to stay offline"
>
> "block Steam internet access with Windows Firewall"
>
> "stop Steam from updating a game"
>
> "block a game from accessing the internet"
>
> "quickly disable internet for one app on Windows"

Steam's built-in Offline Mode is useful, but it is still a client feature. It can
depend on login state, cached credentials, update state, and what Steam decides
it needs to validate before launching.

Steamoff works at the Windows firewall layer instead. When offline mode is
enabled, Steam and selected apps simply do not get network access. That makes the
behavior more predictable when the goal is network isolation rather than social
"appear offline" status.

It is not a Steam patch, not a bypass tool, and not a DRM modification. It is a
local firewall-rule switch.

---

## What Steamoff Does

- **One clear offline switch**  
  Turn Steam network access off or on from a compact desktop window or tray menu.

- **PowerShell ScriptFile strategy first**  
  The primary rule engine generates and runs a local PowerShell firewall script
  with process-scoped execution policy bypass. This proved the most reliable
  path for applying and removing Microsoft Defender Firewall rules on Windows.

- **Blocks the real Steam process set**  
  Steamoff targets Steam itself and its helper processes, including Steam Client
  WebHelper and other discovered Steam executables.

- **Custom apps and folders**  
  Add individual `.exe` files or whole folders. Steamoff can scan folders and
  include executables inside them, so you can quickly isolate a game, launcher,
  mod tool, benchmark, or test app.

- **Inbound and outbound option**  
  By default Steamoff blocks outbound access. You can also enable inbound rule
  creation when you want stricter isolation.

- **Rule coverage display**  
  The main window shows how many expected firewall rules are currently active,
  so you can see whether blocking is complete, partial, or not configured.

- **Tray-first workflow**  
  Steamoff lives in the system tray. You can enable autostart and choose whether
  it starts minimized to tray after the first setup.

- **Startup is safe by design**  
  On first launch, the main window always opens so you can see what the app is
  and choose settings. Later launches can start quietly in the tray if enabled.

- **Short local log and diagnostics**  
  The main window includes a compact log with color-coded status lines. Settings
  include diagnostics for Steam path, firewall access, autostart, tracked files,
  and rule state.

- **Multilingual UI**  
  Steamoff includes localized UI resources for Russian, English, German, French,
  Spanish, Italian, Portuguese, Polish, and Chinese.

---

## Good Use Cases

Steamoff is useful when you want to:

- play Steam games without Steam talking to the network;
- keep Steam from downloading updates until you decide;
- test how an app behaves without internet;
- block a specific game launcher while keeping the rest of Windows online;
- temporarily isolate mod tools, benchmarks, or executables from a folder;
- switch Steam online/offline without opening Windows Defender Firewall manually;
- keep a repeatable firewall setup instead of recreating rules by hand.

It is especially handy for gamers, tinkerers, modders, QA testers, and anyone
who wants a simple "this app is offline now" button.

---

## Privacy And Safety

Steamoff is local-first:

- no telemetry;
- no analytics;
- no accounts;
- no cloud backend;
- no network calls from the app itself;
- no personal data collection;
- no Steam credential access;
- no modification of Steam files or game files.

Settings and logs are stored locally under `%ProgramData%\Steamoff` with an
`%AppData%\Steamoff` fallback if needed.

Steamoff only manages firewall rules it owns. The managed rules use the
`Steamoff` firewall group and deterministic Steamoff rule names, so cleanup is
scoped to its own rules.

Windows Defender Firewall changes require administrator rights. That is normal
for any app that creates or removes firewall rules.

Tested on Windows 10 and Windows 11.

---

## How It Works

Steamoff uses Microsoft Defender Firewall as the source of truth.

When you enable offline mode, Steamoff:

1. builds the list of Steam targets and your custom targets;
2. generates local firewall rules for those executables;
3. applies them through the PowerShell ScriptFile strategy;
4. reads back the actual firewall state;
5. shows rule coverage in the UI.

When you disable offline mode, Steamoff removes the rules it created earlier and
verifies the state again.

The result is intentionally boring in the best way: Windows decides network
access through its normal firewall mechanism, and Steamoff gives you a fast UI
for that switch.

---

## Download / Release Build

The release folder contains two variants:

```text
src/Steamoff.App/release/
  Steamoff-with-dotnet-runtime/      Steamoff.exe + README-RUN.txt
  Steamoff-without-dotnet-runtime/   Steamoff.exe + README-RUN.txt
  release-manifest.json
  release-log.txt
```

- **Steamoff-with-dotnet-runtime**: larger, self-contained, recommended for most
  users.
- **Steamoff-without-dotnet-runtime**: smaller, requires the matching .NET
  desktop runtime installed on Windows.

Run `Steamoff.exe` as administrator.

---

## Build From Source

```powershell
git clone https://github.com/blvckstn/Steamoff.git
cd Steamoff

dotnet restore
dotnet build Steamoff.slnx
dotnet test --filter "Category!=RequiresAdmin"

Set-ExecutionPolicy -Scope Process Bypass -Force
.\build-release.ps1
```

The release script restores, builds, tests, publishes both release variants, and
writes SHA-256 hashes to `release-manifest.json`.

---

## Project Layout

```text
src/Steamoff.Core/            models, interfaces, localization resources
src/Steamoff.Infrastructure/  firewall, PowerShell script writer, settings,
                              diagnostics, Steam discovery, autostart
src/Steamoff.App/             WPF UI, view models, tray, startup orchestration
tests/Steamoff.Tests/         xUnit tests and test doubles
specs/                        feature specs, contracts, quickstarts
```

---

# Steamoff ^{RU}

Steamoff — небольшая Windows-утилита для быстрого отключения Steam от интернета
через правила Microsoft Defender Firewall.

Она сделана для тех, кому нравятся практические плюсы автономного режима Steam,
но нужен более понятный и быстрый сетевой переключатель: один клик — Steam без
интернета, ещё один клик — Steam снова в сети.

При этом Steamoff умеет блокировать не только Steam. Можно добавить свои `.exe`
файлы или целые папки с программами, которым временно нужно запретить доступ в
интернет.

---

## Какую проблему решает Steamoff

Похожие запросы часто выглядят так:

> «Steam offline mode не работает»
>
> «как принудительно оставить Steam offline»
>
> «как заблокировать интернет Steam через firewall»
>
> «как запретить Steam обновлять игру»
>
> «как заблокировать игре доступ в интернет»
>
> «быстро отключить интернет только одному приложению Windows»

Встроенный автономный режим Steam полезен, но это всё ещё режим внутри клиента.
Он может зависеть от авторизации, кэша, состояния обновлений и того, что Steam
решит проверить перед запуском.

Steamoff работает ниже — на уровне Windows Firewall. Если автономный режим
включён, Steam и выбранные приложения просто не получают сетевой доступ. Поэтому
для задачи «не пускать приложение в интернет» поведение получается более
предсказуемым, чем надежда только на внутренний режим Steam.

Это не патч Steam, не обход DRM и не вмешательство в файлы игр. Это локальный
переключатель firewall-правил.

---

## Ключевые возможности

- **Две понятные кнопки: Вкл / Выкл**  
  Включить автономный режим или вернуть Steam в сеть.

- **Надёжный PowerShell ScriptFile-способ**  
  Steamoff применяет правила через локально сгенерированный PowerShell-сценарий.
  Для текущего проекта этот способ выбран основным, потому что на Windows он
  работает стабильнее остальных вариантов применения firewall-правил.

- **Блокировка Steam и его helper-процессов**  
  Учитываются Steam, Steam Client WebHelper, сервисы и вспомогательные
  исполняемые файлы Steam.

- **Свои приложения и папки**  
  Можно добавить отдельный `.exe` или папку. Это удобно для игр, лаунчеров,
  мод-инструментов, тестовых билдов и любых программ, которым нужно временно
  закрыть доступ в интернет.

- **Проверка результата**  
  Steamoff не просто «помнит», что создал правила. Он читает фактическое
  состояние Windows Firewall и показывает, сколько правил активно.

- **Трей и автозапуск**  
  Приложение может жить в трее, запускаться вместе с Windows и после первого
  запуска стартовать свёрнутым.

- **Первый запуск всегда с окном**  
  Чтобы пользователь понимал, что запущено и какие настройки включены.

- **Локальный журнал и диагностика**  
  В главном окне есть короткий журнал событий. В настройках — диагностика
  Steam-пути, firewall-доступа, автозапуска и правил.

- **9 языков интерфейса**  
  RU, EN, DE, FR, ES, IT, PT, PL, ZH.

---

## Для кого это

Steamoff может пригодиться, если вы хотите:

- играть в Steam-игры без сетевого доступа Steam;
- временно остановить обновления Steam/игры;
- проверить, как приложение ведёт себя без интернета;
- быстро заблокировать конкретную игру или launcher;
- оставить Windows онлайн, но отключить от сети только выбранные программы;
- не лазить каждый раз вручную в Windows Defender Firewall;
- иметь аккуратный повторяемый набор firewall-правил.

Это утилита для геймеров, моддеров, тестировщиков, гиков и всех, кто любит
простые инструменты с понятным поведением.

---

## Приватность и спокойствие

Steamoff работает локально:

- не выходит в интернет;
- не собирает личные данные;
- не отправляет телеметрию;
- не использует аккаунты;
- не обращается к облаку;
- не читает логин/пароль Steam;
- не меняет файлы Steam или игр.

Настройки и логи лежат локально в `%ProgramData%\Steamoff` или, если туда нет
доступа, в `%AppData%\Steamoff`.

Steamoff управляет только своими firewall-правилами. Они находятся в группе
`Steamoff`, поэтому приложение может отличить свои правила от чужих и не трогать
настройки, которые пользователь создал вручную.

Протестировано на Windows 10 и Windows 11.

---

## Сборка

```powershell
git clone https://github.com/blvckstn/Steamoff.git
cd Steamoff

dotnet restore
dotnet build Steamoff.slnx
dotnet test --filter "Category!=RequiresAdmin"

Set-ExecutionPolicy -Scope Process Bypass -Force
.\build-release.ps1
```

Готовые сборки появляются в `src/Steamoff.App/release/`.
