# CS 1.6 Panel

Минимальная веб-панель для ReHLDS + YaPB + Reunion.

- Blazor Server / .NET 9
- один контейнер
- без БД
- RCON из backend
- выбор карты
- 0..10 ботов
- сложность Easy/Normal/Hard/Expert
- статус сервера и список игроков
- карты читаются из `/maps`

## Environment

`CS_SERVER_HOST`, `CS_SERVER_PORT`, `CS_RCON_PASSWORD`, `MAPS_PATH`, `PUBLIC_HOST`, `PUBLIC_PORT`.

Для текущего compose проще всего использовать host networking и `CS_SERVER_HOST=127.0.0.1`.
