# Roadbed.Net Reference

Resilient HTTP client wrapper. Provides retry-with-backoff, optional GZip/Deflate compression, named `HttpClient` instances managed by `IHttpClientFactory`, automatic JSON deserialization via Newtonsoft.Json, and a typed response wrapper that flips HTTP errors and JSON deserialization failures into the same `IsSuccessStatusCode` flag.

Used inside SDK class libraries that call REST APIs.

## Type catalog (9 types)

| Type                        | Kind         | Purpose                                                                              |
| --------------------------- | ------------ | ------------------------------------------------------------------------------------ |
| `INetHttpClient`            | Interface    | The contract for making HTTP requests. Inject this in repositories.                  |
| `NetHttpClient`             | Class        | Concrete implementation. Auto-registered by the installer.                           |
| `NetHttpRequest`            | Class        | Request configuration: endpoint, method, headers, auth, retry, timeout.              |
| `NetHttpHeader`             | Record       | One HTTP header (`Name`, `Value`).                                                   |
| `NetHttpAuthentication`     | Class        | `AuthenticationType` (Basic / Bearer / Unknown) + credential `Value`.                |
| `NetHttpAuthenticationType` | Enum         | `Unknown`, `Basic`, `Bearer`.                                                        |
| `NetHttpRetryPattern`       | Class        | `MaxAttempts` (number of *retries*, default 3) + `DelayMultiplierInSeconds` (default 5). |
| `NetHttpResponse<T>`        | Record       | `IsSuccessStatusCode`, `Data`, `Errors`, `HttpStatusCode`, `HttpStatusCodeDescription`. |
| `InstallNetHttpClient`      | Class        | Auto-discovered installer. Registers named clients and `INetHttpClient`.             |

## MUST

- **MUST** inject `INetHttpClient` (the interface) into repositories — never `NetHttpClient` (the concrete class).
- **MUST** create a fresh `NetHttpRequest` per API call. Do not reuse instances.
- **MUST** set `HttpEndPoint` as a `Uri` instance (`new Uri("https://...")`), not a raw string.
- **MUST** check `response.IsSuccessStatusCode` before reading `response.Data`. The check covers HTTP failures **and** JSON deserialization failures.
- **MUST** use the correct generic type parameter: `MakeHttpRequestAsync<TDto>()` for automatic Newtonsoft deserialization, or `MakeHttpRequestAsync<string>()` to receive the raw response body.
- **MUST** use `[JsonProperty(...)]` (Newtonsoft) on response DTOs. `[JsonPropertyName]` (System.Text.Json) is silently ignored.
- **MUST** put `CancellationToken cancellationToken = default` as the last parameter on every async repository method that wraps an HTTP call.

## MUST NOT

- **MUST NOT** instantiate `NetHttpClient` directly. The installer wires `IHttpClientFactory` and the named clients — bypassing it gives you no factory and no retries.
- **MUST NOT** register `IHttpClientFactory` or `INetHttpClient` manually. `InstallNetHttpClient` is auto-discovered by `services.InstallModulesInAppDomain(configuration)`.
- **MUST NOT** wrap `MakeHttpRequestAsync<T>` in a `try/catch (JsonException)`. The client catches JSON failures internally and returns a `Failure()` response — your `IsSuccessStatusCode` check covers it.
- **MUST NOT** wrap it in a manual retry loop. Configure `request.RetryPattern` instead.
- **MUST NOT** set `HttpEndPoint` to a raw string. The property is typed `Uri?`.
- **MUST NOT** set `Authentication` with `AuthenticationType = Unknown` and expect a header to be added. `Unknown` is the explicit "no Authorization header" value.

## Code patterns

### SDK repository injecting `INetHttpClient`

```csharp
namespace Foo.Sdk;

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Roadbed;
using Roadbed.Crud.Repositories.Async;
using Roadbed.Net;

internal sealed class FooRepository
    : BaseAsyncCrudlRepository<Foo, string>,
      IFooRepository
{
    private const string BaseUrl = "https://api.example.com";

    private readonly INetHttpClient _httpClient;

    public FooRepository(
        INetHttpClient httpClient,
        ILogger<FooRepository> logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        this._httpClient = httpClient;
    }

    public override async Task<Foo?> ReadAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var request = new NetHttpRequest
        {
            HttpEndPoint = new Uri($"{BaseUrl}/foos/{id}"),
            HttpHeaders =
            {
                new NetHttpHeader("Accept", "application/json"),
                new NetHttpHeader("User-Agent", "(MyApp, contact@example.com)"),
            },
        };

        NetHttpResponse<FooDto> response =
            await this._httpClient.MakeHttpRequestAsync<FooDto>(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            this.LogWarning(
                "Failed to read foo {Id}: {StatusCode} {Description}",
                id,
                response.HttpStatusCode,
                response.HttpStatusCodeDescription);
            return null;
        }

        var dto = response.Data;
        return new Foo
        {
            Id = dto.Id,
            Name = dto.Name,
            Description = dto.Description,
        };
    }

    // Other CRUDL methods here...
}
```

### POST request with bearer auth

```csharp
var payload = JsonConvert.SerializeObject(entity);

var request = new NetHttpRequest
{
    HttpEndPoint = new Uri($"{BaseUrl}/foos"),
    Method = HttpMethod.Post,
    Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    Authentication = new NetHttpAuthentication
    {
        AuthenticationType = NetHttpAuthenticationType.Bearer,
        Value = this._apiToken,
    },
};

NetHttpResponse<CreateFooResponse> response =
    await this._httpClient.MakeHttpRequestAsync<CreateFooResponse>(request, cancellationToken);
```

### DTO with `[JsonProperty]` (Newtonsoft, never `[JsonPropertyName]`)

```csharp
namespace Foo.Sdk;

using Newtonsoft.Json;

public sealed record FooApiResponse
{
    [JsonProperty("items")]
    required public FooItem[] Items { get; set; }
}

public sealed record FooItem
{
    [JsonProperty("id")]
    required public string Id { get; set; }

    [JsonProperty("attributes")]
    required public FooAttributes Attributes { get; set; }
}

public sealed record FooAttributes
{
    [JsonProperty("name")]
    required public string Name { get; set; }
}
```

### Custom retry configuration

```csharp
var request = new NetHttpRequest
{
    HttpEndPoint = new Uri("https://api.example.com/resource"),
    TimeoutInSecondsPerAttempt = 30,
    RetryPattern = new NetHttpRetryPattern
    {
        MaxAttempts = 5,
        DelayMultiplierInSeconds = 2,
    },
};
// Backoff: 2^0 = 1s, 2^1 = 2s, 2^2 = 4s, 2^3 = 8s, 2^4 = 16s
```

### Disable retries entirely (e.g., idempotency-sensitive POSTs)

```csharp
var request = new NetHttpRequest
{
    HttpEndPoint = new Uri("https://api.example.com/charge"),
    Method = HttpMethod.Post,
    RetryPattern = new NetHttpRetryPattern
    {
        MaxAttempts = 0,
        DelayMultiplierInSeconds = 0,
    },
};
```

### Raw-string response (when DTO deserialization is impractical)

```csharp
var request = new NetHttpRequest
{
    HttpEndPoint = new Uri("https://api.example.com/legacy-xml"),
};

NetHttpResponse<string> response =
    await this._httpClient.MakeHttpRequestAsync<string>(request, cancellationToken);

if (response.IsSuccessStatusCode)
{
    string rawBody = response.Data;
    // ... parse XML or whatever ...
}
```

## Common pitfalls

### Reading `response.Data` without checking `IsSuccessStatusCode`

```csharp
// ❌ Data is default! (null for reference types) on failure.
var items = response.Data.Items;

// ✅ Check first.
if (response.IsSuccessStatusCode)
{
    var items = response.Data.Items;
}
```

### Wrapping in try/catch for JSON errors

```csharp
// ❌ Unnecessary — JSON errors are wrapped in Failure().
try
{
    var response = await this._httpClient.MakeHttpRequestAsync<FooDto>(request, cancellationToken);
    return response.Data;
}
catch (JsonException ex)
{
    // never fires
}

// ✅ The IsSuccessStatusCode check covers JSON failures.
var response = await this._httpClient.MakeHttpRequestAsync<FooDto>(request, cancellationToken);
if (!response.IsSuccessStatusCode)
{
    this.LogWarning("Request failed: {Error}", response.Errors.FirstOrDefault());
    return null;
}
return response.Data;
```

### Manual `HttpClient` allocation

```csharp
// ❌ Bypasses the named-client factory; no compression, no retries.
var client = new HttpClient();
var response = await client.GetAsync(url);

// ✅ Inject INetHttpClient and use NetHttpRequest.
public FooRepository(INetHttpClient httpClient, ILogger<FooRepository> logger) : base(logger) { ... }
```

### `[JsonPropertyName]` on a DTO

```csharp
// ❌ System.Text.Json attribute — Newtonsoft ignores it. Property uses C# name.
using System.Text.Json.Serialization;
public sealed record FooDto
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

// ✅
using Newtonsoft.Json;
public sealed record FooDto
{
    [JsonProperty("id")]
    public string? Id { get; set; }
}
```

### Manually registering `IHttpClientFactory` or `INetHttpClient`

```csharp
// ❌ Duplicates what InstallNetHttpClient already does.
builder.Services.AddHttpClient();
builder.Services.AddScoped<INetHttpClient, NetHttpClient>();

// ✅
builder.Services.InstallModulesInAppDomain(builder.Configuration);
```

### Setting `Authentication` to `Unknown`

```csharp
// ❌ Unknown means "no Authorization header" — the value is ignored.
request.Authentication = new NetHttpAuthentication
{
    AuthenticationType = NetHttpAuthenticationType.Unknown,
    Value = "my-api-key",
};

// ✅ Pick Basic or Bearer.
request.Authentication = new NetHttpAuthentication
{
    AuthenticationType = NetHttpAuthenticationType.Bearer,
    Value = "my-api-key",
};
```

### Raw string for `HttpEndPoint`

```csharp
// ❌ Won't compile — the property is Uri?, not string.
request.HttpEndPoint = "https://api.example.com/foos";

// ✅
request.HttpEndPoint = new Uri("https://api.example.com/foos");
```

## Quick reference

### Using statements

```csharp
using Newtonsoft.Json;     // JsonProperty on DTOs
using Roadbed.Net;         // INetHttpClient, NetHttpRequest, NetHttpResponse, all enums
```

### `NetHttpRequest` defaults

| Property                                | Default                |
| --------------------------------------- | ---------------------- |
| `Method`                                | `HttpMethod.Get`       |
| `EnableCompression`                     | `true`                 |
| `TimeoutInSecondsPerAttempt`            | `15`                   |
| `RetryPattern.MaxAttempts`              | `3`                    |
| `RetryPattern.DelayMultiplierInSeconds` | `5`                    |
| `HttpHeaders`                           | empty list             |
| `Content`                               | `null`                 |
| `Authentication`                        | `null`                 |

### Backoff schedule (defaults)

`Math.Pow(DelayMultiplierInSeconds, attempt)` seconds. With defaults (multiplier = 5, max attempts = 3):

| After attempt | Delay |
| ------------- | ----- |
| 0 (initial)   | 1 s   |
| 1 (1st retry) | 5 s   |
| 2 (2nd retry) | 25 s  |
| 3 (3rd retry) | (no delay — last attempt) |

### Retried HTTP status codes

| Status | Retried? |
| ------ | -------- |
| 503    | yes      |
| 408    | yes      |
| 504    | yes      |
| any other 4xx / 5xx | no |

Plus `HttpRequestException` and `TimeoutException` from the network layer.

### Generic-parameter behavior

| `T`             | Behavior                                                    |
| --------------- | ----------------------------------------------------------- |
| `string`        | Raw response body — no deserialization                      |
| any other type  | `JsonConvert.DeserializeObject<T>(body)` via Newtonsoft     |
