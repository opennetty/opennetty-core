﻿namespace OpenNetty;

/// <summary>
/// Exposes common settings supported by OpenNetty.
/// </summary>
public static class OpenNettySettings
{
    /// <summary>
    /// Action validation (Nitoo only).
    /// </summary>
    public static readonly OpenNettySetting ActionValidation = new("Action validation");

    /// <summary>
    /// Serial port baud rate.
    /// </summary>
    public static readonly OpenNettySetting SerialPortBaudRate = new("Serial port baud rate");

    /// <summary>
    /// Serial port data bits.
    /// </summary>
    public static readonly OpenNettySetting SerialPortDataBits = new("Serial port data bits");

    /// <summary>
    /// Serial port parity.
    /// </summary>
    public static readonly OpenNettySetting SerialPortParity = new("Serial port parity");

    /// <summary>
    /// Serial port stop bits.
    /// </summary>
    public static readonly OpenNettySetting SerialPortStopBits = new("Serial port stop bits");

    /// <summary>
    /// Switch mode.
    /// </summary>
    public static readonly OpenNettySetting SwitchMode = new("Switch mode");
}