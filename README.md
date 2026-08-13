# OpenNetty

## What is OpenNetty?

OpenNetty aims to provide an **advanced solution** for implementing [OpenWebNet](https://en.wikipedia.org/wiki/OpenWebNet)
support in .NET 10.0+ applications.

OpenWebNet is a protocol developed by [BTicino](https://www.bticino.it/) and [Legrand](https://www.legrand.fr/) around 2000 to manage
electrical networks. Although it uses a very basic wire format originally designed to work over PSTN phone lines, the OpenWebNet
protocol is actually fairly complex to implement properly (but also quite powerful!).

To date, three variants of OpenWebNet have been developed by the two companies:
  - OpenWebNet, used to integrate with the [SCS](https://en.wikipedia.org/wiki/Bus_SCS)-based "MyHome" products.
  - OpenWebNet/Nitoo, used to integrate with "In One by Legrand" products (powerline and radio).
  - OpenWebNet/Zigbee, used to integrate with the Zigbee-based "MyHome Play" products.

> [!NOTE]
> OpenNetty is currently the only library that supports all three OpenWebNet variants.

OpenNetty offers both low-level primitives for representing OpenWebNet messages and communicating with OpenWebNet
gateways and a higher-level MQTT integration that can be used directly with home automation software such as
[Home Assistant](https://www.home-assistant.io/), [openHAB](https://www.openhab.org/) or [FHEM](https://fhem.de/).

> [!IMPORTANT]
> **An OpenWebNet gateway is required for OpenNetty to interact with BTicino and Legrand devices**:
>
>   - For In One by Legrand devices, a Legrand 88213 powerline/USB gateway is required. To communicate with
> In One by Legrand radio devices, a Legrand 03606 interface must also be installed in the electrical panel.
>   - For MyHome/MyHome Up devices, both BTicino F454 and MH202 SCS/Ethernet gateways are currently supported.
>   - For MyHome Play devices, a BTicino 3578 or Legrand 88328 Zigbee/USB gateway is required.
>
> Since the "In One by Legrand" and "MyHome Play" product lines are no longer manufactured, purchasing the corresponding gateway
> is generally neither easy nor inexpensive, so this should probably only be considered for large existing installations.

--------------

## Supported devices

The complete list of Legrand and BTicino products currently supported by OpenNetty can be
found in the dedicated [`OpenNettyDevices.xml`](src/OpenNetty/OpenNettyDevices.xml) file.

> [!NOTE]
> Support for additional devices will be progressively added depending on the demand.

--------------

## Using OpenNetty as an OpenWebNet/MQTT gateway

OpenNetty ships with an `OpenNetty.Daemon` executable that can be used directly as an OpenWebNet/MQTT
gateway on any x64, ARM32or ARM64 Linux distribution that supports .NET 10.0 and uses systemd.

> [!IMPORTANT]
> Using OpenNetty as an OpenWebNet/MQTT gateway works best with home automation software that natively supports
> MQTT discovery (such as Home Assistant or openHAB), as it allows all the devices configured in OpenNetty
> to be imported automatically without requiring any additional configuration in the home automation software.

### Install the `libicu` package

.NET relies on the `libicu` package for globalization support. To ensure that OpenNetty works
correctly, make sure to install the latest version of `libicu` available for your distribution.

For example, on [Debian 13/Trixie](https://packages.debian.org/search?searchon=names&keywords=libicu),
you can use the following command to install `libicu` version 76:

```bash
sudo apt install libicu76
```

### Deploy the daemon

Compiled binaries packaged as `.zip` archives can be found in the
[opennetty-resources](https://github.com/opennetty/opennetty-resources) repository, in the releases section.

> [!NOTE]
> These archives are self-contained .NET applications that embed all the required dependencies, so you
> do not need to install any .NET package, SDKor runtime on the machine where OpenNetty is deployed.

> [!TIP]
> Make sure you select the correct architecture when downloading the archive:
>   - x64: typically used for bare metal and virtual machines.
>   - ARM32: compatible with single-board computers (such as Raspberry Pis) that do not support 64-bit mode.
>   - ARM64: best suited for single-board computers that support 64-bit mode (for example, Raspberry Pi 3 and later models running a 64-bit version of Raspberry Pi OS).

First, create a folder on the target machine to host all the files required by OpenNetty. Although OpenNetty can be
deployed anywhere, a folder under `/usr/local/bin` (for example, `/usr/local/bin/opennetty`) is probably the best option.

The recommended way to deploy the daemon is to use an SSH/SFTP client (such as [Bitvise SSH Client](https://bitvise.com/ssh-client-download))
and create the folder over SSH:

```bash
sudo mkdir /usr/local/bin/opennetty
sudo chmod 777 /usr/local/bin/opennetty
```

Once the folder has been created, copy all the files contained in the `.zip` archive into `/usr/local/bin/opennetty`.
You will also need to make the `opennetty-daemon` file executable:

```bash
sudo chmod +x /usr/local/bin/opennetty/opennetty-daemon
```

### Create the systemd service

To ensure that the OpenNetty daemon is started and tracked by the operating system, a systemd service must be created under `/etc/systemd/system`
(for example, `/etc/systemd/system/opennetty.service`). To do so, you can use your SSH client to create the required file:

```bash
sudo nano /etc/systemd/system/opennetty.service
```

```
[Unit]
Description=OpenWebNet/MQTT gateway
After=network.target

[Service]
Type=notify
User=root
WorkingDirectory=/usr/local/bin/opennetty
ExecStart=/usr/local/bin/opennetty/opennetty-daemon
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Once the file is saved, use `systemctl enable` to register the OpenNetty service:

```bash
sudo systemctl enable opennetty
```

> [!TIP]
> At this point, do not start OpenNetty yet, as you must first add the configuration
> file required to communicate with the OpenWebNet gateways and the MQTT broker.

### Create the configuration file

The OpenNetty daemon relies on a configuration file to locate the OpenWebNet gateways and devices. To do so,
create a new XML file named `configuration.xml` (**make sure to preserve the casing**) with the following content,
then replace the server/port/username/password attributes with the values used by your MQTT broker:

```xml
<Configuration>

  <Mqtt Server="192.168.5.1" Port="1883" Username="opennetty" Password="koIiuhTFGtrdRkjLKhYGvgfFSDr" />

</Configuration>
```

> [!IMPORTANT]
> Using a code editor such as [Visual Studio Code](https://code.visualstudio.com/) makes writing the configuration file much easier.

> [!TIP]
> To improve security, OpenNetty supports MQTTS and TLS client authentication. To use TLS, add the required `.crt` and `.key` files
> to the OpenNetty folder and set the `TlsServerCertificateAuthorityFile`, `TlsClientCertificateFile` and `TlsClientCertificatePrivateKeyFile`
> attributes. If necessary, you can also set a custom `TlsServerTargetHost` value:
>
> ```xml
> <Configuration>
> 
>   <Mqtt Server="192.168.5.1" Port="8883" Username="opennetty" Password="koIiuhTFGtrdRkjLKhYGvgfFSDr"
>         TlsServerCertificateAuthorityFile="ca.crt"
>         TlsClientCertificateFile="client.crt"
>         TlsClientCertificatePrivateKeyFile="client.key"
>         TlsServerTargetHost="mosquitto" />
> 
> </Configuration>
> ```

> [!TIP]
> Instead of using a single configuration file, you can split the configuration into multiple files
> and store them under the `/usr/local/bin/opennetty/configuration` directory. OpenNetty will
> automatically load all the `.xml` files in this directory and merge their contents at runtime.

### If necessary, change the UI culture used in the MQTT discovery payloads

By default, OpenNetty uses the current user locale as the UI culture when generating the MQTT discovery payloads used by Home
Assistant (and other compatible software) to automatically create entities for each supported sensor or action exposed by OpenNetty.

If you need to customize the UI culture, you can set the `HomeAssistantDiscoveryUICulture`
attribute to a specific value. At the time of writing, both English and French are supported natively:

```xml
<Configuration>

  <Mqtt Server="192.168.5.1" Port="1883" Username="opennetty" Password="koIiuhTFGtrdRkjLKhYGvgfFSDr"
        HomeAssistantDiscoveryUICulture="fr" />

</Configuration>
```

### Configure the gateways

OpenNetty requires you to list the gateways in the configuration file.

To do so, add a `Device` node with the correct brand/model attributes for each gateway present in the installation,
along with a `Gateway` node containing the gateway details, including whether OpenNetty should use a serial or TCP
socket to initiate OpenWebNet sessions:

```xml
<Configuration>

  <Mqtt Server="192.168.5.1" Port="1883" Username="opennetty" Password="koIiuhTFGtrdRkjLKhYGvgfFSDr" />

  <!-- In One by Legrand gateway -->

  <Device Brand="Legrand" Model="88213">
    <Gateway Type="Serial" Port="/dev/serial/by-id/usb-Btcino_Terraneo_Mod._SFERA_Tele_Loop-if00" />
  </Device>

  <!-- MyHome Play gateway -->

  <Device Brand="Legrand" Model="88328">
    <Gateway Type="Serial" Port="/dev/serial/by-id/usb-Silicon_Labs_CP2102_USB_to_UART_Bridge_Controller_0001-if00-port0" />
  </Device>

  <!-- MyHome gateway -->

  <Device Brand="BTicino" Model="F454">
    <Gateway Type="Tcp" Server="192.168.5.10" Password="aJhYiBHk8" />
  </Device>

</Configuration>
```

> [!IMPORTANT]
> OpenNetty natively supports both the legacy "OPEN authentication" method and the newer and safer
> ["HMAC authentication" mechanism](https://developer.legrand.com/uploads/2019/12/Hmac.pdf) implemented by recent Ethernet-based OpenWebNet gateways (for example, the F454).
>
> Although the IPv4 address of the machine running OpenNetty can also be whitelisted via
> [MyHome Suite](https://www.homesystems-legrandgroup.com/home?p_p_id=it_smc_bticino_homesystems_search_AutocompletesearchPortlet&p_p_lifecycle=0&p_p_state=normal&p_p_mode=view&_it_smc_bticino_homesystems_search_AutocompletesearchPortlet_journalArticleId=2493426&_it_smc_bticino_homesystems_search_AutocompletesearchPortlet_mvcPath=%2Fview_journal_article_content.jsp)
> to avoid requiring authentication, this is not recommended when using OpenNetty.

### Configure the endpoints

To communicate with "In One by Legrand", "MyHome Play" and "MyHome" devices, OpenNetty requires you to list them in the configuration file.

To do so, add a `Device` node with the correct brand/model attributes for each device present in the installation:
  - The serial number (or MAC address for Ethernet gateways) is optional, but strongly recommended whenever possible to help identify devices in Home Assistant.

  - The unit (also known as a "module" in MyHome Suite) MUST match one of the unit identifiers supported by the device. If you are unsure which identifier
  should be used, see [`OpenNettyDevices.xml`](src/OpenNetty/OpenNettyDevices.xml) for the list of supported devices and the units they expose.

  - For In One by Legrand and MyHome Play devices, units that are not explicitly listed are
  automatically added by OpenNetty and the corresponding endpoints are generated using default names.

  - For MyHome devices, the area/point attributes MUST match the values assigned via [MyHome Suite](https://www.homesystems-legrandgroup.com/home?p_p_id=it_smc_bticino_homesystems_search_AutocompletesearchPortlet&p_p_lifecycle=0&p_p_state=normal&p_p_mode=view&_it_smc_bticino_homesystems_search_AutocompletesearchPortlet_journalArticleId=2493426&_it_smc_bticino_homesystems_search_AutocompletesearchPortlet_mvcPath=%2Fview_journal_article_content.jsp).

  - The endpoint name can either be set explicitly or generated implicitly. In both cases, it will be used to derive the MQTT topic for the
  endpoint (for example, state changes dispatched by an endpoint named `Bedroom/Wall light` will be posted under the `opennetty/bedroom/wall light` MQTT topic).

> [!TIP]
> You can also add MyHome SCS light point area or group endpoints that are not attached to a specific device. This is the most efficient
> way to execute a single operation targeting multiple devices at the same time (for example, switching on all the lights in a specific room).
>
> In this case, the `Endpoint` node MUST NOT appear under a `Device` node and MUST contain a list of
> `Capability` nodes that define the set of operations that can be executed on the endpoint.
>
> You can find the complete list of capabilities in [`OpenNettyCapabilities.cs`](src/OpenNetty/OpenNettyCapabilities.cs).

```xml
<Configuration>

  <Mqtt Server="192.168.5.1" Port="1883" Username="opennetty" Password="koIiuhTFGtrdRkjLKhYGvgfFSDr" />

  <!-- In One by Legrand gateway -->

  <Device Brand="Legrand" Model="88213" SerialNumber="148366">
    <Gateway Type="Serial" Port="/dev/serial/by-id/usb-Btcino_Terraneo_Mod._SFERA_Tele_Loop-if00" />
  </Device>

  <!-- MyHome Play gateway -->

  <Device Brand="Legrand" Model="88328" SerialNumber="0026BD26">
    <Gateway Type="Serial" Port="/dev/serial/by-id/usb-Silicon_Labs_CP2102_USB_to_UART_Bridge_Controller_0001-if00-port0" />
  </Device>

  <!-- MyHome gateway -->

  <Device Brand="BTicino" Model="F454" MacAddress="00:03:50:A2:27:1B">
    <Gateway Type="Tcp" Server="192.168.5.10" Password="aJhYiBHk8" />
  </Device>

  <!-- In One by Legrand one-gang PLC switch -->

  <Device Brand="Legrand" Model="67201" SerialNumber="597132">
    <Unit Id="2">
      <Endpoint Name="Bedroom/Wall light" />
    </Unit>
  </Device>

  <!-- In One by Legrand two-gang PLC switch -->

  <Device Brand="Legrand" Model="67202" SerialNumber="479632">
    <Unit Id="3">
      <Endpoint Name="Bedroom/Bedside lamp 1" />
    </Unit>

    <Unit Id="4">
      <Endpoint Name="Bedroom/Bedside lamp 2" />
    </Unit>
  </Device>

  <!-- MyHome Play one-gang wireless command -->

  <Device Brand="Legrand" Model="67223" SerialNumber="0014AC87" />

  <!-- MyHome two-way light actuator -->

  <Device Brand="BTicino" Model="F411U2" SerialNumber="00B582A5">
    <Unit Id="1">
      <Endpoint Name="Garage/Recessed light 1" Area="4" Point="1">
        <Setting Name="Function type" Value="Light actuator" />
      </Endpoint>
    </Unit>

    <Unit Id="2">
      <Endpoint Name="Garage/Recessed light 2" Area="4" Point="2">
        <Setting Name="Function type" Value="Light actuator" />
      </Endpoint>
    </Unit>
  </Device>

  <!-- MyHome shutter actuator -->

  <Device Brand="BTicino" Model="F411U2" SerialNumber="00A472A9">
    <Unit Id="1">
      <Endpoint Name="Living room/Shutter" Area="1" Point="3">
        <Setting Name="Function type" Value="Automation actuator" />
      </Endpoint>
    </Unit>
  </Device>

  <!-- MyHome two-way dimmer -->

  <Device Brand="BTicino" Model="F418U2" SerialNumber="00B582A5">
    <Unit Id="1">
      <Endpoint Name="Living room/Wall light 1" Area="1" Point="1" />
    </Unit>

    <Unit Id="2">
      <Endpoint Name="Living room/Wall light 2" Area="1" Point="2" />
    </Unit>
  </Device>

  <!-- MyHome two-way contacts interface -->

  <Device Brand="BTicino" Model="F428" SerialNumber="000A2E88">
    <Unit Id="1">
      <Endpoint Type="SCS light point" Area="8" Point="1">
        <Setting Name="Function type" Value="Scheduled scenario" />
      </Endpoint>
    </Unit>

    <Unit Id="2">
      <Endpoint Type="SCS scenario plus" Id="1000">
        <Setting Name="Function type" Value="Scheduled scenario plus" />
      </Endpoint>
    </Unit>
  </Device>

  <!-- MyHome light point group endpoint -->

  <Endpoint Name="Garden shed/Downlight LEDs" Type="SCS light point" Group="1">
    <Capability Name="On/off switch control" />
  </Endpoint>

  <!-- MyHome light point area endpoint -->

  <Endpoint Name="Living room/All lights" Type="SCS light point" Area="8">
    <Capability Name="Advanced dimming control" />
    <Capability Name="On/off switch control" />
  </Endpoint>

  <!-- MyHome light point general endpoint -->

  <Endpoint Name="General/All lights" Type="SCS light point" General="true">
    <Capability Name="On/off switch control" />
  </Endpoint>

</Configuration>
```

### Deploy the configuration file and start the daemon

Once your configuration file is ready, copy it to the folder you created for OpenNetty and start the daemon:

```bash
sudo service opennetty start
```

> [!TIP]
> You can use `sudo service opennetty status` to determine whether the daemon is running correctly.
>
> You can also use [MQTT Explorer](http://mqtt-explorer.com/) to verify that state changes are correctly published
> to MQTT and to trigger commands that will be executed by In One by Legrand, MyHome Playor MyHome devices.
>
> For example, to turn `Bedroom/Wall light` on, publish the `ON` value to the `opennetty/bedroom/wall light/switch_state/set`
> topic. If the command was executed successfully by the device, the `ON` value will be published back by OpenNetty to the
> `opennetty/bedroom/wall light/switch_state` topic.
>
> You can also send an empty `opennetty/bedroom/wall light/switch_state/get` message to retrieve the current switch state of the endpoint.

> [!IMPORTANT]
> If your home automation software supports MQTT discovery (such as Home Assistant or openHAB), devices should
> automatically appear with all their supported entities without requiring any additional configuration.
>
> If your home automation software requires devices to be configured manually, the complete list of supported
> MQTT attributes can be found in [`OpenNettyMqttAttributes.cs`](src/OpenNetty.Mqtt/OpenNettyMqttAttributes.cs).

### If necessary, change the default log level

By default, OpenNetty uses `Information` as the log level. You can easily change it by editing the `appsettings.json`
file and restarting the daemon:

```bash
sudo nano /usr/local/bin/opennetty/appsettings.json
```

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "OpenNetty": "Debug"
    }
  }
}
```

```bash
sudo service opennetty restart
```

## Using OpenNetty as a library

### Primitives

To represent raw OpenWebNet frames, OpenNetty exposes three low-level structures—`OpenNettyFrame`, `OpenNettyField` and `OpenNettyParameter`—and
one higher-level primitive — `OpenNettyMessage` — that can be used to represent any message type supported by the three OpenWebNet specifications:
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
    medium  : OpenNettyMedium.Powerline,
    mode    : OpenNettyMode.Unicast);
```

```csharp
var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Nitoo, "*1*1*7806914##");

// (487932, 2)
var (identifier, unit) = OpenNettyAddress.ToNitooAddress(message.Address!.Value);
```

### Sessions

The `OpenNettySession` class is the main entry point for **manually communicating** with an OpenWebNet gateway. It takes care
of initializing the connection and automatically negotiates the desired OpenWebNet session type. If authentication is
required by the remote gateway, it also handles the authentication flow transparently.

`OpenNettySession` implements `IAsyncObservable<OpenNettyMessage>` and can be used natively with any of the extensions
provided by the [System.Reactive.Async package](https://www.nuget.org/packages/System.Reactive.Async) to filter and
observe the messages sent by the OpenWebNet gateway.

```csharp
var gateway = OpenNettyGateway.Create(
    device  : OpenNettyDevice.Create(
        brand     : OpenNettyBrand.BTicino,
        model     : "F454",
        name      : null,
        identifier: OpenNettyDeviceIdentifier.FromMacAddress("00:03:50:A2:27:1B")),
    endpoint: IPEndPoint.Parse("192.168.5.10:20000"),
    password: "aJhYiBHk8");

await using var session = await OpenNettySession.CreateAsync(gateway, OpenNettySessionType.Event);

await using var subscription = await session.SubscribeAsync(message => Console.WriteLine(message.ToString()));
await using var connection = await session.ConnectAsync();

await Task.Delay(-1);
```

### .NET Generic Host integration

Although **sessions can be used directly to communicate with an OpenWebNet gateway, this is not the recommended approach**.

Instead, **users are strongly encouraged to leverage OpenNetty's .NET Generic Host integration**. It registers a worker
for each configured gateway and dynamically manages sessions, processes incoming messages and dispatches outgoing messages.
It also automatically retransmits failed outgoing messages by using a retry policy defined by OpenNetty according to the type of gateway.

Once the OpenNetty services have been registered by using the dedicated `.AddOpenNetty()` extension, the low-level `IOpenNettyService`
interface can be used to execute arbitrary bus commands, dimension requests, dimension sets or status requests,
and to extract the corresponding response returned by the gateway when applicable:

```csharp
var builder = Host.CreateApplicationBuilder();

builder.Services.AddOpenNetty(options =>
{
    // Register the SCS gateway used to communicate with MyHome devices.
    options.AddGateway(OpenNettyGateway.Create(
        device  : OpenNettyDevice.Create(
            brand     : OpenNettyBrand.BTicino,
            model     : "F454",
            name      : null,
            identifier: OpenNettyDeviceIdentifier.FromMacAddress("00:03:50:A2:27:1B")),
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
> While the `IOpenNettyService` service can be very useful for sending arbitrary messages or observing specific incoming messages, it is strongly recommended to define endpoints
> as shown in the next section and to use the strongly typed APIs exposed by `OpenNettyController` whenever possible.

### Endpoints

**For all its high-level operations, OpenNetty relies on the `OpenNettyEndpoint` class**:

  - An endpoint generally has an associated address (although this is not always the case, as gateway endpoints do not have an attached address).
  - In most cases, an endpoint has an attached device definition from which it resolves the supported functions (such as switching
  a connected load on or off or controlling the brightness level), but it is also possible to create endpoints that do not have a
  device attached, which makes it possible to support non-device-specific addresses such as SCS light-point area or group addresses.
  - When no unit or device definition is attached, a list of capabilities must be attached
  to the endpoint before actions can be performed by using the `OpenNettyController` class.

```csharp
var builder = Host.CreateApplicationBuilder();

builder.Services.AddOpenNetty(options =>
{
    options.AddGateway(OpenNettyGateway.Create(
        device  : OpenNettyDevice.Create(
            brand     : OpenNettyBrand.BTicino,
            model     : "F454",
            name      : null,
            identifier: OpenNettyDeviceIdentifier.FromMacAddress("00:03:50:A2:27:1B")),
        endpoint: IPEndPoint.Parse("192.168.5.10:20000"),
        password: "aJhYiBHk8"));

    options.AddEndpoint(new OpenNettyEndpoint
    {
        // SCS light point point-to-point address:
        Address = OpenNettyAddress.FromScsLightPointAddress(
            extension: 0,
            general  : false,
            group    : null,
            area     : 1,
            point    : 3),
        Unit = OpenNettyDevice.Create(
            brand     : OpenNettyBrand.BTicino,
            model     : "F418U2",
            name      : "BTicino F418U2 (00B582A5)",
            identifier: OpenNettyDeviceIdentifier.FromScsSerialNumber("00B582A5")).GetUnit(1),
        Name = "Bathroom/Recessed light",
        Protocol = OpenNettyProtocol.Scs
    });

    options.AddEndpoint(new OpenNettyEndpoint
    {
        // SCS light point area address:
        Address = OpenNettyAddress.FromScsLightPointAddress(
            extension: 0,
            general  : false,
            group    : null,
            area     : 1,
            point    : null),
        Capabilities = [OpenNettyCapabilities.OnOffSwitchControl],
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
all incoming messages sent by the configured gateways and invokes the corresponding events exposed by the `OpenNettyEvents` class.

By implementing the `IOpenNettyHandler` interface, it is possible to subscribe to any event before incoming frames begin to be processed:

```csharp
var builder = Host.CreateApplicationBuilder();

builder.Services.AddOpenNetty(options =>
{
    var file = builder.Environment.ContentRootFileProvider.GetFileInfo("configuration.xml");
    options.ImportFromXmlConfiguration(file);
});

builder.Services.AddSingleton<IOpenNettyHandler, MyEventHandler>();

var app = builder.Build();
await app.RunAsync();

file sealed class MyEventHandler(OpenNettyEvents events) : IOpenNettyHandler
{
    public async ValueTask<IAsyncDisposable> SubscribeAsync() => StableCompositeAsyncDisposable.Create(
    [
        await events.BrightnessReported.SubscribeAsync(static args =>
            Console.WriteLine($"Brightness on endpoint {args.Endpoint.Name}: {args.Level}.")),

        await events.SwitchStateReported.SubscribeAsync(static args =>
            Console.WriteLine(args.State is OpenNettyModels.Lighting.SwitchState.On
                ? $"Switch state on endpoint {args.Endpoint.Name}: ON." 
                : $"Switch state on endpoint {args.Endpoint.Name}: OFF."));
    ]);
}
```

## Nitoo scenarios

Unlike SCS and Zigbee gateways, the Nitoo PLC/USB gateway never reports state changes
that indirectly affect Nitoo devices associated by using a `Push & Learn` scenario (PnL).

> [!TIP]
> These scenarios are stored exclusively in the memory of each associated unit, alongside a
> `Function code` representing the desired outcome (for example, setting the brightness to 50%).

To allow OpenNetty to propagate state changes triggered by Nitoo scenarios, each unit that triggers scenarios resulting in a state
change MUST include one or more `<Scenario>` node(s) indicating the name of the affected endpoint and the Nitoo function code:

```xml
<Device Brand="Legrand" Model="67280" SerialNumber="487125">
  <Unit Id="1">
    <!-- ON command -->

    <Scenario EndpointName="Garage/Sectional door" FunctionCode="101" />
  </Unit>

  <Unit Id="2">
    <!-- ON command -->

    <Scenario EndpointName="Garage/Sliding door" FunctionCode="101" />
  </Unit>

  <Unit Id="3">
    <!-- OFF command -->

    <Scenario EndpointName="Garage/Switched outlet 1" FunctionCode="102" />
    <Scenario EndpointName="Garage/Switched outlet 2" FunctionCode="102" />
  </Unit>

  <Unit Id="4">
    <!-- ON command -->

    <Scenario EndpointName="Garage/Switched outlet 1" FunctionCode="101" />
    <Scenario EndpointName="Garage/Switched outlet 2" FunctionCode="101" />
  </Unit>
</Device>
```

> [!NOTE]
> Unfortunately, the Nitoo function codes are not documented by Legrand/BTicino. To work around this limitation,
> the memory of already configured powerline-based Nitoo units can be retrieved through Home Assistant by using the
> "Get scenarios stored in memory" button. The scenarios are stored as additional attributes and can be accessed from
> the "⋮ → Details" menu of the "Scenarios stored in memory" sensor.
>
> Alternatively, the same information can be retrieved programmatically by using the `OpenNettyController.GetMemoryDataAsync()` API:
>
> ```csharp
> var builder = Host.CreateApplicationBuilder();
> 
> builder.Services.AddOpenNetty(options =>
> {
>     var file = builder.Environment.ContentRootFileProvider.GetFileInfo("configuration.xml");
>     options.ImportFromXmlConfiguration(file);
> });
> 
> var app = builder.Build();
> await app.StartAsync();
> 
> var manager = app.Services.GetRequiredService<OpenNettyManager>();
> var controller = app.Services.GetRequiredService<OpenNettyController>();
> 
> // Resolve the endpoint that reacts to one or more Nitoo scenarios.
> var receiver = await manager.FindEndpointByNameAsync("Garage/Switched outlet 1")
>     ?? throw new InvalidOperationException("The endpoint couldn't be resolved.");
> 
> foreach (var data in await controller.GetMemoryDataAsync(receiver))
> {
>     // Resolve the endpoint that emits the Nitoo scenario matching the memory entry, if possible.
>     var emitter = await manager.FindEndpointsByAddressAsync(receiver.Gateway!, data.Address).FirstOrDefaultAsync();
>     if (emitter is not null)
>     {
>         Console.WriteLine("Nitoo scenario triggered by a known endpoint:");
>         Console.WriteLine("\tEndpoint name: {0}.", emitter.Name);
>         Console.WriteLine("\tFunction code: {0}.", data.FunctionCode);
>         Console.WriteLine();
>         Console.WriteLine();
>     }
> 
>     else
>     {
>         var (identifier, unit) = OpenNettyAddress.ToNitooAddress(data.Address);
> 
>         Console.WriteLine("Nitoo scenario triggered by an unknown endpoint:");
>         Console.WriteLine("\tDevice identifier: {0}.", identifier);
>         Console.WriteLine("\tUnit: {0}.", unit);
>         Console.WriteLine("\tFunction code: {0}.", data.FunctionCode);
>         Console.WriteLine();
>         Console.WriteLine();
>     }
> }
> 
> await app.StopAsync();
> ```

## Advanced settings

OpenNetty allows specific settings to be attached to endpoints to control how events are handled or how commands are sent.
Although the default settings are generally sufficient, overriding them can be useful for some endpoints.

Settings can be attached programmatically or through the configuration file to devices, units or endpoints. For example:

```xml
<Device Brand="Legrand" Model="67222" SerialNumber="487932">
  <Setting Name="Action validation" Value="false" />

  <Unit Id="4">
    <Endpoint Name="Kitchen/Dimmable socket" />
  </Unit>
</Device>
```

```csharp
var builder = Host.CreateApplicationBuilder();

builder.Services.AddOpenNetty(options =>
{
    var device = OpenNettyDevice.Create(
        brand     : OpenNettyBrand.Legrand,
        model     : "67222",
        name      : "Legrand 67222 (487932)",
        identifier: OpenNettyDeviceIdentifier.FromNitooSerialNumber(487932));

    // Note: the properties of the device (or of its units) can be freely mutated
    // until the configuration is finalized and the device is marked as read-only.
    device.Settings = device.Settings.Add(OpenNettySettings.ActionValidation, bool.FalseString);

    var endpoint = new OpenNettyEndpoint
    {
        Address = OpenNettyAddress.FromNitooAddress(identifier: 487932, unit: 4),
        Name = "Kitchen/Dimmable socket",
        Protocol = OpenNettyProtocol.Nitoo,
        Unit = device.GetUnit(4)
    };

    options.AddEndpoint(endpoint);
});
```

### Function type (SCS-only)

Many MyHome devices can be configured to implement different features, such as shutter control or scenario activation.
In some cases (for example, when an actuator is known to support both light and automation modes), OpenNetty requires the function type to be specified:

```xml
<Device Brand="BTicino" Model="F411U2" SerialNumber="00B582A5">
  <Unit Id="1">
    <Endpoint Name="Garage/Recessed light 1" Area="4" Point="1">
      <Setting Name="Function type" Value="Light actuator" />
    </Endpoint>
  </Unit>
</Device>

<Device Brand="BTicino" Model="F411U2" SerialNumber="00A472A9">
  <Unit Id="1">
    <Endpoint Name="Living room/Shutter" Area="1" Point="3">
      <Setting Name="Function type" Value="Automation actuator" />
    </Endpoint>
  </Unit>
</Device>
```

### Action validation (Nitoo-only, powerline-only)

Because they operate over an unreliable medium (that is, power lines), Nitoo PLC devices may not always receive the messages sent by the
OpenWebNet gateway. To mitigate this, these devices automatically report whether a bus command or dimension-set request
was applied successfully by using special VALID ACTION or INVALID ACTION diagnostic frames. OpenNetty monitors these
frames to determine whether the requested action was actually performed. When no confirmation is received, the initial message
is automatically retransmitted by OpenNetty until the maximum number of allowed retransmissions is reached (2 by default) or until
the request is confirmed by the remote device. If no positive confirmation is received, the command is assumed to have failed,
and the state of the endpoint is assumed to be unchanged. For example, when sending an ON command, the `SwitchStateReported`
event will not be triggered if the end device does not report that the command was successful after the allowed number of retransmissions.

When necessary (for example, because a specific endpoint is known to have issues reporting frames back), this mechanism can be disabled.
In that case, every command is assumed to be successful, which is similar to Home Assistant's optimistic mode:

```xml
<Device Brand="Legrand" Model="67222" SerialNumber="487932">
  <Setting Name="Action validation" Value="false" />

  <Unit Id="4">
    <Endpoint Name="Kitchen/Dimmable socket" />
  </Unit>
</Device>
```

### Switch mode

Using MyHome Suite, SCS devices can be configured to use a special PUL mode. When enabled, these devices essentially behave like
push buttons: they no longer react to area or general commands and automatically return to the OFF state after being activated.

When using PUL mode, it is strongly recommended to configure the affected endpoints to use the `Push button`
switch mode so that OpenNetty can correctly report the OFF state and ignore area and general commands.

```xml
<Device Brand="BTicino" Model="F411U1" SerialNumber="0019BF87">
  <Unit Id="1">
    <Endpoint Name="Patio/Doorbell" Area="9" Point="1">
      <Setting Name="Switch mode" Value="Push button" />
    </Endpoint>
  </Unit>
</Device>
```

> [!TIP]
> This setting can also be used for the In One by Legrand 03600 device.

--------------

## Security policy

Security issues and bugs should be reported privately by email to contact@kevinchalet.com.
You should receive a response within 24 hours. If you do not, please follow up by email to ensure that your original message was received.

--------------

## Contributors

**OpenNetty** is actively maintained by **[Kévin Chalet](https://github.com/kevinchalet)**. Contributions are welcome and may be submitted through pull requests.

--------------

## License

This project is licensed under the **Apache License**. This means that you can use, modify and distribute it freely.
See [http://www.apache.org/licenses/LICENSE-2.0.html](http://www.apache.org/licenses/LICENSE-2.0.html) for more details.
