﻿namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty transmission media, as defined by the Nitoo and MyHome specifications.
/// </summary>
public enum OpenNettyMedia
{
    /// <summary>
    /// Bus (used in the MyHome Up products).
    /// </summary>
    Bus = 0,

    /// <summary>
    /// Infrared (used in the In One by Legrand IR and CPL products).
    /// </summary>
    Infrared = 1,

    /// <summary>
    /// Powerline (used in the In One by Legrand PLC products).
    /// </summary>
    Powerline = 2,

    /// <summary>
    /// Radio (used in the In One by Legrand RF and MyHome Play products).
    /// </summary>
    Radio = 3
}
