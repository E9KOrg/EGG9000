namespace EGG9000.Common.Extensions {
    public static class UInt32Extensions {
        public static string Format(this uint num) => num switch {
            >= 1000000 => (num / 1000000D).ToString("0.#M"),
            >= 10000 => (num / 1000D).ToString("0.#K"),
            _ => num.ToString("#,0")
        };
    }
}