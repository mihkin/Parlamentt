# ParlamB API

Локальный `ASP.NET Core` API для регистрации, логина, профиля игрока, монет, статистики и колоды.

## Запуск

```powershell
dotnet run --project Backend/ParlamB.Api/ParlamB.Api.csproj
```

По умолчанию API поднимается на:

```text
http://localhost:5268
```

## SQL Server

Строка подключения лежит в [appsettings.json](./appsettings.json):

```json
"ConnectionStrings": {
  "ParlamBDatabase": "Server=(local)\\DEMOEXAMEN;Database=ParlamB;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

API использует таблицы:

- `players`
- `auth_users`
- `player_statistics`
- `cards`
- `player_owned_cards`
- `player_selected_deck`

Если таблиц нет, API создаст их при старте. Карты из `Assets/StreamingAssets/cards.json` тоже подтянутся автоматически.

## Безопасность

Пароль в БД хранится не в открытом виде, а как:

- `password_hash`
- `password_salt`

Регистрация и логин выдают `JWT` токен. Все профильные ручки работают через `Authorization: Bearer <token>`.

## Эндпоинты

- `GET /api/health`
- `POST /api/auth/register`
- `POST /api/auth/login`
- `GET /api/profile/me`
- `PUT /api/profile/me/deck`
- `POST /api/profile/me/coins`
- `POST /api/profile/me/unlock-card`
- `POST /api/profile/me/match-result`

Готовые примеры запросов лежат в [ParlamB.Api.http](./ParlamB.Api.http).

## Что дальше

Следующий шаг для Unity-клиента: заменить текущий `SQL bridge`/локальное сохранение на `UnityWebRequest` вызовы в этот API.
