namespace EGG9000.Common.Helpers {
    public enum BuildConfiguration { Debug, Dev9001, Dev9002, Release }

    // Single source of truth for build-configuration checks. This is the only file in the repo
    // allowed to contain #if RELEASE/DEBUG/DEV9001/DEV9002 - everywhere else uses these runtime
    // members instead, so config-gated code always compiles regardless of which config you build.
    public static class BuildConfig {
        public static BuildConfiguration Current { get; } =
#if RELEASE
            BuildConfiguration.Release;
#elif DEV9002
            BuildConfiguration.Dev9002;
#elif DEV9001
            BuildConfiguration.Dev9001;
#else
            BuildConfiguration.Debug;
#endif

        public static bool IsRelease => Current == BuildConfiguration.Release;
        public static bool IsDebug => Current == BuildConfiguration.Debug;
        public static bool IsDev9001 => Current == BuildConfiguration.Dev9001;
        public static bool IsDev9002 => Current == BuildConfiguration.Dev9002;
        public static bool IsAnyDev => IsDev9001 || IsDev9002;
    }
}
