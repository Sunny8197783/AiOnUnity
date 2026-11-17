namespace AiOn.Player
{
    /// <summary>
    /// While anything holds the gate (a dialog, a menu), the player neither walks, looks nor picks points.
    /// Hold/Release are counted so overlapping UIs do not free the player early.
    /// </summary>
    public static class InputGate
    {
        static int _holds;

        public static bool Blocked => _holds > 0;
        public static void Hold() => _holds++;
        /// <summary>Frame the gate last opened: the key press that closed a dialog must not also use a point.</summary>
        public static int ReleasedFrame { get; private set; } = -1;
        public static void Release() { if (_holds > 0 && --_holds == 0) ReleasedFrame = UnityEngine.Time.frameCount; }
        public static void Clear() => _holds = 0;

        // Play mode may start without a domain reload: never carry a hold over from the last session.
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _holds = 0;
    }
}
