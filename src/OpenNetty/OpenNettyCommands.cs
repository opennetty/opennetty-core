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
        /// Timed on, 1 minute (WHAT = 11).
        /// </summary>
        public static readonly OpenNettyCommand TimedOn1Minute = new(OpenNettyCategories.Lighting, "11");

        /// <summary>
        /// Timed on, 2 minutes (WHAT = 12).
        /// </summary>
        public static readonly OpenNettyCommand TimedOn2Minutes = new(OpenNettyCategories.Lighting, "12");

        /// <summary>
        /// Timed on, 3 minutes (WHAT = 13).
        /// </summary>
        public static readonly OpenNettyCommand TimedOn3Minutes = new(OpenNettyCategories.Lighting, "13");

        /// <summary>
        /// Timed on, 4 minutes (WHAT = 14).
        /// </summary>
        public static readonly OpenNettyCommand TimedOn4Minutes = new(OpenNettyCategories.Lighting, "14");

        /// <summary>
        /// Timed on, 5 minutes (WHAT = 15).
        /// </summary>
        public static readonly OpenNettyCommand TimedOn5Minutes = new(OpenNettyCategories.Lighting, "15");

        /// <summary>
        /// Timed on, 15 minutes (WHAT = 16).
        /// </summary>
        public static readonly OpenNettyCommand TimedOn15Minutes = new(OpenNettyCategories.Lighting, "16");

        /// <summary>
        /// Timed on, 30 seconds (WHAT = 17).
        /// </summary>
        public static readonly OpenNettyCommand TimedOn30Seconds = new(OpenNettyCategories.Lighting, "17");

        /// <summary>
        /// Timed on, 500 milliseconds (WHAT = 18).
        /// </summary>
        public static readonly OpenNettyCommand TimedOn500Milliseconds = new(OpenNettyCategories.Lighting, "18");

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
    /// Automation commands (WHO = 2).
    /// </summary>
    public static class Automation
    {
        /// <summary>
        /// Stop (WHAT = 0).
        /// </summary>
        public static readonly OpenNettyCommand Stop = new(OpenNettyCategories.Automation, "0");

        /// <summary>
        /// Up (WHAT = 1).
        /// </summary>
        public static readonly OpenNettyCommand Up = new(OpenNettyCategories.Automation, "1");

        /// <summary>
        /// Down (WHAT = 2).
        /// </summary>
        public static readonly OpenNettyCommand Down = new(OpenNettyCategories.Automation, "2");
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

        /// <summary>
        /// Wire pilot shutdown mode (WHAT = 54).
        /// </summary>
        public static readonly OpenNettyCommand WirePilotShutdownMode = new(OpenNettyCategories.TemperatureControl, "54");

        /// <summary>
        /// Cancel wire pilot shutdown mode (WHAT = 55).
        /// </summary>
        public static readonly OpenNettyCommand CancelWirePilotShutdownMode = new(OpenNettyCategories.TemperatureControl, "55");
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
        /// Create Zigbee network (WHAT = 30).
        /// </summary>
        public static readonly OpenNettyCommand CreateZigbeeNetwork = new(OpenNettyCategories.Management, "30");

        /// <summary>
        /// Close Zigbee network (WHAT = 31).
        /// </summary>
        public static readonly OpenNettyCommand CloseZigbeeNetwork = new(OpenNettyCategories.Management, "31");

        /// <summary>
        /// Open Zigbee network (WHAT = 32).
        /// </summary>
        public static readonly OpenNettyCommand OpenZigbeeNetwork = new(OpenNettyCategories.Management, "32");

        /// <summary>
        /// Join Zigbee network (WHAT = 33).
        /// </summary>
        public static readonly OpenNettyCommand JoinZigbeeNetwork = new(OpenNettyCategories.Management, "33");

        /// <summary>
        /// Leave Zigbee network (WHAT = 34).
        /// </summary>
        public static readonly OpenNettyCommand LeaveZigbeeNetwork = new(OpenNettyCategories.Management, "34");

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
    /// Scenarios plus commands (WHO = 25).
    /// </summary>
    public static class ScenariosPlus
    {
        /// <summary>
        /// Action (WHAT = 11).
        /// </summary>
        public static readonly OpenNettyCommand Action = new(OpenNettyCategories.ScenariosPlus, "11");

        /// <summary>
        /// Stop action (WHAT = 16).
        /// </summary>
        public static readonly OpenNettyCommand StopAction = new(OpenNettyCategories.ScenariosPlus, "16");

        /// <summary>
        /// Action for time (WHAT = 17).
        /// </summary>
        public static readonly OpenNettyCommand ActionForTime = new(OpenNettyCategories.ScenariosPlus, "17");

        /// <summary>
        /// Action in time (WHAT = 18).
        /// </summary>
        public static readonly OpenNettyCommand ActionInTime = new(OpenNettyCategories.ScenariosPlus, "18");

        /// <summary>
        /// Short pressure (WHAT = 21).
        /// </summary>
        /// <remarks>
        /// Note: this command MAY require specifying additional parameters.
        /// </remarks>
        public static readonly OpenNettyCommand ShortPressure = new(OpenNettyCategories.ScenariosPlus, "21");

        /// <summary>
        /// Start of extended pressure (WHAT = 22).
        /// </summary>
        /// <remarks>
        /// Note: this command requires specifying additional parameters.
        /// </remarks>
        public static readonly OpenNettyCommand StartOfExtendedPressure = new(OpenNettyCategories.ScenariosPlus, "22");

        /// <summary>
        /// Extended pressure (WHAT = 23).
        /// </summary>
        /// <remarks>
        /// Note: this command requires specifying additional parameters.
        /// </remarks>
        public static readonly OpenNettyCommand ExtendedPressure = new(OpenNettyCategories.ScenariosPlus, "23");

        /// <summary>
        /// End of extended pressure (WHAT = 24).
        /// </summary>
        /// <remarks>
        /// Note: this command requires specifying additional parameters.
        /// </remarks>
        public static readonly OpenNettyCommand EndOfExtendedPressure = new(OpenNettyCategories.ScenariosPlus, "24");

        /// <summary>
        /// Binding request (WHAT = 33).
        /// </summary>
        public static readonly OpenNettyCommand BindingRequest = new(OpenNettyCategories.ScenariosPlus, "33");

        /// <summary>
        /// Unbinding request (WHAT = 34).
        /// </summary>
        public static readonly OpenNettyCommand UnbindingRequest = new(OpenNettyCategories.ScenariosPlus, "34");

        /// <summary>
        /// Open binding (WHAT = 35).
        /// </summary>
        public static readonly OpenNettyCommand OpenBinding = new(OpenNettyCategories.ScenariosPlus, "35");

        /// <summary>
        /// Close binding (WHAT = 36).
        /// </summary>
        public static readonly OpenNettyCommand CloseBinding = new(OpenNettyCategories.ScenariosPlus, "36");

        /// <summary>
        /// Cancel binding (WHAT = 37).
        /// </summary>
        public static readonly OpenNettyCommand CancelBinding = new(OpenNettyCategories.ScenariosPlus, "37");
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
