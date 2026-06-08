# Roadbed.Messaging Reference

Standardized, strongly-typed message envelopes for pub/sub messaging systems (AWS SNS / SQS, Azure Service Bus, RabbitMQ, etc.). Transport-agnostic — produces and consumes JSON envelopes that any broker can carry.

Every envelope shares the same shape: a ULID identifier, publisher metadata, type codename, source and envelope timestamps, and a typed payload `T`.

## Type catalog (4 types)

| Type                          | Kind           | Purpose                                                                             |
| ----------------------------- | -------------- | ----------------------------------------------------------------------------------- |
| `BaseMessagingMessage<T>`     | Abstract class | Shared envelope shape. Concrete request and response classes inherit it.            |
| `MessagingMessageRequest<T>`  | Class          | Concrete envelope for messages sent **to** a system (commands, events).             |
| `MessagingMessageResponse<T>` | Class          | Concrete envelope for **replies**. Adds `OriginalRequestIdentifier` for correlation. |
| `MessagingPublisher`          | Class          | Identifies the publishing process: per-instance ULID + service name (`CommonBusinessKey`). |

## MUST

- **MUST** use `Newtonsoft.Json` (`[JsonProperty(...)]`) for serialization. The envelope properties are decorated with Newtonsoft attributes; `System.Text.Json` ignores them and produces the wrong wire format.
- **MUST** set `MessageTypeCodename` on every message you publish. Use the constructor overload that takes `(publisher, typeCodename)` or `(publisher, typeCodename, identifier, data)`. Routing and broker-side filtering depend on it.
- **MUST** construct **one** `MessagingPublisher` per process and reuse it for every message published from that process. The `Identifier` is the per-instance ULID; treat it as instance identity.
- **MUST** set `OriginalRequestIdentifier` on response messages. This is the only way consumers correlate replies with the originating request.
- **MUST** validate `Data` is not null on the consumer side before processing. `Data` is nullable to permit envelope-only messages.
- **MUST** use the parameterless constructor on `MessagingMessageRequest<T>` and `MessagingMessageResponse<T>` only via `JsonConvert.DeserializeObject<...>(...)` — not by hand. The publishing code path uses the parameterized constructors.

## MUST NOT

- **MUST NOT** use `System.Text.Json` for envelope serialization. The wire format breaks.
- **MUST NOT** construct a `MessagingPublisher` per message. Every message would get a different `Identifier` ULID, defeating instance correlation.
- **MUST NOT** assign `Identifier` directly on a message — it has `internal set` on `BaseMessagingMessage<T>`. Use the constructor overload that accepts an identifier.
- **MUST NOT** skip the null-check on `Data` after deserialization. A consumer that processes `request.Data.SomeField` will throw `NullReferenceException` for envelope-only messages.
- **MUST NOT** mix casing in `MessageTypeCodename`. `Foo.Created` and `foo.created` are different keys to a broker filter.
- **MUST NOT** use `Identifier` from `MessagingMessageResponse` to correlate the reply with the originating request — that's `OriginalRequestIdentifier`. The `Identifier` is the response's own ULID.

## Code patterns

### Construct one publisher per process

```csharp
namespace Foo.Sdk;

using Roadbed;
using Roadbed.Messaging;

public static class FooMessagingHost
{
    public static MessagingPublisher CreatePublisher()
    {
        return new MessagingPublisher(
            name: new CommonBusinessKey("foo-service", "FooService"),
            identifier: Environment.MachineName);
    }
}
```

Register the publisher as a singleton in DI so every component shares the same instance.

### Define payload POCOs with `[JsonProperty]`

```csharp
namespace Foo.Sdk;

using Newtonsoft.Json;

public sealed class FooCreatedPayload
{
    [JsonProperty("foo_id")]
    public long FooId { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }
}

public sealed class FooProcessedPayload
{
    [JsonProperty("foo_id")]
    public long FooId { get; set; }

    [JsonProperty("result")]
    public string? Result { get; set; }
}
```

### Publish a request

```csharp
public sealed class FooPublisher
{
    private readonly MessagingPublisher _publisher;
    private readonly IFooBroker _broker;

    public FooPublisher(MessagingPublisher publisher, IFooBroker broker)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(broker);

        this._publisher = publisher;
        this._broker = broker;
    }

    public async Task PublishFooCreatedAsync(
        FooCreatedPayload payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var message = new MessagingMessageRequest<FooCreatedPayload>(
            this._publisher,
            "foo.created")
        {
            Data = payload,
        };

        string json = JsonConvert.SerializeObject(message);

        await this._broker.PublishAsync(json, cancellationToken);
    }
}
```

### Consume a request and reply

```csharp
public sealed class BarConsumer
{
    private readonly MessagingPublisher _publisher;
    private readonly IBarBroker _broker;

    public BarConsumer(MessagingPublisher publisher, IBarBroker broker)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(broker);

        this._publisher = publisher;
        this._broker = broker;
    }

    public async Task HandleAsync(string body, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        var request = JsonConvert.DeserializeObject<MessagingMessageRequest<FooCreatedPayload>>(body);

        // Always validate Data is not null before processing.
        if (request?.Data is null)
        {
            return;
        }

        // ... do work ...
        var resultPayload = new FooProcessedPayload
        {
            FooId = request.Data.FooId,
            Result = "success",
        };

        var response = new MessagingMessageResponse<FooProcessedPayload>(
            this._publisher,
            "bar.foo_processed")
        {
            Data = resultPayload,
            OriginalRequestIdentifier = request.Identifier,
        };

        string responseJson = JsonConvert.SerializeObject(response);
        await this._broker.PublishAsync(responseJson, cancellationToken);
    }
}
```

### Round-trip is supported

Both `MessagingMessageRequest<T>` and `MessagingMessageResponse<T>` have parameterless constructors specifically so `JsonConvert.DeserializeObject<...>` can rehydrate them. You do not need any custom converter.

## Common pitfalls

### Using `System.Text.Json`

```csharp
// ❌ Ignores [JsonProperty]; produces "Identifier" instead of "message_identifier".
using System.Text.Json;
string json = JsonSerializer.Serialize(message);

// ✅
using Newtonsoft.Json;
string json = JsonConvert.SerializeObject(message);
```

### Missing `OriginalRequestIdentifier` on a response

```csharp
// ❌ Consumer can't tell which request this reply belongs to.
var response = new MessagingMessageResponse<FooProcessedPayload>(this._publisher, "bar.foo_processed")
{
    Data = resultPayload,
};

// ✅
var response = new MessagingMessageResponse<FooProcessedPayload>(this._publisher, "bar.foo_processed")
{
    Data = resultPayload,
    OriginalRequestIdentifier = request.Identifier,
};
```

### Missing `MessageTypeCodename`

```csharp
// ❌ No routing key; broker filters and dead-letter triage are blind.
var message = new MessagingMessageRequest<FooCreatedPayload>(this._publisher)
{
    Data = payload,
};

// ✅
var message = new MessagingMessageRequest<FooCreatedPayload>(this._publisher, "foo.created")
{
    Data = payload,
};
```

### Skipping the null-check on `Data`

```csharp
// ❌ NullReferenceException for envelope-only messages.
var request = JsonConvert.DeserializeObject<MessagingMessageRequest<FooCreatedPayload>>(body);
var fooId = request.Data.FooId;

// ✅
var request = JsonConvert.DeserializeObject<MessagingMessageRequest<FooCreatedPayload>>(body);
if (request?.Data is null)
{
    return;
}
var fooId = request.Data.FooId;
```

### Building a new publisher per message

```csharp
// ❌ Every message gets a different publisher Identifier — instance correlation is impossible.
public async Task PublishAsync(FooCreatedPayload payload, CancellationToken ct)
{
    var publisher = new MessagingPublisher(new CommonBusinessKey("foo-service", "FooService"));
    var message = new MessagingMessageRequest<FooCreatedPayload>(publisher, "foo.created")
    {
        Data = payload,
    };
    // ...
}

// ✅ Inject one publisher constructed at startup.
private readonly MessagingPublisher _publisher;

public FooPublisher(MessagingPublisher publisher)
{
    ArgumentNullException.ThrowIfNull(publisher);
    this._publisher = publisher;
}

public async Task PublishAsync(FooCreatedPayload payload, CancellationToken ct)
{
    var message = new MessagingMessageRequest<FooCreatedPayload>(this._publisher, "foo.created")
    {
        Data = payload,
    };
    // ...
}
```

### Mixing casing in `MessageTypeCodename`

```csharp
// ❌ Broker prefix filters won't match consistently.
"Foo.Created"
"foo.created"
"FOO_CREATED"

// ✅ Pick one style and stay there.
"foo.created"
"foo.updated"
"foo.deleted"
```

## Quick reference

### Using statements

```csharp
using Newtonsoft.Json;       // JsonConvert.SerializeObject / DeserializeObject, [JsonProperty] on payload POCOs
using Roadbed;               // CommonBusinessKey
using Roadbed.Messaging;     // BaseMessagingMessage, request/response, publisher
```

### Wire-format property names

| C# property                            | JSON name                     |
| -------------------------------------- | ----------------------------- |
| `Identifier`                           | `message_identifier`          |
| `MessageTypeCodename`                  | `message_type`                |
| `Publisher`                            | `publisher`                   |
| `Data`                                 | `data`                        |
| `CreatedOn`                            | `message_create_on`           |
| `SourceCreatedOn`                      | `source_create_on`            |
| `OriginalRequestIdentifier` (response) | `original_request_identifier` |
| `MessagingPublisher.Identifier`        | `publisher_identifier`        |
| `MessagingPublisher.Name`              | `publisher_name`              |

### Type-codename conventions

| Pattern                  | Examples                                       |
| ------------------------ | ---------------------------------------------- |
| `entity.action`          | `foo.created`, `foo.updated`, `foo.deleted`    |
| `entity.action.outcome`  | `bar.processed.success`, `bar.processed.failure` |
| `domain.entity.action`   | `inventory.foo.restocked`, `billing.bar.invoiced` |

### Identifier semantics (ULID — 26-char Crockford Base32)

| Property                        | Meaning                                                                |
| ------------------------------- | ---------------------------------------------------------------------- |
| `BaseMessagingMessage.Identifier` | The message's own ULID. Lexicographically sortable by creation time. |
| `MessagingPublisher.Identifier` | The **publishing instance's** ULID — process / container / machine ID. |
| `MessagingMessageResponse.OriginalRequestIdentifier` | The `Identifier` of the request being replied to.   |
