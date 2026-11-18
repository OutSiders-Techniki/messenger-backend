# Защита HTTP API

Задачи MSG-014 / MSG-015 / MSG-016 (#41–#43).

Cookie сессии всегда `HttpOnly`, `Path=/`, по умолчанию `Secure; SameSite=Strict`.
Вне `Development` запуск с `Session:Secure=false` запрещён. Для локального
HTTP допускается только Development-конфигурация. Выход отзывает серверную
сессию и удаляет cookie с теми же атрибутами. Токены в ответ JSON не включаются.

Веб-клиент и API размещаются на одном origin. Изменяющие запросы с чужим
`Origin` или `Sec-Fetch-Site: cross-site` получают 403 до вызова контроллера.
Та же проверка защищает GET handshake SignalR на `/hubs/*`, поскольку CORS
сам по себе не блокирует WebSocket. Запросы без браузерных заголовков разрешены
для совместимых нативных клиентов; браузерный JavaScript не может подделать
`Origin`/`Sec-Fetch-Site`. CORS для чужих origin не включён. При использовании
TLS reverse proxy схема/host должны корректно доходить до приложения;
не следует доверять Forwarded-заголовкам произвольных клиентов.

Dev-proxy текущего frontend меняет upstream Host, сохраняя браузерный Origin.
Поэтому Development-конфигурация явно допускает `http://localhost:5173` и
`http://127.0.0.1:5173`. В остальных средах список пуст. Для доверенного
публичного proxy задайте его точный origin в `Security:AllowedOrigins`.
Wildcard, пути и credentials запрещены; `Sec-Fetch-Site: cross-site` отклоняется
даже для origin из списка. CORS этим параметром не включается.

Все ответы, включая ошибки, получают CSP без `unsafe-inline`/`unsafe-eval`,
`X-Content-Type-Options: nosniff`, запрет iframe, `Referrer-Policy: no-referrer`,
отключение camera/microphone/geolocation и `Cache-Control: no-store`.
`blob:` разрешён для локально расшифрованных фото/видео. В Production HTTPS
включён HSTS на год; сервер также перенаправляет HTTP на HTTPS.

`/auth/register` и `/auth/login` используют общий лимит на IP: 10 запросов
за 900 секунд. Точный поиск ограничен 30 запросами за 60 секунд на пару
пользователь/IP. Повторные неуспешные запросы также расходуют лимит.
Переполнение возвращает 429 `application/problem+json`, код `rate_limited`
и `Retry-After` (секунды). Заголовок `X-Forwarded-For` клиента не меняет ключ,
если доверие к прокси не включено (`ForwardedHeaders:Enabled`).

Настройки: `RateLimits:AuthPermitLimit`, `RateLimits:AuthWindowSeconds`,
`RateLimits:SearchPermitLimit`, `RateLimits:SearchWindowSeconds` — положительные
целые числа. Лимитеры рассчитаны на один процесс API в MVP Compose; при
нескольких экземплярах необходим общий limiter на доверенном gateway/store.

За обратным прокси (TLS-терминация) включите `ForwardedHeaders:Enabled=true`,
чтобы клиент определялся по `X-Forwarded-For`: без этого все клиенты разделят
один лимит. Включать стоит только за прокси — напрямую доступный сервис
доверять заголовку не должен: его можно подделать и обойти лимит.

`SecurityTests` проверяет реальный HTTP pipeline без внешней БД: атрибуты cookie,
отзыв сессии, заголовки, CSP, rate limits, CSRF и WebSocket origin, HSTS.
