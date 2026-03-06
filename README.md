# R.K experiment on .NET 6

Готовое демо-решение для проверки поведения `Rebus.Kafka` в сценарии:

- 1 сервис-публикатор
- 1 сервис-подписчик типа **A**
- 2 экземпляра сервиса-подписчика типа **B**
- публикация **нескольких типов контрактов**
- автоматическая подписка consumers на **все типы сообщений, для которых в сервисе есть handlers**

## Какие контракты используются

В решении опубликованы три разных типа событий:

- `UserRegisteredEvent`
- `OrderSubmittedEvent`
- `PaymentCapturedEvent`

Распределение по сервисам:

- `ConsumerA` обрабатывает:
  - `UserRegisteredEvent`
  - `OrderSubmittedEvent`
- `ConsumerB` обрабатывает:
  - `OrderSubmittedEvent`
  - `PaymentCapturedEvent`

Это позволяет проверить сразу несколько сценариев:

- разные типы контрактов создают разные event topics
- один и тот же контракт (`OrderSubmittedEvent`) может быть подписан несколькими сервисами
- два экземпляра сервиса B с одним `GroupId` делят поток одного и того же типа сообщений между собой

## Как реализована автоматическая подписка

В `ConsumerA` и `ConsumerB` добавлен `AutoSubscriptionStarter`, который:

1. находит в сборке все `IHandleMessages<T>`
2. извлекает типы `T`
3. вызывает `bus.Subscribe<T>()` через reflection

Таким образом подписка выполняется **автоматически по типам сообщений, которые реально обрабатываются handlers текущего сервиса**.

Важно:

- это **не** означает, что Rebus сам подписывается на всё, что когда-либо публикует publisher
- подписчик всё равно должен знать тип контракта на этапе компиляции
- автоматизация возможна на стороне **subscriber startup**, а не на стороне publisher

## Краткий ответ на вопрос

### Возможно ли автоматически создавать очереди и подписываться на них по типу публикуемого сообщения?

Да, но с оговорками:

- **автоматическое создание topic** в `Rebus.Kafka` возможно при включённом `AllowAutoCreateTopics=true`
- **автоматическая подписка по типам** тоже возможна, если на старте сервиса найти все `IHandleMessages<T>` и вызвать `Subscribe<T>()` для каждого найденного типа

Но:

- подписка происходит не потому, что publisher начал что-то публиковать
- подписка происходит потому, что subscriber **знает нужные contract types** и подписывается на них при старте
- автоматически подписаться на «совсем неизвестный новый тип сообщения» без наличия контракта и handler'а в сервисе нельзя

## Состав решения

- `RkExperiment.Contracts` — общие контракты
- `RkExperiment.Publisher` — публикует три типа событий
- `RkExperiment.ConsumerA` — подписчик A
- `RkExperiment.ConsumerB` — подписчик B, один и тот же бинарник запускается в 2 экземплярах
- `RkExperiment.Admin` — вспомогательная утилита для явного создания topic с нужным количеством partitions
- `docker-compose.yml` — Kafka, Kafka UI и сервисы эксперимента

## Идея эксперимента

Используются две consumer group:

- `rk-consumer-a`
- `rk-consumer-b`

Ожидаемое поведение:

- `ConsumerA` получает все `UserRegisteredEvent`
- `ConsumerA` получает все `OrderSubmittedEvent`
- `ConsumerB-1` и `ConsumerB-2` вместе получают все `OrderSubmittedEvent`
- `ConsumerB-1` и `ConsumerB-2` вместе получают все `PaymentCapturedEvent`
- каждое конкретное сообщение внутри группы B обрабатывает только один экземпляр

## Быстрый запуск

### 1. Поднять Kafka и подписчиков

```bash
docker compose up --build kafka kafka-ui consumer-a consumer-b-1 consumer-b-2
```

### 2. Отправить пачку событий

```bash
docker compose --profile publisher up --build publisher
```

### 3. Посмотреть логи

```bash
docker compose logs -f consumer-a consumer-b-1 consumer-b-2 publisher
```

### 4. Открыть UI Kafka

Открой `http://localhost:18080`

Там можно посмотреть:

- какие topics создались
- сколько у них partitions
- какие есть consumer groups
- как распределяются сообщения

## Что смотреть в логах

### Для ConsumerA

Должны быть записи двух типов:

- `A handled UserRegisteredEvent ...`
- `A handled OrderSubmittedEvent ...`

### Для ConsumerB

Должны быть записи двух типов:

- `B handled OrderSubmittedEvent ...`
- `B handled PaymentCapturedEvent ...`

Если у event topic только один partition, один экземпляр B может простаивать.
Если partitions несколько, сообщения этих типов должны делиться между `consumer-b-1` и `consumer-b-2`.

## Что фиксировать по итогам

Рекомендуется отдельно записать:

- какие topics создались для каждого типа контракта
- сколько partitions у каждого topic
- какие типы подписал `ConsumerA`
- какие типы подписал `ConsumerB`
- делит ли группа B поток `OrderSubmittedEvent`
- делит ли группа B поток `PaymentCapturedEvent`
- есть ли гонки при автосоздании служебных topics (`error` и др.)

## Практический вывод

Для production-сценария рекомендуется:

- не полагаться на автосоздание topics
- создавать topics заранее
- явно задавать нужное количество partitions
- подписываться на типы сообщений один раз при старте приложения
- не строить логику на динамической подписке/отписке во время runtime
