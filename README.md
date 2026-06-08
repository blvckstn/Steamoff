# Steamoff
<img width="1916" height="821" alt="Image" src="https://github.com/user-attachments/assets/1a5f2a51-c292-4f44-a07c-e5ed9527d0da" />

![Platform](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?style=for-the-badge&logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet)
![Firewall](https://img.shields.io/badge/Firewall-Microsoft%20Defender-33D17A?style=for-the-badge)
![Privacy](https://img.shields.io/badge/Privacy-no%20telemetry-FF9F1A?style=for-the-badge)


Steamoff is a small Windows utility that gives Steam a real network off switch.

Not "appear offline". Not "maybe Steam will start in Offline Mode today". A clear firewall-level toggle: block Steam from the internet, keep Windows online, and turn everything back on when you are ready.

It is made for players, modders, testers, and PC people who want the useful part of Steam Offline Mode without fighting Steam's mood, login cache, pending updates, or a half-broken hotel Wi-Fi connection.

> **[Русская версия](#steamoff-ru)** ниже / below

---

## The Problem Steamoff Solves

> "I only wanted to launch a single-player game, but Steam started a 40 GB update."
>
> "Steam Offline Mode says I need to go online first."
>
> "I want Discord and the browser online, but Steam completely offline."
>
> "How do I block one game from accessing the internet without unplugging the PC?"
>
> "I am testing a build and need to know what happens when it has no network."

Sound familiar? That is exactly the little everyday PC annoyance Steamoff is built around.

Steam Offline Mode is useful, but it is still a Steam client state. It can depend on cached credentials, update status, recent login state, and whatever the client wants to check before it agrees to behave.

Steamoff takes the more boring and more reliable route: Microsoft Defender Firewall. When offline mode is enabled, Steam and the apps you choose simply cannot reach the network. For the specific job of cutting internet access, firewall rules are more deterministic than asking Steam nicely.

No Steam patching. No DRM tricks. No game-file edits. No router changes. Just local Windows firewall rules with a friendly button on top.

Steamoff is not trying to replace Steam or be a do-everything tool. It solves one specific moment: you want Steam off the network *right now*, on your terms, without losing the rest of your internet connection or guessing whether the client actually listened to you. Click it on, do whatever you needed quiet for, click it off — done.

---

## How Steamoff Works

Steamoff uses Microsoft Defender Firewall as the source of truth.

When you click **Вкл / Enable**, it:

1. finds Steam and its helper executables;
2. adds your custom app and folder targets;
3. generates a local PowerShell firewall script;
4. runs it with process-scoped execution policy bypass;
5. reads the actual firewall state back;
6. shows how many expected rules are active.

When you click **Выкл / Disable**, it removes the rules it created earlier and checks the result again.

The PowerShell ScriptFile path is the primary strategy because it proved to be the most stable way to apply Microsoft Defender Firewall rules on Windows 10 and Windows 11:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
```

That policy change is only for the current PowerShell process. It does not permanently loosen your machine's execution policy.

---

## Features

- One-screen Steam online/offline switch with clear **Вкл** and **Выкл** buttons
- Blocks Steam, Steam Client WebHelper, and discovered Steam helper executables
- Add your own `.exe` files
- Add folders and let Steamoff include executable files inside them
- Optional inbound rules for stricter isolation
- Shows rule coverage, for example `25 из 25`
- Color-coded local journal for quick diagnostics
- System tray icon with current status
- Autostart option
- Start minimized to tray after first setup
- First launch always opens the main window so nothing happens invisibly
- 9 UI languages: RU, EN, DE, FR, ES, IT, PT, PL, ZH
- Tested on Windows 10 and Windows 11

---

## Real Use Cases

**Single-player night, no surprise update**

You sit down to play, Steam decides the game needs an update, and your evening turns into a progress bar. With Steamoff you can keep Steam offline, launch what already exists locally, and update later when you actually want to.

**You're on a slow or metered connection**

Train Wi-Fi, a hotel network, a mobile hotspot with a data cap — Steam doesn't know or care, and it will happily try to sync, update, and phone home in the background anyway. Turn Steam off, keep your browser, Discord, and downloads working normally, and turn Steam back on once you're somewhere with real bandwidth.

**You're testing something and don't want it touching the internet**

A modded build, a cracked or pirated launcher you don't fully trust, a benchmark, a tool you downloaded from somewhere sketchy — point Steamoff at the `.exe` (or the whole folder), flip the switch, run it offline, see what it actually does, then turn the rule off when you're done.

**You need one specific program offline — not your whole PC**

Unplugging the router or turning on Airplane Mode is overkill when the actual problem is "this one app keeps connecting and I don't want it to right now." Steamoff lets you target exactly that program (Steam, a game, a launcher, anything with an `.exe`) while everything else on the machine keeps working normally.

---

## Privacy And Safety

Steamoff is local-first and quiet:

- no telemetry;
- no analytics;
- no accounts;
- no cloud backend;
- no network calls from the app itself;
- no personal data collection;
- no Steam login or password access;
- no modification of Steam files;
- no modification of game files.

Settings and logs are stored locally in `%ProgramData%\Steamoff`, with `%AppData%\Steamoff` used as a fallback when needed.

Steamoff only manages its own firewall rules. They are grouped under `Steamoff`, so cleanup is scoped to the rules this app created.

Administrator rights are required because Windows firewall rules require administrator rights. That is normal for this kind of tool.

---

## Requirements

- Windows 10 / 11
- Administrator rights to apply or remove firewall rules
- .NET 8 SDK only if you build from source

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

Release files are created in:

```text
src/Steamoff.App/release/
  Steamoff-with-dotnet-runtime/
  Steamoff-without-dotnet-runtime/
  release-manifest.json
```

Use the runtime-included build if you just want the easiest launch. Use the smaller build if the matching .NET desktop runtime is already installed.

---

## Troubleshooting

**Steam is still online**

Run Steamoff as administrator, click **Вкл**, and check the rule counter. If the counter is incomplete, open the log and settings diagnostics. Steamoff reports which targets failed and which rule path was used.

**A game will not launch offline**

Some games genuinely require online validation, a third-party launcher, or a first online start. Steamoff blocks network access; it does not bypass game requirements.

**Antivirus warns about firewall changes**

Steamoff creates and removes Microsoft Defender Firewall rules. Security tools may notice that because it is exactly what the app is meant to do.

**I want another app blocked too**

Add the app's `.exe` or its folder in settings. The same offline switch can include Steam, launchers, tools, and selected games.

---

# Steamoff <sup>RU</sup>

Steamoff — маленькая Windows-утилита, которая даёт Steam настоящий сетевой выключатель.

Не статус "невидимка". Не надежда, что встроенный автономный режим Steam сегодня заведётся без капризов. А понятный переключатель: закрыть Steam доступ в интернет через firewall, оставить Windows онлайн и вернуть всё обратно одним кликом.

Утилита сделана для игроков, моддеров, тестировщиков и всех, кто любит простые инструменты с честным поведением.

---

## Проблема, которую решает Steamoff

> "Я хотел просто запустить одиночную игру, а Steam начал качать обновление на 40 ГБ."
>
> "Steam Offline Mode пишет, что сначала надо зайти онлайн."
>
> "Мне нужен браузер и Discord онлайн, но Steam должен молчать."
>
> "Как запретить интернет одной игре, не отключая весь компьютер?"
>
> "Я тестирую билд и хочу понять, как он ведёт себя без сети."

Это очень узнаваемая боль. Вроде бы мелочь, но именно такие мелочи портят вечер, тест, поездку или нормальный рабочий ритм.

Встроенный автономный режим Steam полезен, но он остаётся режимом внутри клиента. Он может зависеть от авторизации, кэша, состояния обновлений и того, что Steam решил проверить перед запуском.

Steamoff работает ниже — на уровне Microsoft Defender Firewall. Если автономный режим включён, Steam и выбранные приложения просто не получают сетевой доступ. Для задачи "не пускать программу в интернет" это надёжнее, чем просить Steam вести себя офлайн.

Никаких правок Steam. Никаких обходов DRM. Никаких изменений файлов игр. Никаких танцев с роутером. Только локальные правила Windows Firewall и нормальная кнопка сверху.

Steamoff не пытается заменить Steam и не лезет в остальные ваши дела. Он решает один конкретный момент: вам нужно, чтобы Steam прямо сейчас замолчал в сети — без потери остального интернета и без гадания, послушался клиент или нет. Включили, сделали то, ради чего хотели тишины, выключили — и всё.

---

## Как это работает

Steamoff использует Microsoft Defender Firewall.

Когда вы нажимаете **Вкл**, приложение:

1. находит Steam и его helper-процессы;
2. добавляет ваши приложения и папки из настроек;
3. генерирует локальный PowerShell-скрипт для firewall;
4. запускает его с временным обходом политики выполнения;
5. перечитывает фактическое состояние правил;
6. показывает, сколько правил активно, например `25 из 25`.

Когда вы нажимаете **Выкл**, Steamoff удаляет созданные им правила и снова проверяет состояние.

Основной способ применения правил — PowerShell ScriptFile, потому что на Windows 10 и Windows 11 он оказался самым стабильным:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
```

Это применяется только к текущему процессу PowerShell и не меняет политику выполнения системы навсегда.

---

## Возможности

- Две понятные кнопки: **Вкл** и **Выкл**
- Блокировка Steam, Steam Client WebHelper и найденных helper-файлов Steam
- Добавление своих `.exe`
- Добавление папок с автоматическим поиском исполняемых файлов
- Опциональные inbound-правила для более строгой изоляции
- Счётчик активных правил, например `25 из 25`
- Цветной локальный журнал для быстрой диагностики
- Иконка в трее с актуальным статусом
- Автозапуск вместе с Windows
- Запуск свёрнутым в трей после первой настройки
- Первый запуск всегда открывает окно, чтобы приложение не работало "втихую"
- 9 языков интерфейса: RU, EN, DE, FR, ES, IT, PT, PL, ZH
- Проверено на Windows 10 и Windows 11

---

## Реальные сценарии

**Одиночная игра без внезапного обновления**

Вы садитесь играть, а Steam внезапно решает обновиться. Steamoff позволяет заранее закрыть Steam интернет, запустить то, что уже есть локально, а обновления оставить на потом.

**Медленный или лимитированный интернет**

Поезд, гостиница, мобильный хотспот с пакетом трафика — Steam об этом не знает и не думает, и спокойно качает обновления и стучится в сеть в фоне. Выключаете Steam — браузер, Discord и загрузки работают как обычно, а Steam включаете обратно, когда окажетесь там, где интернет нормальный.

**Хотите проверить что-то, не пуская это в сеть**

Модифицированная сборка, лаунчер сомнительного происхождения, бенчмарк, программа из непонятного источника — указываете на `.exe` (или сразу на папку), включаете блокировку, смотрите, что оно делает без интернета, потом выключаете правило, когда всё проверили.

**Нужно заблокировать одну конкретную программу, а не весь ПК**

Выдёргивать роутер или включать авиарежим — перебор, если на самом деле проблема в одной программе, которая лезет в сеть, когда не надо. Steamoff нацеливается именно на неё (Steam, игру, лаунчер, любой `.exe`), а всё остальное на компьютере продолжает работать как обычно.

---

## Приватность и спокойствие

Steamoff работает локально:

- не выходит в интернет;
- не собирает личные данные;
- не отправляет телеметрию;
- не использует аккаунты;
- не обращается к облаку;
- не читает логин или пароль Steam;
- не меняет файлы Steam;
- не меняет файлы игр.

Настройки и журнал хранятся локально в `%ProgramData%\Steamoff`, а если туда нет доступа — в `%AppData%\Steamoff`.

Steamoff управляет только своими правилами firewall. Они находятся в группе `Steamoff`, поэтому приложение отличает свои правила от чужих и не трогает то, что пользователь создал вручную.

Права администратора нужны только потому, что Windows требует их для создания и удаления firewall-правил.

---

## Требования

- Windows 10 / 11
- Права администратора для применения правил firewall
- .NET 8 SDK только для сборки из исходников

---

## Сборка из исходников

```powershell
git clone https://github.com/blvckstn/Steamoff.git
cd Steamoff

dotnet restore
dotnet build Steamoff.slnx
dotnet test --filter "Category!=RequiresAdmin"

Set-ExecutionPolicy -Scope Process Bypass -Force
.\build-release.ps1
```

Готовые файлы появляются в:

```text
src/Steamoff.App/release/
  Steamoff-with-dotnet-runtime/
  Steamoff-without-dotnet-runtime/
  release-manifest.json
```

Вариант `with-dotnet-runtime` проще для запуска. Вариант `without-dotnet-runtime` меньше, но требует установленный .NET Desktop Runtime.

---

## Если что-то пошло не так

**Steam всё ещё онлайн**

Запустите Steamoff от администратора, нажмите **Вкл** и посмотрите счётчик правил. Если он неполный, откройте журнал и диагностику в настройках — там будет видно, какой файл или какой способ применения правил не сработал.

**Игра не запускается без интернета**

Некоторые игры действительно требуют онлайн-проверку, сторонний launcher или первый запуск в сети. Steamoff блокирует интернет-доступ, но не обходит требования игры.

**Антивирус предупреждает о firewall**

Steamoff создаёт и удаляет правила Microsoft Defender Firewall. Защитные инструменты могут заметить это, потому что именно этим приложение и занимается.

**Нужно заблокировать не только Steam**

Добавьте `.exe` или папку в настройках. Один переключатель может управлять Steam, launcher-ами, играми, инструментами и тестовыми сборками.
