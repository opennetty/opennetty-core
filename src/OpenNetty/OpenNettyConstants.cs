namespace OpenNetty;

/// <summary>
/// Exposes common OpenNetty constants.
/// </summary>
public static class OpenNettyConstants
{
    /// <summary>
    /// Delimiters.
    /// </summary>
    public static class Delimiters
    {
        /// <summary>
        /// End.
        /// </summary>
        public static ReadOnlySpan<byte> End => "##"u8;

        /// <summary>
        /// Start.
        /// </summary>
        public static ReadOnlySpan<byte> Start => "*"u8;
    }

    /// <summary>
    /// Separators.
    /// </summary>
    public static class Separators
    {
        /// <summary>
        /// Asterisk.
        /// </summary>
        public static ReadOnlySpan<byte> Asterisk => "*"u8;

        /// <summary>
        /// Hash.
        /// </summary>
        public static ReadOnlySpan<byte> Hash => "#"u8;
    }
}
