# Приватный мессенджер — backend

ASP.NET Core / .NET 10, PostgreSQL 17, EF Core и SignalR. Сервер принимает
шифротекст; ключи диалога и расшифрование остаются на клиенте.

## Локальный запуск

Нужны .NET 10 SDK и Docker Compose. Из корня репозитория:

```sh
cp .env.example .env
docker compose up --build
```

API: `http://localhost:8080`, проверка запуска: `GET /health`, OpenAPI:
`GET /openapi/v1.json`. Compose автоматически применяет миграции после
готовности PostgreSQL и сохраняет БД/зашифрованные вложения в volumes.
Локальный профиль Development допускает HTTP-cookie без Secure.

Для разработки вне контейнера:

```sh
docker compose up -d postgres
dotnet tool restore
dotnet dotnet-ef database update --project src/Messenger.Infrastructure --startup-project src/Messenger.Api
dotnet run --project src/Messenger.Api
```

Для текущего frontend используйте HTTP-профиль backend:

```sh
dotnet run --project src/Messenger.Api --launch-profile http
```

Он слушает `http://localhost:5273`. Frontend запускается своей существующей
командой `npm run dev -- --api=http://localhost:5273` на порту 5173.
Этот dev-proxy разрешён только в Development; менять frontend-код не требуется.

Не публикуйте локальную конфигурацию в сеть. Для сетевого развёртывания
нужны HTTPS, `ASPNETCORE_ENVIRONMENT=Production`, `SESSION_COOKIE_SECURE=true`,
собственный пароль PostgreSQL и корректная передача схемы/host от доверенного
TLS proxy. Веб-клиент и API используют один origin. Смотрите [защиту HTTP](docs/security.md).

## Проверка

```sh
dotnet restore MessengerBackend.slnx
dotnet build MessengerBackend.slnx --no-restore
MESSENGER_TEST_DB='Host=localhost;Port=5432;Database=messenger;Username=messenger;Password=messenger' dotnet test MessengerBackend.slnx --no-build
```

Используйте отдельную тестовую БД: тесты создают записи и применяют миграции.
Новые message integration tests пропускают проверки БД, только если она
недоступна и `MESSENGER_TEST_DB` не задана. Если переменная задана, отсутствие
БД и ошибки миграций приводят к падению тестов. Security/service tests не
требуют PostgreSQL. CI поднимает отдельный PostgreSQL 17 и запускает весь набор.

## Контракты

- [Существующие auth/dialog/attachment endpoints](docs/api.md).
- [Сообщения, идемпотентность, SignalR и синхронизация удаления](docs/messaging.md).
- [Схема БД](docs/database.md).
- [Криптографический формат и тестовый вектор](docs/protocol-v1.md).
- [Покрытие задач #41–#53 и проверка](docs/backend-tasks.md).
