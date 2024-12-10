# OpenNetty

## What is OpenNetty?

OpenNetty aims at providing an **advanced solution** for implementing [OpenWebNet](https://en.wikipedia.org/wiki/OpenWebNet)
support in .NET 8.0+ applications.

OpenWebNet is a protocol developed by [BTicino](https://www.bticino.it/) and [Legrand](https://www.legrand.fr/) in 2000 to manage electrical
networks. While it uses a very basic wire format, the OpenWebNet protocol is actually fairly complex to implement properly but also quite powerful.

To this date, 3 variants of OpenWebNet have been developed by the two companies:
  - OpenWebNet, used to integrate with the [SCS](https://en.wikipedia.org/wiki/Bus_SCS)-based "MyHome" products.
  - OpenWebNet/Nitoo, used to integrate with "In One by Legrand" products (powerline and radio).
  - OpenWebNet/Zigbee, used to integrate with the Zigbee-based "MyHome Play" products.

> [!NOTE]
> OpenNetty is currently the only library that supports the 3 OpenWebNet variants.

OpenNetty offers both low-level primitives to represent OpenWebNet messages and communicate with OpenWebNet
gateways and a higher-level MQTT integration that can be directly used with home automation software like
[Home Assistant](https://www.home-assistant.io/), [Jeedom](https://jeedom.com/) or [FHEM](https://fhem.de/).

> [!IMPORTANT]
> **An OpenWebNet gateway is required by OpenNetty to be able to interact with BTicino and Legrand devices**:
>
>   - For In One by Legrand devices, a Legrand 88213 powerline/USB gateway is required. To communicate with
> In One by Legrand radio devices, a Legrand 03606 interface must also be present in the installation.
>   - For MyHome/MyHome Up devices, both BTicino F454 and MH202 SCS/Ethernet gateways are currently supported.
>   - For MyHome Play devices, a BTicino 3578 or Legrand 88328 Zigbee/USB gateway is required.
>
> Since the "In One by Legrand" and "MyHome Play" products are no longer manufactured, buying the corresponding gateway
> is generally not easy (or cheap!), so this should probably only be considered for large existing installations.

--------------

## Supported devices

The following Legrand and BTicino products are partially or fully supported by OpenNetty:

| Product series    | Product collection | Legrand reference | BTicino reference |
|-------------------|--------------------|-------------------|-------------------|
| In One by Legrand | Lexic              | 03600             |                   |
| In One by Legrand | Lexic              | 03648             |                   |
| In One by Legrand | Lexic              | 03809             |                   |
| In One by Legrand |                    | 43214             |                   |
| In One by Legrand | Céliane            | 67201             |                   |
| In One by Legrand | Céliane            | 67202             |                   |
| In One by Legrand | Céliane            | 67203             |                   |
| In One by Legrand | Céliane            | 67204             |                   |
| In One by Legrand | Céliane            | 67208             |                   |
| In One by Legrand | Céliane            | 67210             |                   |
| In One by Legrand | Céliane            | 67212             |                   |
| In One by Legrand | Céliane            | 67214             |                   |
| In One by Legrand | Céliane            | 67215             |                   |
| In One by Legrand | Céliane            | 67220             |                   |
| In One by Legrand | Céliane            | 67222             |                   |
| In One by Legrand | Céliane            | 67280             |                   |
| In One by Legrand | Céliane            | 67290             |                   |
| In One by Legrand | Céliane            | 67445             |                   |
| In One by Legrand | Céliane            | 67448             |                   |
| In One by Legrand | Plexo              | 69510             |                   |
| In One by Legrand | Sagane             | 84520             |                   |
| In One by Legrand | Sagane             | 84522             |                   |
| In One by Legrand | Sagane             | 84523             |                   |
| In One by Legrand | Sagane             | 84524             |                   |
| In One by Legrand | Sagane             | 84525             |                   |
| In One by Legrand | Sagane             | 84529             |                   |
| In One by Legrand | Sagane             | 84530             |                   |
| In One by Legrand | Sagane             | 84531             |                   |
| In One by Legrand | Sagane             | 84542             |                   |
| In One by Legrand |                    | 88205             |                   |
| In One by Legrand |                    | 88213             |                   |
|                   |                    |                   |                   |
| MyHome Up         |                    | 03847             | F411U1            |
| MyHome Up         |                    | 03848             | F411U2            |
| MyHome Up         |                    | 03651             | F418U2            |
| MyHome Up         |                    | 03598             | F454              |
| MyHome Up         |                    | 03535             | MH202             |
|                   |                    |                   |                   |
| MyHome Play       | Céliane            | 67223             |                   |
| MyHome Play       |                    | 88328             | 3578              |
| MyHome Play       |                    | 88337             |                   |

> [!NOTE]
> Support for additional devices will be progressively added depending on the demand.

--------------

## Core components

### Primitives

To represent raw OpenWebNet frames, OpenNetty exposes 3 low-level structures – `OpenNettyFrame`, `OpenNettyField` and `OpenNettyParameter` – and
one high-level primitive – `OpenNettyMessage` – that can be used to represent any message type supported by the 3 OpenWebNet specifications:
  - Bus commands.
  - Dimension requests.
  - Dimension reads.
  - Dimension sets.
  - Status requests.

```csharp
var message = OpenNettyMessage.CreateCommand(
    protocol: OpenNettyProtocol.Nitoo,
    command : OpenNettyCommands.Lighting.On,
    address : OpenNettyAddress.FromNitooAddress(identifier: 487932, unit: 2),
    media   : OpenNettyMedia.Powerline,
    mode    : OpenNettyMode.Unicast);
```

```csharp
var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Nitoo, "*1*1*7806914##");

// (487932, 2)
var (identifier, unit) = OpenNettyAddress.ToNitooAddress(message.Address!.Value);
```

### Sessions and connections

The `OpenNettySession` class is the main entry point for **manually communicating** with an OpenWebNet gateway: it takes care
of initializing the connection and negotiates the desired OpenWebNet session type automatically. If authentication is
required by the remote gateway, it also takes care of the authentication dance in a completely transparent way.

`OpenNettySession` implements `IAsyncObservable<OpenNettyMessage>` and can be natively used with any of the extensions
provided by the [System.Reactive.Async package](https://www.nuget.org/packages/System.Reactive.Async) to filter and
observe the messages sent by the OpenWebNet gateway.

```csharp
var gateway = OpenNettyGateway.Create(
    name    : "SCS-Ethernet gateway",
    brand   : OpenNettyBrand.BTicino,
    model   : "F454",
    endpoint: IPEndPoint.Parse("192.168.5.10:20000"),
    password: "aJhYiBHk8");

await using var session = await OpenNettySession.CreateAsync(gateway, OpenNettySessionType.Event);

await using var subscription = await session.SubscribeAsync(message => Console.WriteLine(message.ToString()));
await using var connection = await session.ConnectAsync();

await Task.Delay(-1);
```

> [!TIP]
> For advanced scenarios that only involve sequential processing, the `OpenNettyConnection` class can also be directly
> used to send and/or receive OpenWebNet frames from a gateway using either a TCP connection or a serial port:
> 
> ```csharp
> using var port = new SerialPort(
>     portName: "/dev/serial/by-id/usb-Silicon_Labs_CP2102_USB_to_UART_Bridge_Controller_0001-if00-port0",
>     baudRate: 19_200,
>     parity  : Parity.None,
>     dataBits: 8,
>     stopBits: StopBits.One);
>
> using var source = new CancellationTokenSource();
> source.CancelAfter(TimeSpan.FromSeconds(10));
> 
> await using var connection = await OpenNettyConnection.CreateSerialConnectionAsync(port, source.Token);
> 
> var message = OpenNettyMessage.CreateCommand(
>     protocol: OpenNettyProtocol.Zigbee,
>     command : OpenNettyCommands.Lighting.On,
>     address : OpenNettyAddress.FromHexadecimalZigbeeAddress(identifier: "0065ACAC", unit: 1),
>     media   : OpenNettyMedia.Radio,
>     mode    : OpenNettyMode.Unicast);
> 
> await connection.SendAsync(message.Frame, source.Token);
> 
> if (await connection.ReceiveAsync(source.Token) != OpenNettyFrames.Acknowledgement)
> {
>     throw new ApplicationException("The frame was not acknowledged by the gateway.");
> }
> ```

### .NET Generic Host integration

While **sessions and connections can be directly used to communicate with an OpenWebNet gateway, it is not the recommended approach**.

Instead, **users are strongly encouraged to leverage OpenNetty's .NET Generic Host integration**: it will register a worker
for each configured gateway and will dynamically manage sessions, process incoming messages and dispatch outgoing messages.
It also automatically retransmit failed outgoing messages using a retry policy defined by OpenNetty based on the type of gateway.

Once the OpenNetty services are registered using the dedicated `.AddOpenNetty()` extension, the low-level `IOpenNettyService`
interface can be leveraged to execute any arbitrary bus command, dimension request, dimension set or status request
and extract the corresponding response returned by the gateway, if applicable:

```csharp
var builder = Host.CreateApplicationBuilder();

builder.Services.AddSystemd()
    .AddWindowsService();

builder.Services.AddOpenNetty(options =>
{
    // Register the SCS gateway used to communicate with MyHome devices.
    options.AddGateway(OpenNettyGateway.Create(
        name    : "F454 gateway",
        brand   : OpenNettyBrand.BTicino,
        model   : "F454",
        endpoint: IPEndPoint.Parse("192.168.5.10:20000"),
        password: "aJhYiBHk8"));
});

var app = builder.Build();
await app.StartAsync();

// Send a WHO=13/DIMENSION=19 request and extract the raw values resolved from the response returned by the gateway.
var service = app.Services.GetRequiredService<IOpenNettyService>();
var values = await service.GetDimensionAsync(OpenNettyProtocol.Scs, OpenNettyDimensions.Management.Uptime);

await app.StopAsync();
```

> [!TIP]
> While the `IOpenNettyService` service can be very useful to send arbitrary messages or observe specific incoming messages, defining endpoints
> as shown in the next section and using the strongly-typed APIs offered by `OpenNettyController` when possible is strongly recommended.

### Endpoints

**For all its high-level operations, OpenNetty relies on the `OpenNettyEndpoint` class**:

  - An endpoint generally has an address associated (but it's not always true, as gateway endpoints don't have an address attached).
  - In most cases, an endpoint has a device definition attached from which it resolves the supported functions (like switching on or
  off a connected load or controlling the brightness level), but it's possible to create endpoints that don't have a device
  attached, which allows supporting non-device-specific addresses like SCS point-of-light area or group addresses.
  - Nitoo and Zigbee endpoints often have a unit definition attached, but non-unit-specific endpoints can also
  be created to perform actions that don't target a specific Nitoo or Zigbee unit (e.g Nitoo device descriptions).
  - When no unit or device definition is attached, a list of capabilities must be attached
  to the endpoint before being able to perform actions using the `OpenNettyController` class.

```csharp
var builder = Host.CreateApplicationBuilder();

builder.Services.AddSystemd()
    .AddWindowsService();

builder.Services.AddOpenNetty(options =>
{
    options.AddGateway(OpenNettyGateway.Create(
        name    : "F454 gateway",
        brand   : OpenNettyBrand.BTicino,
        model   : "F454",
        endpoint: IPEndPoint.Parse("192.168.5.10:20000"),
        password: "aJhYiBHk8"));

    options.AddEndpoint(new OpenNettyEndpoint
    {
        Address = OpenNettyAddress.FromScsLightPointPointToPointAddress(area: 1, point: 3),
        Device = new OpenNettyDevice
        {
            Definition = OpenNettyDevices.GetDeviceByModel(OpenNettyBrand.BTicino, "F418U2")
                ?? throw new InvalidOperationException("The specified gateway model is not supported.")
        },
        Name = "Bathroom/Recessed light",
        Protocol = OpenNettyProtocol.Scs
    });

    options.AddEndpoint(new OpenNettyEndpoint
    {
        Address = OpenNettyAddress.FromScsLightPointAreaAddress(area: 1),
        Capabilities = [OpenNettyCapabilities.OnOffSwitching],
        Name = "Bathroom/All lights",
        Protocol = OpenNettyProtocol.Scs
    });
});

var app = builder.Build();
await app.StartAsync();

var manager = app.Services.GetRequiredService<OpenNettyManager>();
var controller = app.Services.GetRequiredService<OpenNettyController>();

// Resolve the brightness of the dimmable recessed light in area 1.
var brightness = await controller.GetBrightnessAsync(
    await manager.FindEndpointByNameAsync("Bathroom/Recessed light")
        ?? throw new InvalidOperationException("The endpoint couldn't be resolved."));

// Switch off all the lights located in area 1.
await controller.SwitchOffAsync(
    await manager.FindEndpointByNameAsync("Bathroom/All lights")
        ?? throw new InvalidOperationException("The endpoint couldn't be resolved."));

await app.StopAsync();
```

### Events

To infer high-level state changes affecting registered endpoints, OpenNetty includes a built-in `OpenNettyCoordinator` service that monitors
all the incoming messages sent by the configured gateways and invokes the corresponding events exposed by the `OpenNettyEvents` class.

By implementing the `IOpenNettyHandler` interface, it is possible to subscribe to any event before incoming frames start being processed:

```csharp
var builder = Host.CreateApplicationBuilder();

builder.Services.AddSystemd()
    .AddWindowsService();

builder.Services.AddOpenNetty(options =>
{
    var file = builder.Environment.ContentRootFileProvider.GetFileInfo("OpenNettyConfiguration.xml");
    options.ImportFromXmlConfiguration(file);
});

builder.Services.AddSingleton<IOpenNettyHandler, MyEventHandler>();

var app = builder.Build();
await app.RunAsync();

class MyEventHandler(OpenNettyEvents events) : IOpenNettyHandler
{
    public async ValueTask<IAsyncDisposable> SubscribeAsync() => StableCompositeAsyncDisposable.Create(
    [
        await events.BrightnessReported
            .Where(args => !string.IsNullOrEmpty(args.Endpoint.Name))
            .SubscribeAsync(args => Console.WriteLine($"Brightness on endpoint {args.Endpoint.Name}: {args.Level}.")),

        await events.SwitchStateReported
            .Where(args => !string.IsNullOrEmpty(args.Endpoint.Name))
            .SubscribeAsync(args => Console.WriteLine($"Switch state on endpoint {args.Endpoint.Name}:" +
                (args.State is OpenNettyModels.Lighting.SwitchState.On ? "on" : "off")))
    ]);
}
```

--------------

## Security policy

Security issues and bugs should be reported privately by emailing contact@kevinchalet.com.
You should receive a response within 24 hours. If for some reason you do not, please follow up via email to ensure we received your original message.

--------------

## Contributors

**OpenNetty** is actively maintained by **[Kévin Chalet](https://github.com/kevinchalet)**. Contributions are welcome and can be submitted using pull requests.

--------------

## License

This project is licensed under the **Apache License**. This means that you can use, modify and distribute it freely.
See [http://www.apache.org/licenses/LICENSE-2.0.html](http://www.apache.org/licenses/LICENSE-2.0.html) for more details.
