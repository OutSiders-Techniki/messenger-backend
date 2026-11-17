# Покрытие backend-задач

Исходная база: `OutSiders-Techniki/messenger-backend`, main,
`59b08f81bbed7f08d7651edba71aa82fdef36efa`.
Открытые задачи #41–#53 назначены GitHub-пользователю `mysterymarshak`.

| Задача | Реализация | Проверки |
| --- | --- | --- |
| #41 / MSG-014 | HttpOnly/Secure/SameSite; безопасная конфигурация | SecurityTests |
| #42 / MSG-015 | CSP, security headers, HSTS, origin-защита | SecurityTests |
| #43 / MSG-016 | auth/search limiter, 429 + Retry-After | SecurityTests |
| #44 / MSG-017 | Авторизованный hub, повторная проверка сессий | MessageFlowTests, SecurityTests |
| #45 / MSG-018 | v/UUID/UTC/IV/ct валидация | MessageServiceTests, MessageFlowTests |
| #46 / MSG-019 | Две копии в транзакции | MessageFlowTests |
| #47 / MSG-020 | Keyset pagination владельца | MessageFlowTests |
| #48 / MSG-021 | События только подключённым участникам | MessageFlowTests |
| #49 / MSG-022 | Идемпотентность, защита от воскрешения после удаления | MessageServiceTests, MessageFlowTests |
| #50 / MSG-023 | Физическое удаление своей копии | MessageFlowTests |
| #51 / MSG-024 | Атомарное удаление обеих копий, уборка файлов | MessageFlowTests |
| #52 / MSG-025 | Sync missing IDs + ревизии + алгоритм клиента | docs/messaging.md, MessageFlowTests |
| #53 / MSG-026 | HTTP/БД/SignalR integration tests и CI PostgreSQL | MessageFlowTests, backend.yml |

Полный прогон интеграционных тестов требует восстановления NuGet и PostgreSQL.
Наличие кода тестов не является подтверждением их успешного выполнения.
Результаты локальной проверки отражаются отдельно при сдаче.

Согласование frontend-контракта и реализация очистки памяти офлайн-вкладки
нужны на стороне клиента. Задачи в GitHub автоматически не закрываются.
