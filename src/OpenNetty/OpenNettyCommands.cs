/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty commands, as defined by the Nitoo and MyHome specifications.
/// </summary>
public static class OpenNettyCommands
{
    /// <summary>
    /// Lighting commands (WHO = 1).
    /// </summary>
    public static class Lighting
    {
        /// <summary>
        /// Off (WHAT = 0).
        /// </summary>
        public static readonly OpenNettyCommand Off = new(OpenNettyCategories.Lighting, "0");

        /// <summary>
        /// On (WHAT = 1).
        /// </summary>
        public static readonly OpenNettyCommand On = new(OpenNettyCategories.Lighting, "1");

        /// <summary>
        /// On, 20% (WHAT = 2).
        /// </summary>
        public static readonly OpenNettyCommand On20 = new(OpenNettyCategories.Lighting, "2");

        /// <summary>
        /// On, 30% (WHAT = 3).
        /// </summary>
        public static readonly OpenNettyCommand On30 = new(OpenNettyCategories.Lighting, "3");

        /// <summary>
        /// On, 40% (WHAT = 4).
        /// </summary>
        public static readonly OpenNettyCommand On40 = new(OpenNettyCategories.Lighting, "4");

        /// <summary>
        /// On, 50% (WHAT = 5).
        /// </summary>
        public static readonly OpenNettyCommand On50 = new(OpenNettyCategories.Lighting, "5");

        /// <summary>
        /// On, 60% (WHAT = 6).
        /// </summary>
        public static readonly OpenNettyCommand On60 = new(OpenNettyCategories.Lighting, "6");

        /// <summary>
        /// On, 70% (WHAT = 7).
        /// </summary>
        public static readonly OpenNettyCommand On70 = new(OpenNettyCategories.Lighting, "7");

        /// <summary>
        /// On, 80% (WHAT = 8).
        /// </summary>
        public static readonly OpenNettyCommand On80 = new(OpenNettyCategories.Lighting, "8");

        /// <summary>
        /// On, 90% (WHAT = 9).
        /// </summary>
        public static readonly OpenNettyCommand On90 = new(OpenNettyCategories.Lighting, "9");

        /// <summary>
        /// On, 100% (WHAT = 10).
        /// </summary>
        public static readonly OpenNettyCommand On100 = new(OpenNettyCategories.Lighting, "10");

        /// <summary>
        /// Toggle (WHAT = 32).
        /// </summary>
        public static readonly OpenNettyCommand Toggle = new(OpenNettyCategories.Lighting, "32");

        /// <summary>
        /// Dim stop (WHAT = 38).
        /// </summary>
        public static readonly OpenNettyCommand DimStop = new(OpenNettyCategories.Lighting, "38");
    }

    /// <summary>
    /// Temperature control commands (WHO = 4).
    /// </summary>
    public static class TemperatureControl
    {
        /// <summary>
        /// Wire pilot setpoint mode (WHAT = 50).
        /// </summary>
        /// <remarks>
        /// Note: this command requires specifying additional parameters.
        /// </remarks>
        public static readonly OpenNettyCommand WirePilotSetpointMode = new(OpenNettyCategories.TemperatureControl, "50");

        /// <summary>
        /// Wire pilot derogation mode (WHAT = 51).
        /// </summary>
        /// <remarks>
        /// Note: this command requires specifying additional parameters.
        /// </remarks>
        public static readonly OpenNettyCommand WirePilotDerogationMode = new(OpenNettyCategories.TemperatureControl, "51");

        /// <summary>
        /// Cancel wire pilot derogation mode (WHAT = 52).
        /// </summary>
        public static readonly OpenNettyCommand CancelWirePilotDerogationMode = new(OpenNettyCategories.TemperatureControl, "52");
    }

    /// <summary>
    /// Management commands (WHO = 13).
    /// </summary>
    public static class Management
    {
        /// <summary>
        /// Battery weak (WHAT = 24).
        /// </summary>
        public static readonly OpenNettyCommand BatteryWeak = new(OpenNettyCategories.Management, "24");

        /// <summary>
        /// Supervisor (WHAT = 66).
        /// </summary>
        public static readonly OpenNettyCommand Supervisor = new(OpenNettyCategories.Management, "66");

        /// <summary>
        /// Supervisor remove (WHAT = 67).
        /// </summary>
        public static readonly OpenNettyCommand SupervisorRemove = new(OpenNettyCategories.Management, "67");
    }

    /// <summary>
    /// Scenario commands (WHO = 25).
    /// </summary>
    public static class Scenario
    {
        /// <summary>
        /// Action (WHAT = 11).
        /// </summary>
        public static readonly OpenNettyCommand Action = new(OpenNettyCategories.Scenarios, "11");

        /// <summary>
        /// Stop action (WHAT = 16).
        /// </summary>
        public static readonly OpenNettyCommand StopAction = new(OpenNettyCategories.Scenarios, "16");

        /// <summary>
        /// Action for time (WHAT = 17).
        /// </summary>
        public static readonly OpenNettyCommand ActionForTime = new(OpenNettyCategories.Scenarios, "17");

        /// <summary>
        /// Action in time (WHAT = 18).
        /// </summary>
        public static readonly OpenNettyCommand ActionInTime = new(OpenNettyCategories.Scenarios, "18");

        /// <summary>
        /// Short pressure (WHAT = 21).
        /// </summary>
        public static readonly OpenNettyCommand ShortPressure = new(OpenNettyCategories.Scenarios, "21");

        /// <summary>
        /// Binding request (WHAT = 33).
        /// </summary>
        public static readonly OpenNettyCommand BindingRequest = new(OpenNettyCategories.Scenarios, "33");

        /// <summary>
        /// Unbinding request (WHAT = 34).
        /// </summary>
        public static readonly OpenNettyCommand UnbindingRequest = new(OpenNettyCategories.Scenarios, "34");

        /// <summary>
        /// Open binding (WHAT = 35).
        /// </summary>
        public static readonly OpenNettyCommand OpenBinding = new(OpenNettyCategories.Scenarios, "35");

        /// <summary>
        /// Close binding (WHAT = 36).
        /// </summary>
        public static readonly OpenNettyCommand CloseBinding = new(OpenNettyCategories.Scenarios, "36");
    }

    /// <summary>
    /// Diagnostics commands (WHO = 1000).
    /// </summary>
    public static class Diagnostics
    {
        /// <summary>
        /// Open learning (WHAT = 61).
        /// </summary>
        /// <remarks>
        /// Note: this command requires specifying additional parameters.
        /// </remarks>
        public static readonly OpenNettyCommand OpenLearning = new(OpenNettyCategories.Diagnostics, "61");

        /// <summary>
        /// Close learning (WHAT = 62).
        /// </summary>
        public static readonly OpenNettyCommand CloseLearning = new(OpenNettyCategories.Diagnostics, "62");

        /// <summary>
        /// Address erase (WHAT = 63).
        /// </summary>
        public static readonly OpenNettyCommand AddressErase = new(OpenNettyCategories.Diagnostics, "63");

        /// <summary>
        /// Memory reset (WHAT = 64).
        /// </summary>
        public static readonly OpenNettyCommand MemoryReset = new(OpenNettyCategories.Diagnostics, "64");

        /// <summary>
        /// Memory read (WHAT = 66).
        /// </summary>
        public static readonly OpenNettyCommand MemoryRead = new(OpenNettyCategories.Diagnostics, "66");

        /// <summary>
        /// Valid action (WHAT = 72).
        /// </summary>
        public static readonly OpenNettyCommand ValidAction = new(OpenNettyCategories.Diagnostics, "72");

        /// <summary>
        /// Invalid action (WHAT = 73).
        /// </summary>
        public static readonly OpenNettyCommand InvalidAction = new(OpenNettyCategories.Diagnostics, "73");
    }
}
