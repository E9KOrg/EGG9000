namespace EGG9000.Common.Helpers {
    // Well-known Discord snowflakes that were previously hardcoded across the codebase.
    // Swapping a raw literal for one of these consts is value-preserving in every build config.
    public static class KnownGuilds {
        // The Palace home guild. Under the DEV9002 config the bot points at the dev guild instead.
#if DEV9002
        public const ulong Palace = 1108127105088241746;
#else
        public const ulong Palace = 656455567858073601;
#endif

        // The dev/test guild, used explicitly as a fallback even outside DEV9002.
        public const ulong Dev = 1108127105088241746;
    }

    public static class KnownRoles {
        public const ulong Overflow = 775547850134257675;
        public const ulong Registered = 794713762396897280;
        public const ulong Unjoined = 796512753241161748;
        public const ulong Active = 798284088967430144;
    }

    public static class KnownUsers {
        // The EGG9000 bot's own application/user id.
        public const ulong Bot = 514257192803893272;
    }
}
