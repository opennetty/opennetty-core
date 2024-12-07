/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty categories, as defined by the Nitoo and MyHome specifications.
/// </summary>
public static class OpenNettyCategories
{
    /// <summary>
    /// Lighting (WHO = 1).
    /// </summary>
    public static readonly OpenNettyCategory Lighting = new("1");

    /// <summary>
    /// Automation (WHO = 2).
    /// </summary>
    public static readonly OpenNettyCategory Automation = new("2");

    /// <summary>
    /// Temperature control (WHO = 4).
    /// </summary>
    public static readonly OpenNettyCategory TemperatureControl = new("4");

    /// <summary>
    /// Management (WHO = 13).
    /// </summary>
    public static readonly OpenNettyCategory Management = new("13");

    /// <summary>
    /// Scenarios (WHO = 25).
    /// </summary>
    public static readonly OpenNettyCategory Scenarios = new("25");

    /// <summary>
    /// Diagnostics (WHO = 1000).
    /// </summary>
    public static readonly OpenNettyCategory Diagnostics = new("1000");
}
